// Public lobby search over RequestLobbyList. Design: 01 §4.3, 03 §8.3, 06 S7.
// Steam keeps list filters until the next RequestLobbyList, so the query is validated completely first, then every
// AddRequestLobbyList* call and RequestLobbyList run back to back in Send with nothing in between. Hidden filters:
// k.game == GameId, k.proto == KitVersion.Protocol, s.privacy == "public". k.ver is deliberately not filtered, so
// other versions are listed with IsCompatible == false. Setting filters take schema keys without the "s." prefix;
// unknown, HostOnly or unpublished keys fail with InvalidArgument. One request in flight; a new Request or Cancel
// completes the previous done with Cancelled; no answer in KitTimeouts.LobbyList → Timeout.

using System;
using System.Collections.Generic;
using System.Globalization;
using Steamworks;

namespace MultiplayerKit.Steam
{
    public sealed class SteamPublicLobbyBrowser : IPublicLobbyBrowser, ISteamFlushable, IDisposable
    {
        private const int MaxResultsLimit = 50;

        private static readonly string ProtoText = KitVersion.Protocol.ToString(CultureInfo.InvariantCulture);

        private readonly HostSettingsSchema schema;
        private readonly string gameId;
        private readonly SteamLobbySummaryReader reader;

        private CallResult<LobbyMatchList_t> listCall;
        private bool callbacksRegistered;
        private bool registrationWarned;
        private bool disposed;

        private Action<KitResult<IReadOnlyList<LobbySummary>>> pendingDone;
        private float deadline = float.NaN;   // set by the first Flush after the request

        /// <summary>isSupported comes from GameCapabilities.PublicLobbies; without it every Request fails InvalidArgument.</summary>
        public SteamPublicLobbyBrowser(HostSettingsSchema schema, string gameId, string gameVersion, bool isSupported)
        {
            this.schema = schema;
            this.gameId = gameId ?? "";
            reader = new SteamLobbySummaryReader(schema, gameId, gameVersion);
            IsSupported = isSupported;
            TryRegisterCallbacks();
        }

        public bool IsSupported { get; }

        public bool IsRequesting => pendingDone != null;

        public void Request(LobbyBrowserQuery query, Action<KitResult<IReadOnlyList<LobbySummary>>> done)
        {
            Cancel();   // any new request first completes the previous one with Cancelled
            if (!IsSupported || query == null)
            {
                string why = IsSupported ? "no query" : "public lobbies are disabled for this game";
                KitLog.Warning("Lobby list request refused: " + why + ".");
                SteamPlatform.Raise(done, Fail(KitError.InvalidArgument, why), "lobby list done");
                return;
            }
            if (!ValidateSettingFilters(query, out string invalid))
            {
                KitLog.Warning("Lobby list request refused: setting filter '" + KitLog.Truncate(invalid) + "' is not a published setting.");
                SteamPlatform.Raise(done, Fail(KitError.InvalidArgument, "setting filter " + invalid), "lobby list done");
                return;
            }
            if (disposed || !SteamPlatform.IsInitialized || !TryRegisterCallbacks())
            {
                SteamPlatform.WarnOnce("Public lobby browser: Steam is not initialized.");
                SteamPlatform.Raise(done, Fail(KitError.PlatformUnavailable, null), "lobby list done");
                return;
            }

            Cancel();   // a request started from inside the previous done is superseded too (each done runs once)
            SteamAPICall_t call = Send(query);
            if (call == SteamAPICall_t.Invalid)
            {
                KitLog.Warning("RequestLobbyList returned no call handle.");
                SteamPlatform.Raise(done, Fail(KitError.Unknown, "no call handle"), "lobby list done");
                return;
            }
            pendingDone = done;
            deadline = float.NaN;
            listCall.Set(call);
        }

        /// <summary>A pending done is invoked with Cancelled.</summary>
        public void Cancel()
        {
            if (pendingDone == null) return;
            Action<KitResult<IReadOnlyList<LobbySummary>>> done = TakePending(true);
            SteamPlatform.Raise(done, Fail(KitError.Cancelled, null), "lobby list done");
        }

        public void Flush(float now)
        {
            if (disposed || !SteamPlatform.IsInitialized || !TryRegisterCallbacks() || pendingDone == null) return;
            if (float.IsNaN(deadline))
            {
                deadline = now + KitTimeouts.LobbyList;
                return;
            }
            if (now < deadline) return;
            KitLog.Warning("Lobby list request timed out after " + KitTimeouts.LobbyList + " s.");
            Action<KitResult<IReadOnlyList<LobbySummary>>> done = TakePending(true);
            SteamPlatform.Raise(done, Fail(KitError.Timeout, "RequestLobbyList"), "lobby list done");
        }

        public void Dispose()
        {
            if (disposed) return;
            Action<KitResult<IReadOnlyList<LobbySummary>>> done = pendingDone != null ? TakePending(true) : null;
            disposed = true;
            callbacksRegistered = false;
            listCall?.Dispose();
            listCall = null;
            if (done != null) SteamPlatform.Raise(done, Fail(KitError.Cancelled, "disposed"), "lobby list done");
        }

        // Every filter, then the request, with nothing in between (03 §8.3 table, in its order).
        private SteamAPICall_t Send(LobbyBrowserQuery query)
        {
            SteamMatchmaking.AddRequestLobbyListStringFilter(KitKeys.Game, gameId, ELobbyComparison.k_ELobbyComparisonEqual);
            SteamMatchmaking.AddRequestLobbyListStringFilter(KitKeys.Proto, ProtoText, ELobbyComparison.k_ELobbyComparisonEqual);
            SteamMatchmaking.AddRequestLobbyListStringFilter(SettingCodec.ToLobbyKey(CoreSettingKeys.Privacy),
                LobbyEnums.PrivacyPublic, ELobbyComparison.k_ELobbyComparisonEqual);
            SteamMatchmaking.AddRequestLobbyListDistanceFilter(ToSteam(query.Distance));

            string region = query.Region;
            if (!string.IsNullOrEmpty(region) && !string.Equals(region, CoreSettings.RegionAny, StringComparison.Ordinal))
            {
                SteamMatchmaking.AddRequestLobbyListStringFilter(SettingCodec.ToLobbyKey(CoreSettingKeys.Region), region,
                    ELobbyComparison.k_ELobbyComparisonEqual);
            }
            if (query.HideFull) SteamMatchmaking.AddRequestLobbyListFilterSlotsAvailable(1);
            if (query.HidePassworded)
            {
                SteamMatchmaking.AddRequestLobbyListStringFilter(KitKeys.HasPassword, KitKeys.FlagOff, ELobbyComparison.k_ELobbyComparisonEqual);
            }
            if (query.HideInProgress)
            {
                SteamMatchmaking.AddRequestLobbyListStringFilter(KitKeys.Phase, KitKeys.PhaseInGame, ELobbyComparison.k_ELobbyComparisonNotEqual);
            }
            foreach (KeyValuePair<string, string> filter in query.SettingEquals)
            {
                SteamMatchmaking.AddRequestLobbyListStringFilter(SettingCodec.ToLobbyKey(filter.Key), filter.Value ?? "",
                    ELobbyComparison.k_ELobbyComparisonEqual);
            }
            foreach ((string key, NumericCompare op, int value) filter in query.SettingNumeric)
            {
                SteamMatchmaking.AddRequestLobbyListNumericalFilter(SettingCodec.ToLobbyKey(filter.key), filter.value, ToSteam(filter.op));
            }
            SteamMatchmaking.AddRequestLobbyListResultCountFilter(Math.Max(1, Math.Min(MaxResultsLimit, query.MaxResults)));
            return SteamMatchmaking.RequestLobbyList();
        }

        // Steam returns lobby data with list results, so no RequestLobbyData is needed (03 §8.3).
        private void OnLobbyList(LobbyMatchList_t result, bool ioFailure)
        {
            if (pendingDone == null) return;
            Action<KitResult<IReadOnlyList<LobbySummary>>> done = TakePending(false);
            if (ioFailure)
            {
                KitLog.Warning("RequestLobbyList failed: I/O failure.");
                SteamPlatform.Raise(done, Fail(KitError.Timeout, "I/O failure"), "lobby list done");
                return;
            }
            int count = (int)Math.Min(result.m_nLobbiesMatching, (uint)MaxResultsLimit);
            var list = new List<LobbySummary>(count);
            for (int i = 0; i < count; i++)
            {
                CSteamID lobby = SteamMatchmaking.GetLobbyByIndex(i);
                if (!SteamIds.IsUsable(lobby) || !reader.IsThisGame(lobby)) continue;
                list.Add(reader.Read(lobby, true));
            }
            KitLog.Debug("Lobby list: " + list.Count + " of " + result.m_nLobbiesMatching + " lobbies kept.");
            SteamPlatform.Raise(done, KitResult<IReadOnlyList<LobbySummary>>.From(list), "lobby list done");
        }

        // 01 §4.3: filter keys must be schema keys that are published to lobby metadata.
        private bool ValidateSettingFilters(LobbyBrowserQuery query, out string invalid)
        {
            foreach (KeyValuePair<string, string> filter in query.SettingEquals)
            {
                if (!IsPublishedKey(filter.Key))
                {
                    invalid = filter.Key ?? "";
                    return false;
                }
            }
            foreach ((string key, NumericCompare op, int value) filter in query.SettingNumeric)
            {
                if (!IsPublishedKey(filter.key))
                {
                    invalid = filter.key ?? "";
                    return false;
                }
            }
            invalid = null;
            return true;
        }

        private bool IsPublishedKey(string key)
        {
            return schema != null && schema.TryGet(key, out SettingDefinition def) && def.PublishToLobby && !def.HostOnly;
        }

        private Action<KitResult<IReadOnlyList<LobbySummary>>> TakePending(bool cancelCall)
        {
            Action<KitResult<IReadOnlyList<LobbySummary>>> done = pendingDone;
            if (cancelCall) listCall?.Cancel();
            pendingDone = null;
            deadline = float.NaN;
            return done;
        }

        private static KitResult<IReadOnlyList<LobbySummary>> Fail(KitError error, string detail)
        {
            return KitResult<IReadOnlyList<LobbySummary>>.Fail(error, detail);
        }

        private static ELobbyDistanceFilter ToSteam(LobbyDistance distance)
        {
            switch (distance)
            {
                case LobbyDistance.Close: return ELobbyDistanceFilter.k_ELobbyDistanceFilterClose;
                case LobbyDistance.Far: return ELobbyDistanceFilter.k_ELobbyDistanceFilterFar;
                case LobbyDistance.Worldwide: return ELobbyDistanceFilter.k_ELobbyDistanceFilterWorldwide;
                default: return ELobbyDistanceFilter.k_ELobbyDistanceFilterDefault;
            }
        }

        // The lobby's value is compared against the given value (risky API list, 03 §18 item 8).
        private static ELobbyComparison ToSteam(NumericCompare compare)
        {
            switch (compare)
            {
                case NumericCompare.NotEqual: return ELobbyComparison.k_ELobbyComparisonNotEqual;
                case NumericCompare.Less: return ELobbyComparison.k_ELobbyComparisonLessThan;
                case NumericCompare.LessOrEqual: return ELobbyComparison.k_ELobbyComparisonEqualToOrLessThan;
                case NumericCompare.Greater: return ELobbyComparison.k_ELobbyComparisonGreaterThan;
                case NumericCompare.GreaterOrEqual: return ELobbyComparison.k_ELobbyComparisonEqualToOrGreaterThan;
                default: return ELobbyComparison.k_ELobbyComparisonEqual;
            }
        }

        private bool TryRegisterCallbacks()
        {
            if (callbacksRegistered) return true;
            if (disposed) return false;
            if (!SteamPlatform.IsInitialized)
            {
                if (!registrationWarned) KitLog.Warning("SteamPublicLobbyBrowser: Steam is not initialized; callbacks are registered once it is.");
                registrationWarned = true;
                return false;
            }
            listCall = CallResult<LobbyMatchList_t>.Create(OnLobbyList);
            callbacksRegistered = true;
            if (registrationWarned) KitLog.Info("SteamPublicLobbyBrowser: Steam callbacks registered late.");
            return true;
        }
    }
}

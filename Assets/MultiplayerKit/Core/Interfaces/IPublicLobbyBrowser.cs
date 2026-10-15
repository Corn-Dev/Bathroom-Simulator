// Public lobby search with distance, region, fullness, password, in-progress and published-setting filters.
// Design: 01 §4.3; Steam implementation 03 §8.3 (RequestLobbyList, KitTimeouts.LobbyList).
// Setting filter keys are schema keys WITHOUT the "s." prefix; keys that are not in the schema or not
// PublishToLobby fail the request with InvalidArgument. The implementation always adds hidden filters
// k.game == adapter.GameId, k.proto == KitVersion.Protocol and s.privacy == "public".

using System;
using System.Collections.Generic;

namespace MultiplayerKit
{
    public enum LobbyDistance { Close, Default, Far, Worldwide }

    public enum NumericCompare { Equal, NotEqual, Less, LessOrEqual, Greater, GreaterOrEqual }

    public sealed class LobbyBrowserQuery
    {
        public LobbyDistance Distance = LobbyDistance.Default;

        /// <summary>Null or "any" = no filter; else s.region equality.</summary>
        public string Region;

        public bool HideFull = true;
        public bool HidePassworded = false;
        public bool HideInProgress = false;

        /// <summary>Clamped to 1..50 by the implementation.</summary>
        public int MaxResults = 50;

        /// <summary>Schema key (no prefix) and canonical value; string equality.</summary>
        public readonly List<KeyValuePair<string, string>> SettingEquals = new List<KeyValuePair<string, string>>();

        /// <summary>Schema key (no prefix), comparison and integer value.</summary>
        public readonly List<(string key, NumericCompare op, int value)> SettingNumeric =
            new List<(string key, NumericCompare op, int value)>();
    }

    public interface IPublicLobbyBrowser
    {
        /// <summary>False when the adapter disables public lobbies; Request then fails with InvalidArgument.</summary>
        bool IsSupported { get; }

        bool IsRequesting { get; }

        /// <summary>Only one request is in flight; a new Request cancels the previous one (its done gets Cancelled).</summary>
        void Request(LobbyBrowserQuery query, Action<KitResult<IReadOnlyList<LobbySummary>>> done);

        /// <summary>A pending done is invoked with Cancelled.</summary>
        void Cancel();
    }
}

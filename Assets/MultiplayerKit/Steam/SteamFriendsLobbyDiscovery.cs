// Friends currently in a lobby of this game: friend scan (GetFriendGamePlayed, AppID match), RequestLobbyData per
// lobby, LobbyDataUpdate_t resolution, 5 s drop of unresolved lookups. Design: 01 §4.2, 03 §8.2, 06 S6.
// Entries holds unresolved placeholders (LobbyDataResolved == false) until each lobby's data arrives; the UI shows
// them as "Loading". Resolved lobbies without this game's k.game, and invite-only lobbies, are dropped (03 §8.2);
// incompatible proto/version lobbies are kept with IsCompatible == false. EntriesChanged and RefreshCompleted are
// raised from Flush (once per frame), never from inside Refresh.

using System;
using System.Collections.Generic;
using Steamworks;

namespace MultiplayerKit.Steam
{
    public sealed class SteamFriendsLobbyDiscovery : IFriendsLobbyDiscovery, ISteamFlushable, IDisposable
    {
        private static readonly IReadOnlyList<FriendLobbyEntry> NoEntries = new FriendLobbyEntry[0];

        private readonly SteamLobbySummaryReader reader;
        private readonly List<FriendLobbyEntry> entries = new List<FriendLobbyEntry>();
        private readonly Dictionary<ulong, int> pending = new Dictionary<ulong, int>();   // lobby → generation

        private Callback<LobbyDataUpdate_t> dataUpdate;
        private bool callbacksRegistered;
        private bool registrationWarned;
        private bool disposed;

        private IReadOnlyList<FriendLobbyEntry> snapshot = NoEntries;
        private int generation;
        private bool refreshing;
        private bool entriesDirty;
        private float deadline = float.NaN;   // set by the first Flush after Refresh

        /// <summary>schema supplies PublishedSettings (PublishToLobby keys); gameId/gameVersion decide IsCompatible.</summary>
        public SteamFriendsLobbyDiscovery(HostSettingsSchema schema, string gameId, string gameVersion)
        {
            reader = new SteamLobbySummaryReader(schema, gameId, gameVersion);
            TryRegisterCallbacks();
        }

        public bool IsRefreshing => refreshing;

        public IReadOnlyList<FriendLobbyEntry> Entries => snapshot;

        public event Action<IReadOnlyList<FriendLobbyEntry>> EntriesChanged;
        public event Action RefreshCompleted;

        /// <summary>
        /// Restarts the scan. Earlier lookups are discarded (generation bump). Without Steam the refresh completes
        /// empty at the next Flush.
        /// </summary>
        public void Refresh()
        {
            if (disposed) return;
            generation++;
            pending.Clear();
            entries.Clear();
            entriesDirty = true;
            refreshing = true;
            deadline = float.NaN;
            if (!SteamPlatform.IsInitialized || !TryRegisterCallbacks())
            {
                SteamPlatform.WarnOnce("Friends' games: Steam is not initialized.");
                return;
            }
            ScanFriends();
        }

        /// <summary>Stops listening; Entries keeps the last snapshot and RefreshCompleted is not raised.</summary>
        public void Stop()
        {
            generation++;
            pending.Clear();
            refreshing = false;
            deadline = float.NaN;
        }

        public void Flush(float now)
        {
            if (disposed) return;
            bool ready = SteamPlatform.IsInitialized && TryRegisterCallbacks();
            if (ready && refreshing && pending.Count > 0)
            {
                if (float.IsNaN(deadline)) deadline = now + KitTimeouts.FriendLobbyData;
                else if (now >= deadline) DropUnresolved();
            }
            if (entriesDirty)
            {
                entriesDirty = false;
                snapshot = entries.Count == 0 ? NoEntries : entries.ToArray();
                SteamPlatform.Raise(EntriesChanged, snapshot, "EntriesChanged");
            }
            if (refreshing && pending.Count == 0)
            {
                refreshing = false;
                deadline = float.NaN;
                SteamPlatform.Raise(RefreshCompleted, "RefreshCompleted");
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            callbacksRegistered = false;
            pending.Clear();
            refreshing = false;
            dataUpdate?.Dispose();
            dataUpdate = null;
        }

        // 03 §8.2 steps 2-4: immediate friends whose game is this AppID and whose lobby id is valid, one entry per lobby
        // (the first friend found names it; the summary carries the owner).
        private void ScanFriends()
        {
            uint appId = SteamPlatform.AppId;
            int count = SteamFriends.GetFriendCount(EFriendFlags.k_EFriendFlagImmediate);
            for (int i = 0; i < count; i++)
            {
                CSteamID friendId = SteamFriends.GetFriendByIndex(i, EFriendFlags.k_EFriendFlagImmediate);
                if (!SteamFriends.GetFriendGamePlayed(friendId, out FriendGameInfo_t game)) continue;
                if (game.m_gameID.AppID().m_AppId != appId || !SteamIds.IsUsable(game.m_steamIDLobby)) continue;

                ulong lobbyRaw = game.m_steamIDLobby.m_SteamID;
                if (pending.ContainsKey(lobbyRaw)) continue;
                if (!SteamMatchmaking.RequestLobbyData(game.m_steamIDLobby))
                {
                    KitLog.Debug("RequestLobbyData failed for a friend's lobby " + lobbyRaw + "; skipped.");
                    continue;
                }
                pending[lobbyRaw] = generation;
                entries.Add(FriendLobbyEntry.Pending(SteamIds.ToPlayer(friendId),
                    SteamFriends.GetFriendPersonaName(friendId), SteamIds.ToLobby(game.m_steamIDLobby)));
            }
            KitLog.Debug("Friends' games: " + entries.Count + " lobbies requested.");
        }

        // 03 §8.2 step 5. Member-data updates (member != lobby) are not lookup answers.
        private void OnDataUpdate(LobbyDataUpdate_t update)
        {
            ulong lobbyRaw = update.m_ulSteamIDLobby;
            if (update.m_ulSteamIDMember != lobbyRaw) return;
            if (!pending.TryGetValue(lobbyRaw, out int requestedIn) || requestedIn != generation) return;
            pending.Remove(lobbyRaw);

            int index = IndexOf(lobbyRaw);
            if (index < 0) return;
            entriesDirty = true;
            var lobby = new CSteamID(lobbyRaw);
            if (update.m_bSuccess == 0 || !reader.IsThisGame(lobby))
            {
                entries.RemoveAt(index);
                KitLog.Debug("Friends' games: lobby " + lobbyRaw + " dropped (no data or another game).");
                return;
            }
            LobbySummary summary = reader.Read(lobby, false);
            if (summary.Privacy == LobbyPrivacy.InviteOnly)
            {
                entries.RemoveAt(index);
                return;
            }
            FriendLobbyEntry placeholder = entries[index];
            entries[index] = new FriendLobbyEntry(placeholder.Friend, placeholder.FriendName, summary, true);
        }

        // 03 §8.2 step 6: lookups still unanswered KitTimeouts.FriendLobbyData after Refresh are dropped.
        private void DropUnresolved()
        {
            for (int i = entries.Count - 1; i >= 0; i--)
            {
                if (!entries[i].LobbyDataResolved) entries.RemoveAt(i);
            }
            KitLog.Debug("Friends' games: " + pending.Count + " lobby lookups timed out.");
            pending.Clear();
            entriesDirty = true;
        }

        private int IndexOf(ulong lobbyRaw)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].Lobby.Id.Value == lobbyRaw) return i;
            }
            return -1;
        }

        private bool TryRegisterCallbacks()
        {
            if (callbacksRegistered) return true;
            if (disposed) return false;
            if (!SteamPlatform.IsInitialized)
            {
                if (!registrationWarned) KitLog.Warning("SteamFriendsLobbyDiscovery: Steam is not initialized; callbacks are registered once it is.");
                registrationWarned = true;
                return false;
            }
            dataUpdate = Callback<LobbyDataUpdate_t>.Create(OnDataUpdate);
            callbacksRegistered = true;
            if (registrationWarned) KitLog.Info("SteamFriendsLobbyDiscovery: Steam callbacks registered late.");
            return true;
        }
    }
}

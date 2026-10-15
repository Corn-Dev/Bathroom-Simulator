// Friends currently in a lobby of this game, streamed while the platform resolves each lobby's data.
// Design: 01 §4.2; Steam implementation 03 §8.2 (GetFriendGamePlayed + RequestLobbyData, 5 s per lobby).
// The UI joins through SessionController.JoinLobby, never through this interface.

using System;
using System.Collections.Generic;

namespace MultiplayerKit
{
    public sealed class FriendLobbyEntry
    {
        public PlayerId Friend { get; }

        /// <summary>Never null.</summary>
        public string FriendName { get; }

        /// <summary>
        /// Never null. Filled once lobby data arrives; until <see cref="LobbyDataResolved"/> it is a
        /// placeholder that carries only the lobby Id (see <see cref="Pending"/>).
        /// </summary>
        public LobbySummary Lobby { get; }

        /// <summary>False while waiting on the platform lobby-data request.</summary>
        public bool LobbyDataResolved { get; }

        public FriendLobbyEntry(PlayerId friend, string friendName, LobbySummary lobby, bool lobbyDataResolved)
        {
            Friend = friend;
            FriendName = friendName ?? "";
            Lobby = lobby ?? throw new ArgumentNullException(nameof(lobby));
            LobbyDataResolved = lobbyDataResolved;
        }

        /// <summary>Unresolved entry: placeholder summary with only the lobby Id set (not compatible, no settings).</summary>
        public static FriendLobbyEntry Pending(PlayerId friend, string friendName, LobbyId lobby)
        {
            var placeholder = new LobbySummary(
                lobby, PlayerId.None, "", 0, 0, LobbyPrivacy.FriendsOnly, false,
                LobbyPhase.Room, false, false, null);
            return new FriendLobbyEntry(friend, friendName, placeholder, false);
        }
    }

    public interface IFriendsLobbyDiscovery
    {
        bool IsRefreshing { get; }

        /// <summary>Only friends whose game is this app and whose lobby id is valid; one entry per lobby.</summary>
        IReadOnlyList<FriendLobbyEntry> Entries { get; }

        /// <summary>Restarts; previous in-flight results are discarded.</summary>
        void Refresh();

        /// <summary>Stops listening; Entries keeps the last snapshot.</summary>
        void Stop();

        event Action<IReadOnlyList<FriendLobbyEntry>> EntriesChanged;

        /// <summary>All lookups resolved, or the KitTimeouts.FriendLobbyData timeout hit.</summary>
        event Action RefreshCompleted;
    }
}

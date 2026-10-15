// Invites, overlay join requests, the "+connect_lobby <id>" launch argument and rich presence.
// Design: 01 §4.4; Steam implementation 03 §8.4-§8.5; presence rules 02 §10 (SessionRules.PresenceJoinable).
// The service only reports join requests; SessionController decides what to do with them.

using System;

namespace MultiplayerKit
{
    public enum PresenceStatus { InMenu, InLobbyRoom, InGame }

    /// <summary>Rich presence snapshot. Equality is field-wise so the controller can dedupe updates.</summary>
    public readonly struct PresenceInfo : IEquatable<PresenceInfo>
    {
        public readonly PresenceStatus Status;

        /// <summary>None = not joinable (connect key cleared).</summary>
        public readonly LobbyId JoinableLobby;

        /// <summary>Members in the lobby, and the lobby limit; 0/0 in the menu.</summary>
        public readonly int PartySize, PartyMax;

        public PresenceInfo(PresenceStatus status, LobbyId joinableLobby, int partySize, int partyMax)
        {
            Status = status;
            JoinableLobby = joinableLobby;
            PartySize = partySize;
            PartyMax = partyMax;
        }

        public bool Equals(PresenceInfo other)
        {
            return Status == other.Status && JoinableLobby == other.JoinableLobby
                && PartySize == other.PartySize && PartyMax == other.PartyMax;
        }

        public override bool Equals(object obj) => obj is PresenceInfo o && Equals(o);

        public override int GetHashCode()
        {
            unchecked
            {
                int h = (int)Status;
                h = h * 397 ^ JoinableLobby.GetHashCode();
                h = h * 397 ^ PartySize;
                h = h * 397 ^ PartyMax;
                return h;
            }
        }

        public static bool operator ==(PresenceInfo a, PresenceInfo b) => a.Equals(b);
        public static bool operator !=(PresenceInfo a, PresenceInfo b) => !a.Equals(b);
    }

    public interface IInviteService
    {
        /// <summary>False if the platform or its overlay is unavailable, or the lobby id is not valid.</summary>
        bool OpenInviteOverlay(LobbyId lobby);

        bool OpenFriendsOverlay();

        /// <summary>"+connect_lobby &lt;id&gt;" from the command line. Returns true once, then clears.</summary>
        bool TryConsumeLaunchLobby(out LobbyId lobby);

        /// <summary>
        /// Overlay "Join Game" or invite accept while the game is running (GameLobbyJoinRequested_t), and
        /// GameRichPresenceJoinRequested_t carrying "+connect_lobby &lt;id&gt;".
        /// </summary>
        event Action<LobbyId> JoinRequested;

        /// <summary>Steam maps this to the "connect" and "steam_display" rich presence keys.</summary>
        void SetPresence(PresenceInfo info);

        void ClearPresence();
    }
}

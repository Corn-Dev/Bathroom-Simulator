// Pure joinability rules shared by the lobby flag and rich presence, so both agree in every phase.
// Design: 03 §3.2 (SetLobbyJoinable table), 01 §6.3 and 02 §10 step 2 (presence JoinableLobby rule).
// SessionController.UpdatePresence calls PresenceJoinable and adds no rule of its own; it publishes
// CurrentLobby when this returns true (None outside a lobby), else LobbyId.None.

namespace MultiplayerKit
{
    public static class SessionRules
    {
        /// <summary>
        /// Value for ILobbyService.SetJoinable: room → true; starting and inGame → joinInProgress. Fullness is not
        /// part of it; the platform enforces the member limit.
        /// </summary>
        public static bool ShouldBeJoinable(LobbyPhase phase, bool joinInProgress)
        {
            return phase == LobbyPhase.Room || joinInProgress;
        }

        /// <summary>
        /// Whether rich presence advertises the lobby ("Join Game"): privacy is not inviteOnly, there is a free
        /// slot (memberCount &lt; maxPlayers), and the lobby itself is joinable (ShouldBeJoinable). During starting
        /// with joinInProgress off this is false, matching the non-joinable lobby.
        /// </summary>
        public static bool PresenceJoinable(LobbyPhase phase, LobbyPrivacy privacy, int memberCount, int maxPlayers,
            bool joinInProgress)
        {
            return privacy != LobbyPrivacy.InviteOnly
                && memberCount < maxPlayers
                && ShouldBeJoinable(phase, joinInProgress);
        }
    }
}

// Kit timeout tunables, in seconds of unscaled time (the same clock the bootstrap passes to Tick/Flush).
// Design: 01 §6.3 (timeouts), 06 C7 (full list), 02 §2e/§2f/§3d, 03 §7/§8.
// Static fields, not constants, so EditMode tests can shorten them; restore them after each test.

namespace MultiplayerKit
{
    public static class KitTimeouts
    {
        /// <summary>Lobby Create or Join callback (Steam CallResult) and the Joining state deadline.</summary>
        public static float CreateJoin = 15f;

        /// <summary>Client: wait for the host's "authok" after sending "auth" in a passworded lobby.</summary>
        public static float AuthReply = 15f;

        /// <summary>Host: a member who joined a passworded room must send a valid "auth" within this time.</summary>
        public static float HostAuthDeadline = 60f;

        /// <summary>Host before go-live, and client NGO connect plus scene sync.</summary>
        public static float StartingGame = 60f;

        /// <summary>Host world readiness for adapters implementing IGameReadyGate.</summary>
        public static float WorldReadySeconds = 180f;

        /// <summary>Avatar fetch; done is invoked with null when it expires.</summary>
        public static float Avatar = 10f;

        /// <summary>Friends list: per-lobby data request; unresolved entries are dropped.</summary>
        public static float FriendLobbyData = 5f;

        /// <summary>Public browser lobby list request.</summary>
        public static float LobbyList = 15f;
    }
}

// CSteamID <-> platform-neutral LobbyId/PlayerId. The only place CSteamID meets Core types. Internal, so no public
// member of the Steam assembly accepts or returns CSteamID. Design: 03 §2, 06 S2.
// Nil and invalid CSteamIDs map to None, and None maps to CSteamID.Nil. IsValid() rejects ids with an invalid
// universe or account type, so a garbage "+connect_lobby" number fails as LobbyNotFound before any Steam call.

using Steamworks;

namespace MultiplayerKit.Steam
{
    internal static class SteamIds
    {
        public static CSteamID ToSteam(LobbyId id)
        {
            return new CSteamID(id.Value);
        }

        public static CSteamID ToSteam(PlayerId id)
        {
            return new CSteamID(id.Value);
        }

        public static LobbyId ToLobby(CSteamID cs)
        {
            return IsUsable(cs) ? new LobbyId(cs.m_SteamID) : LobbyId.None;
        }

        public static PlayerId ToPlayer(CSteamID cs)
        {
            return IsUsable(cs) ? new PlayerId(cs.m_SteamID) : PlayerId.None;
        }

        /// <summary>Callback structs carry raw ulong ids (m_ulSteamIDLobby, m_ulSteamIDUserChanged, ...).</summary>
        public static LobbyId ToLobby(ulong raw)
        {
            return ToLobby(new CSteamID(raw));
        }

        public static PlayerId ToPlayer(ulong raw)
        {
            return ToPlayer(new CSteamID(raw));
        }

        /// <summary>Not Nil and CSteamID.IsValid().</summary>
        public static bool IsUsable(CSteamID cs)
        {
            return cs != CSteamID.Nil && cs.IsValid();
        }
    }
}

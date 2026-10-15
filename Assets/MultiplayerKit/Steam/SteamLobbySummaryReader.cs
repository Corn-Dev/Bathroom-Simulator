// Supplies Steam's lobby getters to Core's pure LobbySummaryParser, for the friends list and the public browser.
// Design: 03 §8.2 (field rules), §17 (parser split), 06 S8. Internal because it takes CSteamID.
// Member count: lobby-list results pass GetNumLobbyMembers (Steam returns live data with list results); friends-list
// reads pass 0 so the parser falls back to the owner-written k.count (GetNumLobbyMembers is unreliable for
// non-members after RequestLobbyData, 03 §4). Every value is untrusted text; the parser never throws on it.

using Steamworks;

namespace MultiplayerKit.Steam
{
    internal sealed class SteamLobbySummaryReader
    {
        private readonly HostSettingsSchema schema;
        private readonly string gameId;
        private readonly string gameVersion;

        /// <summary>schema may be null (no published settings are read).</summary>
        internal SteamLobbySummaryReader(HostSettingsSchema schema, string gameId, string gameVersion)
        {
            this.schema = schema;
            this.gameId = gameId ?? "";
            this.gameVersion = gameVersion ?? "";
        }

        /// <summary>
        /// k.game equals this game's id. Lobbies of other games on a shared AppID (480 in development) and non-kit
        /// lobbies are dropped by the callers before Read (03 §8.2, §8.3).
        /// </summary>
        internal bool IsThisGame(CSteamID lobby)
        {
            if (gameId.Length == 0 || !SteamPlatform.IsInitialized) return false;
            return string.Equals(SteamMatchmaking.GetLobbyData(lobby, KitKeys.Game), gameId, System.StringComparison.Ordinal);
        }

        /// <summary>Called from Steam callbacks; without Steam the summary carries only the id (not compatible).</summary>
        internal LobbySummary Read(CSteamID lobby, bool fromLobbyList)
        {
            if (!SteamPlatform.IsInitialized)
            {
                return LobbySummaryParser.Parse(SteamIds.ToLobby(lobby), _ => "", 0, 0, schema, gameId, gameVersion);
            }
            int memberCount = fromLobbyList ? SteamMatchmaking.GetNumLobbyMembers(lobby) : 0;
            int memberLimit = SteamMatchmaking.GetLobbyMemberLimit(lobby);
            return LobbySummaryParser.Parse(SteamIds.ToLobby(lobby), key => SteamMatchmaking.GetLobbyData(lobby, key),
                memberCount, memberLimit, schema, gameId, gameVersion);
        }
    }
}

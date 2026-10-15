// Pure parser from a lobby's metadata to a LobbySummary, shared by the friends list and the public browser.
// Design: 03 §17 (split out of SteamLobbySummaryReader so it is unit-testable), 03 §8.2 (field rules),
// 01 §7 (IsCompatible), 01 §5.1 (PublishToLobby). Every value read here is untrusted text written by another
// client; nothing throws on bad data. Dropping non-kit lobbies (k.game missing or different) is the caller's job.

using System;
using System.Collections.Generic;
using System.Globalization;

namespace MultiplayerKit
{
    public static class LobbySummaryParser
    {
        private static readonly string ProtoText = KitVersion.Protocol.ToString(CultureInfo.InvariantCulture);

        /// <summary>
        /// Builds the summary of one lobby (03 §8.2):
        /// Name = s.lobbyName (control characters stripped, cut to the lobbyName MaxLength); Owner = k.owner;
        /// MemberCount = memberCount when &gt; 0 (live count of a lobby-list result), else k.count, else 0;
        /// MaxMembers = memberLimit when &gt; 0, else s.maxPlayers, else 0; Privacy = s.privacy (unknown →
        /// FriendsOnly); HasPassword = k.hasPw == "1"; Phase = k.phase (unknown → Room); JoinInProgress =
        /// s.joinInProgress == "1"; IsCompatible = k.game, k.proto and k.ver equal the local values;
        /// PublishedSettings = every non-empty s.&lt;key&gt; whose schema definition has PublishToLobby.
        /// getData returns "" (or null) for a missing key. schema may be null (no published settings).
        /// </summary>
        public static LobbySummary Parse(LobbyId lobby, Func<string, string> getData, int memberCount, int memberLimit,
            HostSettingsSchema schema, string gameId, string gameVersion)
        {
            if (getData == null) throw new ArgumentNullException(nameof(getData));

            string game = Get(getData, KitKeys.Game);
            bool compatible = game.Length > 0
                && string.Equals(game, gameId, StringComparison.Ordinal)
                && string.Equals(Get(getData, KitKeys.Proto), ProtoText, StringComparison.Ordinal)
                && string.Equals(Get(getData, KitKeys.Version), gameVersion, StringComparison.Ordinal);

            PlayerId.TryParse(Get(getData, KitKeys.Owner), out PlayerId owner);

            int members = memberCount > 0 ? memberCount : ParseCount(Get(getData, KitKeys.Count));
            int maxMembers = memberLimit > 0
                ? memberLimit
                : ParseCount(Get(getData, SettingCodec.ToLobbyKey(CoreSettingKeys.MaxPlayers)));

            // Both parsers return their documented fallback (FriendsOnly, Room) when the text is unknown.
            LobbyEnums.TryParsePrivacy(Get(getData, SettingCodec.ToLobbyKey(CoreSettingKeys.Privacy)), out LobbyPrivacy privacy);
            LobbyEnums.TryParsePhase(Get(getData, KitKeys.Phase), out LobbyPhase phase);

            bool hasPassword = Get(getData, KitKeys.HasPassword) == KitKeys.FlagOn;
            bool joinInProgress = Get(getData, SettingCodec.ToLobbyKey(CoreSettingKeys.JoinInProgress)) == KitKeys.FlagOn;
            string name = SettingCodec.SanitizeText(Get(getData, SettingCodec.ToLobbyKey(CoreSettingKeys.LobbyName)),
                NameMaxLength(schema));

            return new LobbySummary(lobby, owner, name, members, maxMembers, privacy, hasPassword, phase,
                joinInProgress, compatible, ReadPublished(getData, schema));
        }

        private static string Get(Func<string, string> getData, string key)
        {
            return getData(key) ?? "";
        }

        // Decimal digits only; anything else (including negative numbers) is 0, which the UI shows as unknown.
        private static int ParseCount(string text)
        {
            return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int n) ? n : 0;
        }

        private static int NameMaxLength(HostSettingsSchema schema)
        {
            if (schema != null && schema.TryGet(CoreSettingKeys.LobbyName, out SettingDefinition def) && def.Kind == SettingKind.Text)
            {
                return def.MaxLength;
            }
            return CoreSettings.LobbyNameMaxLength;
        }

        // Values are kept as published (sanitized, not normalized): a lobby of another version may use values this
        // schema does not know, and the UI skips those instead of showing a misleading default.
        private static IReadOnlyDictionary<string, string> ReadPublished(Func<string, string> getData, HostSettingsSchema schema)
        {
            var published = new Dictionary<string, string>(StringComparer.Ordinal);
            if (schema == null) return published;

            foreach (SettingDefinition def in schema.Definitions)
            {
                if (!def.PublishToLobby || def.HostOnly) continue;
                string raw = Get(getData, SettingCodec.ToLobbyKey(def.Key));
                if (raw.Length == 0) continue;
                published[def.Key] = SettingCodec.SanitizeText(raw, SettingDefinition.MaxTextLength);
            }
            return published;
        }
    }
}

// Every lobby metadata and member-data key the kit owns, the "k." / "s." prefixes and the flag values.
// Design: 01 §7 (kit keys, chat protocol), 03 §4 (lobby keys, Steam-implementation keys), 03 §5 (k.ready).
// Phase values alias LobbyEnums so the wire strings are defined exactly once. k.owner and k.count are written
// and read only by the Steam implementation and the summary parser; SessionController never touches them.

namespace MultiplayerKit
{
    public static class KitKeys
    {
        /// <summary>Prefix of every kit-owned key.</summary>
        public const string KitPrefix = "k.";

        /// <summary>Prefix of every published setting value: "s." + schema key (01 §5.4).</summary>
        public const string SettingPrefix = "s.";

        // Lobby-level keys, written by the owner (01 §7, 03 §4).

        /// <summary>adapter.GameId, written at create. A lobby without it is not a kit lobby.</summary>
        public const string Game = "k.game";

        /// <summary>KitVersion.Protocol as decimal, written at create.</summary>
        public const string Proto = "k.proto";

        /// <summary>adapter.GameVersion, written at create.</summary>
        public const string Version = "k.ver";

        /// <summary>PhaseRoom / PhaseStarting / PhaseInGame.</summary>
        public const string Phase = "k.phase";

        /// <summary>Host PlayerId as decimal, written when the phase becomes inGame; clients connect NGO to it.</summary>
        public const string Server = "k.server";

        /// <summary>FlagOn when the trimmed password is non-empty, else FlagOff. The password itself is never published.</summary>
        public const string HasPassword = "k.hasPw";

        /// <summary>FlagOn when the session resumes a world save, else FlagOff; written at create.</summary>
        public const string Resume = "k.resume";

        // Member-level key, written by each member for itself (03 §5).

        /// <summary>FlagOn / FlagOff; reset to FlagOff right after entering a lobby.</summary>
        public const string Ready = "k.ready";

        // Steam-implementation keys (03 §4): written by SteamLobbyService, read by LobbySummaryParser.

        /// <summary>Owner PlayerId as decimal, for LobbySummary.Owner on non-members.</summary>
        public const string Owner = "k.owner";

        /// <summary>Member count as decimal, the friends-list fallback for LobbySummary.MemberCount.</summary>
        public const string Count = "k.count";

        // Values.

        public const string PhaseRoom = LobbyEnums.PhaseRoom;
        public const string PhaseStarting = LobbyEnums.PhaseStarting;
        public const string PhaseInGame = LobbyEnums.PhaseInGame;

        /// <summary>Value of every boolean kit key and of Bool settings when true.</summary>
        public const string FlagOn = "1";

        /// <summary>Value of every boolean kit key and of Bool settings when false.</summary>
        public const string FlagOff = "0";

        /// <summary>"s." + key. The key is not validated here; schema keys are validated by SettingDefinition.</summary>
        public static string SettingKey(string key)
        {
            return SettingPrefix + key;
        }
    }
}

// Optional co-op settings a game adds to its BuildSettingsSchema list only if it uses them.
// Design: 01 §5.5 (table: keys, kinds, defaults, ranges, flags, groups). Core reads only autosaveMinutes and
// worldSeed, by key through IReadOnlyHostSettings.Has; every other value is meaning-free to Core. Games may
// relabel choices with WithChoices; choice values stay stable because saves store them.

namespace MultiplayerKit
{
    public static class CommonSettings
    {
        /// <summary>Keys of the optional settings.</summary>
        public static class Keys
        {
            public const string Difficulty = "difficulty";
            public const string WorldSize = "worldSize";

            /// <summary>Read by Core: 0 is replaced by a random positive seed at Start (host).</summary>
            public const string WorldSeed = "worldSeed";

            public const string FriendlyFire = "friendlyFire";
            public const string VoiceChat = "voiceChat";
            public const string TextChat = "textChat";
            public const string SharedEconomy = "sharedEconomy";
            public const string LateJoinerCatchUp = "lateJoinCatchUp";
            public const string GameSpeed = "gameSpeed";
            public const string AllowCheats = "allowCheats";
            public const string IdleKickMinutes = "idleKickMinutes";

            /// <summary>Read by Core: autosave interval in minutes of InGame time; 0 = off.</summary>
            public const string AutosaveMinutes = "autosaveMinutes";
        }

        public const string GroupGameplay = "Gameplay";
        public const string GroupWorld = "World";
        public const string GroupSocial = "Social";
        public const string GroupAdvanced = "Advanced";
        public const string GroupSaves = "Saves";

        // Each property returns a new immutable definition, so no static state is shared between schemas.

        /// <summary>Choice easy / normal / hard / extreme, default normal; published.</summary>
        public static SettingDefinition Difficulty => SettingDefinition.Choice(Keys.Difficulty, "Difficulty", "normal",
            new[]
            {
                new SettingChoice("easy", "Easy"),
                new SettingChoice("normal", "Normal"),
                new SettingChoice("hard", "Hard"),
                new SettingChoice("extreme", "Extreme"),
            },
            group: GroupGameplay, hostOnly: false, editableAfterStart: false, publish: true);

        /// <summary>Choice small / medium / large / huge, default medium; published.</summary>
        public static SettingDefinition WorldSize => SettingDefinition.Choice(Keys.WorldSize, "World size", "medium",
            new[]
            {
                new SettingChoice("small", "Small"),
                new SettingChoice("medium", "Medium"),
                new SettingChoice("large", "Large"),
                new SettingChoice("huge", "Huge"),
            },
            group: GroupWorld, hostOnly: false, editableAfterStart: false, publish: true);

        /// <summary>Int 0..2147483647, default 0 (random at Start).</summary>
        public static SettingDefinition WorldSeed => SettingDefinition.Int(Keys.WorldSeed, "World seed", 0, 0, int.MaxValue, 1,
            group: GroupWorld, hostOnly: false, editableAfterStart: false, publish: false);

        /// <summary>Bool, default false; editable in game; published.</summary>
        public static SettingDefinition FriendlyFire => SettingDefinition.Bool(Keys.FriendlyFire, "Friendly fire", false,
            group: GroupGameplay, hostOnly: false, editableAfterStart: true, publish: true);

        /// <summary>Choice off / proximity / global, default global; editable in game; published.</summary>
        public static SettingDefinition VoiceChat => SettingDefinition.Choice(Keys.VoiceChat, "Voice chat", "global",
            new[]
            {
                new SettingChoice("off", "Off"),
                new SettingChoice("proximity", "Proximity"),
                new SettingChoice("global", "Global"),
            },
            group: GroupSocial, hostOnly: false, editableAfterStart: true, publish: true);

        /// <summary>Bool, default true; editable in game.</summary>
        public static SettingDefinition TextChat => SettingDefinition.Bool(Keys.TextChat, "Text chat", true,
            group: GroupSocial, hostOnly: false, editableAfterStart: true, publish: false);

        /// <summary>Bool, default true; published.</summary>
        public static SettingDefinition SharedEconomy => SettingDefinition.Bool(Keys.SharedEconomy, "Shared economy", true,
            group: GroupGameplay, hostOnly: false, editableAfterStart: false, publish: true);

        /// <summary>Bool, default true; editable in game.</summary>
        public static SettingDefinition LateJoinerCatchUp => SettingDefinition.Bool(Keys.LateJoinerCatchUp, "Late joiner catch-up", true,
            group: GroupGameplay, hostOnly: false, editableAfterStart: true, publish: false);

        /// <summary>Float 0.5..2.0 step 0.25, default 1.0; editable in game.</summary>
        public static SettingDefinition GameSpeed => SettingDefinition.Float(Keys.GameSpeed, "Game speed", 1f, 0.5f, 2f, 0.25f,
            group: GroupGameplay, hostOnly: false, editableAfterStart: true, publish: false);

        /// <summary>Bool, default false; editable in game; published.</summary>
        public static SettingDefinition AllowCheats => SettingDefinition.Bool(Keys.AllowCheats, "Allow cheats", false,
            group: GroupAdvanced, hostOnly: false, editableAfterStart: true, publish: true);

        /// <summary>Int 0..60, default 0 (off); host-only; editable in game.</summary>
        public static SettingDefinition IdleKickMinutes => SettingDefinition.Int(Keys.IdleKickMinutes, "Idle kick (minutes)", 0, 0, 60, 1,
            group: GroupAdvanced, hostOnly: true, editableAfterStart: true, publish: false);

        /// <summary>Int 0..60, default 5 (0 = off); host-only; editable in game.</summary>
        public static SettingDefinition AutosaveMinutes => SettingDefinition.Int(Keys.AutosaveMinutes, "Autosave (minutes)", 5, 0, 60, 1,
            group: GroupSaves, hostOnly: true, editableAfterStart: true, publish: false);
    }
}

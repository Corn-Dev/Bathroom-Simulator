// The seven settings every kit session has (lobbyName, maxPlayers, privacy, password, joinInProgress, region,
// requireReady) and their key constants. Design: 01 §5.3 (table, password normalization), 03 §3.1 (privacy
// values). HostSettingsSchema.Build always merges these; an adapter may redefine label, group, default and
// range of a core key, but not its kind or flags.

using System;
using System.Collections.Generic;

namespace MultiplayerKit
{
    /// <summary>Keys of the Core-understood settings (01 §5.3). Stored in metadata as "s." + key.</summary>
    public static class CoreSettingKeys
    {
        public const string LobbyName = "lobbyName";
        public const string MaxPlayers = "maxPlayers";
        public const string Privacy = "privacy";

        /// <summary>Host-only. Never in lobby metadata or the save snapshot; only "k.hasPw" is published.</summary>
        public const string Password = "password";

        public const string JoinInProgress = "joinInProgress";
        public const string Region = "region";
        public const string RequireReady = "requireReady";

        /// <summary>Core order, which is also the order of the core rows in the settings panel.</summary>
        public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(new[]
        {
            LobbyName, MaxPlayers, Privacy, Password, JoinInProgress, Region, RequireReady,
        });

        public static bool IsCoreKey(string key)
        {
            for (int i = 0; i < All.Count; i++)
            {
                if (string.Equals(All[i], key, StringComparison.Ordinal)) return true;
            }
            return false;
        }
    }

    /// <summary>Factories for the core definitions with their 01 §5.3 kinds, defaults, ranges and flags.</summary>
    public static class CoreSettings
    {
        /// <summary>Group of every core row; the UI shows it as a section header.</summary>
        public const string Group = "Lobby";

        public const int LobbyNameMaxLength = 48;
        public const int PasswordMaxLength = 32;

        /// <summary>Steam's lobby member limit, the upper bound of IGameAdapter.MaxPlayersCap.</summary>
        public const int MaxPlayersLimit = 250;

        /// <summary>region value that means "no region"; browsers treat it as no filter.</summary>
        public const string RegionAny = "any";

        /// <summary>Text, max 48, empty by default (the controller names it "{name}'s game" at create).</summary>
        public static SettingDefinition LobbyName()
        {
            return SettingDefinition.Text(CoreSettingKeys.LobbyName, "Lobby name", "", maxLength: LobbyNameMaxLength,
                group: Group, hostOnly: false, editableAfterStart: true, publish: true);
        }

        /// <summary>Int 1..cap, default cap. Applied with ILobbyService.SetMaxMembers.</summary>
        public static SettingDefinition MaxPlayers(int maxPlayersCap)
        {
            if (maxPlayersCap < 1 || maxPlayersCap > MaxPlayersLimit)
            {
                throw new ArgumentException("MaxPlayersCap must be 1.." + MaxPlayersLimit + ", was " + maxPlayersCap + ".",
                    nameof(maxPlayersCap));
            }
            return SettingDefinition.Int(CoreSettingKeys.MaxPlayers, "Max players", maxPlayersCap, 1, maxPlayersCap, 1,
                group: Group, hostOnly: false, editableAfterStart: false, publish: true);
        }

        /// <summary>Choice inviteOnly / friendsOnly / public, default friendsOnly (LobbyEnums wire strings).</summary>
        public static SettingDefinition Privacy()
        {
            var choices = new[]
            {
                new SettingChoice(LobbyEnums.PrivacyInviteOnly, "Invite only"),
                new SettingChoice(LobbyEnums.PrivacyFriendsOnly, "Friends only"),
                new SettingChoice(LobbyEnums.PrivacyPublic, "Public"),
            };
            return SettingDefinition.Choice(CoreSettingKeys.Privacy, "Privacy", LobbyEnums.PrivacyFriendsOnly, choices,
                group: Group, hostOnly: false, editableAfterStart: true, publish: false);
        }

        /// <summary>Text, max 32, host-only, locked after start. Empty means no password.</summary>
        public static SettingDefinition Password()
        {
            return SettingDefinition.Text(CoreSettingKeys.Password, "Password", "", maxLength: PasswordMaxLength,
                group: Group, hostOnly: true, editableAfterStart: false, publish: false);
        }

        /// <summary>Bool, default true. Forced false by the schema when the JoinInProgress capability is off.</summary>
        public static SettingDefinition JoinInProgress()
        {
            return SettingDefinition.Bool(CoreSettingKeys.JoinInProgress, "Join in progress", true,
                group: Group, hostOnly: false, editableAfterStart: true, publish: true);
        }

        /// <summary>Choice any / na / sa / eu / asia / oce / me / af, default any. Browser filter only.</summary>
        public static SettingDefinition Region()
        {
            var choices = new[]
            {
                new SettingChoice(RegionAny, "Any"),
                new SettingChoice("na", "North America"),
                new SettingChoice("sa", "South America"),
                new SettingChoice("eu", "Europe"),
                new SettingChoice("asia", "Asia"),
                new SettingChoice("oce", "Oceania"),
                new SettingChoice("me", "Middle East"),
                new SettingChoice("af", "Africa"),
            };
            return SettingDefinition.Choice(CoreSettingKeys.Region, "Region", RegionAny, choices,
                group: Group, hostOnly: false, editableAfterStart: true, publish: true);
        }

        /// <summary>Bool, default false. Start needs every non-owner member ready when true.</summary>
        public static SettingDefinition RequireReady()
        {
            return SettingDefinition.Bool(CoreSettingKeys.RequireReady, "Require ready", false,
                group: Group, hostOnly: false, editableAfterStart: false, publish: false);
        }

        /// <summary>All seven, in CoreSettingKeys.All order, before any adapter override or capability rule.</summary>
        public static IReadOnlyList<SettingDefinition> CreateAll(int maxPlayersCap)
        {
            return Array.AsReadOnly(new[]
            {
                LobbyName(),
                MaxPlayers(maxPlayersCap),
                Privacy(),
                Password(),
                JoinInProgress(),
                Region(),
                RequireReady(),
            });
        }
    }
}

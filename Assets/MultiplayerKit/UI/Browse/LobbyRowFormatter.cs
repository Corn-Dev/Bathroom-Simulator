// Row rules shared by both lobby lists (05 §6): why a row cannot be joined (LobbyJoinBlock, in the 05 §6 order),
// its status text, player count, display name and the published-settings line. The save-slot rows reuse the
// settings-line rule on a settings snapshot (05 §8) through the dictionary overload. Pure static functions,
// unit-tested (05 §11). Core stays the authority: every join still goes through SessionController.JoinLobby,
// which checks again. Design: 05 §6, §7, §8, §11; 02 §3b.3; 06 U32.

using System;
using System.Collections.Generic;

namespace MultiplayerKit.UI
{
    /// <summary>Why a listed lobby cannot be joined now. None = joinable.</summary>
    public enum LobbyJoinBlock { None, Loading, Incompatible, Full, InProgress, InviteOnly }

    public static class LobbyRowFormatter
    {
        public const string LoadingText = "Loading\u2026";
        public const string IncompatibleText = "Different version";
        public const string FullText = "Full";
        public const string InProgressText = "In progress";
        public const string InviteOnlyText = "Invite only \u2014 ask for an invite";
        public const string UnnamedGameText = "Unnamed game";

        /// <summary>Items in a settings line unless the caller asks for another number.</summary>
        public const int DefaultSettingsItems = 3;

        private const string OwnerGameSuffix = "'s game";

        /// <summary>
        /// 05 §6 order: not resolved → Loading; !IsCompatible → Incompatible; IsFull → Full; Phase != Room without
        /// JoinInProgress → InProgress; an InviteOnly lobby seen through a friend → InviteOnly (Steam private lobbies
        /// are joined by invite, so a Join there would fail). Otherwise None: LobbySummary.IsJoinableNow plus the two
        /// UI-only reasons.
        /// </summary>
        public static LobbyJoinBlock BlockFor(LobbySummary s, bool resolved, bool fromFriendsList)
        {
            if (s == null || !resolved) return LobbyJoinBlock.Loading;
            if (!s.IsCompatible) return LobbyJoinBlock.Incompatible;
            if (s.IsFull) return LobbyJoinBlock.Full;
            if (s.Phase != LobbyPhase.Room && !s.JoinInProgress) return LobbyJoinBlock.InProgress;
            if (fromFriendsList && s.Privacy == LobbyPrivacy.InviteOnly) return LobbyJoinBlock.InviteOnly;
            return LobbyJoinBlock.None;
        }

        /// <summary>
        /// "Loading…", "Different version", "Full", "In progress", "Invite only — ask for an invite"; a joinable row
        /// shows its phase ("In lobby", "Starting", "In game").
        /// </summary>
        public static string StatusText(LobbyJoinBlock b, LobbyPhase phase)
        {
            switch (b)
            {
                case LobbyJoinBlock.Loading: return LoadingText;
                case LobbyJoinBlock.Incompatible: return IncompatibleText;
                case LobbyJoinBlock.Full: return FullText;
                case LobbyJoinBlock.InProgress: return InProgressText;
                case LobbyJoinBlock.InviteOnly: return InviteOnlyText;
                default: return UiFormat.Phase(phase);
            }
        }

        /// <summary>"2/4"; "" for null.</summary>
        public static string PlayerCount(LobbySummary s)
        {
            return s == null ? "" : UiFormat.PlayerCount(s.MemberCount, s.MaxMembers);
        }

        /// <summary>The lobby name, else "{ownerName}'s game", else "Unnamed game".</summary>
        public static string DisplayName(LobbySummary s, string ownerName)
        {
            if (s != null && !string.IsNullOrEmpty(s.Name)) return s.Name;
            return string.IsNullOrEmpty(ownerName) ? UnnamedGameText : ownerName + OwnerGameSuffix;
        }

        /// <summary>The settings line of a listed lobby, from its PublishedSettings.</summary>
        public static string SettingsLine(LobbySummary s, HostSettingsSchema schema, int maxItems = DefaultSettingsItems)
        {
            return s == null ? "" : SettingsLine(s.PublishedSettings, schema, maxItems);
        }

        /// <summary>
        /// Up to maxItems settings whose schema definition is published, Choice or Bool, and not region or privacy, in
        /// schema order, joined with " · ": Choice → the label of its value; Bool → the definition label when "1",
        /// nothing when "0". Keys and values this schema does not know are skipped silently (another game version).
        /// values are unprefixed keys: LobbySummary.PublishedSettings, or a save's SettingsSnapshot (05 §8), whose
        /// unpublished keys are skipped by the same rule, so a save row reads like the lobby row of its settings.
        /// </summary>
        public static string SettingsLine(IReadOnlyDictionary<string, string> values, HostSettingsSchema schema,
            int maxItems = DefaultSettingsItems)
        {
            if (values == null || values.Count == 0 || schema == null || maxItems <= 0) return "";

            var parts = new List<string>(maxItems);
            foreach (SettingDefinition def in schema.Definitions)
            {
                if (!IsSettingsLineKey(def) || !values.TryGetValue(def.Key, out string raw)) continue;
                string part = PartFor(def, raw);
                if (part == null) continue;
                parts.Add(part);
                if (parts.Count >= maxItems) break;
            }
            return UiFormat.Join(parts.ToArray());
        }

        private static bool IsSettingsLineKey(SettingDefinition def)
        {
            if (def == null || !def.PublishToLobby || def.HostOnly) return false;
            if (def.Kind != SettingKind.Choice && def.Kind != SettingKind.Bool) return false;
            return !string.Equals(def.Key, CoreSettingKeys.Region, StringComparison.Ordinal)
                && !string.Equals(def.Key, CoreSettingKeys.Privacy, StringComparison.Ordinal);
        }

        // null: nothing to show for this key (Bool off, or a value this schema does not know).
        private static string PartFor(SettingDefinition def, string raw)
        {
            if (def.Kind == SettingKind.Bool) return raw == KitKeys.FlagOn ? def.Label : null;
            for (int i = 0; i < def.Choices.Count; i++)
            {
                if (string.Equals(def.Choices[i].Value, raw, StringComparison.Ordinal)) return def.Choices[i].Label;
            }
            return null;
        }
    }
}

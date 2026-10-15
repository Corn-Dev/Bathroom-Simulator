// The merged, validated settings schema: core definitions first (CoreSettings), then the adapter's, with the
// capability rules applied. Design: 01 §5.2 (Build, Definitions, TryGet, Has, Groups), 01 §5.3 (core keys and
// capability effects), 01 §4.8 (GameCapabilities). Build throws ArgumentException on any programmer error and
// the bootstrap lets it propagate, so a bad schema fails at startup, never mid-session. Immutable once built.

using System;
using System.Collections.Generic;

namespace MultiplayerKit
{
    public sealed class HostSettingsSchema
    {
        private readonly IReadOnlyList<SettingDefinition> definitions;
        private readonly Dictionary<string, SettingDefinition> byKey;
        private readonly IReadOnlyList<string> groups;

        private HostSettingsSchema(List<SettingDefinition> merged)
        {
            definitions = merged.AsReadOnly();
            byKey = new Dictionary<string, SettingDefinition>(merged.Count, StringComparer.Ordinal);
            var groupList = new List<string>();
            var seenGroups = new HashSet<string>(StringComparer.Ordinal);
            foreach (SettingDefinition def in merged)
            {
                byKey.Add(def.Key, def);
                if (seenGroups.Add(def.Group)) groupList.Add(def.Group);
            }
            groups = groupList.AsReadOnly();
        }

        /// <summary>Core first (CoreSettingKeys.All order, adapter overrides in place), then adapter order.</summary>
        public IReadOnlyList<SettingDefinition> Definitions => definitions;

        /// <summary>Distinct Group values in first-appearance order ("" is the "General" group).</summary>
        public IEnumerable<string> Groups => groups;

        /// <summary>Ordinal key lookup; false for null or unknown keys.</summary>
        public bool TryGet(string key, out SettingDefinition def)
        {
            if (key == null)
            {
                def = null;
                return false;
            }
            return byKey.TryGetValue(key, out def);
        }

        public bool Has(string key)
        {
            return TryGet(key, out _);
        }

        /// <summary>
        /// Merges adapter.BuildSettingsSchema() with the core definitions and applies the capability rules:
        /// maxPlayers capped to 1..MaxPlayersCap, "public" removed from privacy without PublicLobbies,
        /// joinInProgress pinned to false without JoinInProgress. Throws ArgumentException on a null or duplicate
        /// definition, a core key redefined with another kind or other flags, an invalid MaxPlayersCap, a
        /// privacy choice that is not a privacy value, or a default the capability rules make impossible.
        /// </summary>
        public static HostSettingsSchema Build(IGameAdapter adapter)
        {
            if (adapter == null) throw new ArgumentNullException(nameof(adapter));

            int cap = adapter.MaxPlayersCap;
            if (cap < 1 || cap > CoreSettings.MaxPlayersLimit)
            {
                throw new ArgumentException("IGameAdapter.MaxPlayersCap must be 1.." + CoreSettings.MaxPlayersLimit + ", was " + cap + ".");
            }
            GameCapabilities caps = adapter.Capabilities;

            var merged = new List<SettingDefinition>(CoreSettings.CreateAll(cap));
            int coreCount = merged.Count;
            var seen = new HashSet<string>(StringComparer.Ordinal);

            IReadOnlyList<SettingDefinition> game = adapter.BuildSettingsSchema();
            if (game != null)
            {
                for (int i = 0; i < game.Count; i++)
                {
                    SettingDefinition def = game[i];
                    if (def == null)
                    {
                        throw new ArgumentException("BuildSettingsSchema returned a null definition at index " + i + ".");
                    }
                    if (!seen.Add(def.Key))
                    {
                        throw new ArgumentException("BuildSettingsSchema defines setting '" + def.Key + "' twice.");
                    }

                    int coreIndex = IndexOf(merged, def.Key, coreCount);
                    if (coreIndex < 0)
                    {
                        merged.Add(def);
                        continue;
                    }
                    CheckCoreOverride(merged[coreIndex], def);
                    merged[coreIndex] = def;
                }
            }

            ApplyMaxPlayersCap(merged, cap);
            ApplyPrivacyRule(merged, (caps & GameCapabilities.PublicLobbies) != 0);
            if ((caps & GameCapabilities.JoinInProgress) == 0) PinJoinInProgressOff(merged);

            return new HostSettingsSchema(merged);
        }

        // An adapter may change label, group, default and range of a core key, never its kind or flags (01 §5.3).
        private static void CheckCoreOverride(SettingDefinition core, SettingDefinition def)
        {
            if (def.Kind != core.Kind)
            {
                throw new ArgumentException("Core setting '" + core.Key + "' is " + core.Kind
                    + "; the adapter may not redefine it as " + def.Kind + ".");
            }
            if (def.HostOnly != core.HostOnly || def.EditableAfterStart != core.EditableAfterStart
                || def.PublishToLobby != core.PublishToLobby)
            {
                throw new ArgumentException("Core setting '" + core.Key + "': the adapter may not change HostOnly, "
                    + "EditableAfterStart or PublishToLobby.");
            }
        }

        private static void ApplyMaxPlayersCap(List<SettingDefinition> merged, int cap)
        {
            int i = IndexOf(merged, CoreSettingKeys.MaxPlayers, merged.Count);
            SettingDefinition def = merged[i];
            if (def.Min < 1 || def.Min > cap)
            {
                throw new ArgumentException("Setting 'maxPlayers': min must be 1.." + cap + " (MaxPlayersCap).");
            }
            // WithRange throws if the adapter's default is above the cap.
            if (def.Max > cap) merged[i] = def.WithRange(def.Min, cap);
        }

        private static void ApplyPrivacyRule(List<SettingDefinition> merged, bool publicLobbies)
        {
            int i = IndexOf(merged, CoreSettingKeys.Privacy, merged.Count);
            SettingDefinition def = merged[i];

            var kept = new List<SettingChoice>(def.Choices.Count);
            foreach (SettingChoice choice in def.Choices)
            {
                if (!LobbyEnums.TryParsePrivacy(choice.Value, out _))
                {
                    throw new ArgumentException("Setting 'privacy': choice '" + choice.Value + "' is not one of "
                        + LobbyEnums.PrivacyInviteOnly + ", " + LobbyEnums.PrivacyFriendsOnly + ", " + LobbyEnums.PrivacyPublic + ".");
                }
                if (!publicLobbies && choice.Value == LobbyEnums.PrivacyPublic) continue;
                kept.Add(choice);
            }

            if (kept.Count == def.Choices.Count) return;
            if (def.Default == LobbyEnums.PrivacyPublic)
            {
                throw new ArgumentException("Setting 'privacy': default is public but the adapter has no PublicLobbies capability.");
            }
            merged[i] = def.WithChoices(kept);
        }

        // Without the capability the value is pinned: no TrySet, lobby data or snapshot can switch it on.
        private static void PinJoinInProgressOff(List<SettingDefinition> merged)
        {
            int i = IndexOf(merged, CoreSettingKeys.JoinInProgress, merged.Count);
            SettingDefinition def = merged[i];
            merged[i] = SettingDefinition.Bool(def.Key, def.Label, false, def.Group,
                def.HostOnly, def.EditableAfterStart, def.PublishToLobby).AsFixed();
        }

        private static int IndexOf(List<SettingDefinition> list, string key, int count)
        {
            for (int i = 0; i < count; i++)
            {
                if (string.Equals(list[i].Key, key, StringComparison.Ordinal)) return i;
            }
            return -1;
        }
    }
}

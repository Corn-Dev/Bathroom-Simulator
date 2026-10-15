// Pure filter state → LobbyBrowserQuery mapping for the public browser (05 §7), plus the option list of every filter
// control, so indices and values come from one place and the presenter never hand-builds strings. Static, unit-tested
// (05 §11). Distance: Close, Nearby (LobbyDistance.Default, the default), Far, Worldwide. Region: "Any region" (null,
// no filter) plus every region choice except "any", with its schema label. Published-setting filters: one per
// PublishToLobby Choice or Bool definition except the Core keys handled by other controls or meaningless as filters
// (region, privacy, lobbyName, maxPlayers, joinInProgress); index 0 = Any; Choice → SettingEquals(key, value);
// Bool "On"/"Off" → SettingEquals(key, "1"/"0"). Int filters come only from BrowserFilterState.SettingNumeric, for a
// game's own browser (the kit UI generates none, 05 §7), and pass to SettingNumeric if the key is a published Int.
// Design: 05 §7, §11; 02 §3c.2; 01 §4.3; 06 U40.

using System;
using System.Collections.Generic;

namespace MultiplayerKit.UI
{
    /// <summary>Selected index of every filter control. PublicBrowserView keeps one for the menu scene's lifetime (05 §7).</summary>
    public sealed class BrowserFilterState
    {
        /// <summary>Index into BrowserQueryBuilder.DistanceOptions; the default is "Nearby".</summary>
        public int DistanceIndex = BrowserQueryBuilder.DefaultDistanceIndex;

        /// <summary>Index into BrowserQueryBuilder.RegionOptions(schema); 0 = any region.</summary>
        public int RegionIndex;

        public bool HideFull = true;
        public bool HidePassworded;
        public bool HideInProgress;

        /// <summary>Schema key → index into BrowserQueryBuilder.OptionsFor(definition). Missing or 0 = Any.</summary>
        public readonly Dictionary<string, int> SettingIndices = new Dictionary<string, int>(StringComparer.Ordinal);

        /// <summary>Published Int filters for a game's own browser; the kit UI adds none (05 §7).</summary>
        public readonly List<(string key, NumericCompare op, int value)> SettingNumeric =
            new List<(string key, NumericCompare op, int value)>();
    }

    public static class BrowserQueryBuilder
    {
        public const string AnyOption = "Any";
        public const string AnyRegionOption = "Any region";
        public const int DefaultDistanceIndex = 1;

        /// <summary>Labels of the distance dropdown, in LobbyDistance order.</summary>
        public static IReadOnlyList<string> DistanceOptions { get; } =
            Array.AsReadOnly(new[] { "Close", "Nearby", "Far", "Worldwide" });

        private static readonly LobbyDistance[] Distances =
        {
            LobbyDistance.Close, LobbyDistance.Default, LobbyDistance.Far, LobbyDistance.Worldwide,
        };

        private static readonly IReadOnlyList<string> AnyOnly = Array.AsReadOnly(new[] { AnyOption });

        private static readonly IReadOnlyList<string> BoolOptions =
            Array.AsReadOnly(new[] { AnyOption, SettingControlRules.OnText, SettingControlRules.OffText });

        // Covered by the fixed controls (region, Hide full), or meaningless as a filter (05 §7).
        private static readonly string[] NotFilterable =
        {
            CoreSettingKeys.Region, CoreSettingKeys.Privacy, CoreSettingKeys.LobbyName, CoreSettingKeys.MaxPlayers,
            CoreSettingKeys.JoinInProgress,
        };

        /// <summary>
        /// The query for a filter state. Out-of-range indices read as their default (Nearby, any region, Any), so
        /// a stale state never fails a request. state null → LobbyBrowserQuery defaults; schema null → no setting filters.
        /// </summary>
        public static LobbyBrowserQuery Build(BrowserFilterState state, HostSettingsSchema schema)
        {
            var query = new LobbyBrowserQuery();
            if (state == null) return query;

            query.Distance = DistanceFor(state.DistanceIndex);
            query.Region = RegionValue(schema, state.RegionIndex);
            query.HideFull = state.HideFull;
            query.HidePassworded = state.HidePassworded;
            query.HideInProgress = state.HideInProgress;
            if (schema == null) return query;

            foreach (SettingDefinition def in schema.Definitions)
            {
                if (!IsFilterable(def) || !state.SettingIndices.TryGetValue(def.Key, out int index)) continue;
                string value = ValueFor(def, index);
                if (value != null) query.SettingEquals.Add(new KeyValuePair<string, string>(def.Key, value));
            }

            // The browser fails a whole request with InvalidArgument on an unpublished key, so such filters are dropped here.
            for (int i = 0; i < state.SettingNumeric.Count; i++)
            {
                (string key, NumericCompare op, int value) filter = state.SettingNumeric[i];
                if (schema.TryGet(filter.key, out SettingDefinition def) && def.Kind == SettingKind.Int
                    && def.PublishToLobby && !def.HostOnly)
                {
                    query.SettingNumeric.Add(filter);
                }
                else
                {
                    KitLog.Warning("Browser filter '" + KitLog.Truncate(filter.key) + "' is not a published Int setting; ignored.");
                }
            }
            return query;
        }

        /// <summary>Distance of a DistanceOptions index; out of range → Default ("Nearby").</summary>
        public static LobbyDistance DistanceFor(int index)
        {
            return index >= 0 && index < Distances.Length ? Distances[index] : LobbyDistance.Default;
        }

        /// <summary>"Any region" followed by the label of every region choice except "any", in schema order.</summary>
        public static IReadOnlyList<string> RegionOptions(HostSettingsSchema schema)
        {
            var options = new List<string> { AnyRegionOption };
            SettingDefinition region = RegionDefinition(schema);
            if (region == null) return options;
            for (int i = 0; i < region.Choices.Count; i++)
            {
                if (!IsAnyRegion(region.Choices[i].Value)) options.Add(region.Choices[i].Label);
            }
            return options;
        }

        /// <summary>The region value of a RegionOptions index; null (no filter) for 0 and out-of-range indices. Never "any".</summary>
        public static string RegionValue(HostSettingsSchema schema, int index)
        {
            SettingDefinition region = RegionDefinition(schema);
            if (region == null || index <= 0) return null;

            int position = 0;
            for (int i = 0; i < region.Choices.Count; i++)
            {
                string value = region.Choices[i].Value;
                if (IsAnyRegion(value)) continue;
                if (++position == index) return value;
            }
            return null;
        }

        /// <summary>A generated filter control exists for: published, not host-only, Choice or Bool, not a fixed-control key.</summary>
        public static bool IsFilterable(SettingDefinition d)
        {
            if (d == null || !d.PublishToLobby || d.HostOnly) return false;
            if (d.Kind != SettingKind.Choice && d.Kind != SettingKind.Bool) return false;
            return Array.IndexOf(NotFilterable, d.Key) < 0;
        }

        /// <summary>Every filterable definition, in schema order.</summary>
        public static IReadOnlyList<SettingDefinition> FilterDefinitions(HostSettingsSchema schema)
        {
            var list = new List<SettingDefinition>();
            if (schema == null) return list;
            foreach (SettingDefinition def in schema.Definitions)
            {
                if (IsFilterable(def)) list.Add(def);
            }
            return list;
        }

        /// <summary>Choice: "Any" + the choice labels. Bool: "Any", "On", "Off". Other kinds: "Any" only.</summary>
        public static IReadOnlyList<string> OptionsFor(SettingDefinition d)
        {
            if (d == null) return AnyOnly;
            if (d.Kind == SettingKind.Bool) return BoolOptions;
            if (d.Kind != SettingKind.Choice) return AnyOnly;

            var options = new string[d.Choices.Count + 1];
            options[0] = AnyOption;
            for (int i = 0; i < d.Choices.Count; i++) options[i + 1] = d.Choices[i].Label;
            return options;
        }

        /// <summary>Filter value of an OptionsFor index; null = Any (no filter), also for out-of-range indices.</summary>
        public static string ValueFor(SettingDefinition d, int index)
        {
            if (d == null || index <= 0) return null;
            if (d.Kind == SettingKind.Bool)
            {
                if (index == 1) return KitKeys.FlagOn;
                return index == 2 ? KitKeys.FlagOff : null;
            }
            if (d.Kind != SettingKind.Choice || index > d.Choices.Count) return null;
            return d.Choices[index - 1].Value;
        }

        /// <summary>The state's index for a filter control, or 0 (Any) when missing or out of range.</summary>
        public static int SelectedIndex(BrowserFilterState state, SettingDefinition d)
        {
            if (state == null || d == null || !state.SettingIndices.TryGetValue(d.Key, out int index)) return 0;
            return index > 0 && index < OptionsFor(d).Count ? index : 0;
        }

        private static SettingDefinition RegionDefinition(HostSettingsSchema schema)
        {
            if (schema == null || !schema.TryGet(CoreSettingKeys.Region, out SettingDefinition def)) return null;
            return def.Kind == SettingKind.Choice ? def : null;
        }

        private static bool IsAnyRegion(string value)
        {
            return string.Equals(value, CoreSettings.RegionAny, StringComparison.Ordinal);
        }
    }
}

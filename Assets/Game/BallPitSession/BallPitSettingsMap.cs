// Ball-pit host settings: the game's schema definitions, key constants, and the mapping onto PitSettings for a new
// game. Design: 04 §8 (BuildSettingsSchema row, Apply), 04 §6.1 step 3 (snapshot vs FILL check), 06 G3.
// Deliverable A: the pit sizes come from the VenueConfig (its default content is the five former presets, so the schema,
// labels, default and fallbacks are unchanged), and Apply also sets the profile's ball diameter (0.5 by default).
// The four world keys are group World and locked after Start; a resume locks them in the room too (WorldShapingKeys).
// worldSeed is defined here (range 1..int.MaxValue) instead of CommonSettings.WorldSeed, so Core's "0 = random seed"
// rule never fires and the baked RandomFill cache for the default seed is reused.

using System;
using System.Collections.Generic;
using BallPitFinal;
using BallPitGameplay;
using MultiplayerKit;
using UnityEngine;

namespace BallPitSession
{
    public static class BallPitSettingsMap
    {
        public const string PitSize = "pitSize";
        public const string ColorCount = "colorCount";
        public const string Fill = "fillType";
        public const string WorldSeed = CommonSettings.Keys.WorldSeed; // "worldSeed": Core reads it by key

        public const string FillFcc = "fcc";
        public const string FillRandom = "random";
        public const string DefaultPitSize = "large";

        /// <summary>Locked when resuming a save (IGameAdapter.WorldShapingKeys).</summary>
        public static readonly IReadOnlyCollection<string> WorldShapingKeys =
            Array.AsReadOnly(new[] { PitSize, ColorCount, Fill, WorldSeed });

        /// <summary>The game-specific definitions; Core merges in its own keys (01 §5.3). The pit sizes are the venue's
        /// sanitized profiles (valid keys only, at most 32), labeled "Large (80x60x80)"; the default is its DefaultProfile.</summary>
        public static IReadOnlyList<SettingDefinition> BuildDefinitions(VenueConfig venue)
        {
            venue = VenueConfig.Resolve(venue);
            IReadOnlyList<PitSizeProfile> profiles = venue.Profiles;
            var sizes = new SettingChoice[profiles.Count];
            for (int i = 0; i < profiles.Count; i++) sizes[i] = new SettingChoice(profiles[i].key, profiles[i].ChoiceLabel);
            return new[]
            {
                SettingDefinition.Choice(PitSize, "Pit size", venue.DefaultProfile.key, sizes,
                    group: CommonSettings.GroupWorld, publish: true),
                SettingDefinition.Int(ColorCount, "Colors", 4, 2, 8,
                    group: CommonSettings.GroupWorld, publish: true),
                SettingDefinition.Choice(Fill, "Fill", FillRandom,
                    new[] { new SettingChoice(FillFcc, "Lattice (instant)"), new SettingChoice(FillRandom, "Poured (random)") },
                    group: CommonSettings.GroupWorld),
                SettingDefinition.Int(WorldSeed, "World seed", def: new PitSettings().seed, min: 1, max: int.MaxValue,
                    group: CommonSettings.GroupWorld),
                CommonSettings.AutosaveMinutes,
            };
        }

        /// <summary>
        /// New game: a Clone() of the inspector settings with the world fields from the lobby settings (pit size and its
        /// ball diameter, colors, fill type, seed). Every other field (physics, budget, tools) keeps the inspector value.
        /// </summary>
        public static PitSettings Apply(IReadOnlyHostSettings hs, PitSettings inspectorBase, VenueConfig venue)
        {
            PitSettings s = (inspectorBase ?? new PitSettings()).Clone();
            PitSizeProfile p = ProfileOf(venue, hs.GetString(PitSize));
            s.pitX = p.pitX; s.pitY = p.pitY; s.pitZ = p.pitZ;
            s.ballDiameter = p.ballDiameter; // 0.5 in every default profile, so today's presets are unchanged
            s.colorCount = hs.GetInt(ColorCount);
            s.fillType = hs.GetString(Fill) == FillFcc ? FillType.Fcc : FillType.Random;
            s.seed = hs.GetInt(WorldSeed); // never 0: the definition's range starts at 1
            return s;
        }

        /// <summary>The profile of a pitSize value; an unknown value falls back to the venue's default profile.</summary>
        public static PitSizeProfile ProfileOf(VenueConfig venue, string pitSize)
        {
            venue = VenueConfig.Resolve(venue);
            return venue.TryGetProfile(pitSize, out PitSizeProfile p) ? p : venue.DefaultProfile;
        }

        /// <summary>
        /// Where a save's settings snapshot disagrees with its FILL section (hand-edited metadata), as log text, or
        /// null. The validator logs it at Warning; FILL is authoritative for the world (04 §6.1 step 3).
        /// </summary>
        public static string DescribeSnapshotMismatch(IReadOnlyDictionary<string, string> snapshot, PitFillParams fill, VenueConfig venue)
        {
            if (snapshot == null) return null;
            var diff = new List<string>();
            if (snapshot.TryGetValue(PitSize, out string size))
            {
                bool known = VenueConfig.Resolve(venue).TryGetProfile(size, out PitSizeProfile p);
                if (!known || p.pitX != fill.PitX || p.pitY != fill.PitY || p.pitZ != fill.PitZ)
                    diff.Add($"pitSize '{size}' vs {fill.PitX}x{fill.PitY}x{fill.PitZ}");
                if (known && p.ballDiameter != fill.BallDiameter) // exact: the save stores the profile's float
                    diff.Add($"pitSize '{size}' ballDiameter {p.ballDiameter} vs {fill.BallDiameter}");
            }
            if (snapshot.TryGetValue(ColorCount, out string colors) && colors != fill.ColorCount.ToString(System.Globalization.CultureInfo.InvariantCulture))
                diff.Add($"colorCount '{colors}' vs {fill.ColorCount}");
            if (snapshot.TryGetValue(Fill, out string fillType) && fillType != (fill.FillType == (byte)FillType.Fcc ? FillFcc : FillRandom))
                diff.Add($"fillType '{fillType}' vs {(FillType)fill.FillType}");
            if (snapshot.TryGetValue(WorldSeed, out string seed) && seed != fill.Seed.ToString(System.Globalization.CultureInfo.InvariantCulture))
                diff.Add($"worldSeed '{seed}' vs {fill.Seed}");
            return diff.Count == 0 ? null : string.Join(", ", diff);
        }
    }
}

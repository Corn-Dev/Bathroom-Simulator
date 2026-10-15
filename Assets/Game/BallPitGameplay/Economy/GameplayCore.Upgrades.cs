// GameplayCore.Upgrades: computing the effect of the team-shared upgrade levels (brief §4.3, §4.4). Turns the catalog
// + current levels into the values GameplaySession feeds to every player's PlayerCapabilities (A's capability seam) and
// into named-hook amounts (e.g. "funnel" container-intake scale) that systems built later read without a code change.
using System.Collections.Generic;

namespace BallPitGameplay
{
    public sealed partial class GameplayCore
    {
        /// <summary>
        /// The effective value of <paramref name="stat"/> given <paramref name="baseValue"/> (the PlayerCapabilities
        /// starting value) and the current team levels across the whole catalog. Additive contributions are summed onto
        /// the base; multiplicative contributions scale it. PlayerCapabilities clamps the final value.
        /// </summary>
        public float EffectiveStat(PlayerStat stat, float baseValue)
        {
            float add = 0f, mul = 1f;
            if (catalog != null)
                for (int i = 0; i < catalog.Count; i++)
                {
                    UpgradeDefinition u = catalog.At(i);
                    if (u == null || !u.AffectsStat(stat)) continue;
                    u.StatContribution(stat, LevelOf(u.Id), out float a, out float m);
                    add += a; mul *= m;
                }
            return (baseValue + add) * mul;
        }

        /// <summary>True if any upgrade in the catalog modifies <paramref name="stat"/> (so the session only writes
        /// stats upgrades can touch, leaving the rest at PlayerCapabilities' base).</summary>
        public bool CatalogAffectsStat(PlayerStat stat)
        {
            if (catalog == null) return false;
            for (int i = 0; i < catalog.Count; i++)
            {
                UpgradeDefinition u = catalog.At(i);
                if (u != null && u.AffectsStat(stat)) return true;
            }
            return false;
        }

        /// <summary>Every PlayerStat the catalog affects (for the session to iterate and apply).</summary>
        public IEnumerable<PlayerStat> AffectedStats()
        {
            foreach (PlayerStat s in System.Enum.GetValues(typeof(PlayerStat)))
                if (CatalogAffectsStat(s)) yield return s;
        }

        /// <summary>
        /// The accumulated amount of a named hook (e.g. "funnel") across all levels of every upgrade that declares it.
        /// Additive effects sum perLevel×level; multiplicative effects accumulate (1 + perLevel×level) − 1. Systems
        /// built later (containers' intake scale here) read this; returns 0 when no upgrade uses the hook.
        /// </summary>
        public float NamedHookAmount(string hook)
        {
            float total = 0f;
            if (catalog == null || string.IsNullOrEmpty(hook)) return 0f;
            for (int i = 0; i < catalog.Count; i++)
            {
                UpgradeDefinition u = catalog.At(i);
                if (u == null) continue;
                int level = LevelOf(u.Id);
                if (level <= 0) continue;
                IReadOnlyList<UpgradeEffect> effects = u.Effects;
                if (effects == null) continue;
                for (int e = 0; e < effects.Count; e++)
                {
                    UpgradeEffect fx = effects[e];
                    if (!fx.IsNamedHook || !string.Equals(fx.namedHook, hook, System.StringComparison.Ordinal)) continue;
                    if (fx.kind == UpgradeEffectKind.Additive) total += fx.perLevel * level;
                    else total += fx.perLevel * level; // multiplicative hooks accumulate the same per-level amount
                }
            }
            return total;
        }
    }
}

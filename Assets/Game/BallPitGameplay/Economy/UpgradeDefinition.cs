// UpgradeDefinition: one data-driven, team-shared upgrade (brief §4.3; game-design §3, §4). Pure data: id, display,
// phase/step/line for tree layout, a cost curve (base × growth^level), prerequisites (an upgrade level, or "site
// completed"), and per-level effects. Effects are EITHER a PlayerStat modifier applied through A's PlayerCapabilities
// seam, OR a named hook string for tools/systems that do not exist yet (so Phase 2-4 entries need no code change).
using System.Collections.Generic;
using UnityEngine;

namespace BallPitGameplay
{
    /// <summary>How a per-level stat modifier combines with the player's base value.</summary>
    public enum UpgradeEffectKind { Additive, Multiplicative }

    /// <summary>One per-level effect: either a PlayerStat change or a named hook (for a not-yet-built tool/system).</summary>
    [System.Serializable]
    public struct UpgradeEffect
    {
        [Tooltip("Target stat applied through PlayerCapabilities. Ignored when namedHook is set.")]
        public PlayerStat stat;
        [Tooltip("Additive: base + perLevel×level. Multiplicative: base × (1 + perLevel×level) for floats; rounded for carry.")]
        public UpgradeEffectKind kind;
        [Tooltip("Per-level amount. Carry capacity rounds the result.")]
        public float perLevel;
        [Tooltip("Non-empty = a named hook (e.g. \"funnel\") a future system reads instead of a PlayerStat; the stat/perLevel still carry data for it.")]
        public string namedHook;

        public bool IsNamedHook => !string.IsNullOrEmpty(namedHook);
    }

    /// <summary>A prerequisite: another upgrade at a minimum level, or the current site completed.</summary>
    [System.Serializable]
    public struct UpgradePrereq
    {
        [Tooltip("Required upgrade id (empty = this prereq is just the site-completed flag).")]
        public string upgradeId;
        [Min(0)] public int minLevel;
        [Tooltip("Also require the current pit/site to be completed.")]
        public bool requiresSiteCompleted;

        public bool HasUpgrade => !string.IsNullOrEmpty(upgradeId);
    }

    [CreateAssetMenu(menuName = "Ball Pit/Upgrade Definition", fileName = "Upgrade")]
    public sealed class UpgradeDefinition : ScriptableObject
    {
        [Header("Identity")]
        [Tooltip("Stable key, ^[a-z0-9_.]{1,64}$, unique in the catalog (matches the save's upgrade id rule).")]
        [SerializeField] private string id = "upgrade";
        [SerializeField] private string displayName = "Upgrade";
        [TextArea] [SerializeField] private string description = "";
        [SerializeField] private Sprite icon;

        [Header("Tree layout (visual only)")]
        [Min(1)] [SerializeField] private int phase = 1;
        [Min(0)] [SerializeField] private int step = 0;
        [Min(0)] [SerializeField] private int line = 0;

        [Header("Levels")]
        [Min(1)] [SerializeField] private int maxLevel = 1;
        [Tooltip("Cost of the NEXT level L (0-based): costBase × costGrowth^L.")]
        [Min(0)] [SerializeField] private long costBase = 20;
        [Min(1f)] [SerializeField] private float costGrowth = 1.8f;

        [Header("Prerequisites")]
        [SerializeField] private UpgradePrereq[] prereqs = new UpgradePrereq[0];

        [Header("Effects")]
        [Tooltip("Per-level effects applied cumulatively (level = how many bought).")]
        [SerializeField] private UpgradeEffect[] effects = new UpgradeEffect[0];

        public string Id => id;
        public string DisplayName => string.IsNullOrEmpty(displayName) ? id : displayName;
        public string Description => description;
        public Sprite Icon => icon;
        public int Phase => phase;
        public int Step => step;
        public int Line => line;
        public int MaxLevel => Mathf.Max(1, maxLevel);
        public IReadOnlyList<UpgradePrereq> Prereqs => prereqs;
        public IReadOnlyList<UpgradeEffect> Effects => effects;

        /// <summary>Cost of the next level when currently at <paramref name="currentLevel"/> (0-based). long.MaxValue at max.</summary>
        public long CostAt(int currentLevel)
        {
            if (currentLevel >= MaxLevel) return long.MaxValue;
            double c = costBase * System.Math.Pow(Mathf.Max(1f, costGrowth), Mathf.Max(0, currentLevel));
            if (c >= long.MaxValue) return long.MaxValue;
            return (long)System.Math.Round(c);
        }

        /// <summary>True if this upgrade has any effect on <paramref name="stat"/> (used to apply team levels to players).</summary>
        public bool AffectsStat(PlayerStat stat)
        {
            if (effects == null) return false;
            for (int i = 0; i < effects.Length; i++)
                if (!effects[i].IsNamedHook && effects[i].stat == stat) return true;
            return false;
        }

        /// <summary>
        /// The cumulative modifier this upgrade contributes to <paramref name="stat"/> at <paramref name="level"/>, as a
        /// value to COMBINE with the base. Additive effects sum perLevel×level; multiplicative effects accumulate the
        /// (1 + perLevel×level) factor. Returns (add, mul) so the caller computes base-dependent results consistently.
        /// </summary>
        public void StatContribution(PlayerStat stat, int level, out float add, out float mul)
        {
            add = 0f; mul = 1f;
            if (effects == null) return;
            level = Mathf.Max(0, level);
            for (int i = 0; i < effects.Length; i++)
            {
                UpgradeEffect e = effects[i];
                if (e.IsNamedHook || e.stat != stat) continue;
                if (e.kind == UpgradeEffectKind.Additive) add += e.perLevel * level;
                else mul *= 1f + e.perLevel * level;
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (!BallPitSession.BallPitGameState.IsValidKey(id, BallPitSession.BallPitGameState.MaxIdLength))
                Debug.LogWarning($"[UpgradeDefinition] '{name}': id '{id}' must match ^[a-z0-9_.]{{1,64}}$", this);
        }
#endif
    }
}

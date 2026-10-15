// UpgradeCatalog: the ordered set of upgrades available this bite (brief §4.3). Pure data; GameplayCore and the shop UI
// read it. Lookup by id and a validation pass (unique valid ids) keep the data honest. Phase 2-4 entries drop in by
// adding assets to this list — no code change (brief §5).
using System.Collections.Generic;
using UnityEngine;

namespace BallPitGameplay
{
    [CreateAssetMenu(menuName = "Ball Pit/Upgrade Catalog", fileName = "UpgradeCatalog")]
    public sealed class UpgradeCatalog : ScriptableObject
    {
        [Tooltip("Ordered upgrades. Order drives shop layout and the replicated level array index.")]
        [SerializeField] private UpgradeDefinition[] upgrades = new UpgradeDefinition[0];

        private Dictionary<string, int> indexById;

        public int Count => upgrades != null ? upgrades.Length : 0;
        public UpgradeDefinition At(int index) => (uint)index < (uint)Count ? upgrades[index] : null;
        public IReadOnlyList<UpgradeDefinition> Upgrades => upgrades;

        /// <summary>The catalog index of an id, or -1. The index is the replication slot in GameplayNet's level list.</summary>
        public int IndexOf(string id)
        {
            if (string.IsNullOrEmpty(id)) return -1;
            BuildIndex();
            return indexById.TryGetValue(id, out int i) ? i : -1;
        }

        public UpgradeDefinition Find(string id)
        {
            int i = IndexOf(id);
            return i >= 0 ? upgrades[i] : null;
        }

        private void BuildIndex()
        {
            if (indexById != null) return;
            indexById = new Dictionary<string, int>(System.StringComparer.Ordinal);
            if (upgrades == null) return;
            for (int i = 0; i < upgrades.Length; i++)
                if (upgrades[i] != null && !string.IsNullOrEmpty(upgrades[i].Id) && !indexById.ContainsKey(upgrades[i].Id))
                    indexById[upgrades[i].Id] = i;
        }

        /// <summary>Editor/validator rebuild hook (ids may have changed in the inspector).</summary>
        public void InvalidateIndex() => indexById = null;

        /// <summary>Collects validation problems: null entries, invalid or duplicate ids, non-monotonic costs,
        /// unresolved prerequisites. Returns true when clean.</summary>
        public bool Validate(List<string> problems)
        {
            InvalidateIndex();
            bool ok = true;
            var seen = new HashSet<string>(System.StringComparer.Ordinal);
            if (upgrades == null) return true;
            for (int i = 0; i < upgrades.Length; i++)
            {
                UpgradeDefinition u = upgrades[i];
                if (u == null) { problems?.Add($"entry {i} is empty"); ok = false; continue; }
                if (!BallPitSession.BallPitGameState.IsValidKey(u.Id, BallPitSession.BallPitGameState.MaxIdLength))
                { problems?.Add($"'{u.name}': id '{u.Id}' is not a valid key"); ok = false; }
                else if (!seen.Add(u.Id)) { problems?.Add($"duplicate id '{u.Id}'"); ok = false; }
                long prev = -1;
                for (int l = 0; l < u.MaxLevel; l++)
                {
                    long c = u.CostAt(l);
                    if (c < prev) { problems?.Add($"'{u.Id}' cost at level {l} ({c}) is below the previous ({prev})"); ok = false; }
                    prev = c;
                }
            }
            for (int i = 0; i < upgrades.Length; i++)
            {
                UpgradeDefinition u = upgrades[i];
                if (u?.Prereqs == null) continue;
                foreach (UpgradePrereq pr in u.Prereqs)
                    if (pr.HasUpgrade && IndexOf(pr.upgradeId) < 0)
                    { problems?.Add($"'{u.Id}' requires unknown upgrade '{pr.upgradeId}'"); ok = false; }
            }
            return ok;
        }

#if UNITY_EDITOR
        private void OnValidate() => InvalidateIndex();
#endif
    }
}

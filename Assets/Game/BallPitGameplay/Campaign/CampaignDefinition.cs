// CampaignDefinition: the authored, ordered list of shipment pits for the main run (Deliverable D; DESIGN-D §9, plan item 4).
// A pit is a PREFAB (pit art + a PitSite); the campaign is just "which prefab, with how many colours" per entry. Adding a pit
// is adding an entry (no code change). Colours are decided here, by the campaign, not by the lobby or the pit prefab.
// CampaignMath is pure C# (no Unity types) so it can be unit-tested later.
using System.Collections.Generic;
using UnityEngine;

namespace BallPitGameplay
{
    /// <summary>One shipment: the pit prefab to instantiate and how many colours that pit has.</summary>
    [System.Serializable]
    public sealed class CampaignEntry
    {
        [Tooltip("A Pit_*.prefab containing a PitSite (on the root or any child).")]
        public GameObject pitPrefab;
        [Tooltip("Ball colours in this pit. Clamped 2..8 at runtime (the campaign bypasses the lobby's colour setting).")]
        [Range(2, 8)] public int colorCount = 4;
        [Tooltip("RESERVED (per-shipment tool availability). Unused in v1.")]
        public string[] availableTools;
    }

    [CreateAssetMenu(menuName = "Ball Pit/Campaign Definition", fileName = "MainCampaign")]
    public sealed class CampaignDefinition : ScriptableObject
    {
        [SerializeField] private List<CampaignEntry> entries = new List<CampaignEntry>();

        public IReadOnlyList<CampaignEntry> Entries => entries;
        public int Count => entries.Count;

        /// <summary>The entry at <paramref name="i"/>, clamped into range; null when the campaign is empty.</summary>
        public CampaignEntry At(int i)
        {
            if (entries.Count == 0) return null;
            return entries[CampaignMath.ClampIndex(i, entries.Count)];
        }

        /// <summary>The first entry whose pit prefab ASSET carries a PitSite with this siteId, else -1. Reads the asset
        /// (no instantiate).</summary>
        public int IndexOfSiteId(string siteId)
        {
            if (string.IsNullOrEmpty(siteId)) return -1;
            for (int i = 0; i < entries.Count; i++)
            {
                GameObject prefab = entries[i] != null ? entries[i].pitPrefab : null;
                if (prefab == null) continue;
                PitSite site = prefab.GetComponentInChildren<PitSite>(true);
                if (site != null && string.Equals(site.SiteId, siteId, System.StringComparison.Ordinal)) return i;
            }
            return -1;
        }
    }

    public static class CampaignMath
    {
        public const int MinColors = 2;
        public const int MaxColors = 8;

        public static int ClampColorCount(int colors) => colors < MinColors ? MinColors : (colors > MaxColors ? MaxColors : colors);

        /// <summary>Clamps into [0, count-1]; 0 when count &lt;= 0.</summary>
        public static int ClampIndex(int index, int count)
        {
            if (count <= 0) return 0;
            return index < 0 ? 0 : (index >= count ? count - 1 : index);
        }

        /// <summary>True when an entry exists after <paramref name="index"/>.</summary>
        public static bool HasMore(int index, int count) => count > 0 && index + 1 < count;
    }
}

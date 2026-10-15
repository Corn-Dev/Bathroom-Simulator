// PaletteService: resolves the active PalettePreset by key from an authored set of presets, and raises an event when
// the active key changes (brief §4.5). The visual source of truth for colour names and colours; PaletteApplier reads it
// and pushes the result (after the local colour-blind override) to the renderer. Pure data + lookup; no NGO, no scene.
using System.Collections.Generic;
using UnityEngine;

namespace BallPitGameplay
{
    /// <summary>A small, authored registry of palette presets. Serialize it on PaletteApplier (owner-assigned).</summary>
    [System.Serializable]
    public sealed class PaletteService
    {
        [Tooltip("Authored palette presets. The first entry is the fallback when a requested key is unknown.")]
        [SerializeField] private List<PalettePreset> presets = new List<PalettePreset>();

        private string activeKey;

        /// <summary>Raised when SetActive changes the active preset.</summary>
        public event System.Action Changed;

        /// <summary>The resolved active preset, or the first authored preset, or null when none are assigned.</summary>
        public PalettePreset Active { get; private set; }

        /// <summary>The authored presets (read-only), e.g. to build the lobby 'palette' choice list.</summary>
        public IReadOnlyList<PalettePreset> Presets => presets;

        /// <summary>Selects the preset whose Key matches (ordinal), else the first preset. Raises Changed on a change.</summary>
        public void SetActive(string key)
        {
            activeKey = key;
            PalettePreset next = Resolve(key);
            if (ReferenceEquals(next, Active)) return;
            Active = next;
            Changed?.Invoke();
        }

        /// <summary>The display colours for the active preset at the given colour count (never null; HSV fallback when
        /// no preset is assigned).</summary>
        public Color[] ActiveColors(int count)
        {
            PalettePreset p = Active ?? Resolve(activeKey);
            if (p != null) return p.BuildColors(count);
            int c = Mathf.Clamp(count, PalettePreset.MinColors, PalettePreset.MaxColors);
            var fallback = new Color[c];
            for (int i = 0; i < c; i++) fallback[i] = Color.HSVToRGB((float)i / c, 0.75f, 0.95f);
            return fallback;
        }

        /// <summary>The active preset's name for a colour index ('Color N' when no preset is assigned).</summary>
        public string NameOf(int index) => Active != null ? Active.NameOf(index) : "Color " + (index + 1);

        private PalettePreset Resolve(string key)
        {
            if (presets != null && !string.IsNullOrEmpty(key))
                for (int i = 0; i < presets.Count; i++)
                    if (presets[i] != null && string.Equals(presets[i].Key, key, System.StringComparison.Ordinal))
                        return presets[i];
            if (presets != null)
                for (int i = 0; i < presets.Count; i++)
                    if (presets[i] != null) return presets[i];
            return null;
        }
    }
}

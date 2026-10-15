// PalettePreset: an authored ball-colour palette (deliverable B, bite a; brief §4.5; game-design §5). An ordered list
// of colours with display names, usable for any pit colour count 2..8. The visual palette only; it is applied to the
// GpuBallRenderer through the controller's additive SetPalette, and container/HUD colour names come from NameOf.
// Classic + Pastel ship fully authored; Candy/Neon/Ocean/Sunset start as placeholder assets (the asset menu fills them).
using UnityEngine;

namespace BallPitGameplay
{
    [CreateAssetMenu(menuName = "Ball Pit/Palette Preset", fileName = "PalettePreset")]
    public sealed class PalettePreset : ScriptableObject
    {
        /// <summary>The pit colour-count range the design supports (game-design §5). The code still handles 2..8.</summary>
        public const int MinColors = 2, MaxColors = 8;

        [Tooltip("Stable key referenced by the lobby 'palette' setting (^[a-z0-9_.]{1,32}$).")]
        [SerializeField] private string key = "classic";
        [Tooltip("Shown in menus.")]
        [SerializeField] private string displayName = "Classic";
        [Tooltip("Ordered colours. Entry i is colour index i. Fewer than the pit's colour count = the extra colours wrap.")]
        [SerializeField] private Color[] colors = new Color[0];
        [Tooltip("Ordered colour names (parallel to colors). A missing name falls back to 'Color N'.")]
        [SerializeField] private string[] names = new string[0];

        /// <summary>Stable lookup key.</summary>
        public string Key => key;
        public string DisplayName => string.IsNullOrEmpty(displayName) ? key : displayName;
        /// <summary>How many colours this preset actually authored (0 = placeholder; a built fallback is used).</summary>
        public int AuthoredCount => colors != null ? colors.Length : 0;

        /// <summary>
        /// The colours for a pit with <paramref name="count"/> colours (clamped to 2..8). Authored entries are used in
        /// order; if the preset has fewer than count entries, the extra indices wrap (never throws, never null). A
        /// placeholder preset with no colours yields an evenly-spaced HSV fallback so the game is always playable.
        /// </summary>
        public Color[] BuildColors(int count)
        {
            count = Mathf.Clamp(count, MinColors, MaxColors);
            var result = new Color[count];
            if (colors == null || colors.Length == 0)
            {
                for (int i = 0; i < count; i++) result[i] = Color.HSVToRGB((float)i / count, 0.75f, 0.95f);
                return result;
            }
            for (int i = 0; i < count; i++) result[i] = colors[i % colors.Length];
            return result;
        }

        /// <summary>The display name for a colour index (wraps like BuildColors); 'Color N' when none is authored.</summary>
        public string NameOf(int index)
        {
            if (names != null && names.Length > 0)
            {
                string n = names[Mathf.Abs(index) % names.Length];
                if (!string.IsNullOrEmpty(n)) return n;
            }
            return "Color " + (index + 1);
        }

        /// <summary>Editor/asset-menu authoring helper. Sets the key, name and parallel colour/name arrays.</summary>
        public void Author(string presetKey, string presetName, Color[] presetColors, string[] presetNames)
        {
            key = presetKey;
            displayName = presetName;
            colors = presetColors ?? new Color[0];
            names = presetNames ?? new string[0];
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (!BallPitSession.BallPitGameState.IsValidKey(key, BallPitSession.BallPitGameState.MaxTagLength))
                Debug.LogWarning($"[PalettePreset] '{name}': key '{key}' should match ^[a-z0-9_.]{{1,32}}$", this);
            if (names != null && colors != null && names.Length != 0 && names.Length != colors.Length)
                Debug.LogWarning($"[PalettePreset] '{name}': {names.Length} names vs {colors.Length} colours; names wrap independently", this);
        }
#endif
    }
}

// ColorblindDisplay: a per-player, client-local colour-vision display override (brief §4.5; game-design §5). When a
// mode is set in PlayerPrefs it replaces the active palette's colours with the Okabe-Ito colour-blind-safe set, so the
// local player sees distinguishable ball colours WITHOUT changing what any other player sees. It never leaves this peer
// and never touches host state; it is applied on top of the resolved PalettePreset in PaletteApplier.
using UnityEngine;

namespace BallPitGameplay
{
    public enum ColorblindMode { Off = 0, Protan = 1, Deutan = 2, Tritan = 3 }

    /// <summary>Client-local only: reads/writes PlayerPrefs, never host settings.</summary>
    public static class ColorblindDisplay
    {
        public const string ModeKey = "BallPit.Colorblind.Mode"; // int ColorblindMode

        // Okabe-Ito 8-colour qualitative palette (safe for the common colour-vision deficiencies). Fixed order.
        private static readonly Color[] OkabeIto =
        {
            new Color(0.000f, 0.000f, 0.000f), // black
            new Color(0.902f, 0.624f, 0.000f), // orange
            new Color(0.337f, 0.706f, 0.914f), // sky blue
            new Color(0.000f, 0.620f, 0.451f), // bluish green
            new Color(0.941f, 0.894f, 0.259f), // yellow
            new Color(0.000f, 0.447f, 0.698f), // blue
            new Color(0.835f, 0.369f, 0.000f), // vermillion
            new Color(0.800f, 0.475f, 0.655f), // reddish purple
        };

        /// <summary>Raised when the local mode changes, so PaletteApplier re-applies.</summary>
        public static event System.Action Changed;

        public static ColorblindMode Mode
        {
            get
            {
                int v = PlayerPrefs.GetInt(ModeKey, 0);
                return v >= 0 && v <= 3 ? (ColorblindMode)v : ColorblindMode.Off;
            }
            set
            {
                PlayerPrefs.SetInt(ModeKey, (int)value);
                Changed?.Invoke();
            }
        }

        /// <summary>
        /// The display colours for the local player: the preset colours unchanged when the mode is Off, else the
        /// Okabe-Ito safe set (first <paramref name="count"/> entries, wrapping). count is clamped to 2..8. The three
        /// deficiency modes currently share the Okabe-Ito set (it is safe for all three); the enum leaves room for
        /// per-mode tuning later without a call-site change.
        /// </summary>
        public static Color[] Apply(Color[] presetColors, int count)
        {
            count = Mathf.Clamp(count, PalettePreset.MinColors, PalettePreset.MaxColors);
            if (Mode == ColorblindMode.Off)
            {
                if (presetColors != null && presetColors.Length == count) return presetColors;
                var trimmed = new Color[count];
                for (int i = 0; i < count; i++) trimmed[i] = presetColors != null && presetColors.Length > 0
                    ? presetColors[i % presetColors.Length] : Color.HSVToRGB((float)i / count, 0.75f, 0.95f);
                return trimmed;
            }
            var result = new Color[count];
            for (int i = 0; i < count; i++) result[i] = OkabeIto[i % OkabeIto.Length];
            return result;
        }
    }
}

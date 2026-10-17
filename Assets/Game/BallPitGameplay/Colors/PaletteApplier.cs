// PaletteApplier: resolves the active ball palette and pushes it to the renderer (brief §4.5). On every PitWorldBuilt,
// and whenever the palette selection or the local colour-blind pref changes, it asks the PaletteService for the active
// preset's colours at the pit's colour count, applies the client-local ColorblindDisplay override on top, and calls
// controller.SetPalette (the one additive renderer hook). The palette is visual only and client-local for the
// colour-blind part, so it never affects host state or other players. It also shares its PaletteService with
// GameplaySession so colour names resolve in the HUD/containers. Author-placed in the gameplay scene.
using BallPitFinal;
using UnityEngine;

namespace BallPitGameplay
{
    [DisallowMultipleComponent]
    public sealed class PaletteApplier : MonoBehaviour
    {
        [Tooltip("Authored palette presets; the first is the fallback.")]
        [SerializeField] private PaletteService paletteService = new PaletteService();
        [Tooltip("Default active palette key (overridden live by SetPaletteKey, e.g. from the lobby 'palette' setting).")]
        [SerializeField] private string defaultPaletteKey = "classic";
        [Tooltip("Optional: the GameplaySession to share the PaletteService with (colour names in the HUD).")]
        [SerializeField] private GameplaySession gameplaySession;

        private BallPitFinalController controller;
        private bool subscribed;
        // The last display colours pushed to the renderer (post colour-blind). GameplaySession reads this via
        // DisplayColor(int) so a container's fill matches the ball colour the LOCAL player sees. Refreshed at the end
        // of every Apply (palette change, colour-blind change, PitWorldBuilt).
        private Color[] displayCache;

        /// <summary>The resolved palette service (colour names for the HUD/containers).</summary>
        public PaletteService Service => paletteService;

        private void Awake()
        {
            controller = FindObjectOfType<BallPitFinalController>();
            paletteService.SetActive(defaultPaletteKey);
            if (gameplaySession == null) gameplaySession = FindObjectOfType<GameplaySession>();
            if (gameplaySession != null)
            {
                gameplaySession.SetPaletteService(paletteService);
                gameplaySession.SetPaletteApplier(this); // so container fills recolour to the displayed ball colour
            }
        }

        /// <summary>The display colour for a colour index (post colour-blind), as last pushed to the renderer. Returns a
        /// safe value before the first Apply: the recomputed display colour when the world's colour count is known, else
        /// Color.white. Read-only; it never triggers an Apply or SetPalette.</summary>
        public Color DisplayColor(int index)
        {
            if (displayCache != null && index >= 0 && index < displayCache.Length) return displayCache[index];
            // Not applied yet (or out of range): recompute from the live world count if we can, else a neutral default.
            if (controller != null && controller.World != null && index >= 0)
            {
                int count = controller.World.Store.Colors;
                if (index < count)
                {
                    Color[] display = ColorblindDisplay.Apply(paletteService.ActiveColors(count), count);
                    if (display != null && index < display.Length) return display[index];
                }
            }
            return Color.white;
        }

        private void OnEnable()
        {
            if (controller != null)
            {
                controller.PitWorldBuilt += OnPitWorldBuilt;
                subscribed = true;
            }
            paletteService.Changed += Apply;
            ColorblindDisplay.Changed += Apply;
            if (controller != null && controller.IsPitWorldReady) Apply();
        }

        private void OnDisable()
        {
            if (subscribed && controller != null) controller.PitWorldBuilt -= OnPitWorldBuilt;
            subscribed = false;
            paletteService.Changed -= Apply;
            ColorblindDisplay.Changed -= Apply;
        }

        /// <summary>Live palette change (lobby 'palette' setting or an options screen). Re-applies immediately.</summary>
        public void SetPaletteKey(string key) => paletteService.SetActive(key);

        private void OnPitWorldBuilt(PitWorld world, IPitSite site) => Apply();

        private void Apply()
        {
            if (controller == null || controller.World == null) return;
            int count = controller.World.Store.Colors;
            Color[] preset = paletteService.ActiveColors(count);
            Color[] display = ColorblindDisplay.Apply(preset, count);
            controller.SetPalette(display);
            displayCache = display; // refreshed on every palette / colour-blind change (both route here)
        }
    }
}

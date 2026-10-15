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

        /// <summary>The resolved palette service (colour names for the HUD/containers).</summary>
        public PaletteService Service => paletteService;

        private void Awake()
        {
            controller = FindObjectOfType<BallPitFinalController>();
            paletteService.SetActive(defaultPaletteKey);
            if (gameplaySession == null) gameplaySession = FindObjectOfType<GameplaySession>();
            if (gameplaySession != null) gameplaySession.SetPaletteService(paletteService);
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
        }
    }
}

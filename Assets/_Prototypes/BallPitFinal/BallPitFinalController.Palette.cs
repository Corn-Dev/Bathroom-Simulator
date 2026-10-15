// BallPitFinalController.Palette: a thin, additive forwarder for the one allowed GpuBallRenderer palette change
// (deliverable B, bite a; brief §4.5, boundary §6.4). Gameplay's PaletteApplier calls controller.SetPalette(colors)
// on PitWorldBuilt and whenever the palette setting or the local colour-blind pref changes. Safe before a world exists
// (the renderer is null then; the next build applies its own palette, and PaletteApplier re-applies on PitWorldBuilt).
// This partial adds NO new fields and touches no existing member; it only reads the private gpu reference.
using UnityEngine;

namespace BallPitFinal
{
    public sealed partial class BallPitFinalController
    {
        /// <summary>Re-applies the ball palette live. No-op before the renderer exists (null gpu).</summary>
        public void SetPalette(Color[] palette) => gpu?.SetPalette(palette);
    }
}

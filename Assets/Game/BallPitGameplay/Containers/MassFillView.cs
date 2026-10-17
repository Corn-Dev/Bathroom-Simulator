// MassFillView: the DEFAULT visible container fill (brief §3.2; DESIGN-C §2; game-design §2 "Visible fill is a core
// satisfaction"). A single colored child mesh whose local Y scale + position lerp inside a serialized fill-volume box
// between empty and full, so the container reads as "this color, this fraction full" — O(1) in sold count, so a
// container can hold 100k+ sold balls at zero per-ball cost (the container analogue of the pit's data-not-objects
// trick). It NEVER drives the container root (reusing DefaultFillView's guard): deforming the root would flatten the
// whole container + its intake box + art. SetColor recolors via a MaterialPropertyBlock so the fill matches the
// displayed ball color per peer (incl. colorblind). No compute/GPU buffer; never references GpuBallRenderer.
using UnityEngine;

namespace BallPitGameplay
{
    [DisallowMultipleComponent]
    public sealed class MassFillView : MonoBehaviour, IContainerFillView
    {
        [Tooltip("Dedicated child whose scale/position shows fill. MUST NOT be the container root (a root drive is a no-op).")]
        [SerializeField] private Transform fillVolume;
        [Tooltip("The MeshRenderer recolored by SetColor. Empty = the renderer on fillVolume, if any.")]
        [SerializeField] private MeshRenderer fillRenderer;
        [Tooltip("Local Y scale of fillVolume at empty and full.")]
        [SerializeField] private float emptyScaleY = 0.001f, fullScaleY = 1f;
        [Tooltip("Local Y position of fillVolume at empty and full (fillVolume rests on the box floor and grows up).")]
        [SerializeField] private float emptyPosY = 0f, fullPosY = 0.5f;

        private MaterialPropertyBlock mpb;
        private static readonly int ColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int LegacyColorId = Shader.PropertyToID("_Color");

        // Default to NO fill volume: SetFill is a safe no-op until the owner assigns a dedicated child. Driving the
        // fill against the container root would move/flatten the whole container (DefaultFillView guards the same way).
        private void Reset() => fillVolume = null;

        public void SetFill(float fraction)
        {
            Transform t = fillVolume;
            // No volume, or the volume IS this container's root/the view's own object: do nothing. The fill needs its
            // own child transform; scaling the root would deform the entire container (the regression this guards).
            if (t == null || t == transform) return;
            float f = Mathf.Clamp01(float.IsNaN(fraction) ? 0f : fraction);
            Vector3 s = t.localScale;
            s.y = Mathf.Lerp(emptyScaleY, fullScaleY, f);
            t.localScale = s;
            Vector3 p = t.localPosition;
            p.y = Mathf.Lerp(emptyPosY, fullPosY, f);
            t.localPosition = p;
        }

        /// <summary>Recolor the fill to the displayed ball color (pushed per-peer by GameplaySession; matches colorblind).</summary>
        public void SetColor(Color color)
        {
            MeshRenderer r = fillRenderer != null ? fillRenderer
                           : fillVolume != null ? fillVolume.GetComponent<MeshRenderer>() : null;
            if (r == null) return;
            mpb ??= new MaterialPropertyBlock();
            r.GetPropertyBlock(mpb);
            mpb.SetColor(ColorId, color);
            mpb.SetColor(LegacyColorId, color); // cover both URP (_BaseColor) and built-in (_Color) placeholder mats
            r.SetPropertyBlock(mpb);
        }

#if UNITY_EDITOR
        private bool warned;
        private void OnValidate()
        {
            if (!warned && fillVolume == null)
            {
                warned = true; // log ONCE: a missing fill volume means SetFill is inert (owner must assign a child)
                Debug.LogWarning($"[MassFillView] '{name}': assign a dedicated child to 'Fill Volume' (not the root) or the fill will not show.", this);
            }
        }
#endif
    }
}

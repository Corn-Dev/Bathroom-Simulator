// DefaultFillView: a placeholder container fill view (brief §4.1). Moves and scales a pivot Transform along its local
// up axis between empty and full, so the game shows fill without the owner's final art. Author-attached; the owner
// swaps in their own IContainerFillView later.
using UnityEngine;

namespace BallPitGameplay
{
    [DisallowMultipleComponent]
    public sealed class DefaultFillView : MonoBehaviour, IContainerFillView
    {
        [Tooltip("The transform scaled/moved to show fill. Empty = this transform.")]
        [SerializeField] private Transform pivot;
        [Tooltip("Local Y scale at empty and full.")]
        [SerializeField] private float emptyScaleY = 0.02f, fullScaleY = 1f;
        [Tooltip("Local Y position at empty and full (centre of the fill volume).")]
        [SerializeField] private float emptyPosY = 0f, fullPosY = 0.5f;

        private void Reset() => pivot = transform;

        public void SetFill(float fraction)
        {
            Transform t = pivot != null ? pivot : transform;
            float f = Mathf.Clamp01(float.IsNaN(fraction) ? 0f : fraction);
            Vector3 s = t.localScale;
            s.y = Mathf.Lerp(emptyScaleY, fullScaleY, f);
            t.localScale = s;
            Vector3 p = t.localPosition;
            p.y = Mathf.Lerp(emptyPosY, fullPosY, f);
            t.localPosition = p;
        }
    }
}

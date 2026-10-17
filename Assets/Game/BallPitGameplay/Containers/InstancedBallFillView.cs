// InstancedBallFillView: an OPT-IN alternative container fill (brief §3.2; DESIGN-C §2). Where MassFillView shows a
// shaped colored mass, this draws a literal clump of fake balls via its OWN Graphics.DrawMeshInstanced — its own
// mesh/material/MaterialPropertyBlock, independent of the pit renderer. The visible count and stack height track the
// fill fraction (capped at a few hundred, packed dense enough to read as "full"). The default container ships with
// MassFillView (owner Q1); this component is NOT assigned by default — the owner opts in per container/theme.
// HARD BOUNDARY: it must NOT reference or modify GpuBallRenderer / the pit renderer path; these are its own draws.
using UnityEngine;

namespace BallPitGameplay
{
    [DisallowMultipleComponent]
    public sealed class InstancedBallFillView : MonoBehaviour, IContainerFillView
    {
        [Tooltip("Mesh for a single faked ball (a low-poly sphere). Owner-assigned; empty = nothing drawn.")]
        [SerializeField] private Mesh ballMesh;
        [Tooltip("GPU-instancing-enabled material for the faked balls. Owner-assigned; empty = nothing drawn.")]
        [SerializeField] private Material ballMaterial;
        [Tooltip("Local-space box the clump fills (centre + size). The clump stacks up inside it with the fraction.")]
        [SerializeField] private Vector3 volumeCenterLocal = new Vector3(0f, 0.5f, 0f);
        [SerializeField] private Vector3 volumeSizeLocal = new Vector3(1f, 1f, 1f);
        [Tooltip("Diameter of a faked ball in local units (sets how dense the clump packs).")]
        [SerializeField] private float ballDiameter = 0.25f;
        [Tooltip("Max faked balls drawn at full (cap for cost; packed to read as full regardless of real sold count).")]
        [Min(1)] [SerializeField] private int maxInstances = 256;

        private readonly Matrix4x4[] matrices = new Matrix4x4[256];
        private MaterialPropertyBlock mpb;
        private static readonly int ColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int LegacyColorId = Shader.PropertyToID("_Color");
        private int visible;
        private bool built;

        public void SetFill(float fraction)
        {
            float f = Mathf.Clamp01(float.IsNaN(fraction) ? 0f : fraction);
            int cap = Mathf.Clamp(maxInstances, 1, matrices.Length);
            int target = Mathf.RoundToInt(f * cap);
            if (target != visible || !built) { visible = target; Rebuild(cap); }
        }

        public void SetColor(Color color)
        {
            mpb ??= new MaterialPropertyBlock();
            mpb.SetColor(ColorId, color);
            mpb.SetColor(LegacyColorId, color);
        }

        // Lay the visible instances out in a dense column of rows inside the local volume, stacking bottom-up.
        private void Rebuild(int cap)
        {
            built = true;
            if (visible <= 0) return;
            float d = Mathf.Max(0.01f, ballDiameter);
            int perRow = Mathf.Max(1, Mathf.FloorToInt(volumeSizeLocal.x / d));
            int perLayer = perRow * Mathf.Max(1, Mathf.FloorToInt(volumeSizeLocal.z / d));
            Vector3 min = volumeCenterLocal - volumeSizeLocal * 0.5f + Vector3.one * (d * 0.5f);
            for (int i = 0; i < visible && i < matrices.Length; i++)
            {
                int layer = i / Mathf.Max(1, perLayer);
                int inLayer = i % Mathf.Max(1, perLayer);
                int row = inLayer / Mathf.Max(1, perRow);
                int col = inLayer % Mathf.Max(1, perRow);
                Vector3 local = min + new Vector3(col * d, layer * d, row * d);
                matrices[i] = Matrix4x4.TRS(local, Quaternion.identity, Vector3.one * d);
            }
        }

        private void Update()
        {
            if (ballMesh == null || ballMaterial == null || visible <= 0) return;
            mpb ??= new MaterialPropertyBlock();
            int n = Mathf.Min(visible, matrices.Length);
            // Draw in LOCAL space transformed by this object (so the clump follows the container). Our own draw call —
            // not the pit renderer. Transform each local matrix by the container's localToWorld.
            Matrix4x4 l2w = transform.localToWorldMatrix;
            for (int i = 0; i < n; i++) worldBuffer[i] = l2w * matrices[i];
            Graphics.DrawMeshInstanced(ballMesh, 0, ballMaterial, worldBuffer, n, mpb);
        }

        private readonly Matrix4x4[] worldBuffer = new Matrix4x4[256];
    }
}

// BallVisualConfig: the ball look asset (deliverable A, file plan F5, design b 12).
// Lives in the pit namespace so the controller can reference it without naming a gameplay type (file plan R4).
// GpuBallRenderer and its shaders are unchanged: the palette and meshes go through the existing constructor, and
// gloss/spec/ambient are written to the renderer's runtime material, found by its name and shader.
using System.Collections.Generic;
using UnityEngine;

namespace BallPitFinal
{
    [CreateAssetMenu(menuName = "Ball Pit/Ball Visual Config", fileName = "BallVisualConfig")]
    public sealed class BallVisualConfig : ScriptableObject
    {
        [System.Serializable]
        public struct PaletteEntry
        {
            public string name;
            public Color color;
        }

        /// <summary>Shader limit: float4 _Palette[16], read as _Palette[packed &amp; 15].</summary>
        public const int MaxPaletteEntries = 16;
        /// <summary>The lobby's largest colorCount choice (BallPitSettingsMap). Fewer entries fall back to HSV.</summary>
        public const int LobbyMaxColorCount = 8;

        // GpuBallRenderer's literals (read-only use; the renderer is not modified).
        private const string BallShaderName = "BallPitInstancedBall";
        private const string RuntimeMaterialName = "BallPitInstancedBall (runtime)";

        [Tooltip("Index = ball color index. B shows the names (containers, UI). Max 16 (shader limit); rgb only.")]
        public List<PaletteEntry> palette = new List<PaletteEntry>();
        [Tooltip("Optional LOD meshes: a radius-0.5 sphere centered on the origin, submesh 0 only (the shader scales by " +
                 "2 x radius and derives normals from positions). Null = BallMeshes icosphere.")]
        public Mesh nearMesh, farMesh;
        [Range(4f, 256f)] public float gloss = 48f;           // _Gloss           (shader default 48)
        [Range(0f, 1f)] public float specStrength = 0.3f;     // _SpecStrength    (0.3)
        [Range(0f, 2f)] public float ambientStrength = 1f;    // _AmbientStrength (1)
        [Min(0f)] public float lodPixelRadius = 12f;          // GpuBallRenderer.LodPixelRadius (controller default 12)
        public bool castShadows = false;                      // GpuBallRenderer.CastShadows On/Off (controller default Off)
        public bool receiveShadows = true;                    // GpuBallRenderer.ReceiveShadows (controller default true)

        private static bool warnedNoMaterial;
#if UNITY_EDITOR
        [System.NonSerialized] private string lastValidation;
#endif

        // Default palette (b 12.4): entries 0-3 are today's 4-color HSV palette exactly; 4-7 fill the hue gaps.
        private static readonly float[] DefaultHues = { 0f, 0.25f, 0.5f, 0.75f, 0.125f, 0.375f, 0.625f, 0.875f };
        private static readonly string[] DefaultNames = { "Red", "Lime", "Cyan", "Violet", "Orange", "Green", "Blue", "Pink" };
        private const float DefaultSaturation = 0.75f, DefaultValue = 0.95f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => warnedNoMaterial = false;

        /// <summary>
        /// Palette for the renderer constructor: length max(1, colorCount). Entry i is palette[i].color when present,
        /// else today's HSV fallback HSVToRGB(i / colorCount, 0.75, 0.95), with one Warning per call.
        /// </summary>
        public Color[] BuildPalette(int colorCount)
        {
            int n = Mathf.Max(1, colorCount);
            var result = new Color[n];
            int usable = Mathf.Min(palette != null ? palette.Count : 0, MaxPaletteEntries);
            int missing = 0;
            for (int i = 0; i < n; i++)
            {
                if (i < usable)
                {
                    Color c = palette[i].color;
                    result[i] = new Color(c.r, c.g, c.b, 1f);
                }
                else
                {
                    result[i] = Color.HSVToRGB((float)i / n, DefaultSaturation, DefaultValue);
                    missing++;
                }
            }
            if (missing > 0)
                Debug.LogWarning($"[BallVisualConfig] '{name}' has {usable} usable palette entries for colorCount {n}; " +
                                 $"{missing} color(s) use the HSV fallback.", this);
            return result;
        }

        /// <summary>Display name of a color index: the palette name if set, else "Color {index + 1}".</summary>
        public string ColorName(int index)
        {
            if (palette != null && index >= 0 && index < palette.Count && index < MaxPaletteEntries &&
                !string.IsNullOrWhiteSpace(palette[index].name))
                return palette[index].name;
            return $"Color {index + 1}";
        }

        /// <summary>
        /// Writes gloss/spec/ambient to the renderer's runtime ball material(s). Run once per build, after the renderer
        /// is created. Returns the number of materials updated; none found logs one Warning per process (recoverable:
        /// the shader defaults stay).
        /// </summary>
        public int ApplyToRuntimeMaterials()
        {
            Shader shader = Resources.Load<Shader>(BallShaderName);
            int updated = 0;
            if (shader != null)
            {
                Material[] all = Resources.FindObjectsOfTypeAll<Material>();
                for (int i = 0; i < all.Length; i++)
                {
                    Material m = all[i];
                    if (m == null || m.shader != shader || m.name != RuntimeMaterialName) continue;
                    m.SetFloat("_Gloss", gloss);
                    m.SetFloat("_SpecStrength", specStrength);
                    m.SetFloat("_AmbientStrength", ambientStrength);
                    updated++;
                }
            }
            if (updated == 0 && !warnedNoMaterial)
            {
                warnedNoMaterial = true;
                Debug.LogWarning("[BallVisualConfig] no runtime ball material found; gloss/spec/ambient stay at shader defaults.", this);
            }
            return updated;
        }

        /// <summary>In-memory instance with today's look (b 12.4).</summary>
        public static BallVisualConfig CreateDefault()
        {
            var c = CreateInstance<BallVisualConfig>();
            c.ApplyDefaults();
            return c;
        }

        private void Reset() => ApplyDefaults();

        private void ApplyDefaults()
        {
            palette = new List<PaletteEntry>(DefaultHues.Length);
            for (int i = 0; i < DefaultHues.Length; i++)
                palette.Add(new PaletteEntry
                {
                    name = DefaultNames[i],
                    color = Color.HSVToRGB(DefaultHues[i], DefaultSaturation, DefaultValue)
                });
            nearMesh = null;
            farMesh = null;
            gloss = 48f;
            specStrength = 0.3f;
            ambientStrength = 1f;
            lodPixelRadius = 12f;
            castShadows = false;
            receiveShadows = true;
        }

        // ------------------------------------------------------------------ Validation (b 12.3)

        /// <summary>The 12.3 rules. Errors and warnings are appended; returns true when there are no errors.</summary>
        public bool Validate(List<string> errors, List<string> warnings)
        {
            int count = palette != null ? palette.Count : 0;
            if (count == 0) errors.Add("palette is empty; every color uses the HSV fallback");
            else if (count > MaxPaletteEntries)
                warnings.Add($"palette has {count} entries; entries {MaxPaletteEntries}+ are ignored (shader limit)");
            if (count > 0 && count < LobbyMaxColorCount)
                warnings.Add($"palette has {count} entries but the lobby offers up to {LobbyMaxColorCount} colors; " +
                             "missing indices use the HSV fallback");

            var seen = new HashSet<string>();
            for (int i = 0; i < count && i < MaxPaletteEntries; i++)
            {
                string n = palette[i].name;
                if (string.IsNullOrWhiteSpace(n)) warnings.Add($"palette entry {i} has no name (shown as \"Color {i + 1}\")");
                else if (!seen.Add(n.Trim())) warnings.Add($"palette name '{n}' is used more than once");
            }
            CheckMesh(nearMesh, "nearMesh", warnings);
            CheckMesh(farMesh, "farMesh", warnings);
            return errors.Count == 0;
        }

        private static void CheckMesh(Mesh m, string field, List<string> warnings)
        {
            if (m == null) return;
            Bounds b = m.bounds;
            if (b.center.magnitude > 0.01f) warnings.Add($"{field} '{m.name}' is not centered on the origin ({b.center})");
            Vector3 e = b.extents;
            if (Mathf.Abs(e.x - 0.5f) > 0.01f || Mathf.Abs(e.y - 0.5f) > 0.01f || Mathf.Abs(e.z - 0.5f) > 0.01f)
                warnings.Add($"{field} '{m.name}' extents {e} are not within 2% of 0.5 (radius-0.5 sphere expected)");
            if (m.subMeshCount != 1) warnings.Add($"{field} '{m.name}' has {m.subMeshCount} submeshes; only submesh 0 is drawn");
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            UnityEditor.EditorApplication.delayCall += DelayedValidate;
        }

        private void DelayedValidate()
        {
            if (this == null) return;
            var errors = new List<string>();
            var warnings = new List<string>();
            Validate(errors, warnings);
            string text = errors.Count + warnings.Count == 0
                ? null
                : string.Join("\n", errors) + (errors.Count > 0 && warnings.Count > 0 ? "\n" : "") + string.Join("\n", warnings);
            if (text == lastValidation) return;
            lastValidation = text;
            if (text == null) return;
            if (errors.Count > 0) Debug.LogError($"[BallVisualConfig] '{name}':\n{text}", this);
            else Debug.LogWarning($"[BallVisualConfig] '{name}':\n{text}", this);
        }
#endif
    }
}

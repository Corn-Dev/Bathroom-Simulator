// VenueConfig: the pit-size profiles the lobby offers and the venue builds (deliverable A, file plan V1; design b 3,
// b 11.1). It replaces BallPitSettingsMap's hardcoded SizeKeys/SizeValues; the built-in defaults are those five sizes,
// so existing saves and the dev scene keep working. VenueMath holds the venue predictions shared by PitSite.TryApplyTo,
// the gizmo and the validator, built on the PitWorld statics the simulation uses. Names no batch-2 type.
using System.Collections.Generic;
using System.Globalization;
using BallPitFinal;
using UnityEngine;

namespace BallPitGameplay
{
    [System.Serializable]
    public sealed class PitSizeProfile
    {
        [Tooltip("Lobby choice value and save key: ^[a-zA-Z0-9_-]{1,24}$, unique. Never rename a shipped key: saves store it.")]
        public string key = "large";
        public string displayName = "Large";
        [Min(1)] public int pitX = 80, pitY = 60, pitZ = 80;
        [Tooltip("Ball diameter (m) for this pit, applied to PitSettings.ballDiameter. 0.5 = today.")]
        public float ballDiameter = 0.5f;
        [Range(1, PitAuthority.MaxPlayers)] public int recommendedPlayersMin = 1;
        [Range(1, PitAuthority.MaxPlayers)] public int recommendedPlayersMax = PitAuthority.MaxPlayers;
        [Tooltip("Free text for the lobby, e.g. \"about 2 h\". Empty = not shown.")]
        public string estimatedPlayTime = "";
        [TextArea] public string notes = "";

        public long BallCount => (long)pitX * pitY * pitZ;
        public string DisplayNameOrKey => string.IsNullOrWhiteSpace(displayName) ? key : displayName;

        /// <summary>Today's lobby label ("Large (80x60x80)") for 0.5 m balls; a retuned size reads "Large (80x60x80, 0.4 m balls)".</summary>
        public string ChoiceLabel
        {
            get
            {
                CultureInfo inv = CultureInfo.InvariantCulture;
                string dims = pitX.ToString(inv) + "x" + pitY.ToString(inv) + "x" + pitZ.ToString(inv);
                if (ballDiameter != 0.5f) dims += ", " + ballDiameter.ToString("0.###", inv) + " m balls";
                return DisplayNameOrKey + " (" + dims + ")";
            }
        }

        public PitSizeProfile Clone() => (PitSizeProfile)MemberwiseClone();

        internal static PitSizeProfile Make(string key, string displayName, int x, int y, int z) =>
            new PitSizeProfile { key = key, displayName = displayName, pitX = x, pitY = y, pitZ = z };
    }

    [CreateAssetMenu(menuName = "Ball Pit/Venue Config", fileName = "BallPitVenueConfig")]
    public sealed class VenueConfig : ScriptableObject
    {
        public const string ResourcePath = "BallPitVenueConfig";   // Resources/BallPitVenueConfig.asset
        public const int MaxGpuBalls = 65535 * 64;                // GpuBallRenderer: (capacity + 63) / 64 <= 65535
        /// <summary>Equal to BallPitWorldSave.MaxPitAxis (the save's per-axis rule); gameplay never names BallPitSession.</summary>
        public const int MaxPitAxis = 1024;
        /// <summary>The kit's SettingDefinition.MaxChoices.</summary>
        public const int MaxProfiles = 32;
        /// <summary>Equal to BallPitSettingsMap.DefaultPitSize.</summary>
        public const string DefaultKey = "large";
        public const int MaxKeyLength = 24;
        public const float MinBallDiameter = 0.01f, MaxBallDiameter = 10f;                 // the save's FILL rule
        public const float SupportedMinBallDiameter = 0.15f, SupportedMaxBallDiameter = 0.5f; // the range A behaves in

        [SerializeField] private List<PitSizeProfile> profiles = new List<PitSizeProfile>();
        [SerializeField] private string defaultProfileKey = DefaultKey;

        [System.NonSerialized] private List<PitSizeProfile> sanitized;
        [System.NonSerialized] private PitSizeProfile defaultProfile;
#if UNITY_EDITOR
        [System.NonSerialized] private string lastValidation;
#endif

        private static VenueConfig cachedDefault;
        private static bool warnedNoAsset;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { cachedDefault = null; warnedNoAsset = false; }

        /// <summary>The sanitized profiles (invalid and duplicate entries skipped, players clamped), built once per instance.</summary>
        public IReadOnlyList<PitSizeProfile> Profiles { get { EnsureSanitized(); return sanitized; } }
        /// <summary>The profile named by defaultProfileKey, else the first valid profile.</summary>
        public PitSizeProfile DefaultProfile { get { EnsureSanitized(); return defaultProfile; } }
        /// <summary>True for the in-memory fallback used when no Resources asset exists.</summary>
        public bool IsBuiltInDefault { get; private set; }

        /// <summary>Ordinal key match on the sanitized profiles.</summary>
        public bool TryGetProfile(string key, out PitSizeProfile profile)
        {
            IReadOnlyList<PitSizeProfile> list = Profiles;
            for (int i = 0; i < list.Count; i++)
                if (string.Equals(list[i].key, key, System.StringComparison.Ordinal)) { profile = list[i]; return true; }
            profile = null;
            return false;
        }

        /// <summary>The design b 3.3 rules, for the editor and the validator. Logs nothing.</summary>
        public void Validate(List<string> errors, List<string> warnings) => Sanitize(errors, warnings, out _, out _);

        public static VenueConfig Resolve(VenueConfig assigned) => assigned != null ? assigned : Load();

        /// <summary>The asset at Resources/BallPitVenueConfig, else a cached in-memory CreateDefault() (one Warning per process).</summary>
        public static VenueConfig Load()
        {
            var asset = Resources.Load<VenueConfig>(ResourcePath);
            if (asset != null) return asset;
            if (!warnedNoAsset)
            {
                warnedNoAsset = true;
                Debug.LogWarning("[VenueConfig] no VenueConfig asset at Resources/" + ResourcePath +
                                 "; using the built-in sizes (Tools > Ball Pit > Create Default Venue Config)");
            }
            if (cachedDefault == null)
            {
                cachedDefault = CreateDefault();
                cachedDefault.hideFlags = HideFlags.HideAndDontSave;
                cachedDefault.IsBuiltInDefault = true;
            }
            return cachedDefault;
        }

        /// <summary>A new in-memory config holding today's five sizes (also what Reset fills in).</summary>
        public static VenueConfig CreateDefault()
        {
            var c = CreateInstance<VenueConfig>();
            c.FillDefaults();
            return c;
        }

        /// <summary>Today's SizeKeys/SizeValues, in order, all 0.5 m balls and 1-MaxPlayers players.</summary>
        public static List<PitSizeProfile> BuiltInProfiles() => new List<PitSizeProfile>
        {
            PitSizeProfile.Make("small", "Small", 40, 25, 40),
            PitSizeProfile.Make("medium", "Medium", 60, 40, 60),
            PitSizeProfile.Make("large", "Large", 80, 60, 80),
            PitSizeProfile.Make("huge", "Huge", 100, 100, 100),
            PitSizeProfile.Make("giant", "Giant", 133, 133, 133),
        };

        private void Reset() => FillDefaults();

        private void FillDefaults()
        {
            profiles = BuiltInProfiles();
            defaultProfileKey = DefaultKey;
            sanitized = null;
            defaultProfile = null;
        }

        // ------------------------------------------------------------------ Sanitizing (b 3.3)

        private void EnsureSanitized()
        {
            if (sanitized != null) return;
            var errors = new List<string>();
            var warnings = new List<string>();
            sanitized = Sanitize(errors, warnings, out defaultProfile, out string defaultWarning);
            for (int i = 0; i < errors.Count; i++) Debug.LogError($"[VenueConfig] '{name}': {errors[i]}", this);
            if (defaultWarning != null) Debug.LogWarning($"[VenueConfig] '{name}': {defaultWarning}", this);
        }

        private List<PitSizeProfile> Sanitize(List<string> errors, List<string> warnings, out PitSizeProfile def,
                                              out string defaultWarning)
        {
            var list = new List<PitSizeProfile>();
            var keys = new HashSet<string>(System.StringComparer.Ordinal);
            int count = profiles != null ? profiles.Count : 0;
            for (int i = 0; i < count; i++)
            {
                PitSizeProfile p = profiles[i];
                if (p == null) continue;
                string where = $"profile {i} '{p.key}'";
                if (!IsValidKey(p.key)) { errors.Add($"{where}: key must match ^[a-zA-Z0-9_-]{{1,{MaxKeyLength}}}$; skipped"); continue; }
                if (!keys.Add(p.key)) { errors.Add($"{where}: duplicate key; skipped"); continue; }
                if (p.pitX < 1 || p.pitY < 1 || p.pitZ < 1 || p.pitX > MaxPitAxis || p.pitY > MaxPitAxis || p.pitZ > MaxPitAxis)
                { errors.Add($"{where}: pitX/pitY/pitZ must be 1..{MaxPitAxis}; skipped"); continue; }
                if (p.BallCount > MaxGpuBalls) { errors.Add($"{where}: {p.BallCount:n0} balls exceeds the renderer limit {MaxGpuBalls:n0}; skipped"); continue; }
                if (float.IsNaN(p.ballDiameter) || float.IsInfinity(p.ballDiameter) ||
                    p.ballDiameter < MinBallDiameter || p.ballDiameter > MaxBallDiameter)
                { errors.Add($"{where}: ballDiameter {p.ballDiameter} must be {MinBallDiameter}..{MaxBallDiameter} m; skipped"); continue; }
                if (list.Count >= MaxProfiles) { errors.Add($"{where}: more than {MaxProfiles} profiles; ignored"); continue; }

                PitSizeProfile q = p.Clone(); // clamping never edits the asset
                if (q.ballDiameter < SupportedMinBallDiameter || q.ballDiameter > SupportedMaxBallDiameter)
                    warnings.Add($"{where}: ballDiameter {q.ballDiameter} m is outside {SupportedMinBallDiameter}..{SupportedMaxBallDiameter} m, " +
                                 "the range the player and pit tuning is built for");
                if (string.IsNullOrWhiteSpace(q.displayName)) warnings.Add($"{where}: no display name; the key is shown");
                int min = Mathf.Clamp(q.recommendedPlayersMin, 1, PitAuthority.MaxPlayers);
                int max = Mathf.Clamp(q.recommendedPlayersMax, min, PitAuthority.MaxPlayers);
                if (min != q.recommendedPlayersMin || max != q.recommendedPlayersMax)
                {
                    warnings.Add($"{where}: recommended players {q.recommendedPlayersMin}-{q.recommendedPlayersMax} clamped to {min}-{max}");
                    q.recommendedPlayersMin = min; q.recommendedPlayersMax = max;
                }
                list.Add(q);
            }

            defaultWarning = null;
            if (list.Count == 0)
            {
                errors.Add("no valid profile; the built-in sizes are used");
                list = BuiltInProfiles();
            }
            def = null;
            for (int i = 0; i < list.Count; i++)
                if (string.Equals(list[i].key, defaultProfileKey, System.StringComparison.Ordinal)) { def = list[i]; break; }
            if (def == null)
            {
                def = list[0];
                defaultWarning = $"defaultProfileKey '{defaultProfileKey}' names no valid profile; '{def.key}' is the default";
                warnings.Add(defaultWarning);
            }
            return list;
        }

        /// <summary>The kit's SettingChoice value rule: ^[a-zA-Z0-9_-]{1,24}$.</summary>
        public static bool IsValidKey(string key)
        {
            if (string.IsNullOrEmpty(key) || key.Length > MaxKeyLength) return false;
            for (int i = 0; i < key.Length; i++)
            {
                char c = key[i];
                bool ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_' || c == '-';
                if (!ok) return false;
            }
            return true;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            sanitized = null; // rebuilt from the edited list on next access
            defaultProfile = null;
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
            if (errors.Count > 0) Debug.LogError($"[VenueConfig] '{name}':\n{text}", this);
            else Debug.LogWarning($"[VenueConfig] '{name}':\n{text}", this);
        }
#endif
    }

    /// <summary>
    /// Venue predictions and limits (design b 11.1), built on the PitWorld statics so the venue binding, the gizmo and
    /// the validator agree with the simulation. Boxes and planes only, never per ball.
    /// </summary>
    public static class VenueMath
    {
        /// <summary>Collision proxy thickness (m): PitSiteEnvironment's, equal to PitWorld.BuildWalls' wall thickness.</summary>
        public const float ProxyThickness = 1f;
        /// <summary>SpatialGrid chunk-table size limits for a venue box: warn above 64 MB, reject above 512 MB.</summary>
        public const long WarnGridIndexBytes = 64L << 20, MaxGridIndexBytes = 512L << 20;

        /// <summary>
        /// baseSettings (the scene controller's inspector settings, else defaults) cloned with the profile's size and
        /// diameter and pitAnchor = anchorPoint, play-bounds override off: the pit a new session at that anchor builds.
        /// </summary>
        public static PitSettings PreviewSettings(Vector3 anchorPoint, PitSizeProfile p, PitSettings baseSettings)
        {
            PitSettings s = (baseSettings ?? new PitSettings()).Clone();
            if (p != null) { s.pitX = p.pitX; s.pitY = p.pitY; s.pitZ = p.pitZ; s.ballDiameter = p.ballDiameter; }
            s.pitAnchor = anchorPoint;
            s.overridePlayBounds = false;
            return s;
        }

        /// <summary>Predicted WallTop (fill top + 2 diameters) for s.fillType: exact-ish for FCC, an estimate for Random.</summary>
        public static float PredictWallTop(PitSettings s) =>
            (s.fillType == FillType.Fcc ? PitWorld.PredictFccFillTop(s) : EstimateRandomFillTop(s)) + 2f * s.ballDiameter;

        /// <summary>Estimated Random fill top: floor + n(pi/6)dc^3 / (0.56 A) + dc. An estimate (RandomFill packs ~0.58).</summary>
        public static float EstimateRandomFillTop(PitSettings s)
        {
            PitWorld.ComputeInterior(s, out float x0, out float x1, out float z0, out float z1, out float floorY);
            float dc = 2.002f * PitWorld.ColliderRadiusOf(s);
            double area = System.Math.Max(1e-6, (double)(x1 - x0 - dc) * (z1 - z0 - dc));
            double n = (double)s.pitX * s.pitY * s.pitZ;
            return floorY + (float)(n * (System.Math.PI / 6.0) * dc * dc * dc / (0.56 * area)) + dc;
        }

        /// <summary>The collision proxies' outer box (floor and four walls from FloorY - T up to the rim top), using the
        /// higher of the FCC and Random WallTop predictions so it holds for either fill choice.</summary>
        public static Bounds PredictProxyBox(PitSettings s, float rimHeight)
        {
            PitWorld.ComputeInterior(s, out float x0, out float x1, out float z0, out float z1, out float floorY);
            float wallTop = Mathf.Max(PitWorld.PredictFccFillTop(s), EstimateRandomFillTop(s)) + 2f * s.ballDiameter;
            float rimTop = rimHeight > 0f ? Mathf.Max(wallTop, floorY + rimHeight) : wallTop;
            const float t = ProxyThickness;
            var b = new Bounds();
            b.SetMinMax(new Vector3(x0 - t, floorY - t, z0 - t), new Vector3(x1 + t, rimTop, z1 + t));
            return b;
        }

        /// <summary>Bytes of the two SpatialGrid chunk-pointer tables (Resting + Live) for a box at diameter d.</summary>
        public static long GridIndexBytes(Vector3 boundsSize, float d)
        {
            long cx = Chunks(boundsSize.x, d), cy = Chunks(boundsSize.y, d), cz = Chunks(boundsSize.z, d);
            if (cx < 0 || cy < 0 || cz < 0) return long.MaxValue;
            double bytes = 2.0 * 8.0 * cx * cy * cz;
            return bytes >= long.MaxValue ? long.MaxValue : (long)bytes;
        }

        // ceil(ceil(v / d) / 8), SpatialGrid's chunk count per axis; -1 when not representable.
        private static long Chunks(float v, float d)
        {
            double cells = System.Math.Max(1.0, System.Math.Ceiling((double)(v * (1f / d))));
            if (double.IsNaN(cells) || cells > 1e15) return -1;
            return ((long)cells + 7) / 8;
        }

        /// <summary>PitWorld.GridCellCount: must stay at or below PitWorld.MaxGridCells.</summary>
        public static double GridCellCount(Vector3 boundsSize, float d) => PitWorld.GridCellCount(boundsSize, d);

        /// <summary>Rest-pose resolution (Q21): largest axis / 2,097,151.</summary>
        public static float QuantumQ21(Bounds b) => MaxAxis(b) / 2097151f;
        /// <summary>Moving-snapshot resolution (Q16): largest axis / 65,535.</summary>
        public static float QuantumQ16(Bounds b) => MaxAxis(b) / 65535f;

        private static float MaxAxis(Bounds b) => Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
    }
}

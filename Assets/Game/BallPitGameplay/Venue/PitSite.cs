// PitSite: a place in the gameplay scene where a pit can exist (deliverable A, file plan V2; design b 4.1, 4.6, 4.7,
// 10, 11.3; brief 11.1-11.2). Discovered by type (PitSites); no parent or child is required. TryApplyTo is the only
// code that writes venue values into a PitSettings. Also PitSiteEnvironment, the invisible collision proxies (floor
// and four inner walls) the simulation needs, built from the world's own geometry.
using System.Collections.Generic;
using System.Globalization;
using BallPitFinal;
using UnityEngine;

namespace BallPitGameplay
{
    [DisallowMultipleComponent]
    public sealed class PitSite : MonoBehaviour, IPitSite
    {
        /// <summary>Equal to BallPitGameState.MaxTagLength (the save's key rule); gameplay never names BallPitSession.</summary>
        public const int MaxSiteIdLength = 32;
        /// <summary>Largest anchor coordinate (m): float spacing there is about 1 mm, the normalization grid.</summary>
        public const float MaxAnchorCoordinate = 10000f;

        [Tooltip("Unique id, saved with the world: ^[a-z0-9_.]{1,32}$ (the save key rule). Never rename a shipped id.")]
        [SerializeField] private string siteId = "site";
        [Tooltip("VenueConfig profile (pit dimensions, ball size). The default selector matches it to the lobby's pitSize.")]
        [SerializeField] private string profileKey = "large";
        [Tooltip("Pit floor center. Only its position is used; its world rotation must be identity (validated).")]
        [SerializeField] private Transform anchor;
        [Tooltip("Optional. Activated while this site is active and deactivated otherwise. Nothing has to live under it.")]
        [SerializeField] private GameObject siteRoot;
        [Tooltip("Axis-aligned box, relative to the anchor, of everywhere balls may exist (pit + surrounding floor + throw " +
                 "headroom). Becomes PitWorld.PlayBounds. Use the context menu 'Fit Venue Bounds To Pit' as a start.")]
        [SerializeField] private Bounds venueBounds;
        [Tooltip("Pool rim height above the floor (m). The proxy walls rise to max(fill WallTop, rim). 0 = WallTop only.")]
        [SerializeField, Min(0f)] private float rimHeight;
        [Tooltip("Layer of this site's invisible collision proxies. Not the ball layer, not the Player layer.")]
        [SerializeField] private int proxyLayer = 0;
        [Tooltip("Optional loading view: an authored Camera placed anywhere, may be shared by sites. Leave it disabled in " +
                 "the scene. Site activation disables every site's overview camera and AudioListener; the runtime " +
                 "enables the active site's one while no player camera is registered.")]
        [SerializeField] private Camera overviewCamera;

        // Set by PitSites.Activate: a site whose GameObject first wakes inside its own activation keeps its root on.
        internal bool IsActiveSite;

#if UNITY_EDITOR
        [System.NonSerialized] private string lastValidation;
#endif

        public string SiteId => siteId;
        public string ProfileKey => profileKey;
        public Transform Anchor => anchor;
        public GameObject SiteRoot => siteRoot;
        /// <summary>The rim height; a non-finite or negative value reads as 0.</summary>
        public float RimHeight => float.IsNaN(rimHeight) || float.IsInfinity(rimHeight) || rimHeight < 0f ? 0f : rimHeight;
        public int ProxyLayer => proxyLayer;
        /// <summary>The site's loading view, or null (the runtime's fallback loading camera).</summary>
        public Camera OverviewCamera => overviewCamera;
        /// <summary>The anchor position on the 1 mm grid. Callers check Anchor != null first.</summary>
        public Vector3 AnchorPoint => NormalizeAnchor(anchor.position);
        /// <summary>venueBounds placed at the anchor point (world space). Callers check Anchor != null first.</summary>
        public Bounds WorldVenueBounds => new Bounds(AnchorPoint + venueBounds.center, venueBounds.size);

        /// <summary>Rounds to the 1 mm grid and strips -0, so every peer derives the same anchor and "zero" is exact.</summary>
        public static Vector3 NormalizeAnchor(Vector3 p) => new Vector3(Mm(p.x), Mm(p.y), Mm(p.z));

        private static float Mm(float v)
        {
            float r = Mathf.Round(v * 1000f) / 1000f;
            return r == 0f ? 0f : r; // -0 -> +0
        }

        /// <summary>1..32 characters of [a-z0-9_.]: equal to BallPitGameState.IsValidKey(id, MaxTagLength).</summary>
        public static bool IsValidSiteId(string id)
        {
            if (string.IsNullOrEmpty(id) || id.Length > MaxSiteIdLength) return false;
            for (int i = 0; i < id.Length; i++)
            {
                char c = id[i];
                if (!((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_' || c == '.')) return false;
            }
            return true;
        }

        /// <summary>
        /// Writes this site into s (design b 4.7): the profile's size and diameter (when profile != null), the anchor,
        /// the venue bounds as the play-bounds override (when they pass the checks), and no PitWorld walls. False only
        /// for a missing or unusable anchor, which is fatal for a host session.
        /// </summary>
        public bool TryApplyTo(PitSettings s, PitSizeProfile profile, out string error)
        {
            error = null;
            if (s == null) { error = $"PitSite '{siteId}': no settings to apply to"; return false; }
            if (anchor == null) { error = $"PitSite '{siteId}': anchor not assigned"; return false; }
            Vector3 raw = anchor.position;
            if (!IsUsableAnchor(raw))
            {
                error = $"PitSite '{siteId}': anchor position {Fmt(raw)} is not finite or beyond +-{MaxAnchorCoordinate} m";
                return false;
            }
            if (Quaternion.Angle(anchor.rotation, Quaternion.identity) > 0.01f)
                Debug.LogError($"[PitSite] '{siteId}': anchor '{anchor.name}' is rotated; only its position is used", this);

            if (profile != null)
            {
                s.pitX = profile.pitX; s.pitY = profile.pitY; s.pitZ = profile.pitZ;
                s.ballDiameter = profile.ballDiameter;
            }
            s.pitAnchor = AnchorPoint;
            if (CheckVenueBounds(s, out string boundsError))
            {
                s.overridePlayBounds = true;
                s.playBoundsOverride = WorldVenueBounds;
            }
            else
            {
                s.overridePlayBounds = false;
                Debug.LogError($"[PitSite] '{siteId}': {boundsError}; using the computed bounds around the anchor", this);
            }
            s.pitWalls = false;   // the collision proxies replace PitWorld's walls
            s.renderWalls = false;
            return true;
        }

        /// <summary>The venueBounds rules of TryApplyTo, for settings that already carry this site's size and anchor:
        /// a finite positive box that contains the predicted proxy box, within the grid-index and cell-count limits.</summary>
        public bool CheckVenueBounds(PitSettings s, out string error)
        {
            Vector3 size = venueBounds.size;
            if (!Finite(size) || !Finite(venueBounds.center) || size.x <= 0f || size.y <= 0f || size.z <= 0f)
            { error = "venueBounds is not set (every size axis must be > 0)"; return false; }
            Bounds world = WorldVenueBounds;
            Bounds proxies = VenueMath.PredictProxyBox(s, RimHeight);
            if (!Encloses(world, proxies))
            {
                error = $"venueBounds {Fmt(world.min)}..{Fmt(world.max)} does not contain the pit and its proxies " +
                        $"{Fmt(proxies.min)}..{Fmt(proxies.max)}";
                return false;
            }
            long bytes = VenueMath.GridIndexBytes(size, s.ballDiameter);
            if (bytes > VenueMath.MaxGridIndexBytes)
            {
                error = $"venueBounds needs {bytes >> 20} MB of grid index at {s.ballDiameter} m balls (limit {VenueMath.MaxGridIndexBytes >> 20} MB)";
                return false;
            }
            double cells = VenueMath.GridCellCount(size, s.ballDiameter);
            if (cells > PitWorld.MaxGridCells)
            {
                error = $"venueBounds holds {cells:0} grid cells at {s.ballDiameter} m balls (limit {PitWorld.MaxGridCells:0})";
                return false;
            }
            error = null;
            return true;
        }

        /// <summary>The site's own cheap rules (design b 11.3): siteId format, profile present, anchor assigned, usable and
        /// unrotated, venueBounds set (Errors); no rim height (Warning). Logs nothing. Duplicates are PitSites' rules.</summary>
        public void Validate(VenueConfig config, List<string> errors, List<string> warnings)
        {
            if (!IsValidSiteId(siteId)) errors.Add($"siteId '{siteId}' must match ^[a-z0-9_.]{{1,{MaxSiteIdLength}}}$");
            if (config != null && !config.TryGetProfile(profileKey, out _))
                errors.Add($"profile '{profileKey}' is not in VenueConfig '{config.name}'");
            if (anchor == null) errors.Add("anchor not assigned");
            else
            {
                if (!IsUsableAnchor(anchor.position))
                    errors.Add($"anchor position {Fmt(anchor.position)} is not finite or beyond +-{MaxAnchorCoordinate} m");
                if (Quaternion.Angle(anchor.rotation, Quaternion.identity) > 0.01f)
                    errors.Add($"anchor '{anchor.name}' is rotated; it must be axis-aligned (only its position is used)");
            }
            Vector3 size = venueBounds.size;
            if (!Finite(size) || size.x <= 0f || size.y <= 0f || size.z <= 0f)
                errors.Add("venueBounds size is 0 (context menu: Fit Venue Bounds To Pit)");
            if (warnings != null && RimHeight == 0f) warnings.Add("rimHeight is 0: the proxy walls stop at the fill's WallTop");
        }

        private void Awake()
        {
            if (IsActiveSite) return; // woke inside its own activation (PitSites.Activate)
            if (siteRoot != null) siteRoot.SetActive(false); // activation is explicit (PitSites.Activate)
            DisableOverview(overviewCamera);
        }

        /// <summary>Camera.enabled = false and its AudioListener (if any) disabled; null-safe.</summary>
        internal static void DisableOverview(Camera c)
        {
            if (c == null) return;
            c.enabled = false;
            var listener = c.GetComponent<AudioListener>();
            if (listener != null) listener.enabled = false;
        }

        internal static bool IsUsableAnchor(Vector3 p) =>
            Finite(p) && Mathf.Abs(p.x) <= MaxAnchorCoordinate && Mathf.Abs(p.y) <= MaxAnchorCoordinate &&
            Mathf.Abs(p.z) <= MaxAnchorCoordinate;

        internal static bool Finite(Vector3 v) => Finite(v.x) && Finite(v.y) && Finite(v.z);
        private static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);

        // Inclusive box containment.
        private static bool Encloses(Bounds outer, Bounds inner)
        {
            Vector3 a = outer.min, b = outer.max, c = inner.min, d = inner.max;
            return c.x >= a.x && c.y >= a.y && c.z >= a.z && d.x <= b.x && d.y <= b.y && d.z <= b.z;
        }

        internal static string Fmt(Vector3 v)
        {
            CultureInfo inv = CultureInfo.InvariantCulture;
            return "(" + v.x.ToString("0.###", inv) + ", " + v.y.ToString("0.###", inv) + ", " + v.z.ToString("0.###", inv) + ")";
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            UnityEditor.EditorApplication.delayCall -= DelayedValidate; // Resources and scene queries are unsafe here
            UnityEditor.EditorApplication.delayCall += DelayedValidate;
        }

        private void DelayedValidate()
        {
            if (this == null) return;
            var errors = new List<string>();
            var warnings = new List<string>();
            Validate(VenueConfig.Load(), errors, warnings);
            errors.AddRange(warnings);
            string text = errors.Count == 0 ? null : string.Join("\n", errors);
            if (text == lastValidation) return;
            lastValidation = text;
            if (text != null) Debug.LogWarning($"[PitSite] '{siteId}':\n{text}", this);
        }
#endif
    }

    /// <summary>
    /// The site's invisible collision proxies (design b 10): the pit floor (interior only) and four inner walls up to
    /// max(WallTop, floor + rim), each a BoxCollider plus a ClimbableSurface, under one unparented root. Built from the
    /// world's own geometry, so the inner faces lie exactly on the planes the fill uses. No renderers, no material.
    /// </summary>
    public sealed class PitSiteEnvironment : IPitEnvironment
    {
        /// <summary>Equal to PitWorld.BuildWalls' wall thickness (m), independent of the ball size.</summary>
        public const float ProxyThickness = VenueMath.ProxyThickness;

        private static readonly HashSet<string> warnedNoRim = new HashSet<string>();
        private readonly string siteId;
        private GameObject root;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => warnedNoRim.Clear();

        public PitSiteEnvironment(PitSite site)
        {
            if (site == null) throw new System.ArgumentNullException(nameof(site));
            Site = site;
            siteId = site.SiteId;
        }

        public PitSite Site { get; }
        public bool IsBuilt { get; private set; }
        /// <summary>While built: the world the proxies were built for.</summary>
        public PitWorld World { get; private set; }
        /// <summary>While built: the top of the proxy walls.</summary>
        public float RimTopY { get; private set; }
        /// <summary>While built: the outer box of all five proxies.</summary>
        public Bounds ProxyBox { get; private set; }

        /// <summary>Builds the proxies for w (every peer, inside the controller's BuildRoutine). Throws only on an engine
        /// failure, which fails the build; a partial set is removed by Teardown.</summary>
        public void Build(PitWorld w)
        {
            if (w == null) throw new System.ArgumentNullException(nameof(w));
            Teardown();
            const float t = ProxyThickness;
            float rim = Site.RimHeight;
            float rimTop = rim > 0f ? Mathf.Max(w.WallTop, w.FloorY + rim) : w.WallTop;
            float cx = (w.WallMinX + w.WallMaxX) * 0.5f, cz = (w.WallMinZ + w.WallMaxZ) * 0.5f;
            float lenX = w.WallMaxX - w.WallMinX, lenZ = w.WallMaxZ - w.WallMinZ;
            float yBot = w.FloorY - t;
            float yc = (yBot + rimTop) * 0.5f, h = rimTop - yBot;
            int layer = ResolveLayer(w);

            root = new GameObject("PitCollisionProxies [" + siteId + "]"); // unparented, in the active scene
            Proxy("Floor", new Vector3(cx, w.FloorY - t * 0.5f, cz), new Vector3(lenX, t, lenZ), layer);
            Proxy("Wall -X", new Vector3(w.WallMinX - t * 0.5f, yc, cz), new Vector3(t, h, lenZ + 2f * t), layer);
            Proxy("Wall +X", new Vector3(w.WallMaxX + t * 0.5f, yc, cz), new Vector3(t, h, lenZ + 2f * t), layer);
            Proxy("Wall -Z", new Vector3(cx, yc, w.WallMinZ - t * 0.5f), new Vector3(lenX + 2f * t, h, t), layer);
            Proxy("Wall +Z", new Vector3(cx, yc, w.WallMaxZ + t * 0.5f), new Vector3(lenX + 2f * t, h, t), layer);
            Physics.SyncTransforms(); // autoSyncTransforms is off

            Bounds region = w.PlayBounds;
            region.Expand(4f * w.Diameter); // 2 diameters per side: nothing farther can touch a resting ball
            w.EnvironmentRegion = region;

            var box = new Bounds();
            box.SetMinMax(new Vector3(w.WallMinX - t, yBot, w.WallMinZ - t), new Vector3(w.WallMaxX + t, rimTop, w.WallMaxZ + t));
            ProxyBox = box;
            RimTopY = rimTop;
            World = w;
            IsBuilt = true;

            if (rim > 0f && w.WallTop > w.FloorY + rim)
                Debug.LogWarning($"[PitSite] site '{siteId}': fill top + 2 balls (y={w.WallTop:0.###}) is " +
                                 $"{w.WallTop - (w.FloorY + rim):0.###} m above the authored rim (y={w.FloorY + rim:0.###}); " +
                                 "proxy walls rise to WallTop, above the pool art");
            else if (rim == 0f && warnedNoRim.Add(siteId))
                Debug.LogWarning($"[PitSite] site '{siteId}' has no rim height; proxy walls stop at this fill's WallTop");
        }

        /// <summary>Deactivates, then destroys the root (so scene queries in this frame no longer see the proxies).
        /// Idempotent; also removes a partial set, and skips a root the scene unload already destroyed.</summary>
        public void Teardown()
        {
            if (root != null)
            {
                root.SetActive(false);
                Object.Destroy(root);
            }
            root = null;
            IsBuilt = false;
            World = null;
        }

        // Positioned first, then the collider (autoSyncTransforms is off), then the marker, whose OnEnable registers it.
        private void Proxy(string name, Vector3 center, Vector3 size, int layer)
        {
            var go = new GameObject(name);
            go.layer = layer;
            go.transform.SetParent(root.transform, false);
            go.transform.position = center;
            var box = go.AddComponent<BoxCollider>();
            box.size = size;
            go.AddComponent<ClimbableSurface>();
        }

        private int ResolveLayer(PitWorld w)
        {
            int layer = Site.ProxyLayer;
            bool bad = layer < 0 || layer > 31 || layer == w.S.ballLayer || (w.S.playerLayer >= 0 && layer == w.S.playerLayer);
            if (!bad) return layer;
            Debug.LogError($"[PitSite] site '{siteId}': proxyLayer {layer} is outside 0..31 or equals the ball or Player layer; " +
                           "using layer 0 (Default)");
            return 0;
        }
    }
}

// PlayerSpawnPoint: an authored spawn marker placed anywhere, with an optional site and slot (deliverable A, file plan V4;
// design d.9; brief 11.5). Also PlayerSpawnResolver (slot -> pose: active-site and unlinked points, slot match, then
// round-robin skipping points a player capsule blocks; the computed ring and the pile top as fallbacks, with no
// geometry), the spawn types, and PlayerSpawnValidation, the rules the editor validator runs. Every peer computes the
// same answer from the same scene data. Links to the spawner and the volumes only through SpawnTemplate and the
// forbidden delegate.
using System.Collections.Generic;
using BallPitFinal;
using UnityEngine;

namespace BallPitGameplay
{
    [DisallowMultipleComponent]
    public sealed class PlayerSpawnPoint : MonoBehaviour
    {
        [Tooltip("Optional. The site this point serves; empty = whichever site is active.")]
        public PitSite site;
        [Tooltip("Preferred player slot (0-based); -1 = any. Facing is this transform's yaw; feet at its position.")]
        [Min(-1)] public int slot = -1;

        private static readonly List<PlayerSpawnPoint> active = new List<PlayerSpawnPoint>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => active.Clear();

        /// <summary>Enabled points (OnEnable/OnDisable): a point under an inactive site root is never usable.</summary>
        public static IReadOnlyList<PlayerSpawnPoint> Active => active;
        public Vector3 Feet => transform.position;
        /// <summary>The forward vector's heading on XZ (degrees, [0, 360)); 0 when it points straight up or down.</summary>
        public float Yaw
        {
            get
            {
                Vector3 f = transform.forward;
                return f.x * f.x + f.z * f.z < 1e-8f ? 0f : LookMath.WrapYaw(Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg);
            }
        }

        private void OnEnable() { if (!active.Contains(this)) active.Add(this); }
        private void OnDisable() => active.Remove(this);

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (Vector3.Angle(transform.up, Vector3.up) > 1f)
                Debug.LogWarning($"[PlayerSpawnPoint] '{name}' has pitch or roll; only its yaw is used", this);
            if (slot >= PitAuthority.MaxPlayers)
                Debug.LogWarning($"[PlayerSpawnPoint] '{name}': slot {slot} never matches (slots are 0..{PitAuthority.MaxPlayers - 1}); it acts as 'any'", this);
        }
#endif
    }

    public enum SpawnSource { None, Authored, AuthoredBlocked, Ring, PileTop }

    /// <summary>What the resolver needs to know about the player prefab (design d.9, d.10).</summary>
    public readonly struct SpawnTemplate
    {
        /// <summary>Non-finite or non-positive dimensions fall back to 0.35 / 1.8.</summary>
        public SpawnTemplate(float radius, float height, bool requireInsidePlayBounds)
        {
            Radius = float.IsNaN(radius) || float.IsInfinity(radius) || radius <= 0f ? 0.35f : radius;
            Height = float.IsNaN(height) || float.IsInfinity(height) || height <= 0f ? 1.8f : height;
            RequireInsidePlayBounds = requireInsidePlayBounds;
        }

        public float Radius { get; }
        public float Height { get; }
        /// <summary>The prefab PlayerConfig's respawnOutsidePlayBounds, so the spawn rule and the teleport rule agree.</summary>
        public bool RequireInsidePlayBounds { get; }
        /// <summary>PlayerConfig's defaults (0.35, 1.8, out-of-bounds rule on).</summary>
        public static SpawnTemplate Default => new SpawnTemplate(0.35f, 1.8f, true);
    }

    public readonly struct SpawnCandidate
    {
        public SpawnCandidate(Vector3 feet, float yaw, int slot) { Feet = feet; Yaw = yaw; Slot = slot; }
        public Vector3 Feet { get; }
        public float Yaw { get; }
        public int Slot { get; }
    }

    public static class PlayerSpawnResolver
    {
        private static readonly List<SpawnCandidate> candidates = new List<SpawnCandidate>();
        private static readonly HashSet<string> warned = new HashSet<string>();
        private static readonly PlayerPitPhysicsConfig defaults = new PlayerPitPhysicsConfig();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { candidates.Clear(); warned.Clear(); }

        /// <summary>PlayerSpawner.BeginSession: the once-per-session warnings may log again.</summary>
        public static void ResetSessionWarnings() => warned.Clear();

        /// <summary>
        /// The spawn pose for a slot (design d.9 steps 1-6). ignore = the requester's capsule (null: template's capsule).
        /// forbidden = an extra arrival test (the spawner passes the out-of-bounds volumes), null = none. None only when no
        /// point is usable and there is no world for the ring.
        /// </summary>
        public static SpawnSource Resolve(int slot, Collider ignore, in SpawnTemplate template, IPitSite activeSite, PitWorld world,
                                          PlayerPitPhysicsConfig cfg, System.Func<Vector3, bool> forbidden, out Vector3 feet, out float yaw)
        {
            feet = Vector3.zero; yaw = 0f;
            if (cfg == null) cfg = defaults;
            PitSite site = activeSite as PitSite;
            var own = ignore as CapsuleCollider;
            float rSpawn = own != null ? own.radius : template.Radius, hSpawn = own != null ? own.height : template.Height;
            float clearance = cfg.spawnClearance;
            bool hasWorld = world != null;
            Bounds play = hasWorld ? world.PlayBounds : default;

            // 1. Usable points: the active site's and unlinked ones that pass the arrival filter, in a peer-independent order.
            candidates.Clear();
            IReadOnlyList<PlayerSpawnPoint> points = PlayerSpawnPoint.Active;
            for (int i = 0; i < points.Count; i++)
            {
                PlayerSpawnPoint p = points[i];
                if (p == null || !(p.site == null || p.site == site)) continue;
                if (!IsArrivalSafe(p.Feet, hasWorld, play, template.RequireInsidePlayBounds, forbidden))
                {
                    bool outside = hasWorld && template.RequireInsidePlayBounds && !play.Contains(p.Feet + Vector3.up * MotorMath.BoundsProbeLift);
                    WarnOnce("point:" + p.GetInstanceID(), $"PlayerSpawnPoint '{PitSites.PathOf(p)}' is " +
                             (outside ? "outside PlayBounds" : "inside an OutOfBoundsVolume") + "; skipped", p);
                    continue;
                }
                candidates.Add(new SpawnCandidate(p.Feet, p.Yaw, p.slot));
            }
            candidates.Sort(CompareCandidates);

            // 2-5. Slot match, then round-robin over unblocked points, then a blocked point (the teleport lifts it).
            if (candidates.Count > 0)
            {
                int pick = Select(candidates, slot, k => IsBlocked(candidates[k].Feet, rSpawn, hSpawn, ignore, clearance), out SpawnSource source);
                if (source == SpawnSource.AuthoredBlocked)
                    WarnOnce("blocked", "every usable PlayerSpawnPoint is blocked by a player; the teleport lifts the player clear", null);
                feet = candidates[pick].Feet;
                yaw = candidates[pick].Yaw;
                return source;
            }
            if (!hasWorld) return SpawnSource.None; // the caller waits for a world

            // 6. The computed ring on the floor around the pit, else the pile top. No objects are created.
            string siteName = site != null ? site.SiteId : "(no site)";
            float topY = Mathf.Max(world.WallTop, world.FloorY + (site != null ? site.RimHeight : 0f));
            var center = new Vector3(world.PitCenter.x, topY, world.PitCenter.z);
            float ringRadius = 0.5f * Mathf.Sqrt(world.PitSize.x * world.PitSize.x + world.PitSize.z * world.PitSize.z) + cfg.spawnRingMargin;
            int count = PitAuthority.MaxPlayers, first = ((slot % count) + count) % count;
            for (int k = 0; k < count; k++)
            {
                Vector3 p = RingPoint(center, ringRadius, first + k, count);
                var origin = new Vector3(p.x, topY + cfg.spawnProbeUp, p.z);
                if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit, cfg.spawnProbeUp + cfg.spawnProbeDown, world.EnvMask,
                                     QueryTriggerInteraction.Ignore)) continue;
                if (!IsArrivalSafe(hit.point, true, play, true, forbidden) || IsBlocked(hit.point, rSpawn, hSpawn, ignore, clearance)) continue;
                WarnOnce("ring:" + siteName, $"site '{siteName}' has no usable PlayerSpawnPoint; using a computed ring point. Author spawn points.", site);
                feet = hit.point;
                yaw = FaceToward(center, feet);
                return SpawnSource.Ring;
            }

            float y = world.WallTop - 2f * world.Diameter + world.VisualRadius; // FinishFill's WallTop = maxY + 2D, plus one radius
            var pileCenter = new Vector3(world.PitCenter.x, y, world.PitCenter.z);
            float spread = 2f * rSpawn + clearance;
            feet = ClampToInterior(RingPoint(pileCenter, spread, first, count), world, rSpawn);
            for (int k = 0; k < count; k++)
            {
                Vector3 p = ClampToInterior(RingPoint(pileCenter, spread, first + k, count), world, rSpawn);
                if (!IsBlocked(p, rSpawn, hSpawn, ignore, clearance)) { feet = p; break; }
            }
            WarnOnce("piletop:" + siteName, $"site '{siteName}' has no usable PlayerSpawnPoint and no floor around the pit; " +
                     "spawning on the pile top. Author spawn points.", site);
            yaw = FaceToward(center, feet);
            return SpawnSource.PileTop;
        }

        /// <summary>The arrival filter: inside PlayBounds at feet + BoundsProbeLift (when required and a world exists), and
        /// not forbidden there.</summary>
        public static bool IsArrivalSafe(Vector3 feet, bool hasWorld, Bounds playBounds, bool requireInsidePlayBounds,
                                         System.Func<Vector3, bool> forbidden)
        {
            Vector3 q = feet + Vector3.up * MotorMath.BoundsProbeLift;
            if (hasWorld && requireInsidePlayBounds && !playBounds.Contains(q)) return false;
            return forbidden == null || !forbidden(q);
        }

        /// <summary>Steps 3-5: the first unblocked point preferring this slot, else round-robin from slot mod n, else the
        /// slot-match point (or the start point) as AuthoredBlocked. -1 and None for an empty list.</summary>
        public static int Select(IReadOnlyList<SpawnCandidate> points, int slot, System.Func<int, bool> blocked, out SpawnSource source)
        {
            source = SpawnSource.None;
            int n = points != null ? points.Count : 0;
            if (n == 0) return -1;
            int match = -1;
            for (int i = 0; i < n; i++)
            {
                if (points[i].Slot != slot) continue;
                if (match < 0) match = i;
                if (!blocked(i)) { source = SpawnSource.Authored; return i; }
            }
            int start = ((slot % n) + n) % n;
            for (int k = 0; k < n; k++)
            {
                int i = (start + k) % n;
                if (!blocked(i)) { source = SpawnSource.Authored; return i; }
            }
            source = SpawnSource.AuthoredBlocked;
            return match >= 0 ? match : start;
        }

        /// <summary>Slot k of count on a circle: angle 2 pi k / count, at (c.x + r sin, c.y, c.z + r cos).</summary>
        public static Vector3 RingPoint(Vector3 center, float radius, int slot, int count)
        {
            float theta = 2f * Mathf.PI * slot / Mathf.Max(1, count);
            return new Vector3(center.x + radius * Mathf.Sin(theta), center.y, center.z + radius * Mathf.Cos(theta));
        }

        // Another avatar's live capsule axis within rOther + rSpawn + clearance of the point's axis (exact, no physics query).
        private static bool IsBlocked(Vector3 feet, float rSpawn, float hSpawn, Collider ignore, float clearance)
        {
            Vector3 a2 = feet + Vector3.up * rSpawn, b2 = feet + Vector3.up * Mathf.Max(rSpawn, hSpawn - rSpawn);
            IReadOnlyList<PlayerAvatar> avatars = PlayerAvatar.All;
            for (int i = 0; i < avatars.Count; i++)
            {
                PlayerAvatar av = avatars[i];
                if (av == null || (ignore != null && av.Capsule == ignore)) continue;
                if (!PlayerCapsules.GetLiveAxis(av, out Vector3 a, out Vector3 b, out float rOther)) continue;
                float limit = rOther + rSpawn + clearance;
                if (Geometry.SegmentSegmentSqrDistance(a, b, a2, b2) < limit * limit) return true;
            }
            return false;
        }

        private static Vector3 ClampToInterior(Vector3 p, PitWorld w, float r) =>
            new Vector3(Mathf.Clamp(p.x, w.WallMinX + r, w.WallMaxX - r), p.y, Mathf.Clamp(p.z, w.WallMinZ + r, w.WallMaxZ - r));

        private static float FaceToward(Vector3 center, Vector3 p)
        {
            float dx = center.x - p.x, dz = center.z - p.z;
            return dx * dx + dz * dz < 1e-8f ? 0f : LookMath.WrapYaw(Mathf.Atan2(dx, dz) * Mathf.Rad2Deg);
        }

        private static int CompareCandidates(SpawnCandidate a, SpawnCandidate b)
        {
            int c = a.Feet.x.CompareTo(b.Feet.x);
            if (c == 0) c = a.Feet.z.CompareTo(b.Feet.z);
            if (c == 0) c = a.Feet.y.CompareTo(b.Feet.y);
            return c != 0 ? c : a.Slot.CompareTo(b.Slot);
        }

        private static void WarnOnce(string key, string message, Object context)
        {
            if (warned.Add(key)) Debug.LogWarning("[PlayerSpawn] " + message, context);
        }
    }

    /// <summary>The spawn-point rules the editor validator runs per site (design d.9; b 11.4 checks 14, 14b).</summary>
    public static class PlayerSpawnValidation
    {
        /// <summary>
        /// Errors: a linked point outside the site's venue bounds (when requireInsidePlayBounds) or inside a forbidden
        /// volume. Warnings: fewer than MaxPlayers usable points, points that block each other, no floor within 3 m
        /// below, a point inside the interior below the rim. Returns the usable count; nothing for a site without an anchor.
        /// </summary>
        public static int Check(PitSite site, float capsuleRadius, float spawnClearance, bool requireInsidePlayBounds,
                                System.Func<Vector3, bool> forbidden, List<string> warnings, List<string> errors)
        {
            if (site == null || site.Anchor == null) return 0;
            PitSite[] sites = Object.FindObjectsOfType<PitSite>(true);
            var usable = new List<PlayerSpawnPoint>();
            Bounds venue = site.WorldVenueBounds;
            foreach (PlayerSpawnPoint p in Object.FindObjectsOfType<PlayerSpawnPoint>(true))
            {
                if (!(p.site == null || p.site == site) || UnderOtherSiteRoot(p, site, sites)) continue;
                Vector3 q = p.Feet + Vector3.up * MotorMath.BoundsProbeLift;
                if (requireInsidePlayBounds && !venue.Contains(q))
                { errors?.Add($"PlayerSpawnPoint '{PitSites.PathOf(p)}' is outside site '{site.SiteId}'s venue bounds; it is skipped at runtime"); continue; }
                if (forbidden != null && forbidden(q))
                { errors?.Add($"PlayerSpawnPoint '{PitSites.PathOf(p)}' is inside an OutOfBoundsVolume; it is skipped at runtime"); continue; }
                usable.Add(p);
            }
            if (warnings == null) return usable.Count;
            if (usable.Count < PitAuthority.MaxPlayers)
                warnings.Add($"site '{site.SiteId}' has {usable.Count} usable PlayerSpawnPoints (want {PitAuthority.MaxPlayers})" +
                             (usable.Count == 0 ? "; the runtime uses a computed ring" : ""));
            float apart = 2f * capsuleRadius + spawnClearance;
            for (int i = 0; i < usable.Count; i++)
                for (int j = i + 1; j < usable.Count; j++)
                    if ((usable[i].Feet - usable[j].Feet).sqrMagnitude < apart * apart)
                        warnings.Add($"PlayerSpawnPoints '{usable[i].name}' and '{usable[j].name}' are closer than {apart:0.##} m and block each other");

            var controller = Object.FindObjectOfType<BallPitFinalController>();
            int ballLayer = controller != null ? controller.settings.ballLayer : new PitSettings().ballLayer;
            int floorMask = Physics.DefaultRaycastLayers & ~(1 << ballLayer);
            bool hasInterior = VenueConfig.Load().TryGetProfile(site.ProfileKey, out PitSizeProfile profile);
            PitSettings preview = hasInterior ? VenueMath.PreviewSettings(site.AnchorPoint, profile, new PitSettings()) : null;
            float rimTop = hasInterior ? VenueMath.PredictProxyBox(preview, site.RimHeight).max.y : 0f;
            float x0 = 0f, x1 = 0f, z0 = 0f, z1 = 0f, floorY = 0f;
            if (hasInterior) PitWorld.ComputeInterior(preview, out x0, out x1, out z0, out z1, out floorY);
            foreach (PlayerSpawnPoint p in usable)
            {
                Vector3 f = p.Feet;
                if (!Physics.Raycast(f + Vector3.up * MotorMath.BoundsProbeLift, Vector3.down, 3f + MotorMath.BoundsProbeLift, floorMask,
                                     QueryTriggerInteraction.Ignore))
                    warnings.Add($"PlayerSpawnPoint '{PitSites.PathOf(p)}' has no floor collider within 3 m below it");
                if (hasInterior && f.x > x0 && f.x < x1 && f.z > z0 && f.z < z1 && f.y >= floorY && f.y < rimTop)
                    warnings.Add($"PlayerSpawnPoint '{PitSites.PathOf(p)}' is inside the pit below the rim; players spawn on the pile");
            }
            return usable.Count;
        }

        // Points under another site's siteRoot belong to that site's art (inactive while this site runs).
        private static bool UnderOtherSiteRoot(PlayerSpawnPoint p, PitSite site, PitSite[] sites)
        {
            for (int i = 0; i < sites.Length; i++)
            {
                PitSite other = sites[i];
                if (other == null || other == site || other.SiteRoot == null) continue;
                if (site.SiteRoot != null && other.SiteRoot == site.SiteRoot) continue; // a shared root is this site's too
                if (p.transform.IsChildOf(other.SiteRoot.transform)) return true;
            }
            return false;
        }
    }
}

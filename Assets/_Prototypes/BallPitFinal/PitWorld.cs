using System.Collections.Generic;
using UnityEngine;

namespace BallPitFinal
{
    /// <summary>
    /// State shared by host and clients: the store (truth), the two indexes, the optional collider shell, the
    /// quantizer, the pit geometry and the fill. Every state change goes through ToResting / ToLive / ToGone, which
    /// keep the ledger, both grids, the colliders and the renderer (OnRestingChanged) consistent, and record the ball
    /// as modified since the fill (that list is the join-in-progress diff). No rules live here: the host's
    /// PitAuthority decides what happens; clients replay the host's events through the same three functions.
    /// </summary>
    public sealed class PitWorld : System.IDisposable
    {
        public readonly PitSettings S;
        public readonly BallStore Store;
        public readonly SpatialGrid Resting;   // resting balls only
        public readonly SpatialGrid Live;      // active + held balls
        public readonly ColliderShell Colliders; // null on clients
        public readonly PhysicMaterial Material;
        public readonly Quantizer Q;
        public readonly Bounds PlayBounds;
        public readonly float Diameter, VisualRadius, ColliderRadius;
        public readonly int EnvMask;

        public float WallMinX, WallMaxX, WallMinZ, WallMaxZ, WallTop, FloorY;
        public Vector3 PitCenter, PitSize;
        public ulong FillChecksum { get; private set; }
        public System.Action<int> OnRestingChanged;

        /// <summary>
        /// Optional region filter for RegisterSceneEnvironment (default null = today's whole-scene scan). When set,
        /// colliders whose bounds do not intersect it are not registered. The venue binding sets it before registering.
        /// </summary>
        public Bounds? EnvironmentRegion;

        /// <summary>Absolute kill height: the pit floor plus S.killY (identical to S.killY with a zero anchor).</summary>
        public float KillY => S.pitAnchor.y + S.killY;

        /// <summary>SpatialGrid's linear cell index is an int, so a grid may hold at most this many cells.</summary>
        public const double MaxGridCells = int.MaxValue;

        public int ModifiedCount => modifiedCount;
        public int ModifiedAt(int i) => modifiedList[i];

        private readonly bool[] modified;
        private readonly int[] modifiedList;
        private int modifiedCount;
        private readonly int[] rayStamp;
        private int rayStampValue;
        private readonly List<GameObject> envObjects = new List<GameObject>();
        private readonly List<Bounds> envBounds = new List<Bounds>();

        public PitWorld(PitSettings settings, Bounds playBounds, bool withColliders)
        {
            S = settings;
            PlayBounds = playBounds;
            Diameter = S.ballDiameter;
            VisualRadius = Diameter * 0.5f;
            ColliderRadius = VisualRadius * S.colliderRadiusScale;
            int capacity = Mathf.Max(1, S.pitX * S.pitY * S.pitZ);

            Store = new BallStore(capacity, S.colorCount);
            Resting = new SpatialGrid(PlayBounds, Diameter, capacity);
            Live = new SpatialGrid(PlayBounds, Diameter, capacity);
            Q = new Quantizer(PlayBounds);
            Material = new PhysicMaterial("Ball")
            {
                bounciness = S.bounciness, dynamicFriction = S.friction, staticFriction = S.friction,
                frictionCombine = PhysicMaterialCombine.Average, bounceCombine = PhysicMaterialCombine.Average
            };
            if (withColliders) Colliders = new ColliderShell(S.maxColliders, capacity, ColliderRadius, Material, S.ballLayer);
            EnvMask = Physics.DefaultRaycastLayers & ~(1 << S.ballLayer);
            if (S.playerLayer >= 0 && S.playerLayer < 32 && S.playerLayer != S.ballLayer)
                EnvMask &= ~(1 << S.playerLayer); // players never support balls (-1 = no players, today's mask)

            modified = new bool[capacity];
            modifiedList = new int[capacity];
            rayStamp = new int[capacity];

            ComputeInterior(S, out WallMinX, out WallMaxX, out WallMinZ, out WallMaxZ, out FloorY);
        }

        // ------------------------------------------------------------------ Geometry

        public static void FccGeometry(PitSettings s, out float a, out float rowDz, out float layerH,
                                       out float ox, out float oz, out float jitter)
        {
            float dc = s.ballDiameter * s.colliderRadiusScale;
            a = dc * 1.002f;
            rowDz = a * 0.8660254f;
            layerH = a * 0.8164966f;
            ox = s.pitAnchor.x + (-(s.pitX * a) * 0.5f);
            oz = s.pitAnchor.z + (-(s.pitZ * rowDz) * 0.5f);
            jitter = Mathf.Max(0f, (a - dc) * 0.5f / 1.7320508f) * 0.95f * s.jitter;
        }

        /// <summary>The constructor's ColliderRadius for these settings (same operand order, so the same bits).</summary>
        public static float ColliderRadiusOf(PitSettings s) => s.ballDiameter * 0.5f * s.colliderRadiusScale;

        /// <summary>
        /// The exact pit interior the simulation uses: wall planes and floor height. The constructor calls this, and the
        /// venue gizmo, validator and collision proxies use it, so all of them agree with the simulation.
        /// </summary>
        public static void ComputeInterior(PitSettings s, out float wallMinX, out float wallMaxX, out float wallMinZ,
                                           out float wallMaxZ, out float floorY)
        {
            FccGeometry(s, out float a, out float rowDz, out _, out float ox, out float oz, out float j);
            float pad = ColliderRadiusOf(s) + 0.002f + j;
            wallMinX = ox - pad;
            wallMaxX = ox + (s.pitX + 0.5f) * a + pad;
            wallMinZ = oz - pad;
            wallMaxZ = oz + (s.pitZ - 1) * rowDz + 0.5773503f * a + pad;
            floorY = s.pitAnchor.y;
        }

        /// <summary>
        /// Upper bound on FillFcc's highest ball center (exact to within the jitter plus one Q21 quantum). Editor tools only.
        /// </summary>
        public static float PredictFccFillTop(PitSettings s)
        {
            FccGeometry(s, out _, out _, out float layerH, out _, out _, out float j);
            return s.pitAnchor.y + (ColliderRadiusOf(s) + (s.pitY - 1) * layerH) + (s.pitY > 1 ? j : 0f);
        }

        /// <summary>SpatialGrid's cell count (Nx * Ny * Nz) for a box, in double so it cannot overflow.</summary>
        public static double GridCellCount(Vector3 size, float cellSize) =>
            Cells(size.x, cellSize) * Cells(size.y, cellSize) * Cells(size.z, cellSize);

        private static double Cells(float v, float cellSize) =>
            System.Math.Max(1.0, System.Math.Ceiling((double)(v * (1f / cellSize))));

        /// <summary>A play-bounds override the Quantizer and both grids can use at diameter d.</summary>
        private static bool IsUsableOverride(Bounds b, float d)
        {
            Vector3 c = b.center, z = b.size;
            if (!Finite(c.x) || !Finite(c.y) || !Finite(c.z)) return false;
            if (!Finite(z.x) || !Finite(z.y) || !Finite(z.z) || z.x <= 0f || z.y <= 0f || z.z <= 0f) return false;
            if (!Finite(d) || d <= 0f) return false;
            return GridCellCount(z, d) <= MaxGridCells;
        }

        private static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);

        /// <summary>Deterministic play bounds for these settings (the host sends the exact result to clients).</summary>
        public static Bounds ComputeBounds(PitSettings s)
        {
            if (s.overridePlayBounds)
            {
                if (IsUsableOverride(s.playBoundsOverride, s.ballDiameter)) return s.playBoundsOverride;
                Debug.LogError($"[PitWorld] playBoundsOverride {s.playBoundsOverride} is not usable at ball diameter " +
                               $"{s.ballDiameter} ({GridCellCount(s.playBoundsOverride.size, s.ballDiameter):0} grid cells, " +
                               $"max {MaxGridCells:0}; or a non-finite or empty box). Using the computed bounds.");
            }
            FccGeometry(s, out float a, out float rowDz, out float layerH, out float ox, out float oz, out _);
            float w = (s.pitX + 1.5f) * a + 2f, dz = (s.pitZ + 1) * rowDz + 2f;
            float n = (float)s.pitX * s.pitY * s.pitZ;
            float dc = s.ballDiameter * s.colliderRadiusScale;
            float hFcc = s.pitY * layerH + a;
            float hRand = n * 0.5235988f * dc * dc * dc / (0.5f * w * dz) + a; // packing >= 0.5
            float h = Mathf.Max(hFcc, hRand) + 4f;
            float span = Mathf.Max(w, dz) * 3f + 20f;
            float cx = ox + (s.pitX + 0.5f) * a * 0.5f, cz = oz + s.pitZ * rowDz * 0.5f;
            const float below = 10f, above = 20f;
            return new Bounds(new Vector3(cx, s.pitAnchor.y + ((h + above - below) * 0.5f), cz), new Vector3(span, h + above + below, span));
        }

        // ------------------------------------------------------------------ Fill

        /// <summary>
        /// Overlap-free FCC (ABC stacking) fill on y = 0. Spacing = collider diameter, so thawing a deep ball never
        /// pops it out of an overlap. Data only: safe to call from a worker thread. Call FinishFill afterwards.
        /// </summary>
        public void FillFcc()
        {
            FccGeometry(S, out float a, out float rowDz, out float layerH, out float ox, out float oz, out float j);
            var rng = new System.Random(S.seed);
            for (int y = 0; y < S.pitY; y++)
            {
                int k = y % 3;
                float lx = k * a * 0.5f, lz = k * a * 0.28867513f;
                float py0 = FloorY + (ColliderRadius + y * layerH);
                for (int z = 0; z < S.pitZ; z++)
                {
                    float rowX = (z & 1) == 1 ? a * 0.5f : 0f;
                    for (int x = 0; x < S.pitX; x++)
                    {
                        var p = new Vector3(ox + x * a + rowX + lx, py0, oz + z * rowDz + lz);
                        if (j > 0f)
                        {
                            p.x += ((float)rng.NextDouble() * 2f - 1f) * j;
                            p.z += ((float)rng.NextDouble() * 2f - 1f) * j;
                            if (y > 0) p.y += ((float)rng.NextDouble() * 2f - 1f) * j;
                        }
                        p = Q.Snap(p);
                        int id = Store.Add(p, rng.Next(0, S.colorCount), BallStore.State.Resting);
                        Resting.Insert(id, p);
                    }
                }
            }
        }

        /// <summary>Main thread, after any fill: checksum, pit extents, walls.</summary>
        public void FinishFill(Material wallMaterial)
        {
            const ulong FnvOffset = 14695981039346656037UL, FnvPrime = 1099511628211UL;
            ulong h = FnvOffset;
            float maxY = FloorY;
            for (int id = 0; id < Store.Count; id++)
            {
                ulong q = Q.Encode(Store.pos[id]);
                for (int b = 0; b < 8; b++) { h ^= (q >> (b * 8)) & 0xFF; h *= FnvPrime; }
                h ^= Store.color[id]; h *= FnvPrime;
                if (Store.pos[id].y > maxY) maxY = Store.pos[id].y;
            }
            FillChecksum = h;
            WallTop = maxY + Diameter * 2f;
            PitCenter = new Vector3((WallMinX + WallMaxX) * 0.5f, (FloorY + maxY) * 0.5f, (WallMinZ + WallMaxZ) * 0.5f);
            PitSize = new Vector3(WallMaxX - WallMinX, (maxY - FloorY) + ColliderRadius, WallMaxZ - WallMinZ);
            if (S.pitWalls) BuildWalls(wallMaterial);
        }

        private void BuildWalls(Material mat)
        {
            const float t = 1f;
            float height = WallTop - FloorY;
            float lenX = WallMaxX - WallMinX + 2f * t, lenZ = WallMaxZ - WallMinZ + 2f * t;
            float cx = PitCenter.x, cz = PitCenter.z;
            MakeWall(new Vector3(WallMinX - t * 0.5f, FloorY + height * 0.5f, cz), new Vector3(t, height, lenZ), mat);
            MakeWall(new Vector3(WallMaxX + t * 0.5f, FloorY + height * 0.5f, cz), new Vector3(t, height, lenZ), mat);
            MakeWall(new Vector3(cx, FloorY + height * 0.5f, WallMinZ - t * 0.5f), new Vector3(lenX, height, t), mat);
            MakeWall(new Vector3(cx, FloorY + height * 0.5f, WallMaxZ + t * 0.5f), new Vector3(lenX, height, t), mat);
        }

        private void MakeWall(Vector3 center, Vector3 size, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "PitWall";
            go.transform.position = center;
            go.transform.localScale = size;
            var r = go.GetComponent<MeshRenderer>();
            if (S.renderWalls && mat != null) r.sharedMaterial = mat; else r.enabled = false;
            envObjects.Add(go);
        }

        // ------------------------------------------------------------------ Transitions (the only state writers)

        public void ToResting(int id, Vector3 pos)
        {
            BallStore.State old = Store.state[id];
            if (old == BallStore.State.Resting) Colliders?.Remove(id);
            else if (Live.Contains(id)) Live.Remove(id);
            Store.pos[id] = pos;
            Store.SetState(id, BallStore.State.Resting);
            Resting.Insert(id, pos); // Insert re-bins if already present
            MarkModified(id);
            OnRestingChanged?.Invoke(id);
        }

        /// <summary>Into Active or Held at pos (from any state).</summary>
        public void ToLive(int id, BallStore.State s, Vector3 pos)
        {
            BallStore.State old = Store.state[id];
            bool wasResting = old == BallStore.State.Resting;
            if (wasResting) { Resting.Remove(id); Colliders?.Remove(id); }
            Store.pos[id] = pos;
            Store.SetState(id, s);
            Live.Insert(id, pos);
            MarkModified(id);
            if (wasResting) OnRestingChanged?.Invoke(id);
        }

        /// <summary>Out of the pile and out of physics: Stored, Lost or Hopper.</summary>
        public void ToGone(int id, BallStore.State s)
        {
            BallStore.State old = Store.state[id];
            bool wasResting = old == BallStore.State.Resting;
            if (wasResting) { Resting.Remove(id); Colliders?.Remove(id); }
            else if (Live.Contains(id)) Live.Remove(id);
            Store.SetState(id, s);
            MarkModified(id);
            if (wasResting) OnRestingChanged?.Invoke(id);
        }

        private void MarkModified(int id)
        {
            if (modified[id]) return;
            modified[id] = true;
            modifiedList[modifiedCount++] = id;
        }

        // ------------------------------------------------------------------ Environment registry

        /// <summary>
        /// Records the world AABBs of every non-ball collider in the scene (ground, walls, containers, ramps) so the
        /// support test only issues PhysX queries for balls that can actually touch the environment. Call after the
        /// environment is built, and again whenever static level geometry is added.
        /// </summary>
        public void RegisterSceneEnvironment()
        {
            envBounds.Clear();
            Physics.SyncTransforms();
            foreach (Collider c in Object.FindObjectsOfType<Collider>())
            {
                if (!c.enabled || c.isTrigger || c.gameObject.layer == S.ballLayer) continue;
                if (S.playerLayer >= 0 && c.gameObject.layer == S.playerLayer) continue; // players never support balls
                if (c.attachedRigidbody != null && !c.attachedRigidbody.isKinematic) continue; // moving props are not static support
                if (EnvironmentRegion.HasValue && !EnvironmentRegion.Value.Intersects(c.bounds)) continue; // venue region filter
                envBounds.Add(c.bounds);
            }
        }

        public void RegisterEnvironment(Bounds worldBounds) => envBounds.Add(worldBounds);

        public bool NearEnvironment(Vector3 p, float radius)
        {
            if (envBounds.Count == 0) return true; // nothing registered: always run the real PhysX query (correct, slower)
            float r2 = radius * radius;
            for (int i = 0; i < envBounds.Count; i++) if (envBounds[i].SqrDistance(p) <= r2) return true;
            return false;
        }

        // ------------------------------------------------------------------ Queries

        public bool RaycastEnvironment(Ray ray, float maxDist, out RaycastHit hit) =>
            Physics.Raycast(ray, out hit, maxDist, EnvMask, QueryTriggerInteraction.Ignore);

        /// <summary>
        /// Exact first hit against an explicit list of spheres (the live balls as they were drawn last frame), so picking
        /// matches what the player sees on host and client alike.
        /// </summary>
        public static bool RaycastSpheres(Ray ray, BallInstance[] spheres, int[] ids, int count, float radius, float maxDist,
                                          out int hitId, out float hitT)
        {
            hitId = -1; hitT = float.MaxValue;
            Vector3 dir = ray.direction.normalized;
            float r2 = radius * radius;
            for (int i = 0; i < count; i++)
            {
                Vector3 oc = ray.origin - spheres[i].pos;
                float b = Vector3.Dot(oc, dir), c = Vector3.Dot(oc, oc) - r2, disc = b * b - c;
                if (disc < 0f) continue;
                float sq = Mathf.Sqrt(disc), t0 = -b - sq, t = t0 >= 0f ? t0 : -b + sq;
                if (t >= 0f && t < hitT && t <= maxDist) { hitT = t; hitId = ids[i]; }
            }
            return hitId >= 0;
        }

        /// <summary>Exact first hit against resting balls (grid DDA + ray/sphere). Independent of colliders.</summary>
        public bool Raycast(Ray ray, float maxDist, out int hitId, out float hitT)
        {
            hitId = -1; hitT = float.MaxValue;
            Vector3 dir = ray.direction.normalized;
            var b = new Bounds(PlayBounds.center, PlayBounds.size);
            if (!b.IntersectRay(new Ray(ray.origin, dir), out float tEnter)) return false;
            tEnter = Mathf.Max(0f, tEnter);
            Vector3 o = ray.origin + dir * tEnter;
            int cx = Resting.CellX(o.x), cy = Resting.CellY(o.y), cz = Resting.CellZ(o.z);
            int sx = dir.x > 0 ? 1 : -1, sy = dir.y > 0 ? 1 : -1, sz = dir.z > 0 ? 1 : -1;
            float cs = Resting.CellSize;
            Vector3 mn = Resting.Min;
            float NextT(float oc, float d, int c, int s, float m)
            {
                if (Mathf.Abs(d) < 1e-8f) return float.MaxValue;
                float boundary = m + (c + (s > 0 ? 1 : 0)) * cs;
                return (boundary - oc) / d;
            }
            float tmx = NextT(o.x, dir.x, cx, sx, mn.x), tmy = NextT(o.y, dir.y, cy, sy, mn.y), tmz = NextT(o.z, dir.z, cz, sz, mn.z);
            float tdx = Mathf.Abs(dir.x) < 1e-8f ? float.MaxValue : cs / Mathf.Abs(dir.x);
            float tdy = Mathf.Abs(dir.y) < 1e-8f ? float.MaxValue : cs / Mathf.Abs(dir.y);
            float tdz = Mathf.Abs(dir.z) < 1e-8f ? float.MaxValue : cs / Mathf.Abs(dir.z);
            float tCell = 0f, r = VisualRadius, r2 = r * r;
            int stamp = ++rayStampValue;
            int maxSteps = Resting.Nx + Resting.Ny + Resting.Nz + 3;

            for (int it = 0; it < maxSteps; it++)
            {
                if (hitId >= 0 && tEnter + tCell > hitT + cs * 2f) break;
                if (tEnter + tCell > maxDist) break;
                for (int dz = -1; dz <= 1; dz++)
                for (int dyc = -1; dyc <= 1; dyc++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int x = cx + dx, y = cy + dyc, z = cz + dz;
                    if (x < 0 || y < 0 || z < 0 || x >= Resting.Nx || y >= Resting.Ny || z >= Resting.Nz) continue;
                    for (int id = Resting.Head(x, y, z); id >= 0; id = Resting.Next(id))
                    {
                        if (rayStamp[id] == stamp) continue;
                        rayStamp[id] = stamp;
                        Vector3 oc = ray.origin - Store.pos[id];
                        float bq = Vector3.Dot(oc, dir), cq = Vector3.Dot(oc, oc) - r2, disc = bq * bq - cq;
                        if (disc < 0f) continue;
                        float sq = Mathf.Sqrt(disc), t0 = -bq - sq, t = t0 >= 0f ? t0 : -bq + sq;
                        if (t >= 0f && t < hitT && t <= maxDist) { hitT = t; hitId = id; }
                    }
                }
                if (tmx < tmy && tmx < tmz) { cx += sx; tCell = tmx; tmx += tdx; if (cx < 0 || cx >= Resting.Nx) break; }
                else if (tmy < tmz) { cy += sy; tCell = tmy; tmy += tdy; if (cy < 0 || cy >= Resting.Ny) break; }
                else { cz += sz; tCell = tmz; tmz += tdz; if (cz < 0 || cz >= Resting.Nz) break; }
            }
            return hitId >= 0;
        }

        /// <summary>Ledger + index consistency (dev check).</summary>
        public bool Verify(out string error)
        {
            if (!Store.VerifyLedger(out error)) return false;
            int resting = Store.StateTotal(BallStore.State.Resting);
            if (Resting.Count != resting) { error = $"resting grid {Resting.Count} vs ledger {resting}"; return false; }
            int live = Store.StateTotal(BallStore.State.Active) + Store.StateTotal(BallStore.State.Held);
            if (Live.Count != live) { error = $"live grid {Live.Count} vs ledger {live}"; return false; }
            return true;
        }

        public void Dispose()
        {
            Colliders?.Dispose();
            for (int i = 0; i < envObjects.Count; i++) if (envObjects[i] != null) Object.Destroy(envObjects[i]);
            envObjects.Clear();
            if (Material != null) Object.Destroy(Material);
        }
    }
}

using System.Collections.Generic;
using UnityEngine;

namespace BallPitFinal
{
    [System.Serializable]
    public sealed class PitSettings
    {
        [Header("Pit (FCC packing: pitX per row, pitZ rows, pitY layers)")]
        public int pitX = 100, pitY = 100, pitZ = 100;
        public float ballDiameter = 0.5f;
        [Range(1, 16)] public int colorCount = 4;
        public int seed = 12345;
        [Tooltip("Collider radius relative to the visual radius. Lattice spacing = collider diameter, so visuals overlap slightly (reads as soft, dense balls).")]
        [Range(0.9f, 1f)] public float colliderRadiusScale = 0.97f;
        [Tooltip("Fraction of the maximum overlap-free jitter (0 = perfect lattice).")]
        [Range(0f, 1f)] public float jitter = 1f;
        public bool pitWalls = true;
        public bool renderWalls = true;

        [Header("Active physics")]
        public int maxActive = 1500;
        public int activePoolSize = 1600;
        public float linearDrag = 0.05f, angularDrag = 1.2f, maxDepenetrationVelocity = 1f;
        public float bounciness = 0.2f, friction = 0.5f;
        [Range(20, 120)] public int physicsHz = 50;

        [Header("Collapse rules")]
        public int supportThawsPerStep = 200;
        public int impactThawsPerStep = 40;
        [Tooltip("Speed (m/s) above which an active ball thaws resting balls in its path before the step.")]
        public float fastSpeed = 2.5f;
        [Tooltip("Motion event threshold as a fraction of the ball radius.")]
        public float thawMoveFraction = 0.1f;
        [Tooltip("Jitter fallback: freeze if the ball moved less than this fraction of the diameter for stillSeconds.")]
        public float stillEpsilonFraction = 0.004f;
        public float stillSeconds = 1f;
        [Tooltip("A resting ball is stable if its support contacts surround its center with no angular gap above this.")]
        [Range(120f, 180f)] public float maxSupportGapDegrees = 170f;
        public float killY = -20f;

        [Header("Collider shell")]
        public int maxColliders = 8000;
        [Tooltip("Collider reach around each active ball, in ball diameters (plus a speed term).")]
        public float colliderReachDiameters = 1.3f;
        public float colliderKeepSeconds = 1f;
        public int colliderReleasePerStep = 256;
    }

    /// <summary>
    /// Host-side simulation rules: build, thaw/freeze, support-loss collapse, impacts, grab/throw/dig, raycast.
    /// Owns the store (truth), two indexes (resting balls / active balls), the active pool and the collider shell.
    /// Every state transition goes through Thaw / Freeze / Lose, so the ledger and all indexes stay consistent.
    /// No NGO dependency: OnThawed / OnFrozen / OnLost are the hooks a network layer would replicate.
    /// </summary>
    public sealed class PitSimulation : System.IDisposable
    {
        public readonly PitSettings S;
        public readonly BallStore Store;
        public readonly SpatialGrid Resting;     // Resting balls only (static truth for support/exposure/raycast)
        public readonly SpatialGrid ActiveGrid;  // Active + held balls, re-binned every step
        public readonly ActivePhysics Active;
        public readonly ColliderShell Colliders;
        public readonly PhysicMaterial Material;
        public readonly float Diameter, VisualRadius, ColliderRadius;
        public readonly Bounds PlayBounds;

        public System.Action<int> OnRestingChanged;  // renderer dirty hook
        public System.Action<int> OnThawed;
        public System.Action<int, Vector3> OnFrozen;
        public System.Action<int> OnLost;

        public int HeldId { get; private set; } = -1;
        public Vector3 HandTarget;
        public int StepIndex { get; private set; }
        public int QueueLength => supportQueue.Count;
        public float FloorY { get; private set; }
        public Vector3 PitCenter { get; private set; }
        public Vector3 PitSize { get; private set; }

        // Stats (last step)
        public int StatSupportThaws, StatImpactThaws, StatFreezes, StatMotionEvents;

        private readonly Queue<int> supportQueue = new Queue<int>(4096);
        private readonly bool[] queued;
        private readonly int[] rayStamp;
        private int rayStampValue;
        private readonly List<int> scratchA = new List<int>(256), scratchB = new List<int>(256), scratchC = new List<int>(256);
        private readonly List<int> movedIds = new List<int>(2048), freezeCands = new List<int>(2048), lostIds = new List<int>(64);
        private readonly List<Vector3> movedFrom = new List<Vector3>(2048);
        private readonly List<Vector4> toolSources = new List<Vector4>(8);
        private readonly float[] angles = new float[32];
        private readonly HeightComparer heightComparer;
        private bool hasWalls;
        private float wallMinX, wallMaxX, wallMinZ, wallMaxZ;
        private readonly List<GameObject> envObjects = new List<GameObject>();

        private float ContactDist => ColliderRadius * 2f * 1.04f + 0.01f;
        private float SupportDy => ColliderRadius * 0.1f;

        public PitSimulation(PitSettings settings)
        {
            S = settings;
            Diameter = S.ballDiameter;
            VisualRadius = Diameter * 0.5f;
            ColliderRadius = VisualRadius * S.colliderRadiusScale;
            int capacity = Mathf.Max(1, S.pitX * S.pitY * S.pitZ);

            float a = ColliderRadius * 2f * 1.002f;
            float sizeX = (S.pitX + 1.5f) * a, sizeZ = (S.pitZ * 0.8660254f + 1.5f) * a, sizeY = S.pitY * a * 0.8164966f + a;
            float spanXZ = Mathf.Max(sizeX, sizeZ) * 3f + 20f;
            PlayBounds = new Bounds(new Vector3(0f, sizeY * 0.5f + 5f, 0f), new Vector3(spanXZ, sizeY + 30f, spanXZ));

            Store = new BallStore(capacity, S.colorCount);
            Resting = new SpatialGrid(PlayBounds, Diameter, capacity);
            ActiveGrid = new SpatialGrid(PlayBounds, Diameter, capacity);
            Material = new PhysicMaterial("Ball")
            {
                bounciness = S.bounciness, dynamicFriction = S.friction, staticFriction = S.friction,
                frictionCombine = PhysicMaterialCombine.Average, bounceCombine = PhysicMaterialCombine.Average
            };
            Active = new ActivePhysics(capacity, S.activePoolSize, S.maxActive, ColliderRadius, Material,
                                       S.linearDrag, S.angularDrag, S.maxDepenetrationVelocity);
            Colliders = new ColliderShell(S.maxColliders, capacity, ColliderRadius, Material);
            queued = new bool[capacity];
            rayStamp = new int[capacity];
            heightComparer = new HeightComparer(Store);
        }

        // ------------------------------------------------------------------ Build

        /// <summary>
        /// Overlap-free FCC (ABC stacking) fill resting on y = 0. Spacing = collider diameter, so the packing is
        /// physically consistent: thawing a deep ball never pops it out of an overlap.
        /// (FCC, not HCP: HCP has straight vertical see-through channels.)
        /// </summary>
        public void BuildFcc(Material wallMaterial)
        {
            float dc = ColliderRadius * 2f;
            float a = dc * 1.002f;
            float rowDz = a * 0.8660254f, layerH = a * 0.8164966f;
            float maxJitter = Mathf.Max(0f, (a - dc) * 0.5f / 1.7320508f) * 0.95f;
            float j = maxJitter * S.jitter;
            var rng = new System.Random(S.seed);
            float ox = -(S.pitX * a) * 0.5f, oz = -(S.pitZ * rowDz) * 0.5f;
            FloorY = 0f;

            float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue, maxY = 0f;
            for (int y = 0; y < S.pitY; y++)
            {
                int k = y % 3;
                float lx = k * a * 0.5f, lz = k * a * 0.28867513f; // k * (a/2, a/(2*sqrt3))
                float py0 = ColliderRadius + y * layerH;
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
                        int id = Store.Add(p, rng.Next(0, S.colorCount), BallStore.State.Resting);
                        Resting.Insert(id, p);
                        if (p.x < minX) minX = p.x; if (p.x > maxX) maxX = p.x;
                        if (p.z < minZ) minZ = p.z; if (p.z > maxZ) maxZ = p.z;
                        if (p.y > maxY) maxY = p.y;
                    }
                }
            }

            float pad = ColliderRadius + 0.002f;
            wallMinX = minX - pad; wallMaxX = maxX + pad; wallMinZ = minZ - pad; wallMaxZ = maxZ + pad;
            PitCenter = new Vector3((wallMinX + wallMaxX) * 0.5f, maxY * 0.5f, (wallMinZ + wallMaxZ) * 0.5f);
            PitSize = new Vector3(wallMaxX - wallMinX, maxY + ColliderRadius, wallMaxZ - wallMinZ);
            hasWalls = S.pitWalls;
            if (hasWalls) BuildWalls(wallMaterial, maxY + Diameter * 2f);
        }

        private void BuildWalls(Material mat, float height)
        {
            const float t = 1f;
            float lenX = wallMaxX - wallMinX + 2f * t, lenZ = wallMaxZ - wallMinZ + 2f * t, cx = PitCenter.x, cz = PitCenter.z;
            MakeWall(new Vector3(wallMinX - t * 0.5f, height * 0.5f, cz), new Vector3(t, height, lenZ), mat);
            MakeWall(new Vector3(wallMaxX + t * 0.5f, height * 0.5f, cz), new Vector3(t, height, lenZ), mat);
            MakeWall(new Vector3(cx, height * 0.5f, wallMinZ - t * 0.5f), new Vector3(lenX, height, t), mat);
            MakeWall(new Vector3(cx, height * 0.5f, wallMaxZ + t * 0.5f), new Vector3(lenX, height, t), mat);
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

        // ------------------------------------------------------------------ Player actions

        public bool TryGrab(int id)
        {
            if (HeldId >= 0 || id < 0 || id >= Store.Count || Store.state[id] != BallStore.State.Resting) return false;
            Vector3 old = Store.pos[id];
            if (!Thaw(id, Vector3.zero, carried: true)) return false;
            HeldId = id;
            QueueDependents(old, id);
            return true;
        }

        public void ThrowHeld(Vector3 velocity)
        {
            if (HeldId < 0) return;
            Active.Release(HeldId, velocity);
            Store.SetState(HeldId, BallStore.State.Active);
            HeldId = -1;
        }

        /// <summary>Thaws up to max resting balls within radius of center and flings them outward.</summary>
        public int Dig(Vector3 center, float radius, int max, float outSpeed)
        {
            Resting.Gather(center, radius, Store.pos, scratchA);
            int n = 0;
            for (int i = 0; i < scratchA.Count && n < max; i++)
            {
                int id = scratchA[i];
                Vector3 old = Store.pos[id];
                Vector3 dir = old - center;
                if (dir.sqrMagnitude < 1e-6f) dir = Vector3.up;
                Vector3 vel = dir.normalized * outSpeed + Vector3.up * (outSpeed * 0.5f);
                if (!Thaw(id, vel, carried: false)) break;
                QueueDependents(old, id);
                n++;
            }
            return n;
        }

        /// <summary>Deposit a ball into a container: leaves play, ledger moves it to Stored.</summary>
        public void Deposit(int id)
        {
            if (Store.state[id] == BallStore.State.Resting) { Colliders.Remove(id); Resting.Remove(id); OnRestingChanged?.Invoke(id); }
            else if (Active.IsActive(id)) { Active.Demote(id); ActiveGrid.Remove(id); }
            if (HeldId == id) HeldId = -1;
            Store.SetState(id, BallStore.State.Stored);
        }

        /// <summary>Extra collider sources for this step (tools, hands, vacuum nozzles). Cleared after each Step.</summary>
        public void AddToolSource(Vector3 p, float radius) => toolSources.Add(new Vector4(p.x, p.y, p.z, radius));

        // ------------------------------------------------------------------ Step

        public void Step(float dt)
        {
            StepIndex++;
            StatSupportThaws = StatImpactThaws = StatFreezes = StatMotionEvents = 0;

            if (HeldId >= 0) Active.MoveHeld(HeldId, HandTarget);
            ThawAheadOfFastMovers(dt);
            DrainSupportQueue();
            RefreshColliders(dt);

            Physics.SyncTransforms();
            Physics.Simulate(dt);

            movedIds.Clear(); movedFrom.Clear(); freezeCands.Clear(); lostIds.Clear();
            Active.PostStep(dt, VisualRadius * S.thawMoveFraction, Diameter * S.stillEpsilonFraction, S.stillSeconds,
                            Store, ActiveGrid, movedIds, movedFrom, freezeCands);
            StatMotionEvents = movedIds.Count;
            for (int i = 0; i < movedIds.Count; i++) QueueDependents(movedFrom[i], movedIds[i]);

            FreezeBottomUp();

            for (int i = 0; i < Active.Count; i++) if (Active.PosAt(i).y < S.killY) lostIds.Add(Active.IdAt(i));
            for (int i = 0; i < lostIds.Count; i++) Lose(lostIds[i]);

            int keepSteps = Mathf.Max(1, Mathf.RoundToInt(S.colliderKeepSeconds / dt));
            Colliders.Expire(StepIndex, keepSteps, S.colliderReleasePerStep);
            toolSources.Clear();
        }

        // ------------------------------------------------------------------ Transitions

        private bool Thaw(int id, Vector3 velocity, bool carried)
        {
            if (Store.state[id] != BallStore.State.Resting || !Active.CanPromote) return false;
            Vector3 p = Store.pos[id];
            Colliders.Remove(id);
            Resting.Remove(id);
            Active.Promote(id, p, velocity, carried);
            ActiveGrid.Insert(id, p);
            Store.SetState(id, carried ? BallStore.State.Held : BallStore.State.Active);
            OnRestingChanged?.Invoke(id);
            OnThawed?.Invoke(id);
            return true;
        }

        private void Freeze(int id)
        {
            Vector3 pose = Active.Demote(id); // exact physics rest pose, no snapping
            ActiveGrid.Remove(id);
            Store.pos[id] = pose;
            Store.SetState(id, BallStore.State.Resting);
            Resting.Insert(id, pose);
            Colliders.Ensure(id, pose, StepIndex); // never leave a gap under balls resting on it
            OnRestingChanged?.Invoke(id);
            OnFrozen?.Invoke(id, pose);
            StatFreezes++;
        }

        private void Lose(int id)
        {
            if (Active.IsActive(id)) Active.Demote(id);
            ActiveGrid.Remove(id);
            if (HeldId == id) HeldId = -1;
            Store.SetState(id, BallStore.State.Lost);
            OnLost?.Invoke(id);
        }

        // ------------------------------------------------------------------ Rules

        /// <summary>Queue resting balls that were resting on a ball at 'at' (in contact and above its center).</summary>
        private void QueueDependents(Vector3 at, int exclude)
        {
            Resting.Gather(at, ContactDist, Store.pos, scratchB, exclude);
            float dy = SupportDy;
            for (int i = 0; i < scratchB.Count; i++)
            {
                int n = scratchB[i];
                if (Store.pos[n].y - at.y > dy && !queued[n]) { queued[n] = true; supportQueue.Enqueue(n); }
            }
        }

        private void DrainSupportQueue()
        {
            int budget = S.supportThawsPerStep;
            int n = supportQueue.Count;
            while (n-- > 0 && budget > 0)
            {
                int id = supportQueue.Dequeue();
                queued[id] = false;
                if (Store.state[id] != BallStore.State.Resting) continue;
                if (IsSupported(id)) continue;
                if (!Active.CanPromote) { queued[id] = true; supportQueue.Enqueue(id); break; } // at cap: retry next step
                if (Thaw(id, Vector3.zero, carried: false)) { budget--; StatSupportThaws++; }
            }
        }

        /// <summary>
        /// Frictionless static stability for a sphere: the horizontal directions to its supporting contacts
        /// (resting or active balls below its center, plus pit walls) must surround the center, i.e. no angular gap
        /// larger than maxSupportGapDegrees, with at least one support below. Errs toward "unsupported":
        /// a wrongly thawed ball just sleeps and re-freezes at the same pose (costs budget, never visible),
        /// whereas a wrongly "supported" ball would float.
        /// </summary>
        private bool IsSupported(int id)
        {
            Vector3 p = Store.pos[id];
            if (p.y - ColliderRadius <= FloorY + Diameter * 0.02f) return true;
            int na = 0, below = 0;
            float dy = SupportDy, cd = ContactDist;

            Resting.Gather(p, cd, Store.pos, scratchC, id);
            for (int i = 0; i < scratchC.Count && na < angles.Length; i++)
            {
                Vector3 d = Store.pos[scratchC[i]] - p;
                if (d.y < -dy) { below++; if (AddAngle(d, ref na)) return true; }
            }
            ActiveGrid.Gather(p, cd, Store.pos, scratchC, id);
            for (int i = 0; i < scratchC.Count && na < angles.Length; i++)
            {
                Vector3 d = Store.pos[scratchC[i]] - p;
                if (d.y < -dy) { below++; if (AddAngle(d, ref na)) return true; }
            }
            if (below == 0) return false;

            if (hasWalls)
            {
                float reach = ColliderRadius + Diameter * 0.03f;
                if (p.x - wallMinX <= reach && na < angles.Length) angles[na++] = Mathf.PI;
                if (wallMaxX - p.x <= reach && na < angles.Length) angles[na++] = 0f;
                if (p.z - wallMinZ <= reach && na < angles.Length) angles[na++] = -Mathf.PI * 0.5f;
                if (wallMaxZ - p.z <= reach && na < angles.Length) angles[na++] = Mathf.PI * 0.5f;
            }
            if (na < 2) return false;

            // insertion sort (na is small)
            for (int i = 1; i < na; i++)
            {
                float v = angles[i]; int k = i - 1;
                while (k >= 0 && angles[k] > v) { angles[k + 1] = angles[k]; k--; }
                angles[k + 1] = v;
            }
            float maxGap = angles[0] + 2f * Mathf.PI - angles[na - 1];
            for (int i = 1; i < na; i++) maxGap = Mathf.Max(maxGap, angles[i] - angles[i - 1]);
            return maxGap < S.maxSupportGapDegrees * Mathf.Deg2Rad;
        }

        /// <returns>true if this single contact is directly underneath (treated as full support).</returns>
        private bool AddAngle(Vector3 d, ref int na)
        {
            float h2 = d.x * d.x + d.z * d.z;
            if (h2 < (ColliderRadius * 0.05f) * (ColliderRadius * 0.05f)) return true;
            angles[na++] = Mathf.Atan2(d.z, d.x);
            return false;
        }

        /// <summary>Thaw resting balls in the swept path of fast active balls so impacts are dynamic-vs-dynamic.</summary>
        private void ThawAheadOfFastMovers(float dt)
        {
            int budget = S.impactThawsPerStep;
            int n = Active.Count;
            float fast2 = S.fastSpeed * S.fastSpeed;
            for (int i = 0; i < n && budget > 0; i++)
            {
                if (Active.CarriedAt(i)) continue;
                Vector3 v = Active.VelAt(i);
                if (v.sqrMagnitude < fast2) continue;
                Vector3 p = Active.PosAt(i);
                Resting.Gather(p + v * dt, ContactDist, Store.pos, scratchA);
                for (int k = 0; k < scratchA.Count && budget > 0; k++)
                {
                    int r = scratchA[k];
                    if (Vector3.Dot(Store.pos[r] - p, v) <= 0f) continue;
                    if (!Thaw(r, Vector3.zero, carried: false)) { budget = 0; break; }
                    budget--; StatImpactThaws++;
                }
            }
        }

        private void RefreshColliders(float dt)
        {
            int step = StepIndex;
            int maxAge = Mathf.Max(1, Mathf.RoundToInt(S.colliderKeepSeconds / dt) / 2);
            float minMove = Diameter * 0.25f, minMove2 = minMove * minMove;
            float reach = Diameter * S.colliderReachDiameters;
            for (int i = 0; i < Active.Count; i++)
            {
                if (Active.CarriedAt(i)) continue;
                if (!Active.NeedsSourceRefresh(i, step, minMove2, maxAge)) continue;
                Vector3 p = Active.PosAt(i);
                float r = reach + Active.VelAt(i).magnitude * dt * 2f + minMove;
                Resting.Gather(p, r, Store.pos, scratchA);
                for (int k = 0; k < scratchA.Count; k++) Colliders.Ensure(scratchA[k], Store.pos[scratchA[k]], step);
                Active.MarkSourced(i, step);
            }
            for (int s = 0; s < toolSources.Count; s++)
            {
                Vector4 t = toolSources[s];
                Resting.Gather(new Vector3(t.x, t.y, t.z), t.w, Store.pos, scratchA);
                for (int k = 0; k < scratchA.Count; k++) Colliders.Ensure(scratchA[k], Store.pos[scratchA[k]], step);
            }
        }

        private void FreezeBottomUp()
        {
            if (freezeCands.Count == 0) return;
            freezeCands.Sort(heightComparer);
            float dy = SupportDy, cd = ContactDist;
            for (int i = 0; i < freezeCands.Count; i++)
            {
                int id = freezeCands[i];
                if (Store.state[id] != BallStore.State.Active) continue;
                Vector3 p = Store.pos[id];
                ActiveGrid.Gather(p, cd, Store.pos, scratchA, id);
                bool activeBelow = false;
                for (int k = 0; k < scratchA.Count; k++)
                    if (Store.pos[scratchA[k]].y < p.y - dy) { activeBelow = true; break; }
                if (activeBelow) continue; // something under it is still live; freeze it later
                Freeze(id);
            }
        }

        // ------------------------------------------------------------------ Queries

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

        /// <summary>Index/ledger consistency (dev check).</summary>
        public bool VerifyAll(out string error)
        {
            if (!Store.VerifyLedger(out error)) return false;
            int resting = Store.StateTotal(BallStore.State.Resting);
            if (Resting.Count != resting) { error = $"resting grid {Resting.Count} vs ledger {resting}"; return false; }
            int live = Store.StateTotal(BallStore.State.Active) + Store.StateTotal(BallStore.State.Held);
            if (Active.Count != live) { error = $"active pool {Active.Count} vs ledger {live}"; return false; }
            if (ActiveGrid.Count != live) { error = $"active grid {ActiveGrid.Count} vs ledger {live}"; return false; }
            return true;
        }

        public void Dispose()
        {
            Active.Dispose();
            Colliders.Dispose();
            for (int i = 0; i < envObjects.Count; i++) if (envObjects[i] != null) Object.Destroy(envObjects[i]);
            envObjects.Clear();
            Object.Destroy(Material);
        }

        private sealed class HeightComparer : IComparer<int>
        {
            private readonly BallStore store;
            public HeightComparer(BallStore s) { store = s; }
            public int Compare(int a, int b) => store.pos[a].y.CompareTo(store.pos[b].y);
        }
    }
}

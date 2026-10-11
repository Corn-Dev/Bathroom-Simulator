using System.Collections.Generic;
using UnityEngine;

namespace BallPitProto
{
    /// <summary>
    /// PROTOTYPE — not final.
    ///
    /// The ACTIVE BAND of the hybrid pit: the only real rigidbody balls in the
    /// scene. Everything else is cheap voxel data. This manager owns a pool of
    /// real physics balls and the promote/demote lifecycle:
    ///
    ///   PROMOTE: a grid cell near a disturbance becomes a real rigidbody that
    ///            rolls with physics (so collapse/throws look real).
    ///   DEMOTE:  once a ball comes to rest (slow for a grace period), it is
    ///            removed from physics entirely and folded back into the grid as
    ///            a cell — NOT left as a sleeping rigidbody (sleeping still
    ///            burdens the broadphase + GameObject count, per our research).
    ///
    /// A hard cap bounds how many rigidbodies exist at once. If a cascade would
    /// exceed the cap, the overflow is handled by the cheap grid settler instead
    /// of promoting — so we never blow the physics budget no matter how big the
    /// collapse.
    /// </summary>
    public sealed class ActiveBallManager
    {
        public struct ActiveBall
        {
            public Rigidbody rb;
            public Transform tr;
            public int color;
            public float slowTimer;   // how long it's been slow enough to demote
            public bool carried;      // held by a player (never auto-demotes)
        }

        private readonly VoxelPitGrid grid;
        private readonly Material[] colorMats;
        private readonly Mesh ballMesh;
        private readonly float ballVisualScale;

        private readonly List<ActiveBall> active = new List<ActiveBall>();
        private readonly Stack<Rigidbody> freePool = new Stack<Rigidbody>();
        private readonly Transform poolRoot;

        // --- tunables ---
        public int MaxActive = 1200;          // hard cap on concurrent rigidbodies
        public float DemoteSpeed = 1.2f;      // below this speed = "slow" (generous → settles fast)
        public float DemoteDelay = 0.15f;     // slow this long = demote (short → balls settle quickly)
        public float LinearDrag = 0.05f;      // near-zero: balls fall/throw at natural speed (NOT slow-mo)
        public float AngularDrag = 1.5f;      // some rotational drag so they stop ROLLING without slowing flight

        public int ActiveCount => active.Count;
        public int LastPromoted { get; private set; }
        public int LastDemoted { get; private set; }

        // instanced draw buffer for the active balls (so they match the pit look)
        private const int BatchSize = 1023;
        private readonly Matrix4x4[] drawBuf = new Matrix4x4[BatchSize];

        public ActiveBallManager(VoxelPitGrid grid, Mesh ballMesh, Material[] colorMats,
                                 float ballVisualScale, int poolSize, PhysicMaterial physMat)
        {
            this.grid = grid;
            this.ballMesh = ballMesh;
            this.colorMats = colorMats;
            this.ballVisualScale = ballVisualScale;

            poolRoot = new GameObject("Hybrid_ActivePool").transform;

            for (int i = 0; i < poolSize; i++)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                go.name = "Active_" + i;
                go.transform.SetParent(poolRoot);
                go.transform.localScale = Vector3.one * (grid.CellSize * ballVisualScale);

                // Cheapest viable physics settings (research-backed).
                var rb = go.AddComponent<Rigidbody>();
                rb.interpolation = RigidbodyInterpolation.Interpolate;
                rb.collisionDetectionMode = CollisionDetectionMode.Discrete;
                var col = go.GetComponent<SphereCollider>();
                if (physMat != null) col.sharedMaterial = physMat;

                // We draw them via instancing to match the pit; kill the renderer.
                Object.Destroy(go.GetComponent<MeshRenderer>());

                go.SetActive(false);
                freePool.Push(rb);
            }
        }

        public bool CanPromote => active.Count < MaxActive && freePool.Count > 0;

        /// <summary>
        /// Set true while any cell still needs its support re-checked (pending
        /// promotions beyond the cap). The controller keeps calling
        /// ProcessSupportQueue until this clears.
        /// </summary>
        public bool HasPendingSupport => supportQueue.Count > 0;
        public int PendingSupport => supportQueue.Count;

        // Cells to re-check for support (collapse front). A HashSet dedupes.
        private readonly Queue<Vector3Int> supportQueue = new Queue<Vector3Int>();
        private readonly HashSet<int> supportSet = new HashSet<int>();

        /// <summary>
        /// Promote one grid cell to a real rigidbody at its visual position, with
        /// an optional initial velocity. Clears the cell. Returns the rigidbody,
        /// or null if we're at the cap.
        /// </summary>
        public Rigidbody Promote(int cx, int cy, int cz, Vector3 initialVel, bool carried = false)
        {
            if (!CanPromote) return null;
            if (!grid.InBounds(cx, cy, cz) || grid.IsEmpty(cx, cy, cz)) return null;

            int color = grid.Get(cx, cy, cz);
            Vector3 pos = grid.CellToWorldVisual(cx, cy, cz);
            grid.Set(cx, cy, cz, VoxelPitGrid.Empty);

            // Removing this cell may unsupport its neighbors → queue them.
            QueueSupportCheck(cx, cy, cz);

            return PromoteAt(pos, color, initialVel, carried);
        }

        // --- Support propagation: the ONE collapse rule (no data-settler) ---

        /// <summary>
        /// Called whenever a cell is emptied (grab / dig / a ball promoted or
        /// demoted). Queues the cells that might now be unsupported — the cell
        /// above and the diagonal-up neighbors (which could slump into the gap).
        /// </summary>
        public void QueueSupportCheck(int x, int y, int z)
        {
            Enqueue(x, y + 1, z);
            Enqueue(x + 1, y, z); Enqueue(x - 1, y, z);
            Enqueue(x, y, z + 1); Enqueue(x, y, z - 1);
            Enqueue(x + 1, y + 1, z); Enqueue(x - 1, y + 1, z);
            Enqueue(x, y + 1, z + 1); Enqueue(x, y + 1, z - 1);
        }

        private void Enqueue(int x, int y, int z)
        {
            if (!grid.InBounds(x, y, z)) return;
            int idx = grid.Index(x, y, z);
            if (supportSet.Add(idx)) supportQueue.Enqueue(new Vector3Int(x, y, z));
        }

        /// <summary>
        /// Process up to maxThisFrame queued cells: any FILLED cell that is now
        /// unsupported (nothing directly below, not on the floor) PROMOTES to a
        /// physics ball and falls for real. Promoting empties the cell, which
        /// re-queues ITS neighbors — so the collapse front propagates through
        /// real physics, never by teleporting data. Bounded per frame + by the
        /// active cap (overflow stays queued and promotes as budget frees).
        /// </summary>
        public void ProcessSupportQueue(int maxThisFrame)
        {
            int budget = maxThisFrame;
            int toProcess = supportQueue.Count;

            while (budget > 0 && toProcess > 0 && supportQueue.Count > 0 && CanPromote)
            {
                var c = supportQueue.Dequeue();
                supportSet.Remove(grid.Index(c.x, c.y, c.z));
                toProcess--;

                if (grid.IsEmpty(c.x, c.y, c.z)) continue;
                if (IsSupported(c.x, c.y, c.z)) continue; // still held up → leave as data

                // Unsupported → become a real falling ball.
                int color = grid.Get(c.x, c.y, c.z);
                Vector3 pos = grid.CellToWorldVisual(c.x, c.y, c.z);
                grid.Set(c.x, c.y, c.z, VoxelPitGrid.Empty);
                PromoteAt(pos, color, Vector3.zero);
                budget--;

                // Its removal may unsupport further cells.
                QueueSupportCheck(c.x, c.y, c.z);
            }
        }

        /// <summary>
        /// A cell is supported if it rests on the floor OR has a filled cell
        /// directly below it. (Structural tolerance is intentionally low — a
        /// ball pit slumps readily; it does not hold big overhangs.)
        /// </summary>
        private bool IsSupported(int x, int y, int z)
        {
            if (y == 0) return true;                 // on the floor
            return !grid.IsEmpty(x, y - 1, z);       // something directly beneath
        }

        /// <summary>Promote a ball at an explicit position/color (e.g. a known surface ball).</summary>
        public Rigidbody PromoteAt(Vector3 pos, int color, Vector3 initialVel, bool carried = false)
        {
            if (!CanPromote) return null;

            var rb = freePool.Pop();
            var go = rb.gameObject;
            go.transform.position = pos;
            go.SetActive(true);
            rb.drag = LinearDrag;            // apply tunable drag at activation
            rb.angularDrag = AngularDrag;
            rb.isKinematic = carried;        // carried balls are kinematic (hand-driven)
            rb.velocity = carried ? Vector3.zero : initialVel;
            rb.angularVelocity = Vector3.zero;
            rb.WakeUp();

            active.Add(new ActiveBall
            {
                rb = rb,
                tr = go.transform,
                color = color,
                slowTimer = 0f,
                carried = carried
            });
            LastPromoted++;
            return rb;
        }

        /// <summary>
        /// Per-frame: check each active ball; demote any that have come to rest
        /// (and aren't carried) back into the grid as a cell. Removes them from
        /// physics entirely.
        /// </summary>
        public void Tick(float dt)
        {
            LastPromoted = 0;
            LastDemoted = 0;

            float sqrDemote = DemoteSpeed * DemoteSpeed;

            for (int i = active.Count - 1; i >= 0; i--)
            {
                var a = active[i];
                if (a.carried) { active[i] = a; continue; }

                bool slow = a.rb.velocity.sqrMagnitude < sqrDemote &&
                            a.rb.angularVelocity.sqrMagnitude < sqrDemote;

                if (slow) a.slowTimer += dt;
                else a.slowTimer = 0f;

                // Rested long enough → fold into the grid wherever it landed
                // (pit or floor). Whole-area grid: rest anywhere = become data.
                if (a.slowTimer >= DemoteDelay && TryDemoteToGrid(ref a))
                {
                    active.RemoveAt(i);
                    LastDemoted++;
                    continue;
                }
                active[i] = a;
            }
        }

        /// <summary>
        /// Fold a rested ball into the grid at the cell just above its column's
        /// current surface — ANYWHERE in the grid footprint (pit OR the floor
        /// around it). This is the "whole play area is a grid" model: a ball
        /// that comes to rest becomes cheap data wherever it landed, so a huge
        /// pile dumped on the floor turns into grid cells instead of thousands
        /// of live rigidbodies.
        ///
        /// Returns false only if the ball rested OUTSIDE the grid footprint
        /// entirely (shouldn't happen if the grid spans the play area) — in that
        /// case it's recycled to respect the cap rather than lingering.
        /// </summary>
        private bool TryDemoteToGrid(ref ActiveBall a)
        {
            Vector3 p = a.tr.position;

            // Map the ball's ACTUAL rest position to the nearest grid cell in all
            // three axes — do NOT snap to the column top. This is the fix for the
            // jarring teleport: a ball demotes into the cell where it physically
            // came to rest, so visually it barely moves.
            int cx = Mathf.FloorToInt((p.x - grid.Origin.x) / grid.CellSize);
            int cz = Mathf.FloorToInt((p.z - grid.Origin.z) / grid.CellSize);
            int cy = Mathf.FloorToInt((p.y - grid.Origin.y) / (grid.CellSize * grid.LayerSpacing));

            if (!grid.InBounds(cx, 0, cz)) { Recycle(a.rb); return true; } // off the grid
            cy = Mathf.Clamp(cy, 0, grid.SizeY - 1);

            if (FindNearestFreeCell(cx, cy, cz, out int fx, out int fy, out int fz))
            {
                grid.Set(fx, fy, fz, a.color);
                Recycle(a.rb);
                return true;
            }

            // Couldn't place (fully packed locally) — recycle to respect the cap.
            Recycle(a.rb);
            return true;
        }

        /// <summary>
        /// Find the empty cell nearest (cx,cy,cz) — the ball's real rest spot.
        /// Prefers the exact cell, then expands outward. This lands the demoted
        /// ball where it actually stopped instead of snapping to a column top.
        /// </summary>
        private bool FindNearestFreeCell(int cx, int cy, int cz, out int fx, out int fy, out int fz)
        {
            fx = cx; fy = cy; fz = cz;

            // Exact cell free? Use it.
            if (grid.InBounds(cx, cy, cz) && grid.IsEmpty(cx, cy, cz)) return true;

            // Expand in a small radius, nearest-first, biased slightly upward
            // (a resting ball sits ON others, so the free spot is usually just
            // above/beside where it mapped).
            for (int r = 1; r <= 3; r++)
            {
                for (int dy = 0; dy <= r; dy++)       // check same level + upward first
                for (int dx = -r; dx <= r; dx++)
                for (int dz = -r; dz <= r; dz++)
                {
                    if (Mathf.Max(Mathf.Abs(dx), Mathf.Max(dy, Mathf.Abs(dz))) != r) continue;
                    int x = cx + dx, y = cy + dy, z = cz + dz;
                    if (!grid.InBounds(x, y, z)) continue;
                    if (grid.IsEmpty(x, y, z))
                    {
                        // Only accept if supported (floor or something below) so
                        // we don't place a floating ball.
                        if (y == 0 || !grid.IsEmpty(x, y - 1, z)) { fx = x; fy = y; fz = z; return true; }
                    }
                }
            }
            return false;
        }

        /// <summary>Promote any grid cells near a point that lost support (used
        /// when something external — a thrown ball landing — disturbs the pit).</summary>
        public void DisturbAt(Vector3 world)
        {
            int cx = Mathf.FloorToInt((world.x - grid.Origin.x) / grid.CellSize);
            int cz = Mathf.FloorToInt((world.z - grid.Origin.z) / grid.CellSize);
            int cy = Mathf.FloorToInt((world.y - grid.Origin.y) / (grid.CellSize * grid.LayerSpacing));
            QueueSupportCheck(cx, Mathf.Clamp(cy, 0, grid.SizeY - 1), cz);
        }

        private void Recycle(Rigidbody rb)
        {
            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic = false;
            rb.gameObject.SetActive(false);
            freePool.Push(rb);
        }

        /// <summary>
        /// Clear the 'carried' flag on a ball (e.g. when the player throws it),
        /// so it becomes a normal active ball that will demote into the mesh once
        /// it rests. Without this, a thrown ball stays 'carried' forever and
        /// never settles/remeshes.
        /// </summary>
        public void ReleaseCarried(Rigidbody rb)
        {
            for (int i = 0; i < active.Count; i++)
            {
                if (active[i].rb == rb)
                {
                    var a = active[i];
                    a.carried = false;
                    a.slowTimer = 0f;
                    active[i] = a;
                    return;
                }
            }
        }

        /// <summary>Force-recycle a specific carried ball (e.g. after it's sold).</summary>
        public void ForceRecycle(Rigidbody rb)
        {
            for (int i = 0; i < active.Count; i++)
            {
                if (active[i].rb == rb)
                {
                    active.RemoveAt(i);
                    break;
                }
            }
            Recycle(rb);
        }

        /// <summary>Draw all active balls via instancing so they match the pit visuals.</summary>
        public void Draw()
        {
            Vector3 scale = Vector3.one * (grid.CellSize * ballVisualScale);
            for (int c = 0; c < colorMats.Length; c++)
            {
                int n = 0;
                for (int i = 0; i < active.Count; i++)
                {
                    if (active[i].color != c) continue;
                    var tr = active[i].tr;
                    drawBuf[n++] = Matrix4x4.TRS(tr.position, tr.rotation, scale);
                    if (n == BatchSize)
                    {
                        Graphics.DrawMeshInstanced(ballMesh, 0, colorMats[c], drawBuf, n);
                        n = 0;
                    }
                }
                if (n > 0)
                    Graphics.DrawMeshInstanced(ballMesh, 0, colorMats[c], drawBuf, n);
            }
        }

        public void ClearAll()
        {
            for (int i = 0; i < active.Count; i++)
                Recycle(active[i].rb);
            active.Clear();
        }
    }
}

using System.Collections.Generic;
using UnityEngine;

namespace BallPitProto
{
    /// <summary>
    /// PROTOTYPE — not final. Local single-player, NO networking.
    ///
    /// THE MERGED HYBRID PIT — everything we've learned combined:
    ///   - BULK = voxel grid data + GPU-instanced surface rendering (buried
    ///     cells culled) + ONE heightfield surface collider. Cheap at 100k+.
    ///   - ACTIVE BAND = a bounded pool of REAL rigidbody balls that roll with
    ///     physics. Surface cells PROMOTE to real balls when disturbed, and
    ///     DEMOTE back to grid data once they come to rest (removed from physics
    ///     entirely — not left as sleeping bodies).
    ///   - COLLAPSE IS REAL PHYSICS. When a cell loses support it promotes to a
    ///     physics ball and falls for real; it demotes back into the mesh only
    ///     once it rests. No data teleporting. A hard cap bounds concurrent
    ///     rigidbodies; overflow stays queued and promotes as balls settle.
    ///   - COUNT stays exact: grid cells + active balls are all one-ball-each.
    ///
    /// Drop on ONE empty GameObject in an empty scene and press Play.
    ///
    /// CONTROLS
    ///   Left click (pit)    : grab the ball you click into your hand (promotes
    ///                         to a real physics ball, animates to hand)
    ///   Right click         : throw the held ball
    ///   Right-drag          : orbit    wheel: zoom
    ///   B                   : bulk-dig under cursor (promotes a crater of real
    ///                         balls; overflow collapses as data) — the stress case
    ///   [ ]                 : bulk-dig size
    ///   1-5                 : pit size presets
    ///   R                   : reset    L: log
    /// </summary>
    public sealed class HybridPitController : MonoBehaviour
    {
        [Header("Pit size (the full starting ball mass)")]
        public int pitX = 40;
        public int pitY = 25;
        public int pitZ = 40; // 40*25*40 = 40,000 balls in the pit

        [Header("Play area (grid extends past the pit so balls can rest on the floor around it)")]
        [Tooltip("Extra columns of floor on each side of the pit. Balls dumped out settle here as cheap grid cells.")]
        public int floorMargin = 12;
        [Tooltip("Extra grid height above the pit for piles stacking up outside/on top.")]
        public int extraHeight = 6;

        // Full grid dimensions (pit + surrounding floor margin). Computed.
        private int sizeX, sizeY, sizeZ;
        // Where the pit region sits inside the full grid.
        private int pitX0, pitX1, pitZ0, pitZ1;

        [Header("Visuals")]
        public float cellSize = 0.5f;
        [Range(0.8f, 1.6f)] public float ballScale = 1.35f;
        public int colorCount = 4;
        public int seed = 12345;
        [Range(0f, 0.6f)] public float packOffset = 0.5f;
        [Range(0.6f, 1f)] public float layerSpacing = 0.82f;
        [Range(0f, 0.2f)] public float jitter = 0.08f;
        [Range(1, 4)] public int shellDepth = 2;

        [Header("Active band (real physics)")]
        [Tooltip("Hard cap on concurrent real rigidbody balls. Overflow collapses as data.")]
        public int maxActiveBalls = 1200;
        [Tooltip("Pooled rigidbodies allocated up front (>= maxActiveBalls).")]
        public int ballPoolSize = 1400;
        [Tooltip("Ball speed below which it's 'at rest' and demotes into the mesh. Higher = settles sooner.")]
        public float demoteSpeed = 1.2f;
        [Tooltip("How long a ball must stay slow before demoting. Short = quick settle.")]
        public float demoteDelay = 0.15f;
        [Tooltip("Linear drag on active balls. Keep LOW (~0.05) or throws/falls look slow-motion.")]
        public float ballLinearDrag = 0.05f;
        [Tooltip("Angular drag — stops balls ROLLING forever without slowing their flight.")]
        public float ballAngularDrag = 1.5f;

        [Header("Collapse (real physics)")]
        [Tooltip("Max cells promoted to physics per frame as the collapse front spreads. Bounds the per-frame spawn spike.")]
        public int supportPromotesPerFrame = 200;

        [Header("Dig / throw")]
        public int bulkDigSize = 60;
        public int maxBulkDig = 600;
        public float throwSpeed = 12f;
        public float throwUpBias = 2.5f;
        public float grabAnimTime = 0.16f;

        [Header("Physics globals")]
        [Range(20, 60)] public int physicsHz = 50;
        [Range(1, 10)] public int solverIterations = 4;

        [Header("Logging")]
        public float autoLogInterval = 2f;

        // --- runtime ---
        private VoxelPitGrid grid;
        private VoxelPitRenderer pitRenderer;
        private VoxelSurfaceCollider surfaceCollider;
        private ActiveBallManager activeBalls;

        private Mesh ballMesh;
        private Material[] colorMats;
        private PhysicMaterial ballPhysMat;
        private GameObject[] pitWalls;

        private Camera cam;
        private Transform camPivot;
        private float orbitYaw = 35f, orbitPitch = 35f, orbitDist;
        private bool rmbDragged;

        private int lastRebuildFilled = -1;
        private bool surfaceDirty;

        // held ball
        private enum HeldState { None, Animating, Held }
        private HeldState heldState = HeldState.None;
        private Rigidbody heldBall;
        private Vector3 grabStart;
        private float grabT;

        // hover selection (what the cursor is pointing at this frame)
        private bool hoverValid;
        private int hoverX, hoverY, hoverZ;
        private Vector3 hoverPos;
        private Material highlightMat;
        private Mesh highlightMesh;
        [Header("Selection")]
        [Tooltip("Max surface cells to test around the ray hit when picking the clicked ball.")]
        public int pickSearchRadius = 3;

        // profiling
        private readonly ProtoProfiler profiler = new ProtoProfiler();
        private float logTimer;
        private GUIStyle labelStyle;

        private static readonly Vector3Int[] Presets =
        {
            new Vector3Int(25, 16, 25),  // 10k
            new Vector3Int(32, 20, 32),  // ~20k
            new Vector3Int(40, 25, 40),  // 40k
            new Vector3Int(50, 30, 50),  // 75k
            new Vector3Int(64, 40, 64),  // 164k
        };

        private void Start()
        {
            Time.fixedDeltaTime = 1f / physicsHz;
            Physics.defaultSolverIterations = solverIterations;
            Physics.autoSyncTransforms = false;

            BuildMeshAndMats();
            BuildGrid();
            BuildEnvironment();
            BuildCamera();
            BuildActive();

            pitRenderer = new VoxelPitRenderer(grid, ballMesh, colorMats, ballScale) { ShellDepth = shellDepth };
            RebuildAll();
        }

        // ------------------------------------------------------------- build

        private void BuildMeshAndMats()
        {
            var temp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            ballMesh = temp.GetComponent<MeshFilter>().sharedMesh;
            Destroy(temp);

            colorMats = new Material[colorCount];
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            for (int i = 0; i < colorCount; i++)
            {
                var c = Color.HSVToRGB((float)i / colorCount, 0.75f, 0.95f);
                var m = new Material(shader) { enableInstancing = true };
                m.color = c;
                if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
                colorMats[i] = m;
            }

            // Highlight: a bright emissive sphere drawn slightly larger than a
            // ball, around whatever the cursor is pointing at.
            highlightMesh = ballMesh;
            highlightMat = new Material(shader);
            var hi = new Color(1f, 1f, 0.2f);
            highlightMat.color = hi;
            if (highlightMat.HasProperty("_BaseColor")) highlightMat.SetColor("_BaseColor", hi);
            if (highlightMat.HasProperty("_EmissionColor"))
            {
                highlightMat.EnableKeyword("_EMISSION");
                highlightMat.SetColor("_EmissionColor", hi * 1.5f);
            }
        }

        private void BuildGrid()
        {
            // Full grid = pit + a floor margin all around, so dumped balls that
            // land outside the pit settle into grid cells (cheap) instead of
            // lingering as thousands of live rigidbodies.
            sizeX = pitX + floorMargin * 2;
            sizeZ = pitZ + floorMargin * 2;
            sizeY = pitY + extraHeight;
            pitX0 = floorMargin; pitX1 = floorMargin + pitX;
            pitZ0 = floorMargin; pitZ1 = floorMargin + pitZ;

            Vector3 origin = new Vector3(-sizeX * cellSize * 0.5f, 0f, -sizeZ * cellSize * 0.5f);
            grid = new VoxelPitGrid(sizeX, sizeY, sizeZ, cellSize, origin)
            {
                PackOffset = packOffset,
                LayerSpacing = layerSpacing,
                Jitter = jitter
            };
            // Only the central pit region starts full; the floor margin is empty.
            grid.FillRegion(pitX0, pitX1, pitZ0, pitZ1, pitY, colorCount, seed);
        }

        private void BuildEnvironment()
        {
            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "Hybrid_Ground";
            ground.transform.position = new Vector3(0f, -0.5f, 0f);
            ground.transform.localScale = new Vector3(sizeX * cellSize * 4f, 1f, sizeZ * cellSize * 4f);
            var gmat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            gmat.color = new Color(0.18f, 0.18f, 0.2f);
            ground.GetComponent<MeshRenderer>().sharedMaterial = gmat;

            BuildWalls();

            ballPhysMat = new PhysicMaterial("Ball")
            {
                bounciness = 0.2f,
                dynamicFriction = 0.5f,
                staticFriction = 0.5f,
                bounceCombine = PhysicMaterialCombine.Average,
                frictionCombine = PhysicMaterialCombine.Average
            };
            surfaceCollider = new VoxelSurfaceCollider(grid, transform, ballPhysMat);
        }

        private void BuildWalls()
        {
            float w = sizeX * cellSize, d = sizeZ * cellSize, h = sizeY * cellSize, t = 0.2f;
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            mat.color = new Color(0.1f, 0.1f, 0.12f);
            var list = new List<GameObject>();
            void Wall(string n, Vector3 p, Vector3 s)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = n; go.transform.position = p; go.transform.localScale = s;
                go.GetComponent<MeshRenderer>().sharedMaterial = mat;
                list.Add(go);
            }
            Wall("W+X", new Vector3(w * 0.5f + t * 0.5f, h * 0.5f, 0), new Vector3(t, h, d));
            Wall("W-X", new Vector3(-w * 0.5f - t * 0.5f, h * 0.5f, 0), new Vector3(t, h, d));
            Wall("W+Z", new Vector3(0, h * 0.5f, d * 0.5f + t * 0.5f), new Vector3(w + 2 * t, h, t));
            Wall("W-Z", new Vector3(0, h * 0.5f, -d * 0.5f - t * 0.5f), new Vector3(w + 2 * t, h, t));
            pitWalls = list.ToArray();
        }

        private void BuildCamera()
        {
            var camGo = new GameObject("Hybrid_Cam");
            cam = camGo.AddComponent<Camera>();
            camGo.AddComponent<AudioListener>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.05f, 0.06f, 0.08f);

            var lightGo = new GameObject("Hybrid_Light");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional; light.intensity = 1.1f;
            lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            camPivot = new GameObject("Hybrid_Pivot").transform;
            camPivot.position = new Vector3(0f, sizeY * cellSize * 0.4f, 0f);
            orbitDist = Mathf.Max(sizeX, sizeZ) * cellSize * 1.6f;
            UpdateCam();
        }

        private void BuildActive()
        {
            activeBalls = new ActiveBallManager(grid, ballMesh, colorMats, ballScale, ballPoolSize, ballPhysMat)
            {
                MaxActive = maxActiveBalls,
                DemoteSpeed = demoteSpeed,
                DemoteDelay = demoteDelay,
                LinearDrag = ballLinearDrag,
                AngularDrag = ballAngularDrag
            };
        }

        private void RebuildAll()
        {
            profiler.BeginSample();
            pitRenderer.Rebuild();
            profiler.RecordRebuild(profiler.EndSampleMs());
            profiler.BeginSample();
            surfaceCollider.Rebuild();
            profiler.RecordCollider(profiler.EndSampleMs());
            lastRebuildFilled = grid.FilledCount;
            surfaceDirty = false;
        }

        // ------------------------------------------------------------- update

        private void Update()
        {
            profiler.Tick(Time.unscaledDeltaTime);
            UpdateHover();
            HandleInput();
            UpdateHeld();

            // COLLAPSE = REAL PHYSICS. Any grid cell that lost support promotes
            // to a physics ball and falls for real (no data teleporting). The
            // collapse front propagates as balls fall, demote, and expose more
            // unsupported cells. Bounded per frame + by the active cap; overflow
            // stays queued and promotes as balls settle and free up budget.
            if (activeBalls.HasPendingSupport)
            {
                activeBalls.ProcessSupportQueue(supportPromotesPerFrame);
                surfaceDirty = true;
            }

            // Demote rested balls into the mesh (physics ball -> grid cell).
            activeBalls.Tick(Time.deltaTime);
            bool demotedThisFrame = activeBalls.LastDemoted > 0;
            if (demotedThisFrame) surfaceDirty = true;

            // Rebuild the surface. A promote/demote/dig is a discrete handoff, so
            // rebuild IMMEDIATELY (the grid ball appears the frame the physics
            // ball leaves, and vice versa — no flicker). The throttle only avoids
            // doing the ~40ms rebuild many times in one frame; here it's at most
            // once per frame anyway, so just rebuild whenever dirty.
            if (surfaceDirty)
            {
                RebuildAll();
                surfaceDirty = false;
            }

            pitRenderer.Draw();
            activeBalls.Draw();

            // Draw the hover highlight slightly larger than a ball.
            if (hoverValid && heldState == HeldState.None)
            {
                float s = cellSize * ballScale * 1.18f;
                var m = Matrix4x4.TRS(hoverPos, Quaternion.identity, Vector3.one * s);
                Graphics.DrawMesh(highlightMesh, m, highlightMat, 0);
            }

            profiler.ActiveBalls = activeBalls.ActiveCount;

            logTimer += Time.unscaledDeltaTime;
            if (logTimer >= autoLogInterval)
            {
                logTimer = 0f;
                profiler.LogSnapshot("auto", grid.FilledCount, sizeX * sizeY * sizeZ,
                    pitRenderer.TotalDrawCalls, pitRenderer.VisibleCount);
            }
        }

        private void HandleInput()
        {
            if (Input.GetKeyDown(KeyCode.LeftBracket)) bulkDigSize = Mathf.Max(1, bulkDigSize - 10);
            if (Input.GetKeyDown(KeyCode.RightBracket)) bulkDigSize = Mathf.Min(maxBulkDig, bulkDigSize + 10);
            if (Input.GetKeyDown(KeyCode.R)) ResetPit();
            if (Input.GetKeyDown(KeyCode.B)) BulkDigAtCursor();
            if (Input.GetKeyDown(KeyCode.L))
                profiler.LogSnapshot("manual", grid.FilledCount, sizeX * sizeY * sizeZ, pitRenderer.TotalDrawCalls, pitRenderer.VisibleCount);
            if (Input.GetKeyDown(KeyCode.Alpha1)) ApplyPreset(0);
            if (Input.GetKeyDown(KeyCode.Alpha2)) ApplyPreset(1);
            if (Input.GetKeyDown(KeyCode.Alpha3)) ApplyPreset(2);
            if (Input.GetKeyDown(KeyCode.Alpha4)) ApplyPreset(3);
            if (Input.GetKeyDown(KeyCode.Alpha5)) ApplyPreset(4);

            if (Input.GetMouseButton(1))
            {
                float dx = Input.GetAxis("Mouse X"), dy = Input.GetAxis("Mouse Y");
                if (Mathf.Abs(dx) > 0.01f || Mathf.Abs(dy) > 0.01f)
                {
                    rmbDragged = true;
                    orbitYaw += dx * 4f;
                    orbitPitch = Mathf.Clamp(orbitPitch - dy * 3f, 5f, 85f);
                }
            }
            if (Input.GetMouseButtonUp(1))
            {
                if (!rmbDragged && heldState == HeldState.Held) ThrowHeld();
                rmbDragged = false;
            }

            float wheel = Input.GetAxis("Mouse ScrollWheel");
            if (Mathf.Abs(wheel) > 0.001f)
                orbitDist = Mathf.Clamp(orbitDist - wheel * orbitDist, cellSize * 4f, 500f);
            UpdateCam();

            if (Input.GetMouseButtonDown(0) && heldState == HeldState.None && !Input.GetMouseButton(1))
                GrabAtCursor();
        }

        private void UpdateCam()
        {
            if (cam == null) return;
            Quaternion rot = Quaternion.Euler(orbitPitch, orbitYaw, 0f);
            cam.transform.position = camPivot.position + rot * new Vector3(0, 0, -orbitDist);
            cam.transform.rotation = Quaternion.LookRotation(camPivot.position - cam.transform.position, Vector3.up);
        }

        // ------------------------------------------------------------- grab/throw

        private Vector3 HandPos() => cam.ViewportToWorldPoint(new Vector3(0.78f, 0.22f, cellSize * 3.2f));

        /// <summary>
        /// Determine which surface ball the cursor is pointing at, every frame,
        /// using a true ray-vs-sphere test against the visible surface balls
        /// (not column mapping). Stores the result for the highlight + grab so
        /// what you click is exactly what's highlighted.
        /// </summary>
        private void UpdateHover()
        {
            hoverValid = false;
            if (cam == null) return;

            Ray ray = cam.ScreenPointToRay(Input.mousePosition);

            // Seed the search near where the ray meets the pit (collider hit, or
            // a plane fallback), then test the surrounding surface balls.
            if (!AimColumn(out int cx, out int cz)) return;

            float ballRadius = cellSize * ballScale * 0.5f;
            float bestT = float.MaxValue;
            float bestMiss = float.MaxValue;
            int R = Mathf.Max(1, pickSearchRadius);

            // Test every EXPOSED ball in the neighborhood columns — not just each
            // column's single top cell. A ball on a crater wall or slope is
            // exposed but isn't its column's top; it must be selectable too.
            // (Old code only tested TopFilledY, so you could only ever grab the
            // highest ball of each column.)
            for (int dx = -R; dx <= R; dx++)
            for (int dz = -R; dz <= R; dz++)
            {
                int x = cx + dx, z = cz + dz;
                if (x < 0 || x >= grid.SizeX || z < 0 || z >= grid.SizeZ) continue;

                int top = grid.TopFilledY(x, z);
                if (top < 0) continue;

                // Scan from the column top downward over exposed cells only.
                for (int y = top; y >= 0; y--)
                {
                    if (grid.IsEmpty(x, y, z)) continue;
                    if (!IsExposedCell(x, y, z)) break; // once we hit buried cells, stop (deeper are hidden)

                    Vector3 c = grid.CellToWorldVisual(x, y, z);
                    if (RaySphere(ray, c, ballRadius, out float t))
                    {
                        if (t < bestT)
                        {
                            bestT = t;
                            hoverValid = true;
                            hoverX = x; hoverY = y; hoverZ = z; hoverPos = c;
                        }
                    }
                    else if (!hoverValid)
                    {
                        // No direct hit anywhere yet — track closest-to-ray as a
                        // fallback so there's always a sensible selection.
                        float miss = Vector3.Cross(ray.direction, c - ray.origin).sqrMagnitude;
                        if (miss < bestMiss)
                        {
                            bestMiss = miss;
                            hoverX = x; hoverY = y; hoverZ = z; hoverPos = c;
                            hoverFallback = true;
                        }
                    }
                }
            }

            if (!hoverValid && hoverFallback) hoverValid = true;
            hoverFallback = false;
        }

        private bool hoverFallback;

        /// <summary>A cell is exposed (visible/selectable) if any face-neighbor
        /// is empty or out of bounds.</summary>
        private bool IsExposedCell(int x, int y, int z)
        {
            return NbrEmpty(x + 1, y, z) || NbrEmpty(x - 1, y, z) ||
                   NbrEmpty(x, y + 1, z) || NbrEmpty(x, y - 1, z) ||
                   NbrEmpty(x, y, z + 1) || NbrEmpty(x, y, z - 1);
        }
        private bool NbrEmpty(int x, int y, int z) => !grid.InBounds(x, y, z) || grid.IsEmpty(x, y, z);

        /// <summary>Ray vs sphere; returns nearest positive hit distance t.</summary>
        private static bool RaySphere(Ray ray, Vector3 center, float radius, out float t)
        {
            t = 0f;
            Vector3 oc = ray.origin - center;
            float b = Vector3.Dot(oc, ray.direction);
            float c = Vector3.Dot(oc, oc) - radius * radius;
            float disc = b * b - c;
            if (disc < 0f) return false;
            float sq = Mathf.Sqrt(disc);
            float t0 = -b - sq;
            float t1 = -b + sq;
            t = t0 >= 0f ? t0 : t1;
            return t >= 0f;
        }

        private void GrabAtCursor()
        {
            if (!hoverValid) return;
            int x = hoverX, y = hoverY, z = hoverZ;
            if (grid.IsEmpty(x, y, z)) return; // stale (surface moved) — skip this click

            int color = grid.Get(x, y, z);
            Vector3 pos = grid.CellToWorldVisual(x, y, z);
            grid.Set(x, y, z, VoxelPitGrid.Empty);
            heldBall = activeBalls.PromoteAt(pos, color, Vector3.zero, carried: true);
            if (heldBall == null) { grid.Set(x, y, z, color); return; } // at cap; abort

            grabStart = pos; grabT = 0f; heldState = HeldState.Animating;
            activeBalls.QueueSupportCheck(x, y, z); // neighbors may now collapse (physics)
            surfaceDirty = true;
        }

        private void UpdateHeld()
        {
            if (heldState == HeldState.Animating)
            {
                grabT += Time.deltaTime / Mathf.Max(0.0001f, grabAnimTime);
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(grabT));
                heldBall.transform.position = Vector3.Lerp(grabStart, HandPos(), t);
                if (grabT >= 1f) heldState = HeldState.Held;
            }
            else if (heldState == HeldState.Held)
            {
                heldBall.transform.position = HandPos();
            }
        }

        private void ThrowHeld()
        {
            var rb = heldBall;
            heldBall = null; heldState = HeldState.None;
            rb.isKinematic = false;
            activeBalls.ReleaseCarried(rb); // clear 'carried' so it demotes when it rests
            Vector3 dir = (cam.transform.forward + Vector3.up * (throwUpBias / Mathf.Max(1f, throwSpeed))).normalized;
            rb.velocity = dir * throwSpeed;
            // It's now a free active ball; the manager will demote it when it rests.
        }

        // ------------------------------------------------------------- bulk dig

        private void BulkDigAtCursor()
        {
            if (!AimColumn(out int cx, out int cz)) return;

            // Carve a crater: pull surface balls out as REAL physics that scatter
            // from the crater, and queue each removal so the surrounding mass
            // collapses into the hole via physics (ProcessSupportQueue). If we're
            // at the cap, we still remove the cell and queue it — it'll promote
            // once budget frees as other balls settle. No data teleporting.
            int remaining = bulkDigSize;
            int radius = Mathf.Clamp(Mathf.RoundToInt(Mathf.Sqrt(bulkDigSize) * 0.5f), 0, 8);

            for (int ring = 0; ring <= radius && remaining > 0; ring++)
            for (int dx = -ring; dx <= ring && remaining > 0; dx++)
            for (int dz = -ring; dz <= ring && remaining > 0; dz++)
            {
                if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz)) != ring) continue;
                int x = cx + dx, z = cz + dz;
                if (x < 0 || x >= grid.SizeX || z < 0 || z >= grid.SizeZ) continue;
                int topY = grid.TopFilledY(x, z);
                if (topY < 0) continue;

                int color = grid.Get(x, topY, z);
                Vector3 pos = grid.CellToWorldVisual(x, topY, z);
                grid.Set(x, topY, z, VoxelPitGrid.Empty);

                if (activeBalls.CanPromote)
                {
                    Vector3 outward = new Vector3(dx, 0.6f, dz);
                    if (outward.sqrMagnitude < 0.01f) outward = Vector3.up;
                    activeBalls.PromoteAt(pos, color, outward.normalized * 4f);
                }
                // Whether or not we promoted this one, the removal may unsupport
                // its neighbors → they collapse as physics via the support queue.
                activeBalls.QueueSupportCheck(x, topY, z);
                remaining--;
            }
            surfaceDirty = true;
        }

        // ------------------------------------------------------------- helpers

        private bool AimColumn(out int cx, out int cz)
        {
            cx = cz = -1;
            Ray ray = cam.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit, 2000f))
                return WorldToColumn(hit.point, out cx, out cz);

            float planeY = grid.Origin.y + grid.SizeY * grid.CellSize * 0.5f;
            var plane = new Plane(Vector3.up, new Vector3(0, planeY, 0));
            if (plane.Raycast(ray, out float enter))
            {
                Vector3 p = ray.GetPoint(enter);
                if (p.x >= grid.Origin.x && p.x <= grid.Origin.x + grid.SizeX * grid.CellSize &&
                    p.z >= grid.Origin.z && p.z <= grid.Origin.z + grid.SizeZ * grid.CellSize)
                    return WorldToColumn(p, out cx, out cz);
            }
            return false;
        }

        private bool WorldToColumn(Vector3 world, out int cx, out int cz)
        {
            cx = Mathf.Clamp(Mathf.FloorToInt((world.x - grid.Origin.x) / grid.CellSize), 0, grid.SizeX - 1);
            cz = Mathf.Clamp(Mathf.FloorToInt((world.z - grid.Origin.z) / grid.CellSize), 0, grid.SizeZ - 1);
            return true;
        }

        private void ResetPit()
        {
            activeBalls.ClearAll();
            heldBall = null; heldState = HeldState.None;
            grid.FillRegion(pitX0, pitX1, pitZ0, pitZ1, pitY, colorCount, seed);
            RebuildAll();
        }

        private void ApplyPreset(int i)
        {
            if (i < 0 || i >= Presets.Length) return;
            var s = Presets[i];
            pitX = s.x; pitY = s.y; pitZ = s.z; // BuildGrid recomputes full grid

            activeBalls.ClearAll();
            heldBall = null; heldState = HeldState.None;

            if (surfaceCollider?.GameObject != null) Destroy(surfaceCollider.GameObject);
            if (pitWalls != null) foreach (var w in pitWalls) if (w) Destroy(w);

            BuildGrid();
            BuildWalls();
            surfaceCollider = new VoxelSurfaceCollider(grid, transform, ballPhysMat);
            pitRenderer = new VoxelPitRenderer(grid, ballMesh, colorMats, ballScale) { ShellDepth = shellDepth };
            BuildActive();

            camPivot.position = new Vector3(0f, sizeY * cellSize * 0.4f, 0f);
            orbitDist = Mathf.Max(sizeX, sizeZ) * cellSize * 1.6f;
            UpdateCam();

            profiler.ResetPeaks();
            RebuildAll();
            profiler.LogSnapshot($"preset {sizeX}x{sizeY}x{sizeZ}", grid.FilledCount,
                sizeX * sizeY * sizeZ, pitRenderer.TotalDrawCalls, pitRenderer.VisibleCount);
        }

        // ------------------------------------------------------------- UI

        private void OnGUI()
        {
            if (labelStyle == null)
            {
                labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 15, richText = true };
                labelStyle.normal.textColor = Color.white;
            }

            const int pad = 10;
            GUI.Box(new Rect(pad, pad, 390, 250), GUIContent.none);
            var r = new Rect(pad + 10, pad + 8, 370, 22);

            GUI.Label(r, "<b>Hybrid Pit Prototype</b> (not final)", labelStyle); r.y += 24;
            GUI.Label(r, $"Pit {pitX}x{pitY}x{pitZ}  grid {sizeX}x{sizeY}x{sizeZ}   balls remaining {grid.FilledCount:n0}", labelStyle); r.y += 22;

            float fps = profiler.AvgFps, worst = profiler.MinFps;
            string fc = fps >= 60 ? "lime" : fps >= 30 ? "yellow" : "red";
            GUI.Label(r, $"FPS avg <color={fc}>{fps:n0}</color>  worst {worst:n0}   frame {profiler.FrameMsAvg:n1}ms", labelStyle); r.y += 22;

            int ab = activeBalls.ActiveCount;
            string ac = ab < maxActiveBalls * 0.6f ? "lime" : ab < maxActiveBalls ? "yellow" : "red";
            GUI.Label(r, $"ACTIVE balls (real physics): <color={ac}>{ab:n0}</color> / {maxActiveBalls:n0}", labelStyle); r.y += 22;
            GUI.Label(r, $"Drawn: pit {pitRenderer.VisibleCount:n0} + active {ab:n0}   draws {pitRenderer.TotalDrawCalls}", labelStyle); r.y += 22;
            GUI.Label(r, $"Rebuild {profiler.LastRebuildMs:n1}ms  collider {profiler.LastColliderMs:n1}ms  collapse-pending {activeBalls.PendingSupport:n0}", labelStyle); r.y += 22;
            GUI.Label(r, $"Holding: {(heldState != HeldState.None ? "yes (RMB to throw)" : "no")}", labelStyle); r.y += 26;

            GUI.Label(new Rect(pad + 10, r.y, 180, 24), $"Bulk dig: {bulkDigSize}", labelStyle);
            if (GUI.Button(new Rect(pad + 150, r.y, 30, 24), "-")) bulkDigSize = Mathf.Max(1, bulkDigSize - 10);
            if (GUI.Button(new Rect(pad + 184, r.y, 30, 24), "+")) bulkDigSize = Mathf.Min(maxBulkDig, bulkDigSize + 10);
            if (GUI.Button(new Rect(pad + 222, r.y, 60, 24), "Dig (B)")) BulkDigAtCursor();
            if (GUI.Button(new Rect(pad + 288, r.y, 70, 24), "Reset")) ResetPit();
            r.y += 30;

            GUI.Label(new Rect(pad + 10, r.y, 370, 22), "Pit size:", labelStyle);
            for (int i = 0; i < Presets.Length; i++)
            {
                var s = Presets[i];
                if (GUI.Button(new Rect(pad + 72 + i * 60, r.y, 56, 22), $"{s.x * s.y * s.z / 1000}k"))
                    ApplyPreset(i);
            }
            r.y += 26;

            GUI.Label(r, "<b>LMB</b> grab  <b>RMB</b> throw/orbit  <b>B</b> dig  <b>1-5</b> size  <b>R</b> reset  <b>L</b> log", labelStyle);
        }
    }
}

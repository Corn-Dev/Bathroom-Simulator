using System.Collections.Generic;
using UnityEngine;

namespace BallPitProto
{
    /// <summary>
    /// PROTOTYPE — not final. Local single-player, NO networking.
    ///
    /// Drop this on ONE empty GameObject in an empty scene and press Play.
    /// It builds everything procedurally: the voxel pit (default 10,000 balls),
    /// a camera, a light, the ground + pit walls, instanced rendering, a
    /// generated surface collider, a pool of real physics balls, and UI.
    ///
    /// Goal: stress the voxel dig + collapse (settling) logic AND feel out the
    /// promote / hold / throw loop. Throwaway test harness, not shipping code.
    ///
    /// CONTROLS
    ///   Left click (on pit) : promote the top ball under the cursor into your
    ///                         hand (animates to a held position by the camera)
    ///   Left click (holding): nothing (already holding) — right click to throw
    ///   Right click         : throw the held ball forward
    ///   Right-drag          : orbit the camera
    ///   Mouse wheel         : zoom
    ///   [ and ]             : decrease / increase balls removed per BULK grab
    ///   B                   : bulk-remove removePerGrab balls under the cursor
    ///                         (auto-tossed — for stress-testing collapse)
    ///   R                   : reset / refill the pit
    /// </summary>
    public sealed class BallPitProtoController : MonoBehaviour
    {
        [Header("Pit size (X * Y * Z should ~= target ball count)")]
        [Tooltip("Width in cells.")]  public int sizeX = 25;
        [Tooltip("Height in cells.")] public int sizeY = 16;
        [Tooltip("Depth in cells.")]  public int sizeZ = 25;
        // 25 * 16 * 25 = 10,000 cells.

        [Header("Visuals")]
        public float cellSize = 0.5f;
        [Tooltip("Ball render size vs cell. >1 overlaps neighbors so there are no gaps (real settled balls visually touch).")]
        [Range(0.8f, 1.6f)] public float ballScale = 1.35f;
        public int colorCount = 4;
        public int seed = 12345;

        [Header("Packing (how balls nestle, like a real settled pit)")]
        [Tooltip("Offset of alternate rows/layers so balls sit in valleys below. 0=cubic stacks, 0.5=hex close-pack.")]
        [Range(0f, 0.6f)] public float packOffset = 0.5f;
        [Tooltip("Vertical layer spacing vs cell. ~0.82 = real close-packed sphere layers interlock. Lower = denser/deeper.")]
        [Range(0.6f, 1f)] public float layerSpacing = 0.82f;
        [Tooltip("Random per-ball jitter vs cell, for a natural irregular surface.")]
        [Range(0f, 0.2f)] public float jitter = 0.08f;
        [Tooltip("How many ball layers to draw behind the surface. 2 plugs the front gaps so you can't see through.")]
        [Range(1, 4)] public int shellDepth = 2;
        [Tooltip("Draw a dark backing surface behind the balls as a last-resort no-see-through. Try OFF first; the overlapping shell may be enough.")]
        public bool useVisibleBacking = false;

        [Header("Settling")]
        [Tooltip("Max cell moves resolved per frame. Lower = cascades spread over more frames.")]
        public int settleMovesPerFrame = 400;

        [Header("Bulk dig (stress test)")]
        [Tooltip("How many balls the B key / bulk button removes at once.")]
        public int removePerGrab = 1;
        public int maxRemovePerGrab = 500;

        [Header("Hold / Throw")]
        [Tooltip("Seconds for a grabbed ball to animate into the hand.")]
        public float grabAnimTime = 0.18f;
        [Tooltip("Throw speed (m/s) when you right-click.")]
        public float throwSpeed = 12f;
        public float throwUpBias = 2.5f;
        [Tooltip("Impulse applied to bulk-dug balls so they scatter.")]
        public float bulkTossForce = 5f;
        public int ballPoolSize = 256;

        // --- runtime ---
        private VoxelPitGrid grid;
        private VoxelSettler settler;
        private VoxelPitRenderer pitRenderer;
        private VoxelSurfaceCollider surfaceCollider;
        private PhysicMaterial ballPitPhysMat;
        private Material backingMat;
        private GameObject[] pitWalls;

        private Mesh ballMesh;
        private Material[] colorMaterials;
        private Color[] colorValues;

        private Camera cam;
        private Transform camPivot;
        private float orbitYaw = 35f, orbitPitch = 35f, orbitDist;

        private readonly List<Rigidbody> ballPool = new List<Rigidbody>();
        private int ballPoolCursor;

        private int lastRebuildFilled = -1;
        private bool surfaceDirty = true;

        private readonly ProtoProfiler profiler = new ProtoProfiler();
        private float logTimer;
        [Tooltip("Seconds between automatic console perf logs.")]
        public float autoLogInterval = 2f;

        // Pit-size presets switchable at runtime with number keys 1-5.
        private static readonly Vector3Int[] SizePresets =
        {
            new Vector3Int(25, 16, 25),  // 1: 10,000
            new Vector3Int(32, 20, 32),  // 2: ~20,480
            new Vector3Int(40, 25, 40),  // 3: 40,000
            new Vector3Int(50, 30, 50),  // 4: 75,000
            new Vector3Int(64, 40, 64),  // 5: 163,840
        };

        private GUIStyle labelStyle;

        // --- held-ball state machine ---
        private enum HeldState { None, Animating, Held }
        private HeldState heldState = HeldState.None;
        private Rigidbody heldBall;
        private Vector3 grabStartPos;
        private float grabAnimT;

        private void Start()
        {
            BuildMeshAndMaterials();
            BuildGrid();
            BuildEnvironment();
            BuildCamera();
            BuildBallPool();

            pitRenderer = new VoxelPitRenderer(grid, ballMesh, colorMaterials, ballScale);
            pitRenderer.ShellDepth = shellDepth;
            pitRenderer.Rebuild();
            surfaceCollider.Rebuild();
            lastRebuildFilled = grid.FilledCount;
            surfaceDirty = false;
        }

        // ---------------------------------------------------------------- build

        private void BuildMeshAndMaterials()
        {
            var temp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            ballMesh = temp.GetComponent<MeshFilter>().sharedMesh;
            Destroy(temp);

            colorValues = new Color[colorCount];
            colorMaterials = new Material[colorCount];
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");

            for (int i = 0; i < colorCount; i++)
            {
                float h = (float)i / Mathf.Max(1, colorCount);
                colorValues[i] = Color.HSVToRGB(h, 0.75f, 0.95f);
                var mat = new Material(shader) { enableInstancing = true };
                mat.color = colorValues[i];
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", colorValues[i]);
                colorMaterials[i] = mat;
            }
        }

        private void BuildGrid()
        {
            Vector3 origin = new Vector3(-sizeX * cellSize * 0.5f, 0f, -sizeZ * cellSize * 0.5f);
            grid = new VoxelPitGrid(sizeX, sizeY, sizeZ, cellSize, origin);
            grid.PackOffset = packOffset;
            grid.LayerSpacing = layerSpacing;
            grid.Jitter = jitter;
            grid.FillSolid(colorCount, seed);
            settler = new VoxelSettler(grid);
        }

        private void BuildEnvironment()
        {
            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "Proto_Ground";
            ground.transform.position = new Vector3(0f, -0.5f, 0f);
            ground.transform.localScale = new Vector3(sizeX * cellSize * 4f, 1f, sizeZ * cellSize * 4f);
            var gmat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            gmat.color = new Color(0.18f, 0.18f, 0.2f);
            ground.GetComponent<MeshRenderer>().sharedMaterial = gmat;

            BuildPitWalls();

            // One collider mesh for the whole pit surface. Promoted balls roll
            // on this. Bouncy-ish physic material so it reads like a ball pit.
            ballPitPhysMat = new PhysicMaterial("BallPit")
            {
                bounciness = 0.35f,
                dynamicFriction = 0.4f,
                staticFriction = 0.4f,
                bounceCombine = PhysicMaterialCombine.Maximum
            };
            // Dark opaque backing surface shown through any residual ball gaps.
            backingMat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            backingMat.color = new Color(0.06f, 0.06f, 0.08f);
            if (backingMat.HasProperty("_BaseColor")) backingMat.SetColor("_BaseColor", new Color(0.06f, 0.06f, 0.08f));

            surfaceCollider = new VoxelSurfaceCollider(grid, transform, ballPitPhysMat);
            // Optional dark backing behind the balls. Off by default — the
            // overlapping shell usually hides see-through on its own.
            if (useVisibleBacking)
                surfaceCollider.EnableVisibleBacking(backingMat, cellSize * 0.9f);
        }

        private void BuildPitWalls()
        {
            float w = sizeX * cellSize;
            float d = sizeZ * cellSize;
            float h = sizeY * cellSize;
            var wallMat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            wallMat.color = new Color(0.1f, 0.1f, 0.12f, 1f);

            var list = new System.Collections.Generic.List<GameObject>();
            void Wall(string name, Vector3 pos, Vector3 scale)
            {
                var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
                wall.name = name;
                wall.transform.position = pos;
                wall.transform.localScale = scale;
                wall.GetComponent<MeshRenderer>().sharedMaterial = wallMat;
                list.Add(wall);
            }

            float t = 0.2f;
            Wall("Wall_+X", new Vector3(w * 0.5f + t * 0.5f, h * 0.5f, 0), new Vector3(t, h, d));
            Wall("Wall_-X", new Vector3(-w * 0.5f - t * 0.5f, h * 0.5f, 0), new Vector3(t, h, d));
            Wall("Wall_+Z", new Vector3(0, h * 0.5f, d * 0.5f + t * 0.5f), new Vector3(w + t * 2f, h, t));
            Wall("Wall_-Z", new Vector3(0, h * 0.5f, -d * 0.5f - t * 0.5f), new Vector3(w + t * 2f, h, t));
            pitWalls = list.ToArray();
        }

        private void BuildCamera()
        {
            var camGo = new GameObject("Proto_Camera");
            cam = camGo.AddComponent<Camera>();
            camGo.AddComponent<AudioListener>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.05f, 0.06f, 0.08f);

            var lightGo = new GameObject("Proto_Light");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            camPivot = new GameObject("Proto_CamPivot").transform;
            camPivot.position = new Vector3(0f, sizeY * cellSize * 0.4f, 0f);
            orbitDist = Mathf.Max(sizeX, sizeZ) * cellSize * 1.6f;
            UpdateCameraTransform();
        }

        private void BuildBallPool()
        {
            var poolRoot = new GameObject("Proto_BallPool").transform;
            for (int i = 0; i < ballPoolSize; i++)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                go.name = "Ball_" + i;
                go.transform.SetParent(poolRoot);
                go.transform.localScale = Vector3.one * (cellSize * ballScale);
                var rb = go.AddComponent<Rigidbody>();
                rb.interpolation = RigidbodyInterpolation.Interpolate;
                rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                go.SetActive(false);
                ballPool.Add(rb);
            }
        }

        // --------------------------------------------------------------- update

        private void Update()
        {
            profiler.Tick(Time.unscaledDeltaTime);

            HandleInput();
            UpdateHeldBall();

            settler.StepFrame(settleMovesPerFrame);
            if (settler.ChangedThisStep)
            {
                surfaceDirty = true;
            }

            if (surfaceDirty || grid.FilledCount != lastRebuildFilled)
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

            pitRenderer.Draw();

            profiler.ActiveBalls = CountActiveBalls();

            // Periodic console log so there's a readable trail while testing.
            logTimer += Time.unscaledDeltaTime;
            if (logTimer >= autoLogInterval)
            {
                logTimer = 0f;
                profiler.LogSnapshot("auto", grid.FilledCount, sizeX * sizeY * sizeZ,
                    pitRenderer?.TotalDrawCalls ?? 0, pitRenderer?.VisibleCount ?? -1);
            }
        }

        private int CountActiveBalls()
        {
            int n = 0;
            for (int i = 0; i < ballPool.Count; i++)
                if (ballPool[i].gameObject.activeSelf) n++;
            return n;
        }

        private void HandleInput()
        {
            if (Input.GetKeyDown(KeyCode.LeftBracket)) removePerGrab = Mathf.Max(1, removePerGrab - 1);
            if (Input.GetKeyDown(KeyCode.RightBracket)) removePerGrab = Mathf.Min(maxRemovePerGrab, removePerGrab + 1);
            if (Input.GetKeyDown(KeyCode.R)) ResetPit();
            if (Input.GetKeyDown(KeyCode.B)) BulkDigUnderCursor();
            if (Input.GetKeyDown(KeyCode.L))
                profiler.LogSnapshot("manual", grid.FilledCount, sizeX * sizeY * sizeZ, pitRenderer?.TotalDrawCalls ?? 0, pitRenderer?.VisibleCount ?? -1);

            // Pit-size presets (rebuilds the whole pit).
            if (Input.GetKeyDown(KeyCode.Alpha1)) ApplyPreset(0);
            if (Input.GetKeyDown(KeyCode.Alpha2)) ApplyPreset(1);
            if (Input.GetKeyDown(KeyCode.Alpha3)) ApplyPreset(2);
            if (Input.GetKeyDown(KeyCode.Alpha4)) ApplyPreset(3);
            if (Input.GetKeyDown(KeyCode.Alpha5)) ApplyPreset(4);

            // Orbit (right-drag) vs. throw (right-click without drag) is
            // ambiguous, so: right-drag orbits; a right-click that didn't drag
            // throws. We detect drag by mouse movement while the button is held.
            bool rmb = Input.GetMouseButton(1);
            if (rmb)
            {
                float dx = Input.GetAxis("Mouse X");
                float dy = Input.GetAxis("Mouse Y");
                if (Mathf.Abs(dx) > 0.01f || Mathf.Abs(dy) > 0.01f)
                {
                    rmbDragged = true;
                    orbitYaw += dx * 4f;
                    orbitPitch = Mathf.Clamp(orbitPitch - dy * 3f, 5f, 85f);
                }
            }
            if (Input.GetMouseButtonUp(1))
            {
                if (!rmbDragged && heldState == HeldState.Held) ThrowHeldBall();
                rmbDragged = false;
            }

            float wheel = Input.GetAxis("Mouse ScrollWheel");
            if (Mathf.Abs(wheel) > 0.001f)
                orbitDist = Mathf.Clamp(orbitDist - wheel * orbitDist, cellSize * 4f, 500f);
            UpdateCameraTransform();

            // Left click promotes a ball into the hand (only if not already holding).
            if (Input.GetMouseButtonDown(0) && heldState == HeldState.None)
                TryGrabUnderCursor();
        }

        private bool rmbDragged;

        private void UpdateCameraTransform()
        {
            if (cam == null) return;
            Quaternion rot = Quaternion.Euler(orbitPitch, orbitYaw, 0f);
            Vector3 offset = rot * new Vector3(0f, 0f, -orbitDist);
            cam.transform.position = camPivot.position + offset;
            cam.transform.rotation = Quaternion.LookRotation(camPivot.position - cam.transform.position, Vector3.up);
        }

        // ------------------------------------------------------------- grabbing

        /// <summary>World position where a held ball sits (lower-right of view).</summary>
        private Vector3 HandWorldPos()
        {
            // Viewport (x=right, y=up, z=forward distance).
            return cam.ViewportToWorldPoint(new Vector3(0.78f, 0.22f, cellSize * 3.2f));
        }

        private void TryGrabUnderCursor()
        {
            if (!AimAtColumn(out int cx, out int cz))
                return;

            int topY = grid.TopFilledY(cx, cz);
            if (topY < 0) return;

            // 1) The ball you clicked → goes into your hand.
            int color = grid.Get(cx, topY, cz);
            Vector3 ballPos = grid.CellToWorldVisual(cx, topY, cz);
            grid.Set(cx, topY, cz, VoxelPitGrid.Empty);
            settler.MarkNeighborhoodDirty(cx, topY, cz);

            heldBall = Activate(ballPos, color);
            heldBall.isKinematic = true; // animated into the hand by hand
            grabStartPos = ballPos;
            grabAnimT = 0f;
            heldState = HeldState.Animating;

            // 2) removePerGrab-1 ADJACENT top balls also pop out, as scattered
            //    physics balls (so a grab can pull a small clump, not just one).
            int extra = Mathf.Max(0, removePerGrab - 1);
            if (extra > 0) PromoteAdjacentTops(cx, cz, extra);

            surfaceDirty = true;
            lastRebuildFilled = -1;
        }

        /// <summary>
        /// Pop the top ball from the columns nearest (cx,cz), outward ring by
        /// ring, until `count` are promoted. Each becomes a scattered physics
        /// ball (you can only hold one; the rest spill out).
        /// </summary>
        private void PromoteAdjacentTops(int cx, int cz, int count)
        {
            int promoted = 0;
            for (int ring = 1; ring <= 6 && promoted < count; ring++)
            for (int dx = -ring; dx <= ring && promoted < count; dx++)
            for (int dz = -ring; dz <= ring && promoted < count; dz++)
            {
                if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz)) != ring) continue; // ring shell
                int x = cx + dx, z = cz + dz;
                if (x < 0 || x >= grid.SizeX || z < 0 || z >= grid.SizeZ) continue;

                int topY = grid.TopFilledY(x, z);
                if (topY < 0) continue;

                int color = grid.Get(x, topY, z);
                Vector3 pos = grid.CellToWorldVisual(x, topY, z);
                grid.Set(x, topY, z, VoxelPitGrid.Empty);
                settler.MarkNeighborhoodDirty(x, topY, z);

                var rb = Activate(pos, color);
                Vector3 outward = new Vector3(dx, 0.4f, dz);
                if (outward.sqrMagnitude < 0.01f) outward = Vector3.up;
                rb.AddForce(outward.normalized * (bulkTossForce * 0.6f), ForceMode.Impulse);
                promoted++;
            }
        }

        /// <summary>
        /// Resolve the cursor to a grid column. Prefer a raycast against the
        /// surface collider; if that misses (e.g. clicking just past an edge),
        /// fall back to intersecting a horizontal plane at the mean surface
        /// height so a click near the pit still maps to a column.
        /// </summary>
        private bool AimAtColumn(out int cx, out int cz)
        {
            cx = cz = -1;
            Ray ray = cam.ScreenPointToRay(Input.mousePosition);

            if (Physics.Raycast(ray, out RaycastHit hit, 2000f))
                return WorldToColumn(hit.point, out cx, out cz);

            // Fallback plane at a representative surface height.
            float planeY = grid.Origin.y + grid.SizeY * grid.CellSize * 0.5f;
            var plane = new Plane(Vector3.up, new Vector3(0f, planeY, 0f));
            if (plane.Raycast(ray, out float enter))
            {
                Vector3 p = ray.GetPoint(enter);
                if (p.x >= grid.Origin.x && p.x <= grid.Origin.x + grid.SizeX * grid.CellSize &&
                    p.z >= grid.Origin.z && p.z <= grid.Origin.z + grid.SizeZ * grid.CellSize)
                    return WorldToColumn(p, out cx, out cz);
            }
            return false;
        }

        private void UpdateHeldBall()
        {
            if (heldState == HeldState.Animating)
            {
                grabAnimT += Time.deltaTime / Mathf.Max(0.0001f, grabAnimTime);
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(grabAnimT));
                heldBall.transform.position = Vector3.Lerp(grabStartPos, HandWorldPos(), t);
                if (grabAnimT >= 1f) heldState = HeldState.Held;
            }
            else if (heldState == HeldState.Held)
            {
                // Stick to the hand position (follows camera orbit).
                heldBall.transform.position = HandWorldPos();
            }
        }

        private void ThrowHeldBall()
        {
            var rb = heldBall;
            heldBall = null;
            heldState = HeldState.None;

            rb.isKinematic = false;
            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            Vector3 dir = (cam.transform.forward + Vector3.up * (throwUpBias / Mathf.Max(1f, throwSpeed))).normalized;
            rb.velocity = dir * throwSpeed;
        }

        // ------------------------------------------------------------- bulk dig

        private void BulkDigUnderCursor()
        {
            Ray ray = cam.ScreenPointToRay(Input.mousePosition);
            if (!Physics.Raycast(ray, out RaycastHit hit, 1000f)) return;
            if (!WorldToColumn(hit.point, out int cx, out int cz)) return;
            BulkDigAt(cx, cz);
        }

        // ------------------------------------------------------------- helpers

        /// <summary>Activate a pooled ball at a position with a color.</summary>
        private Rigidbody Activate(Vector3 pos, int color)
        {
            var rb = ballPool[ballPoolCursor];
            ballPoolCursor = (ballPoolCursor + 1) % ballPool.Count;

            // If we're about to reuse the ball we're currently holding, skip it.
            if (rb == heldBall)
            {
                rb = ballPool[ballPoolCursor];
                ballPoolCursor = (ballPoolCursor + 1) % ballPool.Count;
            }

            var go = rb.gameObject;
            go.SetActive(true);
            go.transform.position = pos;
            rb.isKinematic = false;
            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;

            var mr = go.GetComponent<MeshRenderer>();
            if (color >= 0 && color < colorMaterials.Length)
                mr.sharedMaterial = colorMaterials[color];
            return rb;
        }

        /// <summary>Map a world point to the nearest grid column (x,z).</summary>
        private bool WorldToColumn(Vector3 world, out int cx, out int cz)
        {
            float fx = (world.x - grid.Origin.x) / grid.CellSize;
            float fz = (world.z - grid.Origin.z) / grid.CellSize;
            cx = Mathf.Clamp(Mathf.FloorToInt(fx), 0, grid.SizeX - 1);
            cz = Mathf.Clamp(Mathf.FloorToInt(fz), 0, grid.SizeZ - 1);
            return true;
        }

        private void ResetPit()
        {
            grid.FillSolid(colorCount, seed);
            pitRenderer.Rebuild();
            surfaceCollider.Rebuild();
            lastRebuildFilled = grid.FilledCount;
            surfaceDirty = false;
            heldBall = null;
            heldState = HeldState.None;
            foreach (var rb in ballPool) rb.gameObject.SetActive(false);
        }

        /// <summary>
        /// Rebuild the whole pit at a preset size (number keys 1-5). Tears down
        /// the old collider + walls and recreates everything so we can compare
        /// cost across pit sizes live. Logs a snapshot after the switch.
        /// </summary>
        private void ApplyPreset(int index)
        {
            if (index < 0 || index >= SizePresets.Length) return;
            var s = SizePresets[index];
            sizeX = s.x; sizeY = s.y; sizeZ = s.z;

            // Clear held/active balls.
            heldBall = null;
            heldState = HeldState.None;
            foreach (var rb in ballPool) rb.gameObject.SetActive(false);

            // Destroy old pit-specific objects.
            if (surfaceCollider != null && surfaceCollider.GameObject != null)
                Destroy(surfaceCollider.GameObject);
            if (pitWalls != null)
                foreach (var w in pitWalls) if (w != null) Destroy(w);

            // Rebuild data + visuals + collision.
            BuildGrid();
            BuildPitWalls();
            surfaceCollider = new VoxelSurfaceCollider(grid, transform, ballPitPhysMat);
            if (useVisibleBacking)
                surfaceCollider.EnableVisibleBacking(backingMat, cellSize * 0.9f);
            pitRenderer = new VoxelPitRenderer(grid, ballMesh, colorMaterials, ballScale);
            pitRenderer.ShellDepth = shellDepth;

            profiler.ResetPeaks();

            profiler.BeginSample();
            pitRenderer.Rebuild();
            profiler.RecordRebuild(profiler.EndSampleMs());

            profiler.BeginSample();
            surfaceCollider.Rebuild();
            profiler.RecordCollider(profiler.EndSampleMs());

            lastRebuildFilled = grid.FilledCount;
            surfaceDirty = false;

            // Re-aim the camera at the new (bigger/smaller) pit.
            camPivot.position = new Vector3(0f, sizeY * cellSize * 0.4f, 0f);
            orbitDist = Mathf.Max(sizeX, sizeZ) * cellSize * 1.6f;
            UpdateCameraTransform();

            profiler.LogSnapshot($"preset#{index + 1} {sizeX}x{sizeY}x{sizeZ}",
                grid.FilledCount, sizeX * sizeY * sizeZ, pitRenderer.TotalDrawCalls, pitRenderer.VisibleCount);
        }

        // ----------------------------------------------------------------- UI

        private void OnGUI()
        {
            if (labelStyle == null)
            {
                labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 15, richText = true };
                labelStyle.normal.textColor = Color.white;
            }

            const int pad = 10;
            GUI.Box(new Rect(pad, pad, 380, 290), GUIContent.none);

            var r = new Rect(pad + 10, pad + 8, 360, 22);
            GUI.Label(r, "<b>Ball Pit Voxel Prototype</b>  (not final)", labelStyle); r.y += 24;
            GUI.Label(r, $"Pit: {sizeX}x{sizeY}x{sizeZ} = {sizeX * sizeY * sizeZ:n0} cells   remaining {grid.FilledCount:n0}", labelStyle); r.y += 22;

            // Color FPS by health so regressions are obvious at a glance.
            float avgFps = profiler.AvgFps, worstFps = profiler.MinFps;
            string fpsColor = avgFps >= 60f ? "lime" : avgFps >= 30f ? "yellow" : "red";
            GUI.Label(r, $"FPS avg <color={fpsColor}>{avgFps:n0}</color>  worst {worstFps:n0}   (frame {profiler.FrameMsAvg:n2}ms, max {profiler.FrameMsMax:n2}ms)", labelStyle); r.y += 22;
            GUI.Label(r, $"Draw calls: {pitRenderer?.TotalDrawCalls ?? 0}   drawn balls: {pitRenderer?.VisibleCount ?? 0:n0} (surface only)", labelStyle); r.y += 22;
            GUI.Label(r, $"Rebuild ms  last {profiler.LastRebuildMs:n2}  peak {profiler.PeakRebuildMs:n2}", labelStyle); r.y += 22;
            GUI.Label(r, $"Collider ms last {profiler.LastColliderMs:n2}  peak {profiler.PeakColliderMs:n2}", labelStyle); r.y += 22;
            GUI.Label(r, $"Settle pending {settler.PendingCount:n0}  moves {settler.LastStepMoves}   active balls {profiler.ActiveBalls}", labelStyle); r.y += 22;
            GUI.Label(r, $"Holding: {(heldState != HeldState.None ? "yes (right-click to throw)" : "no")}", labelStyle); r.y += 26;

            GUI.Label(new Rect(pad + 10, r.y, 170, 24), $"Balls per grab: {removePerGrab}", labelStyle);
            if (GUI.Button(new Rect(pad + 185, r.y, 30, 24), "-")) removePerGrab = Mathf.Max(1, removePerGrab - 1);
            if (GUI.Button(new Rect(pad + 219, r.y, 30, 24), "+")) removePerGrab = Mathf.Min(maxRemovePerGrab, removePerGrab + 1);
            if (GUI.Button(new Rect(pad + 257, r.y, 60, 24), "x10")) removePerGrab = Mathf.Min(maxRemovePerGrab, removePerGrab * 10);
            r.y += 30;

            if (GUI.Button(new Rect(pad + 10, r.y, 110, 24), "Bulk dig (B)")) BulkDigUnderCursor_UI();
            if (GUI.Button(new Rect(pad + 128, r.y, 90, 24), "Reset (R)")) ResetPit();
            if (GUI.Button(new Rect(pad + 226, r.y, 110, 24), "Log now (L)"))
                profiler.LogSnapshot("manual", grid.FilledCount, sizeX * sizeY * sizeZ, pitRenderer?.TotalDrawCalls ?? 0, pitRenderer?.VisibleCount ?? -1);
            r.y += 30;

            GUI.Label(new Rect(pad + 10, r.y, 360, 22), "Pit size preset:", labelStyle); r.y += 22;
            for (int i = 0; i < SizePresets.Length; i++)
            {
                var s = SizePresets[i];
                if (GUI.Button(new Rect(pad + 10 + i * 72, r.y, 68, 24), $"{i + 1}: {s.x * s.y * s.z / 1000}k"))
                    ApplyPreset(i);
            }

            GUI.Label(new Rect(pad, 310, 760, 40),
                "<b>LMB</b> grab   <b>RMB click</b> throw   <b>RMB drag</b> orbit   <b>wheel</b> zoom   <b>B</b> bulk dig   <b>[ ]</b> count   <b>1-5</b> pit size   <b>L</b> log   <b>R</b> reset",
                labelStyle);
        }

        // The UI button can't easily use the cursor ray (pointer is over the
        // button), so route it through the screen center for convenience.
        private void BulkDigUnderCursor_UI()
        {
            // The pointer is over the button, so aim at screen center instead.
            Ray ray = cam.ScreenPointToRay(new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, 0f));
            if (!Physics.Raycast(ray, out RaycastHit hit, 1000f)) return;
            if (!WorldToColumn(hit.point, out int cx, out int cz)) return;
            BulkDigAt(cx, cz);
        }

        private void BulkDigAt(int cx, int cz)
        {
            int remaining = removePerGrab;
            int radius = Mathf.Clamp(Mathf.RoundToInt(Mathf.Sqrt(removePerGrab) * 0.5f), 0, 6);
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
                settler.MarkNeighborhoodDirty(x, topY, z);
                var rb = Activate(pos, color);
                Vector3 outward = new Vector3(dx, 0f, dz);
                if (outward.sqrMagnitude < 0.01f) outward = Random.insideUnitSphere;
                rb.AddForce(outward.normalized * bulkTossForce + Vector3.up * bulkTossForce, ForceMode.Impulse);
                remaining--;
            }
            surfaceDirty = true;
            lastRebuildFilled = -1;
        }
    }
}

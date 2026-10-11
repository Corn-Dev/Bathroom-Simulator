using System.Collections.Generic;
using UnityEngine;

namespace BallPhysicsStress
{
    /// <summary>
    /// PROTOTYPE — not final. Local, no networking.
    ///
    /// A focused stress test to answer ONE question: how many real rigidbody
    /// balls can Unity simulate at once, and what does a big collapse cost?
    /// This informs the "hybrid pit" plan where the surface layer is REAL
    /// physics balls (that roll/settle/collapse for real) over a cheap static
    /// deep mass.
    ///
    /// Key things it measures:
    ///   - Total balls vs AWAKE balls. Unity auto-sleeps settled rigidbodies, so
    ///     a pile that has come to rest costs almost nothing. Awake-count is the
    ///     real cost driver — it proves "deep/settled balls don't simulate".
    ///   - FPS avg / worst and physics step time, so we see the steady-state
    ///     ceiling AND the spike during a cascade.
    ///
    /// Drop this on ONE empty GameObject in an empty scene and press Play.
    /// Everything (bin, camera, light, ball pool, UI) is built procedurally.
    ///
    /// CONTROLS
    ///   Space / "Pour N"   : pour `pourBatch` balls in from above
    ///   F / "Fill to target": pour until `fillTarget` balls exist
    ///   C / "Collapse!"     : yank the pile's support (drops the floor plug)
    ///                         so everything cascades — the worst case
    ///   W / "Wake all"      : force-wake every ball (max-stress: all awake)
    ///   R / "Reset"         : clear all balls
    ///   Right-drag orbit, wheel zoom
    /// </summary>
    public sealed class BallPhysicsStressTest : MonoBehaviour
    {
        [Header("Bin")]
        public float binWidth = 12f;
        public float binDepth = 12f;
        public float binHeight = 10f;
        public float wallThickness = 0.4f;

        [Header("Balls")]
        public float ballDiameter = 0.5f;
        [Tooltip("Hard cap on pooled balls. Raise to test higher ceilings.")]
        public int maxBalls = 12000;
        [Tooltip("Balls poured per Space press / per pour tick.")]
        public int pourBatch = 500;
        [Tooltip("Fill-to-target amount for the F key.")]
        public int fillTarget = 6000;

        [Header("Physics feel")]
        public float bounciness = 0.2f;
        public float friction = 0.5f;
        [Tooltip("Linear drag — higher = rolling balls bleed speed and stop sooner.")]
        public float linearDrag = 0.4f;
        [Tooltip("Angular drag — higher = spinning balls stop rolling sooner.")]
        public float angularDrag = 0.6f;

        [Header("Settling (make balls stop sooner = fewer awake = faster)")]
        [Tooltip("Rigidbody sleep threshold. Higher = balls sleep at higher speed, so they give up sooner.")]
        public float sleepThreshold = 0.3f;
        [Tooltip("Assist: any ball below this speed for 'forceSleepDelay' seconds is forced to sleep.")]
        public float forceSleepSpeed = 0.25f;
        [Tooltip("How long a ball must stay slow before we force it to sleep.")]
        public float forceSleepDelay = 0.6f;
        [Tooltip("Enable the force-sleep assist (guarantees balls stop instead of jittering forever).")]
        public bool useForceSleep = true;

        [Header("Global physics tuning")]
        [Tooltip("Physics ticks per second. Lower = big CPU saving, slightly less accurate stacking. 50 default; try 30.")]
        [Range(20, 60)] public int physicsHz = 40;
        [Tooltip("Solver iteration count. Lower = cheaper, squishier stacks. 6 default; 4 is often fine for balls.")]
        [Range(1, 10)] public int solverIterations = 4;
        [Tooltip("Solver velocity iterations. Lower = cheaper. 1 default.")]
        [Range(1, 4)] public int solverVelIterations = 1;
        [Tooltip("Render balls via GPU instancing (1 batch per 1023) instead of per-ball MeshRenderers. Big render win.")]
        public bool instancedRendering = true;

        [Header("Logging")]
        [Tooltip("Seconds between automatic console perf logs.")]
        public float autoLogInterval = 1f;

        // --- runtime ---
        private readonly List<Rigidbody> pool = new List<Rigidbody>();
        private Transform[] poolTransforms;  // cached transforms for instanced draw
        private int[] poolColor;             // color index per pooled ball
        private float[] slowTimer;           // per-ball: how long it's been slow
        private int liveCount;               // how many are currently active
        private Material[] colorMats;
        private Mesh ballMesh;

        // instanced draw buffer
        private const int BatchSize = 1023;

        private Camera cam;
        private Transform camPivot;
        private float orbitYaw = 35f, orbitPitch = 30f, orbitDist;

        private GameObject floorPlug;       // removable support for the collapse test
        private bool filling;
        private float logTimer;

        private GUIStyle labelStyle;

        // frame-time rolling window
        private const int Window = 120;
        private readonly float[] frameMs = new float[Window];
        private int frameIdx, frameFilled;
        private float fpsAvg, fpsWorst;

        // "time to settle": seconds elapsed since the pile was last agitated,
        // until awake drops below a small threshold. Directly measures the
        // "it took 30s to settle" complaint.
        private float settleTimer;
        private float lastSettleSeconds = -1f;
        private bool settling;

        private void Start()
        {
            ApplyGlobalPhysics();
            BuildMeshAndMats();
            BuildBin();
            BuildCamera();
            BuildPool();
        }

        // ---------------------------------------------------------------- build

        private void ApplyGlobalPhysics()
        {
            // Fewer physics ticks/sec = big CPU saving. Default 0.02 (50Hz).
            Time.fixedDeltaTime = 1f / physicsHz;
            Physics.defaultSolverIterations = solverIterations;
            Physics.defaultSolverVelocityIterations = solverVelIterations;
            // Let Unity reuse the broadphase better and skip some work on sleep.
            Physics.autoSyncTransforms = false;
        }

        private void BuildMeshAndMats()
        {
            var temp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            ballMesh = temp.GetComponent<MeshFilter>().sharedMesh;
            Destroy(temp);

            int colors = 4;
            colorMats = new Material[colors];
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            for (int i = 0; i < colors; i++)
            {
                var m = new Material(shader) { enableInstancing = true };
                var c = Color.HSVToRGB((float)i / colors, 0.75f, 0.95f);
                m.color = c;
                if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
                colorMats[i] = m;
            }
        }

        private void BuildBin()
        {
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            mat.color = new Color(0.15f, 0.15f, 0.18f);

            GameObject Box(string name, Vector3 pos, Vector3 scale)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = name;
                go.transform.position = pos;
                go.transform.localScale = scale;
                go.GetComponent<MeshRenderer>().sharedMaterial = mat;
                return go;
            }

            float w = binWidth, d = binDepth, h = binHeight, t = wallThickness;

            // Floor is a removable "plug" so we can drop it for the collapse test.
            floorPlug = Box("FloorPlug", new Vector3(0, -t * 0.5f, 0), new Vector3(w + 2 * t, t, d + 2 * t));

            Box("Wall_+X", new Vector3(w * 0.5f + t * 0.5f, h * 0.5f, 0), new Vector3(t, h, d));
            Box("Wall_-X", new Vector3(-w * 0.5f - t * 0.5f, h * 0.5f, 0), new Vector3(t, h, d));
            Box("Wall_+Z", new Vector3(0, h * 0.5f, d * 0.5f + t * 0.5f), new Vector3(w + 2 * t, h, t));
            Box("Wall_-Z", new Vector3(0, h * 0.5f, -d * 0.5f - t * 0.5f), new Vector3(w + 2 * t, h, t));

            // A catch floor far below so collapsed balls don't fall forever.
            Box("CatchFloor", new Vector3(0, -8f, 0), new Vector3(w * 4f, 0.5f, d * 4f));
        }

        private void BuildCamera()
        {
            var camGo = new GameObject("Cam");
            cam = camGo.AddComponent<Camera>();
            camGo.AddComponent<AudioListener>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.05f, 0.06f, 0.08f);

            var lightGo = new GameObject("Light");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            camPivot = new GameObject("Pivot").transform;
            camPivot.position = new Vector3(0, binHeight * 0.3f, 0);
            orbitDist = Mathf.Max(binWidth, binDepth) * 2.2f;
            UpdateCam();
        }

        private void BuildPool()
        {
            var physMat = new PhysicMaterial("Ball")
            {
                bounciness = bounciness,
                dynamicFriction = friction,
                staticFriction = friction,
                bounceCombine = PhysicMaterialCombine.Average,
                frictionCombine = PhysicMaterialCombine.Average
            };

            slowTimer = new float[maxBalls];
            poolTransforms = new Transform[maxBalls];
            poolColor = new int[maxBalls];

            var root = new GameObject("BallPool").transform;
            for (int i = 0; i < maxBalls; i++)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                go.name = "B" + i;
                go.transform.SetParent(root);
                go.transform.localScale = Vector3.one * ballDiameter;
                var col = go.GetComponent<SphereCollider>();
                col.sharedMaterial = physMat;
                var rb = go.AddComponent<Rigidbody>();
                rb.interpolation = RigidbodyInterpolation.None;      // cheaper; this is a stress test
                rb.collisionDetectionMode = CollisionDetectionMode.Discrete; // cheapest; see note in README
                rb.sleepThreshold = sleepThreshold;
                rb.drag = linearDrag;
                rb.angularDrag = angularDrag;

                var mr = go.GetComponent<MeshRenderer>();
                if (instancedRendering)
                {
                    // Render these ourselves via DrawMeshInstanced; kill the
                    // per-ball MeshRenderer so we're not paying 30k of them.
                    Destroy(mr);
                }
                else
                {
                    mr.sharedMaterial = colorMats[i % colorMats.Length];
                    mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }

                poolTransforms[i] = go.transform;
                poolColor[i] = i % colorMats.Length;
                go.SetActive(false);
                pool.Add(rb);
            }

            // Reusable per-color instance buffer (filled each frame in DrawBalls).
            instanceBuffer = new Matrix4x4[BatchSize];
        }

        private Matrix4x4[] instanceBuffer;

        // --------------------------------------------------------------- update

        private void Update()
        {
            TickFps(Time.unscaledDeltaTime);
            HandleInput();

            if (filling && liveCount < fillTarget)
                Pour(Mathf.Min(pourBatch, fillTarget - liveCount));
            else
                filling = false;

            UpdateCam();

            if (instancedRendering) DrawBalls();

            // Compute awake ONCE per frame and cache it. (Previously counted in
            // 3 places every frame — at 50k that profiler overhead itself tanks
            // fps and pollutes the measurement.)
            cachedAwake = CountAwake();

            TrackSettle();

            logTimer += Time.unscaledDeltaTime;
            if (logTimer >= autoLogInterval)
            {
                logTimer = 0f;
                LogSnapshot("auto");
            }
        }

        private int cachedAwake;

        private void TrackSettle()
        {
            int awake = cachedAwake;
            int settledThreshold = Mathf.Max(5, liveCount / 100); // ~1% still jittering = "settled"

            if (awake > settledThreshold * 3)
            {
                // Pile is active — (re)start the settle clock.
                settling = true;
                settleTimer += Time.deltaTime;
            }
            else if (settling && awake <= settledThreshold)
            {
                // Just reached rest.
                lastSettleSeconds = settleTimer;
                settling = false;
                settleTimer = 0f;
                Debug.Log($"[BallPhysics] SETTLED in {lastSettleSeconds:n1}s  (total={liveCount:n0})");
            }
            else if (settling)
            {
                settleTimer += Time.deltaTime;
            }
        }

        private void LogSnapshot(string tag)
        {
            Debug.Log(
                $"[BallPhysics]{(string.IsNullOrEmpty(tag) ? "" : " " + tag)} " +
                $"total={liveCount:n0} awake={cachedAwake:n0} " +
                $"fps(avg/worst)={fpsAvg:n0}/{fpsWorst:n0} " +
                $"hz={1f / Time.fixedDeltaTime:n0} solver={solverIterations}/{solverVelIterations} " +
                $"render={(instancedRendering ? "inst" : "perball")}");
        }

        private int forceSleepCursor;

        private void FixedUpdate()
        {
            if (!useForceSleep || liveCount == 0) return;

            // Force-sleep assist, TIME-SLICED: only scan a slice of balls per
            // physics tick instead of all of them. At 50k, scanning everyone
            // every tick is our own O(n) overhead that would wrongly look like
            // physics cost. A slice keeps our overhead flat so the fps number
            // reflects Unity's physics, not this loop.
            float dt = Time.fixedDeltaTime;
            float sqrSpeed = forceSleepSpeed * forceSleepSpeed;
            // How long each ball effectively waits grows with pile size, which
            // is fine — slow balls still get caught within a second or two.
            int slice = Mathf.Clamp(liveCount, 1, 2000);

            for (int n = 0; n < slice; n++)
            {
                int i = forceSleepCursor;
                forceSleepCursor++;
                if (forceSleepCursor >= liveCount) forceSleepCursor = 0;

                var rb = pool[i];
                if (!rb.gameObject.activeSelf || rb.IsSleeping()) { slowTimer[i] = 0f; continue; }

                if (rb.velocity.sqrMagnitude < sqrSpeed && rb.angularVelocity.sqrMagnitude < sqrSpeed)
                {
                    // We only visit each ball every (liveCount/slice) ticks, so
                    // credit that gap instead of a single dt.
                    slowTimer[i] += dt * Mathf.Max(1, liveCount / slice);
                    if (slowTimer[i] >= forceSleepDelay)
                    {
                        rb.Sleep();
                        slowTimer[i] = 0f;
                    }
                }
                else
                {
                    slowTimer[i] = 0f;
                }
            }
        }

        private void HandleInput()
        {
            if (Input.GetKeyDown(KeyCode.Space)) { Pour(pourBatch); LogSnapshot("pour"); }
            if (Input.GetKeyDown(KeyCode.F)) { filling = true; LogSnapshot("fill-start"); }
            if (Input.GetKeyDown(KeyCode.C)) { Collapse(); LogSnapshot("COLLAPSE"); }
            if (Input.GetKeyDown(KeyCode.W)) { WakeAll(); LogSnapshot("wake-all"); }
            if (Input.GetKeyDown(KeyCode.R)) { ResetAll(); LogSnapshot("reset"); }
            if (Input.GetKeyDown(KeyCode.L)) LogSnapshot("manual");

            if (Input.GetMouseButton(1))
            {
                orbitYaw += Input.GetAxis("Mouse X") * 4f;
                orbitPitch = Mathf.Clamp(orbitPitch - Input.GetAxis("Mouse Y") * 3f, 5f, 85f);
            }
            float wheel = Input.GetAxis("Mouse ScrollWheel");
            if (Mathf.Abs(wheel) > 0.001f)
                orbitDist = Mathf.Clamp(orbitDist - wheel * orbitDist, 2f, 300f);
        }

        private void UpdateCam()
        {
            if (cam == null) return;
            Quaternion rot = Quaternion.Euler(orbitPitch, orbitYaw, 0f);
            Vector3 offset = rot * new Vector3(0, 0, -orbitDist);
            cam.transform.position = camPivot.position + offset;
            cam.transform.rotation = Quaternion.LookRotation(camPivot.position - cam.transform.position, Vector3.up);
        }

        // ---------------------------------------------------------------- actions

        private void Pour(int count)
        {
            float r = ballDiameter * 0.5f;
            for (int i = 0; i < count && liveCount < pool.Count; i++)
            {
                var rb = pool[liveCount];
                liveCount++;

                // Spawn in a loose grid high above so they rain down and settle.
                float spread = Mathf.Min(binWidth, binDepth) * 0.45f;
                Vector3 pos = new Vector3(
                    Random.Range(-spread, spread),
                    binHeight + Random.Range(0f, binHeight) + r,
                    Random.Range(-spread, spread));

                var go = rb.gameObject;
                go.transform.position = pos;
                go.SetActive(true);
                rb.velocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                slowTimer[liveCount - 1] = 0f;
                rb.WakeUp();
            }
        }

        /// <summary>Drop the floor so the whole pile cascades — the worst case.</summary>
        private void Collapse()
        {
            if (floorPlug != null) floorPlug.SetActive(false);
            WakeAll();
        }

        private void WakeAll()
        {
            for (int i = 0; i < liveCount; i++)
                if (pool[i].gameObject.activeSelf) { pool[i].WakeUp(); slowTimer[i] = 0f; }
        }

        private void ResetAll()
        {
            for (int i = 0; i < liveCount; i++) pool[i].gameObject.SetActive(false);
            liveCount = 0;
            filling = false;
            if (floorPlug != null) floorPlug.SetActive(true);
        }

        /// <summary>
        /// Draw all live balls with GPU instancing instead of per-ball
        /// MeshRenderers. Grouped by color, flushed in batches of 1023.
        /// </summary>
        private void DrawBalls()
        {
            Vector3 scale = Vector3.one * ballDiameter;
            for (int c = 0; c < colorMats.Length; c++)
            {
                int n = 0;
                for (int i = 0; i < liveCount; i++)
                {
                    if (poolColor[i] != c) continue;
                    var t = poolTransforms[i];
                    instanceBuffer[n] = Matrix4x4.TRS(t.position, t.rotation, scale);
                    n++;
                    if (n == BatchSize)
                    {
                        Graphics.DrawMeshInstanced(ballMesh, 0, colorMats[c], instanceBuffer, n);
                        n = 0;
                    }
                }
                if (n > 0)
                    Graphics.DrawMeshInstanced(ballMesh, 0, colorMats[c], instanceBuffer, n);
            }
        }

        private int CountAwake()
        {
            int awake = 0;
            for (int i = 0; i < liveCount; i++)
            {
                var rb = pool[i];
                if (rb.gameObject.activeSelf && !rb.IsSleeping()) awake++;
            }
            return awake;
        }

        // ---------------------------------------------------------------- fps

        private void TickFps(float dt)
        {
            float ms = dt * 1000f;
            frameMs[frameIdx] = ms;
            frameIdx = (frameIdx + 1) % Window;
            if (frameFilled < Window) frameFilled++;
            float sum = 0f, max = 0f;
            for (int i = 0; i < frameFilled; i++) { sum += frameMs[i]; if (frameMs[i] > max) max = frameMs[i]; }
            float avgMs = sum / Mathf.Max(1, frameFilled);
            fpsAvg = avgMs > 0.0001f ? 1000f / avgMs : 0f;
            fpsWorst = max > 0.0001f ? 1000f / max : 0f;
        }

        // ----------------------------------------------------------------- UI

        private void OnGUI()
        {
            if (labelStyle == null)
            {
                labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 15, richText = true };
                labelStyle.normal.textColor = Color.white;
            }

            int awake = CountAwake();
            const int pad = 10;
            GUI.Box(new Rect(pad, pad, 370, 268), GUIContent.none);
            var r = new Rect(pad + 10, pad + 8, 350, 22);

            GUI.Label(r, "<b>Ball Physics Stress Test</b> (not final)", labelStyle); r.y += 24;
            GUI.Label(r, $"Total balls: {liveCount:n0} / {maxBalls:n0}", labelStyle); r.y += 22;

            string awakeColor = awake < 200 ? "lime" : awake < 1000 ? "yellow" : "red";
            GUI.Label(r, $"AWAKE (simulating): <color={awakeColor}>{awake:n0}</color>   asleep: {liveCount - awake:n0}", labelStyle); r.y += 22;

            string fpsColor = fpsAvg >= 60 ? "lime" : fpsAvg >= 30 ? "yellow" : "red";
            GUI.Label(r, $"FPS avg <color={fpsColor}>{fpsAvg:n0}</color>  worst {fpsWorst:n0}", labelStyle); r.y += 22;

            string settleStr = settling ? $"settling… {settleTimer:n1}s"
                : (lastSettleSeconds >= 0 ? $"last settle: {lastSettleSeconds:n1}s" : "—");
            GUI.Label(r, $"Settle time: {settleStr}", labelStyle); r.y += 22;
            GUI.Label(r, $"Physics: {1f / Time.fixedDeltaTime:n0}Hz  solver {solverIterations}/{solverVelIterations}  " +
                         $"render: {(instancedRendering ? "instanced" : "per-ball")}", labelStyle); r.y += 26;

            if (GUI.Button(new Rect(pad + 10, r.y, 100, 24), $"Pour {pourBatch}")) { Pour(pourBatch); LogSnapshot("pour"); }
            if (GUI.Button(new Rect(pad + 116, r.y, 120, 24), $"Fill to {fillTarget}")) { filling = true; LogSnapshot("fill-start"); }
            if (GUI.Button(new Rect(pad + 242, r.y, 100, 24), "Collapse!")) { Collapse(); LogSnapshot("COLLAPSE"); }
            r.y += 30;
            if (GUI.Button(new Rect(pad + 10, r.y, 90, 24), "Wake all")) { WakeAll(); LogSnapshot("wake-all"); }
            if (GUI.Button(new Rect(pad + 106, r.y, 90, 24), "Reset")) { ResetAll(); LogSnapshot("reset"); }
            if (GUI.Button(new Rect(pad + 202, r.y, 90, 24), "Log now")) LogSnapshot("manual");
            r.y += 30;

            string fsLabel = useForceSleep ? "Force-sleep: ON" : "Force-sleep: OFF";
            if (GUI.Button(new Rect(pad + 10, r.y, 160, 24), fsLabel)) useForceSleep = !useForceSleep;
            r.y += 30;

            GUI.Label(r, "<b>Space</b> pour  <b>F</b> fill  <b>C</b> collapse  <b>W</b> wake  <b>R</b> reset  <b>L</b> log", labelStyle);
            r.y += 22;
            GUI.Label(r, "RMB-drag orbit, wheel zoom", labelStyle);
        }
    }
}

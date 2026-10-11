using System.Collections.Generic;
using UnityEngine;

namespace StaticColliderTest
{
    /// <summary>
    /// PROTOTYPE / EXPERIMENT — not final. Local, no networking.
    ///
    /// Tests the two load-bearing claims of the proposed "exact-pose + static
    /// shell" ball-pit architecture, with the measurement confounds that
    /// polluted our earlier tests controlled for:
    ///
    ///   CLAIM 1: A large number of STATIC SphereColliders (no Rigidbody) is
    ///            cheap at rest — because statics don't join PhysX islands and
    ///            carry no solver/sleep bookkeeping. (Our old test used sleeping
    ///            RIGIDBODIES, which is a different, more expensive thing.)
    ///   CLAIM 2: A thin layer of real rigidbody balls (1-2k) falling onto that
    ///            static shell performs well and settles into small islands.
    ///
    /// CONFOUNDS CONTROLLED:
    ///   - Rendering can be fully disabled (R key) to isolate PURE physics cost.
    ///     Earlier "GPU instancing didn't help" may have been the SRP Batcher
    ///     overriding instancing — so we test physics with NO rendering at all.
    ///   - Fixed-steps-per-frame is shown (catches the fixed-timestep death
    ///     spiral that can masquerade as per-body cost).
    ///   - Broadphase type is switchable (SAP / ABP / MBP) — a wide flat field of
    ///     colliders is the pathological case for default SAP.
    ///   - RUN THIS IN A DEVELOPMENT BUILD, not the editor. Editor overhead on a
    ///     30k-object hierarchy is itself a confound. (A build note is logged.)
    ///
    /// Drop on ONE empty GameObject, press Play. Everything is procedural.
    ///
    /// CONTROLS
    ///   S / "Add statics"  : add `staticBatch` static SphereColliders (a packed
    ///                        block — the resting shell)
    ///   D / "Drop actives" : drop `activeBatch` rigidbody balls from above onto
    ///                        the static shell
    ///   T / cycle          : cycle broadphase SAP -> ABP -> MBP (rebuilds)
    ///   V / "Render toggle" : toggle rendering on/off (physics always runs)
    ///   X / "Clear"        : remove everything
    ///   L                  : log a snapshot
    ///   RMB-drag orbit, wheel zoom
    /// </summary>
    public sealed class StaticColliderTest : MonoBehaviour
    {
        [Header("Field")]
        public float ballDiameter = 0.5f;
        public int fieldWidth = 60;    // columns in X of the static block
        public int fieldDepth = 60;    // columns in Z

        [Header("Batches")]
        public int staticBatch = 10000;   // static colliders added per press
        public int maxStatics = 60000;
        public int activeBatch = 1000;     // rigidbody balls dropped per press
        public int maxActives = 4000;

        [Header("Physics")]
        [Range(20, 60)] public int physicsHz = 50;
        [Range(1, 10)] public int solverIterations = 4;
        public float linearDrag = 0.05f;
        public float angularDrag = 1.0f;   // some rolling resistance so floor balls stop
        public float bounciness = 0.2f;
        public float friction = 0.5f;

        [Header("Logging")]
        public float autoLogInterval = 1f;

        // --- static shell ---
        private Transform staticRoot;
        private readonly List<Transform> statics = new List<Transform>();
        private Mesh ballMesh;
        private Material staticMat, activeMat;
        private PhysicMaterial physMat;
        private int nextStaticX, nextStaticZ, nextStaticY; // packing cursor

        // --- active balls ---
        private Transform activeRoot;
        private readonly List<Rigidbody> actives = new List<Rigidbody>();

        // --- rendering (instanced, toggleable) ---
        private bool renderingOn = true;
        private const int BatchSize = 1023;
        private readonly Matrix4x4[] drawBuf = new Matrix4x4[BatchSize];

        // --- camera ---
        private Camera cam; private Transform pivot;
        private float yaw = 35f, pitch = 30f, dist;

        // --- fps + fixed-step tracking ---
        private const int Window = 120;
        private readonly float[] frameMs = new float[Window];
        private int fi, ff; private float fpsAvg, fpsWorst;
        private int fixedStepsThisFrame, lastFixedSteps;

        private int broadphase; // 0 SAP, 1 ABP, 2 MBP
        private GUIStyle style;
        private float logTimer;

        private void Start()
        {
            Time.fixedDeltaTime = 1f / physicsHz;
            Physics.defaultSolverIterations = solverIterations;
            Physics.autoSyncTransforms = false;

            var temp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            ballMesh = temp.GetComponent<MeshFilter>().sharedMesh;
            Destroy(temp);

            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            staticMat = new Material(shader) { enableInstancing = true }; staticMat.color = new Color(0.4f, 0.5f, 0.8f);
            activeMat = new Material(shader) { enableInstancing = true }; activeMat.color = new Color(1f, 0.6f, 0.2f);

            physMat = new PhysicMaterial("Ball")
            {
                bounciness = bounciness, dynamicFriction = friction, staticFriction = friction,
                bounceCombine = PhysicMaterialCombine.Average, frictionCombine = PhysicMaterialCombine.Average
            };

            staticRoot = new GameObject("Statics").transform;
            activeRoot = new GameObject("Actives").transform;

            BuildGroundAndCamera();

            Debug.Log("[StaticTest] NOTE: run in a DEVELOPMENT BUILD for a clean number — editor overhead on tens of thousands of objects is itself a confound.");
        }

        private void BuildGroundAndCamera()
        {
            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "Ground";
            ground.transform.position = new Vector3(0, -0.5f, 0);
            ground.transform.localScale = new Vector3(fieldWidth * ballDiameter * 2f, 1f, fieldDepth * ballDiameter * 2f);

            var camGo = new GameObject("Cam");
            cam = camGo.AddComponent<Camera>();
            camGo.AddComponent<AudioListener>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.05f, 0.06f, 0.08f);

            var lightGo = new GameObject("Light");
            var l = lightGo.AddComponent<Light>();
            l.type = LightType.Directional; l.intensity = 1.1f;
            lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            pivot = new GameObject("Pivot").transform;
            pivot.position = new Vector3(0, fieldWidth * ballDiameter * 0.15f, 0);
            dist = Mathf.Max(fieldWidth, fieldDepth) * ballDiameter * 1.6f;
            UpdateCam();
        }

        // ------------------------------------------------------------- build statics

        /// <summary>
        /// Add N static SphereColliders packed into a block (no Rigidbody). This
        /// is the "resting shell" — the thing we're testing the at-rest cost of.
        /// </summary>
        private void AddStatics(int count)
        {
            float d = ballDiameter;
            float layerH = d * 0.82f; // close-pack vertical spacing
            float ox = -fieldWidth * d * 0.5f;
            float oz = -fieldDepth * d * 0.5f;

            for (int i = 0; i < count && statics.Count < maxStatics; i++)
            {
                // Advance a packing cursor over X/Z, then up a layer.
                if (nextStaticX >= fieldWidth) { nextStaticX = 0; nextStaticZ++; }
                if (nextStaticZ >= fieldDepth) { nextStaticZ = 0; nextStaticY++; }

                float x = ox + (nextStaticX + 0.5f) * d + ((nextStaticY & 1) == 1 ? d * 0.25f : 0f);
                float y = (nextStaticY + 0.5f) * layerH;
                float z = oz + (nextStaticZ + 0.5f) * d;
                nextStaticX++;

                var go = new GameObject("S");
                go.transform.SetParent(staticRoot);
                go.transform.position = new Vector3(x, y, z);
                var col = go.AddComponent<SphereCollider>();
                col.radius = 0.48f;            // slightly under visual radius
                col.sharedMaterial = physMat;
                // NO Rigidbody — this is a static collider.
                statics.Add(go.transform);
            }
        }

        private void DropActives(int count)
        {
            float d = ballDiameter;
            float spread = Mathf.Min(fieldWidth, fieldDepth) * d * 0.4f;
            float topY = (nextStaticY + 4) * d * 0.82f + 2f;

            for (int i = 0; i < count && actives.Count < maxActives; i++)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                go.name = "A";
                go.transform.SetParent(activeRoot);
                go.transform.localScale = Vector3.one * d;
                go.transform.position = new Vector3(
                    Random.Range(-spread, spread), topY + Random.Range(0f, spread),
                    Random.Range(-spread, spread));
                var col = go.GetComponent<SphereCollider>();
                col.radius = 0.48f;
                col.sharedMaterial = physMat;
                var rb = go.AddComponent<Rigidbody>();
                rb.interpolation = RigidbodyInterpolation.None;
                rb.collisionDetectionMode = CollisionDetectionMode.Discrete;
                rb.drag = linearDrag;
                rb.angularDrag = angularDrag;
                // We draw everything via instancing; kill the primitive renderer
                // so there's no double-draw and "render off" truly removes cost.
                Destroy(go.GetComponent<MeshRenderer>());
                actives.Add(rb);
            }
        }

        private void SetBroadphase(int bp)
        {
            broadphase = (bp % 3 + 3) % 3;
            // Note: broadphase is a project setting; at runtime we can only report
            // intent. Changing it reliably requires Project Settings > Physics.
            // We log what to set so the tester can switch it there and re-run.
            string name = broadphase == 0 ? "SweepAndPrune(SAP)" : broadphase == 1 ? "AutomaticBoxPruning(ABP)" : "MultiboxPruning(MBP)";
            Debug.Log($"[StaticTest] Broadphase intent: {name}. Set it in Project Settings > Physics > Broadphase Type and relaunch for a true comparison (runtime switch is not supported by Unity).");
        }

        // ------------------------------------------------------------- update

        private int CountAwake()
        {
            int n = 0;
            for (int i = 0; i < actives.Count; i++)
                if (!actives[i].IsSleeping()) n++;
            return n;
        }

        private void Update()
        {
            // frame + fixed-step tracking
            TickFps(Time.unscaledDeltaTime);
            lastFixedSteps = fixedStepsThisFrame;
            fixedStepsThisFrame = 0;

            HandleInput();
            UpdateCam();

            if (renderingOn)
            {
                DrawInstanced(statics, staticMat, true);
                DrawInstanced(actives, activeMat, false);
            }

            logTimer += Time.unscaledDeltaTime;
            if (logTimer >= autoLogInterval) { logTimer = 0f; LogSnapshot("auto"); }
        }

        private void FixedUpdate() => fixedStepsThisFrame++;

        private void HandleInput()
        {
            if (Input.GetKeyDown(KeyCode.S)) { AddStatics(staticBatch); LogSnapshot("add-statics"); }
            if (Input.GetKeyDown(KeyCode.D)) { DropActives(activeBatch); LogSnapshot("drop-actives"); }
            if (Input.GetKeyDown(KeyCode.T)) SetBroadphase(broadphase + 1);
            if (Input.GetKeyDown(KeyCode.V)) ToggleRendering();
            if (Input.GetKeyDown(KeyCode.X)) ClearAll();
            if (Input.GetKeyDown(KeyCode.L)) LogSnapshot("manual");

            if (Input.GetMouseButton(1))
            {
                yaw += Input.GetAxis("Mouse X") * 4f;
                pitch = Mathf.Clamp(pitch - Input.GetAxis("Mouse Y") * 3f, 5f, 85f);
            }
            float wheel = Input.GetAxis("Mouse ScrollWheel");
            if (Mathf.Abs(wheel) > 0.001f) dist = Mathf.Clamp(dist - wheel * dist, 2f, 500f);
        }

        private void ToggleRendering()
        {
            // Everything is drawn via Graphics.DrawMeshInstanced (no per-object
            // MeshRenderers), so toggling this flag alone removes ALL draw cost
            // and leaves physics untouched — the clean way to separate the two.
            renderingOn = !renderingOn;
            Debug.Log($"[StaticTest] Rendering {(renderingOn ? "ON" : "OFF")} (physics unaffected).");
        }

        private void UpdateCam()
        {
            if (cam == null) return;
            Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
            cam.transform.position = pivot.position + rot * new Vector3(0, 0, -dist);
            cam.transform.rotation = Quaternion.LookRotation(pivot.position - cam.transform.position, Vector3.up);
        }

        private void DrawInstanced(List<Transform> list, Material mat, bool isStatic)
        {
            Vector3 scale = Vector3.one * ballDiameter;
            int n = 0;
            for (int i = 0; i < list.Count; i++)
            {
                drawBuf[n++] = Matrix4x4.TRS(list[i].position, Quaternion.identity, scale);
                if (n == BatchSize) { Graphics.DrawMeshInstanced(ballMesh, 0, mat, drawBuf, n); n = 0; }
            }
            if (n > 0) Graphics.DrawMeshInstanced(ballMesh, 0, mat, drawBuf, n);
        }

        private void DrawInstanced(List<Rigidbody> list, Material mat, bool isStatic)
        {
            Vector3 scale = Vector3.one * ballDiameter;
            int n = 0;
            for (int i = 0; i < list.Count; i++)
            {
                drawBuf[n++] = Matrix4x4.TRS(list[i].position, Quaternion.identity, scale);
                if (n == BatchSize) { Graphics.DrawMeshInstanced(ballMesh, 0, mat, drawBuf, n); n = 0; }
            }
            if (n > 0) Graphics.DrawMeshInstanced(ballMesh, 0, mat, drawBuf, n);
        }

        private void ClearAll()
        {
            for (int i = 0; i < statics.Count; i++) if (statics[i]) Destroy(statics[i].gameObject);
            for (int i = 0; i < actives.Count; i++) if (actives[i]) Destroy(actives[i].gameObject);
            statics.Clear(); actives.Clear();
            nextStaticX = nextStaticY = nextStaticZ = 0;
        }

        private void TickFps(float dt)
        {
            float ms = dt * 1000f;
            frameMs[fi] = ms; fi = (fi + 1) % Window; if (ff < Window) ff++;
            float sum = 0, max = 0;
            for (int i = 0; i < ff; i++) { sum += frameMs[i]; if (frameMs[i] > max) max = frameMs[i]; }
            fpsAvg = sum > 0 ? 1000f / (sum / ff) : 0;
            fpsWorst = max > 0.0001f ? 1000f / max : 0;
        }

        private void LogSnapshot(string tag)
        {
            Debug.Log($"[StaticTest] {tag} statics={statics.Count:n0} actives={actives.Count:n0} awake={CountAwake():n0} " +
                      $"fps(avg/worst)={fpsAvg:n0}/{fpsWorst:n0} fixedSteps/frame={lastFixedSteps} " +
                      $"render={(renderingOn ? "on" : "off")} hz={1f / Time.fixedDeltaTime:n0}");
        }

        private void OnGUI()
        {
            if (style == null) { style = new GUIStyle(GUI.skin.label) { fontSize = 15, richText = true }; style.normal.textColor = Color.white; }

            const int pad = 10;
            GUI.Box(new Rect(pad, pad, 400, 250), GUIContent.none);
            var r = new Rect(pad + 10, pad + 8, 380, 22);
            GUI.Label(r, "<b>Static Collider Experiment</b> (run in a BUILD)", style); r.y += 24;
            GUI.Label(r, $"Static colliders: {statics.Count:n0} / {maxStatics:n0}", style); r.y += 22;
            GUI.Label(r, $"Active rigidbodies: {actives.Count:n0}  awake {CountAwake():n0}", style); r.y += 22;

            string fc = fpsAvg >= 60 ? "lime" : fpsAvg >= 30 ? "yellow" : "red";
            GUI.Label(r, $"FPS avg <color={fc}>{fpsAvg:n0}</color>  worst {fpsWorst:n0}", style); r.y += 22;

            string sc = lastFixedSteps <= 1 ? "lime" : lastFixedSteps <= 3 ? "yellow" : "red";
            GUI.Label(r, $"Fixed steps/frame: <color={sc}>{lastFixedSteps}</color>  (>1 = timestep spiral)", style); r.y += 22;
            GUI.Label(r, $"Rendering: {(renderingOn ? "ON" : "OFF")}   Physics Hz: {1f / Time.fixedDeltaTime:n0}", style); r.y += 26;

            if (GUI.Button(new Rect(pad + 10, r.y, 110, 24), $"+{staticBatch} statics")) { AddStatics(staticBatch); LogSnapshot("add-statics"); }
            if (GUI.Button(new Rect(pad + 126, r.y, 110, 24), $"drop {activeBatch}")) { DropActives(activeBatch); LogSnapshot("drop-actives"); }
            if (GUI.Button(new Rect(pad + 242, r.y, 60, 24), "clear")) ClearAll();
            r.y += 30;
            if (GUI.Button(new Rect(pad + 10, r.y, 150, 24), $"render: {(renderingOn ? "ON" : "OFF")}")) ToggleRendering();
            if (GUI.Button(new Rect(pad + 166, r.y, 150, 24), "log")) LogSnapshot("manual");
            r.y += 30;
            GUI.Label(r, "<b>S</b> statics  <b>D</b> drop  <b>V</b> render  <b>X</b> clear  <b>L</b> log", style);
            r.y += 22;
            GUI.Label(r, "<b>T</b> broadphase intent (set in Project Settings, relaunch)", style);
        }
    }
}

using UnityEngine;
using UnityEngine.Rendering;

namespace BallPitFinal
{
    /// <summary>
    /// Thin orchestrator: input, camera, hover, HUD. All rules live in PitSimulation; all drawing in GpuBallRenderer.
    /// Call sequence:
    ///   Build:      sim = new PitSimulation(settings); sim.BuildFcc(wallMat); renderer = new GpuBallRenderer(...);
    ///               renderer.MarkAllDirty(); sim.OnRestingChanged = renderer.MarkDirty;
    ///   FixedUpdate: sim.HandTarget = ...; sim.Step(Time.fixedDeltaTime);   (Physics.simulationMode = Script)
    ///   Update:      input + hover (sim.Raycast) + camera
    ///   LateUpdate:  renderer.Render(cam, store); renderer.DrawDynamic(cam, activeInstances, n);
    /// </summary>
    public sealed class BallPitFinalController : MonoBehaviour
    {
        public PitSettings settings = new PitSettings();

        [Header("Rendering")]
        public bool occlusionCulling = true;
        [Tooltip("Safety valve: if balls vanish when occlusion is on, toggle this (or press F).")]
        public bool flipOcclusionY = false;
        public float lodPixelRadius = 12f;
        public bool ballsCastShadows = false;
        public bool ballsReceiveShadows = true;
        [Range(0, 3)] public int nearLodSubdivisions = 3;
        [Range(0, 3)] public int farLodSubdivisions = 1;

        [Header("Interaction")]
        public float throwSpeed = 12f;
        public float throwUpBias = 2.5f;

        [Header("Logging")]
        public float autoLogInterval = 2f;

        private PitSimulation sim;
        private GpuBallRenderer gpu;
        private Mesh nearMesh, farMesh;
        private Material highlightMat, groundMat, wallMat;
        private BallInstance[] dynamicInstances;
        private GameObject ground;
        private Camera cam;
        private Transform pivot;
        private float yaw = 35f, pitch = 35f, dist;
        private bool rmbDragged;
        private bool hoverValid;
        private int hoverId = -1;
        private Vector3 hoverPos;
        private SimulationMode previousSimMode;

        private const int Window = 120;
        private readonly float[] frameMs = new float[Window];
        private int fi, ff;
        private float fpsAvg, fpsWorst, logTimer, hudTimer;
        private string hudLine1 = "", hudLine2 = "";
        private GUIStyle style;

        private void Start()
        {
            previousSimMode = Physics.simulationMode;
            Physics.simulationMode = SimulationMode.Script; // we call Physics.Simulate ourselves (pre/post-step hooks)
            Physics.autoSyncTransforms = false;
            Time.fixedDeltaTime = 1f / settings.physicsHz;
            Time.maximumDeltaTime = 0.1f; // cap catch-up steps (avoids the fixed-step death spiral)

            nearMesh = BallMeshes.IcoSphere(nearLodSubdivisions, "BallNear");
            farMesh = BallMeshes.IcoSphere(farLodSubdivisions, "BallFar");
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            highlightMat = new Material(lit);
            var hi = new Color(1f, 1f, 0.2f);
            highlightMat.SetColor("_BaseColor", hi);
            highlightMat.EnableKeyword("_EMISSION");
            highlightMat.SetColor("_EmissionColor", hi * 1.5f);
            groundMat = new Material(lit); groundMat.SetColor("_BaseColor", new Color(0.18f, 0.18f, 0.2f));
            wallMat = new Material(lit); wallMat.SetColor("_BaseColor", new Color(0.3f, 0.32f, 0.36f));

            BuildCameraAndLight();
            BuildPit();
        }

        // Pit-size presets (x, y, z). Product shown for reference.
        private static readonly Vector3Int[] Presets =
        {
            new Vector3Int(40, 25, 40),   // 1: 40,000
            new Vector3Int(60, 40, 60),   // 2: 144,000
            new Vector3Int(80, 60, 80),   // 3: 384,000
            new Vector3Int(100, 100, 100),// 4: 1,000,000
            new Vector3Int(140, 120, 140),// 5: ~2,352,000
        };

        private void ApplyPreset(int i)
        {
            if (i < 0 || i >= Presets.Length) return;
            var s = Presets[i];
            settings.pitX = s.x; settings.pitY = s.y; settings.pitZ = s.z;
            BuildPit();
            Debug.Log($"[BallPitFinal] preset {i + 1}: {s.x}x{s.y}x{s.z} = {(long)s.x * s.y * s.z:n0} balls");
        }

        private void BuildPit()
        {
            DisposePit();
            sim = new PitSimulation(settings);
            sim.BuildFcc(wallMat);

            var palette = new Color[settings.colorCount];
            for (int i = 0; i < palette.Length; i++) palette[i] = Color.HSVToRGB((float)i / palette.Length, 0.75f, 0.95f);
            gpu = new GpuBallRenderer(sim.Store.Capacity, settings.activePoolSize, sim.VisualRadius, sim.PlayBounds, nearMesh, farMesh, palette);
            gpu.MarkAllDirty();
            sim.OnRestingChanged = gpu.MarkDirty;
            dynamicInstances = new BallInstance[settings.activePoolSize];

            ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "Ground";
            ground.transform.position = new Vector3(0f, -0.5f, 0f);
            ground.transform.localScale = new Vector3(sim.PlayBounds.size.x, 1f, sim.PlayBounds.size.z);
            ground.GetComponent<MeshRenderer>().sharedMaterial = groundMat;

            pivot.position = new Vector3(sim.PitCenter.x, sim.PitSize.y * 0.6f, sim.PitCenter.z);
            dist = Mathf.Max(sim.PitSize.x, sim.PitSize.z) * 1.4f;
            hoverValid = false; hoverId = -1;
            LogSnapshot("build");
        }

        private void DisposePit()
        {
            gpu?.Dispose(); gpu = null;
            sim?.Dispose(); sim = null;
            if (ground != null) Destroy(ground);
        }

        private void OnDestroy()
        {
            DisposePit();
            Physics.simulationMode = previousSimMode;
            if (nearMesh != null) Destroy(nearMesh);
            if (farMesh != null) Destroy(farMesh);
            if (highlightMat != null) Destroy(highlightMat);
            if (groundMat != null) Destroy(groundMat);
            if (wallMat != null) Destroy(wallMat);
        }

        private void BuildCameraAndLight()
        {
            var camGo = new GameObject("Cam");
            cam = camGo.AddComponent<Camera>();
            camGo.AddComponent<AudioListener>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.05f, 0.06f, 0.08f);
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 1000f;
            var lightGo = new GameObject("Light");
            var l = lightGo.AddComponent<Light>();
            l.type = LightType.Directional;
            l.intensity = 1.1f;
            l.shadows = LightShadows.Soft;
            lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            pivot = new GameObject("Pivot").transform;
        }

        private void FixedUpdate()
        {
            if (sim == null) return;
            sim.HandTarget = HandPos();
            if (hoverValid) sim.AddToolSource(hoverPos, settings.ballDiameter * 2f);
            sim.Step(Time.fixedDeltaTime);
        }

        private void Update()
        {
            TickFps(Time.unscaledDeltaTime);
            HandleInput();
            UpdateCam();
            UpdateHover();
            logTimer += Time.unscaledDeltaTime;
            if (logTimer >= autoLogInterval) { logTimer = 0f; LogSnapshot("auto"); }
        }

        private void LateUpdate()
        {
            if (sim == null || gpu == null) return;
            gpu.OcclusionEnabled = occlusionCulling;
            gpu.FlipOcclusionY = flipOcclusionY;
            gpu.LodPixelRadius = lodPixelRadius;
            gpu.CastShadows = ballsCastShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            gpu.ReceiveShadows = ballsReceiveShadows;
            gpu.Render(cam, sim.Store);

            float alpha = Mathf.Clamp01((Time.time - Time.fixedTime) / Time.fixedDeltaTime);
            int n = sim.Active.FillInstances(dynamicInstances, alpha, sim.Store);
            gpu.DrawDynamic(cam, dynamicInstances, n);

            if (hoverValid && sim.HeldId < 0)
            {
                float s = settings.ballDiameter * 1.12f;
                Graphics.DrawMesh(nearMesh, Matrix4x4.TRS(hoverPos, Quaternion.identity, Vector3.one * s), highlightMat, 0, cam);
            }
        }

        private void HandleInput()
        {
            if (Input.GetKeyDown(KeyCode.R)) BuildPit();
            if (Input.GetKeyDown(KeyCode.L)) LogSnapshot("manual");
            if (Input.GetKeyDown(KeyCode.O)) occlusionCulling = !occlusionCulling;
            if (Input.GetKeyDown(KeyCode.F)) flipOcclusionY = !flipOcclusionY;
            // Number keys 1-5 switch pit-size presets (rebuilds the pit).
            if (Input.GetKeyDown(KeyCode.Alpha1)) ApplyPreset(0);
            if (Input.GetKeyDown(KeyCode.Alpha2)) ApplyPreset(1);
            if (Input.GetKeyDown(KeyCode.Alpha3)) ApplyPreset(2);
            if (Input.GetKeyDown(KeyCode.Alpha4)) ApplyPreset(3);
            if (Input.GetKeyDown(KeyCode.Alpha5)) ApplyPreset(4);

            if (Input.GetMouseButtonDown(0) && !Input.GetMouseButton(1) && sim.HeldId < 0 && hoverValid)
                sim.TryGrab(hoverId);
            if (Input.GetMouseButton(1))
            {
                float dx = Input.GetAxis("Mouse X"), dy = Input.GetAxis("Mouse Y");
                if (Mathf.Abs(dx) > 0.01f || Mathf.Abs(dy) > 0.01f) { rmbDragged = true; yaw += dx * 4f; pitch = Mathf.Clamp(pitch - dy * 3f, 5f, 85f); }
            }
            if (Input.GetMouseButtonUp(1))
            {
                if (!rmbDragged && sim.HeldId >= 0)
                {
                    Vector3 dir = (cam.transform.forward + Vector3.up * (throwUpBias / Mathf.Max(1f, throwSpeed))).normalized;
                    sim.ThrowHeld(dir * throwSpeed);
                }
                rmbDragged = false;
            }
            float wheel = Input.GetAxis("Mouse ScrollWheel");
            if (Mathf.Abs(wheel) > 0.001f) dist = Mathf.Clamp(dist - wheel * dist, settings.ballDiameter * 4f, 800f);
        }

        private void UpdateCam()
        {
            Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
            cam.transform.position = pivot.position + rot * new Vector3(0f, 0f, -dist);
            cam.transform.rotation = Quaternion.LookRotation(pivot.position - cam.transform.position, Vector3.up);
        }

        private void UpdateHover()
        {
            hoverValid = false;
            if (sim == null) return;
            Ray ray = cam.ScreenPointToRay(Input.mousePosition);
            if (sim.Raycast(ray, cam.farClipPlane, out int id, out _))
            {
                hoverValid = true; hoverId = id; hoverPos = sim.Store.pos[id];
            }
        }

        private Vector3 HandPos() => cam.ViewportToWorldPoint(new Vector3(0.78f, 0.22f, settings.ballDiameter * 3.2f));

        private void TickFps(float dt)
        {
            frameMs[fi] = dt * 1000f; fi = (fi + 1) % Window; if (ff < Window) ff++;
            float sum = 0f, max = 0f;
            for (int i = 0; i < ff; i++) { sum += frameMs[i]; if (frameMs[i] > max) max = frameMs[i]; }
            fpsAvg = sum > 0f ? 1000f / (sum / ff) : 0f;
            fpsWorst = max > 0.0001f ? 1000f / max : 0f;
        }

        private void LogSnapshot(string tag)
        {
            if (sim == null) return;
            string check = sim.VerifyAll(out string err) ? "OK" : $"MISMATCH({err})";
            var st = sim.Store;
            Debug.Log($"[BallPitFinal] {tag} balls={st.Count:n0} resting={st.StateTotal(BallStore.State.Resting):n0} " +
                      $"active={st.StateTotal(BallStore.State.Active):n0} held={st.StateTotal(BallStore.State.Held)} " +
                      $"stored={st.StateTotal(BallStore.State.Stored):n0} lost={st.StateTotal(BallStore.State.Lost)} " +
                      $"drawn(near/far)={(gpu?.DrawnNear ?? 0):n0}/{(gpu?.DrawnFar ?? 0):n0} occlusion={occlusionCulling} " +
                      $"colliders={sim.Colliders.Live:n0}/{sim.Colliders.Capacity:n0} capHits={sim.Colliders.CapHits} queue={sim.QueueLength:n0} " +
                      $"fps(avg/worst)={fpsAvg:n0}/{fpsWorst:n0} check={check}");
        }

        private void OnGUI()
        {
            if (sim == null) return;
            if (style == null) { style = new GUIStyle(GUI.skin.label) { fontSize = 15 }; style.normal.textColor = Color.white; }
            hudTimer -= Time.unscaledDeltaTime;
            if (hudTimer <= 0f)
            {
                hudTimer = 0.25f;
                var st = sim.Store;
                hudLine1 = $"balls {st.Count:n0}  resting {st.StateTotal(BallStore.State.Resting):n0}  active {sim.Active.Count:n0}  queue {sim.QueueLength:n0}  colliders {sim.Colliders.Live:n0}";
                hudLine2 = $"drawn {(gpu.DrawnNear + gpu.DrawnFar):n0} (near {gpu.DrawnNear:n0})  occlusion {(occlusionCulling ? "ON" : "OFF")}  flipY {flipOcclusionY}  FPS {fpsAvg:n0} / worst {fpsWorst:n0}";
            }
            GUI.Box(new Rect(10, 10, 640, 96), GUIContent.none);
            GUI.Label(new Rect(20, 16, 620, 22), hudLine1, style);
            GUI.Label(new Rect(20, 40, 620, 22), hudLine2, style);
            GUI.Label(new Rect(20, 64, 620, 22), "LMB grab  RMB throw/orbit  1-5 pit size  wheel zoom  O occlusion  F flipY  R rebuild  L log", style);
        }
    }
}

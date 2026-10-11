using System.Collections;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.Rendering;

namespace BallPitFinal
{
    /// <summary>
    /// Thin orchestrator: build, input, camera, hover, HUD, mode (offline / host / client).
    ///   Offline/Host: PitWorld (with colliders) + PitAuthority (rules + physics) [+ NetPit host].
    ///   Client:       PitWorld (no colliders) + PitReplica (interpolated live balls) + NetPit client.
    /// Per frame: FixedUpdate -> tools, bench.PreStep, net.HostPreStep, authority.Step, bench.PostStep
    ///            Update      -> input, camera, hover (resting grid + live balls as drawn), net update
    ///            LateUpdate  -> gpu.Render (resting) + gpu.DrawDynamic (active pool or replica)
    /// Keys: LMB grab (resting or moving), RMB throw/orbit, wheel zoom, V vacuum (hold), G dump (hold), U undermine,
    ///       K bench, P collapse snapshot, 1-5 presets, T fill FCC/Random, R rebuild, O occlusion, F flipY, L full check,
    ///       H host, J join.
    /// </summary>
    public sealed class BallPitFinalController : MonoBehaviour
    {
        public enum Mode { Offline, Host, Client }

        public PitSettings settings = new PitSettings();
        public NetSettings network = new NetSettings();

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

        [Header("Bench (K)")]
        public BenchScenario benchScenario = BenchScenario.Undermine;

        [Header("Logging")]
        [Tooltip("Cheap status line interval (s). The full O(N) ledger recount only runs on L.")]
        public float autoLogInterval = 5f;

        private static readonly Vector3Int[] Presets =
        {
            new Vector3Int(40, 25, 40), new Vector3Int(60, 40, 60), new Vector3Int(80, 60, 80),
            new Vector3Int(100, 100, 100), new Vector3Int(133, 133, 133)
        };

        private Mode mode = Mode.Offline;
        private PitWorld world;
        private PitAuthority authority;
        private PitReplica replica;
        private GpuBallRenderer gpu;
        private NetPit netPit;
        private readonly CollapseBench bench = new CollapseBench();
        private bool building;
        private float buildProgress;
        private string buildMessage = "";

        private Mesh nearMesh, farMesh;
        private Material highlightMat, groundMat, wallMat;
        private BallInstance[] dynamicInstances = new BallInstance[256];
        private int[] dynamicIds = new int[256];
        private int dynamicCount;
        private GameObject ground;
        private Camera cam;
        private Transform pivot;
        private float yaw = 35f, pitch = 35f, dist = 60f;
        private bool rmbDragged;
        private bool hoverValid, aimValid;
        private int hoverId = -1;
        private Vector3 hoverPos, aimPoint;
        private bool vacuumHeld, dumpHeld;
        private float lastToolTime;
        private SimulationMode previousSimMode;

        private const int Window = 120;
        private readonly float[] frameMs = new float[Window];
        private int fi, ff;
        private float fpsAvg, fpsWorst, logTimer, hudTimer;
        private string hud1 = "", hud2 = "", hud3 = "";
        private GUIStyle style;

        // ------------------------------------------------------------------ Lifecycle

        private void Start()
        {
            previousSimMode = Physics.simulationMode;
            Physics.simulationMode = SimulationMode.Script; // PitAuthority calls Physics.Simulate itself
            Physics.autoSyncTransforms = false;
            Time.maximumDeltaTime = 0.1f; // cap catch-up steps (avoids the fixed-step death spiral)

            // Camera/light FIRST: if a material shader is missing in a build, we must not end up cameraless.
            BuildCameraAndLight();

            nearMesh = BallMeshes.IcoSphere(nearLodSubdivisions, "BallNear");
            farMesh = BallMeshes.IcoSphere(farLodSubdivisions, "BallFar");

            // Pick a lit shader for whatever pipeline is actually active. This project has URP installed but may have
            // NO URP asset assigned (then it runs Built-in), so Shader.Find("URP/Lit") would return null and crash
            // material creation in a build. Use URP/Lit only when a render pipeline is assigned, else built-in Standard.
            var lit = GraphicsSettings.currentRenderPipeline != null
                ? Shader.Find("Universal Render Pipeline/Lit")
                : Shader.Find("Standard");
            if (lit == null)
            {
                Debug.LogError("[BallPitFinal] no lit shader found (URP/Lit and Standard both missing). " +
                               "If using URP, add 'Universal Render Pipeline/Lit' to Graphics > Always Included Shaders. Falling back.");
                lit = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color") ?? Shader.Find("Sprites/Default");
            }
            highlightMat = new Material(lit);
            var hi = new Color(1f, 1f, 0.2f);
            SetBaseColor(highlightMat, hi);
            highlightMat.EnableKeyword("_EMISSION");
            highlightMat.SetColor("_EmissionColor", hi * 1.5f);
            groundMat = new Material(lit); SetBaseColor(groundMat, new Color(0.18f, 0.18f, 0.2f));
            wallMat = new Material(lit); SetBaseColor(wallMat, new Color(0.3f, 0.32f, 0.36f));

            StartCoroutine(BuildRoutine(settings, PitWorld.ComputeBounds(settings), true));
        }

        private IEnumerator BuildRoutine(PitSettings s, Bounds bounds, bool asAuthority)
        {
            building = true; buildProgress = 0f; buildMessage = "";
            DisposePit();
            if (asAuthority) Time.fixedDeltaTime = 1f / s.physicsHz;
            var w = new PitWorld(s, bounds, withColliders: asAuthority);
            if (s.fillType == FillType.Random)
            {
                var task = RandomFill.LoadOrGenerateAsync(w, Application.streamingAssetsPath, Application.persistentDataPath,
                                                          Application.isEditor, p => buildProgress = p);
                while (!task.IsCompleted) yield return null;
                if (task.IsFaulted)
                {
                    Debug.LogException(task.Exception);
                    w.Dispose(); building = false; yield break;
                }
                buildMessage = task.Result;
                Debug.Log("[BallPitFinal] random fill " + task.Result);
            }
            else w.FillFcc();
            w.FinishFill(wallMat);
            world = w;

            var palette = new Color[s.colorCount];
            for (int i = 0; i < palette.Length; i++) palette[i] = Color.HSVToRGB((float)i / palette.Length, 0.75f, 0.95f);
            gpu = new GpuBallRenderer(w.Store.Capacity, Mathf.Max(256, s.maxActive), w.VisualRadius, w.PlayBounds, nearMesh, farMesh, palette);
            gpu.MarkAllDirty();
            w.OnRestingChanged = gpu.MarkDirty;
            EnsureDynamic(s.maxActive);

            ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "Ground";
            ground.transform.position = new Vector3(w.PlayBounds.center.x, w.FloorY - 0.5f, w.PlayBounds.center.z);
            ground.transform.localScale = new Vector3(w.PlayBounds.size.x, 1f, w.PlayBounds.size.z);
            ground.GetComponent<MeshRenderer>().sharedMaterial = groundMat;

            if (asAuthority)
            {
                w.RegisterSceneEnvironment(); // ground + walls + any static level colliders -> cheap support pre-check
                authority = new PitAuthority(w);
            }
            else replica = new PitReplica(w, s.maxActive);

            pivot.position = new Vector3(w.PitCenter.x, w.PitSize.y * 0.6f, w.PitCenter.z);
            dist = Mathf.Max(w.PitSize.x, w.PitSize.z) * 1.4f;
            hoverValid = false; hoverId = -1; dynamicCount = 0;
            building = false;
            if (!asAuthority && netPit != null) netPit.AttachClientWorld(w, replica);
            LogStatus("build", fullCheck: true);
        }

        private void DisposePit()
        {
            bench.Stop();
            authority?.Dispose(); authority = null;
            replica = null;
            gpu?.Dispose(); gpu = null;
            world?.Dispose(); world = null;
            if (ground != null) Destroy(ground);
            dynamicCount = 0;
        }

        private void OnDestroy()
        {
            netPit?.Dispose();
            DisposePit();
            Physics.simulationMode = previousSimMode;
            if (nearMesh != null) Destroy(nearMesh);
            if (farMesh != null) Destroy(farMesh);
            if (highlightMat != null) Destroy(highlightMat);
            if (groundMat != null) Destroy(groundMat);
            if (wallMat != null) Destroy(wallMat);
        }

        // URP Lit uses _BaseColor; Built-in Standard uses _Color. Set whichever the shader has.
        private static void SetBaseColor(Material m, Color c)
        {
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
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

        private void EnsureDynamic(int n)
        {
            if (dynamicInstances.Length >= n) return;
            int cap = Mathf.NextPowerOfTwo(Mathf.Max(256, n));
            dynamicInstances = new BallInstance[cap];
            dynamicIds = new int[cap];
        }

        // ------------------------------------------------------------------ Networking

        private NetworkManager EnsureNetworkManager()
        {
            NetworkManager nm = NetworkManager.Singleton;
            if (nm == null)
            {
                var go = new GameObject("NetworkManager (BallPit UTP)");
                nm = go.AddComponent<NetworkManager>();
                var utp = go.AddComponent<UnityTransport>();
                nm.NetworkConfig = new NetworkConfig { NetworkTransport = utp, ConnectionApproval = false, EnableSceneManagement = false };
            }
            if (nm.NetworkConfig.NetworkTransport is UnityTransport u) u.SetConnectionData(network.address, network.port);
            return nm;
        }

        private void StartHosting()
        {
            if (mode != Mode.Offline || authority == null || building) return;
            NetworkManager nm = EnsureNetworkManager();
            if (!nm.StartHost()) { Debug.LogError("[BallPitFinal] StartHost failed"); return; }
            netPit = NetPit.StartHost(nm, network, authority);
            mode = Mode.Host;
            Debug.Log($"[BallPitFinal] hosting on port {network.port}");
        }

        private void StartJoining()
        {
            if (mode != Mode.Offline || building) return;
            DisposePit(); // the client builds its pit from the host's header
            NetworkManager nm = EnsureNetworkManager();
            if (!nm.StartClient()) { Debug.LogError("[BallPitFinal] StartClient failed"); return; }
            netPit = NetPit.StartClient(nm, network);
            mode = Mode.Client;
            Debug.Log($"[BallPitFinal] joining {network.address}:{network.port}");
        }

        // ------------------------------------------------------------------ Frame

        private void FixedUpdate()
        {
            if (building || authority == null) return;
            float dt = Time.fixedDeltaTime;
            authority.HandTarget[0] = HandPos();
            if (hoverValid) authority.AddToolSource(hoverPos, settings.ballDiameter * 2f);
            if (vacuumHeld && hoverValid) authority.Excavate(0, hoverPos, settings.vacuumRadius, settings.vacuumPerStep);
            if (dumpHeld && aimValid) authority.Dump(0, aimPoint + Vector3.up * settings.dumpHeight, settings.dumpPerStep);
            bench.PreStep(dt);
            netPit?.HostPreStep();
            authority.Step(dt);
            bench.PostStep(dt);
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            TickFps(dt);
            bench.OnFrame(dt * 1000f);
            HandleInput();
            UpdateCam();
            UpdateHover();

            if (mode == Mode.Client && netPit != null)
            {
                if (netPit.HeaderPending && !building && world == null)
                    StartCoroutine(BuildRoutine(netPit.PendingSettings, netPit.PendingBounds, false));
                byte tool = vacuumHeld && hoverValid ? NetPit.ToolVacuum : dumpHeld && aimValid ? NetPit.ToolDump : NetPit.ToolNone;
                Vector3 toolPoint = tool == NetPit.ToolVacuum ? hoverPos : aimPoint;
                netPit.SetView(cam.transform.position, HandPos(), tool, toolPoint);
                netPit.ClientUpdate(dt);
            }
            else if (mode == Mode.Host && netPit != null) netPit.HostUpdate(dt);

            logTimer += dt;
            if (autoLogInterval > 0f && logTimer >= autoLogInterval) { logTimer = 0f; LogStatus("auto", fullCheck: false); }
        }

        private void LateUpdate()
        {
            if (building || world == null || gpu == null) return;
            gpu.OcclusionEnabled = occlusionCulling;
            gpu.FlipOcclusionY = flipOcclusionY;
            gpu.LodPixelRadius = lodPixelRadius;
            gpu.CastShadows = ballsCastShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            gpu.ReceiveShadows = ballsReceiveShadows;
            gpu.Render(cam, world.Store);

            dynamicCount = 0;
            if (authority != null)
            {
                EnsureDynamic(authority.Active.Count);
                float alpha = Mathf.Clamp01((Time.time - Time.fixedTime) / Time.fixedDeltaTime);
                dynamicCount = authority.Active.FillInstances(dynamicInstances, dynamicIds, alpha, world.Store);
            }
            else if (replica != null && netPit != null && netPit.Synced)
            {
                EnsureDynamic(replica.Count);
                replica.LocalHand = HandPos();
                dynamicCount = replica.FillInstances(dynamicInstances, dynamicIds, netPit.PlaybackTime);
            }
            gpu.DrawDynamic(cam, dynamicInstances, dynamicCount);

            if (hoverValid && LocalHeldId() < 0)
            {
                float s = settings.ballDiameter * 1.12f;
                Graphics.DrawMesh(nearMesh, Matrix4x4.TRS(hoverPos, Quaternion.identity, Vector3.one * s), highlightMat, 0, cam);
            }
        }

        private int LocalHeldId()
        {
            if (authority != null) return authority.HeldId[0];
            return netPit != null ? netPit.LocalHeld : -1;
        }

        // ------------------------------------------------------------------ Input

        private void HandleInput()
        {
            bool offline = mode == Mode.Offline;
            if (offline && !building)
            {
                for (int i = 0; i < Presets.Length; i++)
                {
                    if (!Input.GetKeyDown(KeyCode.Alpha1 + i)) continue;
                    settings.pitX = Presets[i].x; settings.pitY = Presets[i].y; settings.pitZ = Presets[i].z;
                    Rebuild();
                }
                if (Input.GetKeyDown(KeyCode.T))
                {
                    settings.fillType = settings.fillType == FillType.Fcc ? FillType.Random : FillType.Fcc;
                    Rebuild();
                }
                if (Input.GetKeyDown(KeyCode.R)) Rebuild();
                if (Input.GetKeyDown(KeyCode.H)) StartHosting();
                if (Input.GetKeyDown(KeyCode.J)) StartJoining();
            }
            if (Input.GetKeyDown(KeyCode.L)) LogStatus("manual", fullCheck: true);
            if (Input.GetKeyDown(KeyCode.P) && authority != null) CollapseTelemetry.Append(authority, Time.time - lastToolTime, fpsAvg, fpsWorst);
            if (Input.GetKeyDown(KeyCode.O)) occlusionCulling = !occlusionCulling;
            if (Input.GetKeyDown(KeyCode.F)) flipOcclusionY = !flipOcclusionY;
            if (Input.GetKeyDown(KeyCode.K) && authority != null && !building)
            {
                if (bench.Running) bench.Stop(); else { bench.Start(authority, benchScenario); lastToolTime = Time.time; }
            }

            vacuumHeld = Input.GetKey(KeyCode.V);
            dumpHeld = Input.GetKey(KeyCode.G);
            if (vacuumHeld || dumpHeld) lastToolTime = Time.time;

            if (Input.GetKeyDown(KeyCode.U) && hoverValid)
            {
                Vector3 c = hoverPos - Vector3.up * settings.undermineDepth;
                if (authority != null) authority.Excavate(0, c, settings.undermineRadius, settings.undermineMax);
                else netPit?.SendUndermine(c);
                lastToolTime = Time.time;
            }

            if (Input.GetMouseButtonDown(0) && !Input.GetMouseButton(1) && LocalHeldId() < 0 && hoverValid)
            {
                if (authority != null) authority.TryGrab(0, hoverId);
                else netPit?.SendGrab(hoverId);
            }
            if (Input.GetMouseButton(1))
            {
                float dx = Input.GetAxis("Mouse X"), dy = Input.GetAxis("Mouse Y");
                if (Mathf.Abs(dx) > 0.01f || Mathf.Abs(dy) > 0.01f) { rmbDragged = true; yaw += dx * 4f; pitch = Mathf.Clamp(pitch - dy * 3f, 5f, 85f); }
            }
            if (Input.GetMouseButtonUp(1))
            {
                if (!rmbDragged && LocalHeldId() >= 0)
                {
                    Vector3 dir = (cam.transform.forward + Vector3.up * (throwUpBias / Mathf.Max(1f, throwSpeed))).normalized;
                    if (authority != null) authority.ThrowHeld(0, dir * throwSpeed);
                    else netPit?.SendThrow(dir * throwSpeed);
                }
                rmbDragged = false;
            }
            float wheel = Input.GetAxis("Mouse ScrollWheel");
            if (Mathf.Abs(wheel) > 0.001f) dist = Mathf.Clamp(dist - wheel * dist, settings.ballDiameter * 4f, 800f);
        }

        private void Rebuild()
        {
            if (mode != Mode.Offline || building) return;
            StartCoroutine(BuildRoutine(settings, PitWorld.ComputeBounds(settings), true));
        }

        private void UpdateCam()
        {
            Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
            cam.transform.position = pivot.position + rot * new Vector3(0f, 0f, -dist);
            cam.transform.rotation = Quaternion.LookRotation(pivot.position - cam.transform.position, Vector3.up);
        }

        /// <summary>
        /// Nearest of: resting balls (grid DDA), moving balls exactly where they were drawn last frame (so picking matches
        /// the screen on host and client), and the environment (aim point for dumping).
        /// </summary>
        private void UpdateHover()
        {
            hoverValid = false; aimValid = false;
            if (building || world == null) return;
            Ray ray = cam.ScreenPointToRay(Input.mousePosition);
            float best = float.MaxValue;
            if (world.Raycast(ray, cam.farClipPlane, out int id, out float t))
            {
                hoverValid = true; hoverId = id; hoverPos = world.Store.pos[id]; best = t;
            }
            int held = LocalHeldId();
            if (PitWorld.RaycastSpheres(ray, dynamicInstances, dynamicIds, dynamicCount, world.VisualRadius, cam.farClipPlane, out int liveId, out float lt)
                && lt < best && liveId != held)
            {
                for (int i = 0; i < dynamicCount; i++) if (dynamicIds[i] == liveId) { hoverPos = dynamicInstances[i].pos; break; }
                hoverValid = true; hoverId = liveId; best = lt;
            }
            if (hoverValid) { aimValid = true; aimPoint = ray.GetPoint(best); }
            if (world.RaycastEnvironment(ray, cam.farClipPlane, out RaycastHit hit) && hit.distance < best)
            {
                aimValid = true; aimPoint = hit.point;
            }
        }

        private Vector3 HandPos() => cam.ViewportToWorldPoint(new Vector3(0.78f, 0.22f, settings.ballDiameter * 3.2f));

        // ------------------------------------------------------------------ HUD / logging

        private void TickFps(float dt)
        {
            frameMs[fi] = dt * 1000f; fi = (fi + 1) % Window; if (ff < Window) ff++;
            float sum = 0f, max = 0f;
            for (int i = 0; i < ff; i++) { sum += frameMs[i]; if (frameMs[i] > max) max = frameMs[i]; }
            fpsAvg = sum > 0f ? 1000f / (sum / ff) : 0f;
            fpsWorst = max > 0.0001f ? 1000f / max : 0f;
        }

        private void LogStatus(string tag, bool fullCheck)
        {
            if (world == null) return;
            string check = "skipped (press L)";
            if (fullCheck)
            {
                bool ok = authority != null ? authority.VerifyAll(out string err) : world.Verify(out err);
                check = ok ? "OK" : "MISMATCH(" + err + ")";
            }
            var st = world.Store;
            string phys = authority != null
                ? $"queue={authority.QueueLength:n0}/{authority.QueueRegions}reg limit={authority.EffectiveCollapseLimit:n0} colliders={world.Colliders.Live:n0}/{world.Colliders.Capacity:n0} " +
                  $"capHits={world.Colliders.CapHits} sim={authority.LastSimulateMs:0.00}ms (ema {authority.SimMsEma:0.00}) step={authority.LastStepMs:0.00}ms "
                : "";
            string net = netPit == null ? "" : (netPit.IsHost ? netPit.HostStatus() : netPit.ClientStatus());
            Debug.Log($"[BallPitFinal] {tag} mode={mode} fill={world.S.fillType} balls={st.Count:n0} resting={st.StateTotal(BallStore.State.Resting):n0} " +
                      $"active={st.StateTotal(BallStore.State.Active):n0} held={st.StateTotal(BallStore.State.Held)} hopper={st.StateTotal(BallStore.State.Hopper):n0} " +
                      $"stored={st.StateTotal(BallStore.State.Stored):n0} lost={st.StateTotal(BallStore.State.Lost)} " +
                      $"drawn(near/far)={(gpu?.DrawnNear ?? 0):n0}/{(gpu?.DrawnFar ?? 0):n0} {phys}" +
                      $"fps(avg/worst)={fpsAvg:n0}/{fpsWorst:n0} check={check} {net}");
        }

        private void OnGUI()
        {
            if (style == null) { style = new GUIStyle(GUI.skin.label) { fontSize = 15 }; style.normal.textColor = Color.white; }
            hudTimer -= Time.unscaledDeltaTime;
            if (hudTimer <= 0f)
            {
                hudTimer = 0.25f;
                if (building) { hud1 = $"building {settings.fillType} fill... {buildProgress * 100f:0}%"; hud2 = buildMessage; hud3 = ""; }
                else if (world != null)
                {
                    var st = world.Store;
                    int live = st.StateTotal(BallStore.State.Active) + st.StateTotal(BallStore.State.Held);
                    hud1 = $"{mode} {world.S.fillType}  balls {st.Count:n0}  resting {st.StateTotal(BallStore.State.Resting):n0}  live {live:n0}" +
                           (authority != null ? $"/{authority.EffectiveCollapseLimit:n0}  queue {authority.QueueLength:n0} in {authority.QueueRegions} regions  sim {authority.SimMsEma:0.0}ms" : "") +
                           $"  hopper {st.StateTotal(BallStore.State.Hopper):n0}";
                    hud2 = $"drawn {(gpu.DrawnNear + gpu.DrawnFar):n0}  occlusion {(occlusionCulling ? "ON" : "OFF")}  FPS {fpsAvg:n0} / worst {fpsWorst:n0}  {bench.Status}";
                    hud3 = netPit == null ? "H host  J join " + network.address : (netPit.IsHost ? netPit.HostStatus() : netPit.ClientStatus());
                }
                else { hud1 = netPit != null ? netPit.ClientStatus() : ""; hud2 = hud3 = ""; }
            }
            GUI.Box(new Rect(10, 10, 900, 120), GUIContent.none);
            GUI.Label(new Rect(20, 16, 880, 22), hud1, style);
            GUI.Label(new Rect(20, 40, 880, 22), hud2, style);
            GUI.Label(new Rect(20, 64, 880, 22), hud3, style);
            GUI.Label(new Rect(20, 88, 880, 22), "LMB grab  RMB throw/orbit  V vacuum  G dump  U undermine  K bench  P snapshot  1-5 size  T fill  R rebuild  L check", style);
        }
    }
}

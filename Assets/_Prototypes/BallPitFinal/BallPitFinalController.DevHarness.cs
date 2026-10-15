// BallPitFinalController.DevHarness: everything dev-only (deliverable A, file plan F4; design a 7.2, 7.4, 8).
// Own camera, orbit, light and rendered ground; presets and T/R; H/J local UTP; the HUD (BallPitDevHud, the only
// OnGUI); L/P/O/F, bench K, mouse hover/grab/throw, V/G/U. Active standalone, or in a kit session only with
// devToolsInSession in a debug build. This file never names a gameplay type.
using System.Collections.Generic;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace BallPitFinal
{
    public sealed partial class BallPitFinalController
    {
        [Header("Interaction")]
        public float throwSpeed = 12f;
        public float throwUpBias = 2.5f;

        [Header("Bench (K)")]
        public BenchScenario benchScenario = BenchScenario.Undermine;

        [Header("Dev tools in session")]
        [Tooltip("Debug builds only. Enables V/G/U/K/P/L/O/F and the HUD in a kit session. A client's tools act only if the host's are on too.")]
        [SerializeField] private bool devToolsInSession = false;

        private static readonly Vector3Int[] Presets =
        {
            new Vector3Int(40, 25, 40), new Vector3Int(60, 40, 60), new Vector3Int(80, 60, 80),
            new Vector3Int(100, 100, 100), new Vector3Int(133, 133, 133)
        };

        private readonly CollapseBench bench = new CollapseBench();
        private Material groundMat, wallMat;
        private GameObject ground;
        private Camera devCam;
        private Transform pivot;
        private float yaw = 35f, pitch = 35f, dist = 60f;
        private bool rmbDragged;
        private bool hoverValid, aimValid;
        private int hoverId = -1;
        private Vector3 hoverPos, aimPoint;
        private bool vacuumHeld, dumpHeld;
        private float lastToolTime;
        private float hudTimer;
        private string hud1 = "", hud2 = "", hud3 = "";
        private BallPitDevHud devHud;

        // ------------------------------------------------------------------ Gate

        /// <summary>Dev tools in a kit session: the toggle is on and this is a debug build (always false in release).</summary>
        public bool SessionDevToolsActive => SessionDriven && devToolsInSession && Debug.isDebugBuild;
        /// <summary>The harness runs standalone, or in a session with SessionDevToolsActive.</summary>
        public bool HarnessActive => ComputeHarness(SessionDriven, devToolsInSession, Debug.isDebugBuild);
        public static bool ComputeHarness(bool sessionDriven, bool toggle, bool debugBuild) => !sessionDriven || (toggle && debugBuild);

        // ------------------------------------------------------------------ DevToolsUsed (achievement integrity seam)

        private readonly HandlerList<System.Action<int>> devToolsUsedHandlers = new HandlerList<System.Action<int>>();
        private readonly HashSet<int> devToolsUsedBy = new HashSet<int>();

        /// <summary>A dev tool acted in a session: the holder index, raised on its first use per session.</summary>
        public event System.Action<int> DevToolsUsed
        {
            add => devToolsUsedHandlers.Add(value);
            remove => devToolsUsedHandlers.Remove(value);
        }

        /// <summary>Set once any dev tool acted in this session (reset by AttachSession and DetachSession). B persists it.</summary>
        public bool DevToolsUsedInSession { get; private set; }

        private void MarkDevToolUsed(int holder)
        {
            if (!SessionDriven) return;
            DevToolsUsedInSession = true;
            if (!devToolsUsedBy.Add(holder)) return;
            Debug.Log($"[BallPitFinal] dev tool used in this session (holder {holder})");
            var hs = devToolsUsedHandlers.Items;
            for (int i = 0; i < hs.Length; i++)
            {
                try { hs[i](holder); }
                catch (System.Exception e) { devToolsUsedHandlers.Report(hs[i], e, nameof(DevToolsUsed)); }
            }
        }

        private void ResetDevToolMarks()
        {
            DevToolsUsedInSession = false;
            devToolsUsedBy.Clear();
        }

        // ------------------------------------------------------------------ Standalone venue preview hook

        /// <summary>Standalone venue preview. Gameplay installs it; null = no preview, today's dev scene.</summary>
        public delegate bool StandalonePrepareHook(PitSettings settings, out IPitEnvironment environment);
        /// <summary>Not reset by ResetStatics: gameplay re-installs it on every Play (BeforeSceneLoad).</summary>
        public static StandalonePrepareHook StandalonePrepare;

        // Before every standalone build (Start, R, T, 1-5): bind the scene's site, or nothing (today's dev scene).
        private void DevPrepareStandaloneBuild()
        {
            if (SessionDriven) return;
            Environment = null;
            if (StandalonePrepare == null) return;
            try
            {
                if (StandalonePrepare(settings, out IPitEnvironment env)) Environment = env;
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
                Environment = null;
            }
        }

        // ------------------------------------------------------------------ Dev camera, light and layout

        private void DevStartStandalone()
        {
            // Uncap the framerate: with VSync on, two instances on one PC fight over the monitor's vblank and share a
            // framerate budget. Off lets each run as fast as the GPU allows. Dev-focused; a shipped game can re-expose VSync.
            QualitySettings.vSyncCount = 0;
            DevPrepareStandaloneBuild();
            bool venue = Environment != null; // the authored venue lights itself and has its own sky

            var camGo = new GameObject("Cam");
            devCam = camGo.AddComponent<Camera>();
            camGo.AddComponent<AudioListener>();
            devCam.clearFlags = venue ? CameraClearFlags.Skybox : CameraClearFlags.SolidColor;
            devCam.backgroundColor = new Color(0.05f, 0.06f, 0.08f);
            devCam.nearClipPlane = 0.1f;
            devCam.farClipPlane = 1000f;
            if (!venue)
            {
                var lightGo = new GameObject("Light");
                var l = lightGo.AddComponent<Light>();
                l.type = LightType.Directional;
                l.intensity = 1.1f;
                l.shadows = LightShadows.Soft;
                lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            }
            pivot = new GameObject("Pivot").transform;
        }

        // Runs whenever no environment is bound, harness or not: the ground cube is the floor collider.
        private void DevBuildLayout(PitWorld w)
        {
            if (groundMat == null) { groundMat = new Material(litShader); SetBaseColor(groundMat, new Color(0.18f, 0.18f, 0.2f)); }
            ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "Ground";
            ground.transform.position = new Vector3(w.PlayBounds.center.x, w.FloorY - 0.5f, w.PlayBounds.center.z);
            ground.transform.localScale = new Vector3(w.PlayBounds.size.x, 1f, w.PlayBounds.size.z);
            ground.GetComponent<MeshRenderer>().sharedMaterial = groundMat;
        }

        private void DevDisposeLayout()
        {
            if (ground != null) Destroy(ground);
            ground = null;
        }

        private Material DevWallMaterial()
        {
            if (wallMat == null) { wallMat = new Material(litShader); SetBaseColor(wallMat, new Color(0.3f, 0.32f, 0.36f)); }
            return wallMat;
        }

        private void DevFrameCamera(PitWorld w)
        {
            hoverValid = false; hoverId = -1;
            if (devCam == null || pivot == null) return;
            pivot.position = new Vector3(w.PitCenter.x, w.FloorY + w.PitSize.y * 0.6f, w.PitCenter.z);
            dist = Mathf.Max(w.PitSize.x, w.PitSize.z) * 1.4f;
        }

        private void EnsureDevHud()
        {
            if (devHud != null || !HarnessActive) return;
            devHud = gameObject.AddComponent<BallPitDevHud>();
            devHud.owner = this;
        }

        // ------------------------------------------------------------------ Dev standalone (local UnityTransport) — never used by the real game

        private NetworkManager DevEnsureLocalUtpNetworkManager()
        {
            if (SessionDriven) { Debug.LogError("[BallPitFinal] dev local-UTP path is disabled in a kit session"); return null; }
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

        private void DevStartHosting()
        {
            if (SessionDriven) { Debug.LogError("[BallPitFinal] dev local-UTP path is disabled in a kit session"); return; }
            if (mode != Mode.Offline || authority == null || building) return;
            NetworkManager nm = DevEnsureLocalUtpNetworkManager();
            if (!nm.StartHost()) { Debug.LogError("[BallPitFinal] StartHost failed"); return; }
            netPit = NetPit.StartHost(nm, network, authority);
            mode = Mode.Host;
            Debug.Log($"[BallPitFinal] hosting on port {network.port}");
        }

        private void DevStartJoining()
        {
            if (SessionDriven) { Debug.LogError("[BallPitFinal] dev local-UTP path is disabled in a kit session"); return; }
            if (mode != Mode.Offline || building) return;
            if (Environment != null) { Environment = null; Debug.LogWarning("[BallPitFinal] J builds the dev layout at the origin"); }
            DisposePit(); // the client builds its pit from the host's header
            NetworkManager nm = DevEnsureLocalUtpNetworkManager();
            if (!nm.StartClient()) { Debug.LogError("[BallPitFinal] StartClient failed"); return; }
            netPit = NetPit.StartClient(nm, network);
            mode = Mode.Client;
            Debug.Log($"[BallPitFinal] joining {network.address}:{network.port}");
        }

        // ------------------------------------------------------------------ Frame and step hooks

        // Update: bench frame time, then (harness on, input not blocked) keys, orbit camera and hover, as today.
        private void DevFrame(float dt)
        {
            bench.OnFrame(dt * 1000f);
            if (!HarnessActive || (InputBlocked != null && InputBlocked()))
            {
                vacuumHeld = dumpHeld = false;
                if (!HarnessActive) { hoverValid = aimValid = false; }
                return;
            }
            DevHandleInput();
            DevUpdateCam();
            DevUpdateHover();
        }

        // FixedUpdate before the step. Standalone order is today's: hand, tool source, V, G, bench.
        private void DevPreStep(float dt)
        {
            if (!HarnessActive) return;
            if (!SessionDriven)
            {
                authority.HandTarget[0] = DevHandPos();
                if (hoverValid) authority.AddToolSource(hoverPos, settings.ballDiameter * 2f);
            }
            if (vacuumHeld && hoverValid) authority.Excavate(0, hoverPos, settings.vacuumRadius, settings.vacuumPerStep);
            if (dumpHeld && aimValid) authority.Dump(0, aimPoint + Vector3.up * settings.dumpHeight, settings.dumpPerStep);
            bench.PreStep(dt);
        }

        private void DevPostStep(float dt)
        {
            if (!HarnessActive) return;
            bench.PostStep(dt);
        }

        /// <summary>The V/G tool fields exactly as today's client view; true while a tool key is held with a valid aim.</summary>
        private bool DevToolOverride(out byte tool, out Vector3 toolPoint)
        {
            tool = vacuumHeld && hoverValid ? NetPit.ToolVacuum : dumpHeld && aimValid ? NetPit.ToolDump : NetPit.ToolNone;
            toolPoint = tool == NetPit.ToolVacuum ? hoverPos : aimPoint;
            return tool != NetPit.ToolNone;
        }

        private void DevDrawHover(Camera rc)
        {
            if (SessionDriven || !hoverValid || LocalHeldBall >= 0) return;
            float s = settings.ballDiameter * 1.12f;
            Graphics.DrawMesh(nearMesh, Matrix4x4.TRS(hoverPos, Quaternion.identity, Vector3.one * s), highlightMat, 0, rc);
        }

        // ------------------------------------------------------------------ Input

        private void DevHandleInput()
        {
            bool offline = !SessionDriven && mode == Mode.Offline; // 1-5, T, R, H, J: dev standalone only
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
                if (Input.GetKeyDown(KeyCode.H)) DevStartHosting();
                if (Input.GetKeyDown(KeyCode.J)) DevStartJoining();
            }
            if (Input.GetKeyDown(KeyCode.L)) LogStatus("manual", fullCheck: true);
            if (Input.GetKeyDown(KeyCode.P) && authority != null) CollapseTelemetry.Append(authority, Time.time - lastToolTime, fpsAvg, fpsWorst);
            if (Input.GetKeyDown(KeyCode.O)) occlusionCulling = !occlusionCulling;
            if (Input.GetKeyDown(KeyCode.F)) flipOcclusionY = !flipOcclusionY;
            if (Input.GetKeyDown(KeyCode.K) && authority != null && !building)
            {
                if (bench.Running) bench.Stop(); else { bench.Start(authority, benchScenario); lastToolTime = Time.time; MarkDevToolUsed(LocalSlot); }
            }

            vacuumHeld = Input.GetKey(KeyCode.V);
            dumpHeld = Input.GetKey(KeyCode.G);
            if (vacuumHeld || dumpHeld) lastToolTime = Time.time;

            if (Input.GetKeyDown(KeyCode.U) && hoverValid)
            {
                Vector3 c = hoverPos - Vector3.up * settings.undermineDepth;
                if (authority != null) authority.Excavate(0, c, settings.undermineRadius, settings.undermineMax);
                else if (netPit != null) { netPit.SendUndermine(c); MarkDevToolUsed(LocalSlot); }
                lastToolTime = Time.time;
            }

            if (SessionDriven) return; // grab, throw, orbit and zoom belong to the player in a session

            if (Input.GetMouseButtonDown(0) && !Input.GetMouseButton(1) && LocalHeldBall < 0 && hoverValid)
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
                if (!rmbDragged && LocalHeldBall >= 0 && devCam != null)
                {
                    Vector3 dir = (devCam.transform.forward + Vector3.up * (throwUpBias / Mathf.Max(1f, throwSpeed))).normalized;
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
            DevPrepareStandaloneBuild();
            StartCoroutine(BuildRoutine(settings, PitWorld.ComputeBounds(settings), true));
        }

        private void DevUpdateCam()
        {
            if (devCam == null || pivot == null) return;
            Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
            devCam.transform.position = pivot.position + rot * new Vector3(0f, 0f, -dist);
            devCam.transform.rotation = Quaternion.LookRotation(pivot.position - devCam.transform.position, Vector3.up);
        }

        /// <summary>
        /// Nearest of: resting balls (grid DDA), moving balls exactly where they were drawn last frame (so picking matches
        /// the screen on host and client), and the environment (aim point for dumping). Standalone aims with the mouse
        /// through the dev camera; a session aims with the registered player camera's center ray (the crosshair).
        /// </summary>
        private void DevUpdateHover()
        {
            hoverValid = false; aimValid = false;
            if (building || world == null) return;
            Camera c = SessionDriven ? (viewCamera != null ? renderCamera : null) : devCam;
            if (c == null) return;
            Ray ray = SessionDriven ? c.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f)) : c.ScreenPointToRay(Input.mousePosition);
            float best = float.MaxValue;
            if (world.Raycast(ray, c.farClipPlane, out int id, out float t))
            {
                hoverValid = true; hoverId = id; hoverPos = world.Store.pos[id]; best = t;
            }
            int held = LocalHeldBall;
            if (PitWorld.RaycastSpheres(ray, dynamicInstances, dynamicIds, dynamicCount, world.VisualRadius, c.farClipPlane, out int liveId, out float lt)
                && lt < best && liveId != held)
            {
                for (int i = 0; i < dynamicCount; i++) if (dynamicIds[i] == liveId) { hoverPos = dynamicInstances[i].pos; break; }
                hoverValid = true; hoverId = liveId; best = lt;
            }
            if (hoverValid) { aimValid = true; aimPoint = ray.GetPoint(best); }
            if (world.RaycastEnvironment(ray, c.farClipPlane, out RaycastHit hit) && hit.distance < best)
            {
                aimValid = true; aimPoint = hit.point;
            }
        }

        private Vector3 DevHandPos() =>
            devCam == null ? Vector3.zero : devCam.ViewportToWorldPoint(new Vector3(0.78f, 0.22f, settings.ballDiameter * 3.2f));

        // ------------------------------------------------------------------ HUD text (drawn by BallPitDevHud)

        /// <summary>HUD rows 0-2. Row 0 refreshes the text every 0.25 s, as today's OnGUI did.</summary>
        internal string DevHudLine(int i)
        {
            if (i == 0)
            {
                hudTimer -= Time.unscaledDeltaTime;
                if (hudTimer <= 0f) { hudTimer = 0.25f; ComposeHud(); }
                return hud1;
            }
            return i == 1 ? hud2 : i == 2 ? hud3 : "";
        }

        private void ComposeHud()
        {
            if (building) { hud1 = $"building {settings.fillType} fill... {buildProgress * 100f:0}%"; hud2 = buildMessage; hud3 = ""; }
            else if (world != null)
            {
                var st = world.Store;
                int live = st.StateTotal(BallStore.State.Active) + st.StateTotal(BallStore.State.Held);
                hud1 = $"{mode} {world.S.fillType}  balls {st.Count:n0}  resting {st.StateTotal(BallStore.State.Resting):n0}  live {live:n0}" +
                       (authority != null ? $"/{authority.EffectiveCollapseLimit:n0}  queue {authority.QueueLength:n0} in {authority.QueueRegions} regions  sim {authority.SimMsEma:0.0}ms" : "") +
                       $"  hopper {st.StateTotal(BallStore.State.Hopper):n0}";
                hud2 = $"drawn {(gpu.DrawnNear + gpu.DrawnFar):n0}  occlusion {(occlusionCulling ? "ON" : "OFF")}  FPS {fpsAvg:n0} / worst {fpsWorst:n0}  {bench.Status}";
                hud3 = netPit == null ? (SessionDriven ? "" : "H host  J join " + network.address) : (netPit.IsHost ? netPit.HostStatus() : netPit.ClientStatus());
            }
            else { hud1 = netPit != null ? netPit.ClientStatus() : sessionHud; hud2 = hud3 = ""; }
        }
    }

    /// <summary>
    /// The dev HUD: the only OnGUI. Runtime-only (declared in the harness file, so it cannot be added in the editor);
    /// the controller adds it while the harness is active, so the shipped path has no OnGUI callback at all.
    /// </summary>
    internal sealed class BallPitDevHud : MonoBehaviour
    {
        internal BallPitFinalController owner;
        private GUIStyle style;

        private void OnGUI()
        {
            if (owner == null || !owner.HarnessActive) return;
            if (style == null) { style = new GUIStyle(GUI.skin.label) { fontSize = 15 }; style.normal.textColor = Color.white; }
            string l0 = owner.DevHudLine(0), l1 = owner.DevHudLine(1), l2 = owner.DevHudLine(2);
            GUI.Box(new Rect(10, 10, 900, 120), GUIContent.none);
            GUI.Label(new Rect(20, 16, 880, 22), l0, style);
            GUI.Label(new Rect(20, 40, 880, 22), l1, style);
            GUI.Label(new Rect(20, 64, 880, 22), l2, style);
            GUI.Label(new Rect(20, 88, 880, 22), "LMB grab  RMB throw/orbit  V vacuum  G dump  U undermine  K bench  P snapshot  1-5 size  T fill  R rebuild  L check", style);
        }
    }
}

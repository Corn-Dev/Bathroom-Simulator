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
    /// Deliverable A split: this file is the production core (build, step, render, venue binding); .Session attaches a
    /// kit session; .View is the gameplay surface (events, accessors, camera handover, step listeners); .DevHarness
    /// holds everything dev-only (own camera, keys, tools, HUD), active only standalone or with devToolsInSession in a
    /// debug build.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public sealed partial class BallPitFinalController : MonoBehaviour
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
        [Tooltip("Optional ball look (palette, LOD meshes, gloss/spec/ambient, LOD radius, shadows). " +
                 "Empty = today's HSV palette, icospheres and the fields above.")]
        [SerializeField] private BallVisualConfig ballVisuals = null;

        [Header("Logging")]
        [Tooltip("Cheap status line interval (s). The full O(N) ledger recount only runs on L.")]
        public float autoLogInterval = 5f;

        private Mode mode = Mode.Offline;
        private PitWorld world;
        private PitAuthority authority;
        private PitReplica replica;
        private GpuBallRenderer gpu;
        private NetPit netPit;
        private bool building;
        private float buildProgress;
        private string buildMessage = "";

        private Mesh nearMesh, farMesh;
        private bool ownedNear, ownedFar; // created here (icospheres); config meshes are assets and never destroyed
        private Material highlightMat;
        private Shader litShader;         // the lit shader Start picked (dev materials are created from it lazily)
        private BallInstance[] dynamicInstances = new BallInstance[256];
        private int[] dynamicIds = new int[256];
        private int dynamicCount;
        private SimulationMode previousSimMode;

        // Build lifecycle (deliverable A)
        private int buildEpoch;                 // bumped by DisposePit: a running build that sees a newer epoch is abandoned
        private IPitEnvironment builtEnvironment; // the environment whose Build succeeded for the current world
        private bool pitWorldReady;             // PitWorldBuilt was raised for the current world
        private bool destroying;                // OnDestroy started: no loading view is created

        private const int Window = 120;
        private readonly float[] frameMs = new float[Window];
        private int fi, ff;
        private float fpsAvg, fpsWorst, logTimer;

        // ------------------------------------------------------------------ Lifecycle

        private void Start()
        {
            previousSimMode = Physics.simulationMode;
            Physics.simulationMode = SimulationMode.Script; // PitAuthority calls Physics.Simulate itself
            Physics.autoSyncTransforms = false;
            Time.maximumDeltaTime = 0.1f; // cap catch-up steps (avoids the fixed-step death spiral)
            // Keep running (and sending snapshots) when the window isn't focused. Without this Unity throttles an
            // unfocused Play-mode/standalone window, so an unfocused host nearly stops simulating and the client
            // starves. Essential for same-PC two-instance testing; also correct for the shipped game.
            Application.runInBackground = true;

            // Camera FIRST: if a material shader is missing in a build, we must not end up cameraless.
            if (!SessionDriven) DevStartStandalone(); // vSync 0, standalone venue preview, dev camera (+ light without a venue)
            else RefreshLoadingView();                // session: the site's overview camera or a fallback loading camera
            if (HarnessActive) EnsureDevHud();

            ownedNear = ballVisuals == null || ballVisuals.nearMesh == null;
            ownedFar = ballVisuals == null || ballVisuals.farMesh == null;
            nearMesh = ownedNear ? BallMeshes.IcoSphere(nearLodSubdivisions, "BallNear") : ballVisuals.nearMesh;
            farMesh = ownedFar ? BallMeshes.IcoSphere(farLodSubdivisions, "BallFar") : ballVisuals.farMesh;

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
            litShader = lit;
            highlightMat = new Material(lit);
            var hi = new Color(1f, 1f, 0.2f);
            SetBaseColor(highlightMat, hi);
            highlightMat.EnableKeyword("_EMISSION");
            highlightMat.SetColor("_EmissionColor", hi * 1.5f);

            // Kit session: the adapter's AttachSession decides what to build (BallPitFinalController.Session.cs).
            if (SessionDriven) { startRan = true; BeginPendingSession(); return; }

            StartCoroutine(BuildRoutine(settings, PitWorld.ComputeBounds(settings), true)); // the standalone preview hook already ran
        }

        private IEnumerator BuildRoutine(PitSettings s, Bounds bounds, bool asAuthority, System.Action<bool, string> onBuilt = null)
        {
            DisposePit();                       // bumps buildEpoch and clears `building`
            int epoch = buildEpoch;             // captured after our own dispose
            IPitEnvironment env = Environment;  // the instance this build binds
            building = true; buildProgress = 0f; buildMessage = "";
            if (asAuthority) Time.fixedDeltaTime = 1f / s.physicsHz;
            var w = new PitWorld(s, bounds, withColliders: asAuthority || (clientRequest != null && clientRequest.ClientColliders));
            if (s.fillType == FillType.Random)
            {
                var task = RandomFill.LoadOrGenerateAsync(w, AnchoredCacheRoot(Application.streamingAssetsPath, s, w.PlayBounds),
                                                          AnchoredCacheRoot(Application.persistentDataPath, s, w.PlayBounds),
                                                          Application.isEditor, p => { if (epoch == buildEpoch) buildProgress = p; });
                while (!task.IsCompleted) yield return null;
                if (epoch != buildEpoch) // abandoned while loading: drop only our own world, touch no shared field
                {
                    if (task.IsFaulted) Debug.LogWarning("[BallPitFinal] abandoned fill failed: " + task.Exception?.GetBaseException().Message);
                    w.Dispose();
                    yield break;
                }
                if (task.IsFaulted)
                {
                    Debug.LogException(task.Exception);
                    w.Dispose(); building = false;
                    onBuilt?.Invoke(false, "random fill failed: " + task.Exception?.GetBaseException().Message);
                    yield break;
                }
                buildMessage = task.Result;
                Debug.Log("[BallPitFinal] random fill " + task.Result);
            }
            else w.FillFcc();
            w.FinishFill(env != null ? null : DevWallMaterial()); // a venue never renders walls
            world = w;

            Color[] palette;
            if (ballVisuals != null) palette = ballVisuals.BuildPalette(s.colorCount);
            else
            {
                palette = new Color[s.colorCount];
                for (int i = 0; i < palette.Length; i++) palette[i] = Color.HSVToRGB((float)i / palette.Length, 0.75f, 0.95f);
            }
            try
            {
                gpu = new GpuBallRenderer(w.Store.Capacity, Mathf.Max(256, s.maxActive), w.VisualRadius, w.PlayBounds, nearMesh, farMesh, palette);
            }
            catch (System.Exception e)
            {
                Debug.LogError("[BallPitFinal] renderer creation failed (likely too many balls for the GPU cull): " + e.Message);
                w.Dispose(); world = null; building = false; buildMessage = "renderer failed: " + e.Message;
                onBuilt?.Invoke(false, buildMessage);
                yield break;
            }
            if (ballVisuals != null) ballVisuals.ApplyToRuntimeMaterials();
            gpu.MarkAllDirty();
            w.OnRestingChanged = gpu.MarkDirty;
            EnsureDynamic(s.maxActive);

            if (env != null)
            {
                try { env.Build(w); builtEnvironment = env; } // teardown follows the instance actually built
                catch (System.Exception e)
                {
                    Debug.LogException(e);
                    env.Teardown();                         // idempotent: drops any partial proxies
                    gpu.Dispose(); gpu = null;
                    w.Dispose(); world = null; building = false;
                    onBuilt?.Invoke(false, "pit environment failed: " + e.Message);
                    yield break;
                }
            }
            else DevBuildLayout(w); // today's ground cube (it is the floor collider without a venue)

            if (asAuthority)
            {
                w.RegisterSceneEnvironment(); // ground + walls + any static level colliders -> cheap support pre-check
                authority = new PitAuthority(w);
            }
            else replica = new PitReplica(w, s.maxActive);

            DevFrameCamera(w);
            dynamicCount = 0;
            building = false;
            if (!asAuthority && netPit != null) netPit.AttachClientWorld(w, replica);
            LogStatus("build", fullCheck: true);
            if (asAuthority && onBuilt == null) MarkWorldReady(); // standalone builds; session hosts: OnHostBuilt; clients: Update
            onBuilt?.Invoke(true, null);
        }

        /// <summary>
        /// Today's cache root for a zero anchor without a bounds override; otherwise
        /// baseDir/Anchored/a_{x}_{y}_{z}_b{hash:x8}: the anchor in integer millimetres plus an FNV-1a hash of the play
        /// bounds in millimetres, so anchored, re-bounded and dev-scene fill caches can never be confused.
        /// </summary>
        public static string AnchoredCacheRoot(string baseDir, PitSettings s, Bounds playBounds)
        {
            if (!s.HasAnchor && !s.overridePlayBounds) return baseDir; // today's root, bit for bit
            uint h = 2166136261u;
            void Mix(float v)
            {
                int k = Mathf.RoundToInt(v * 1000f);
                for (int i = 0; i < 4; i++) { h ^= (uint)((k >> (8 * i)) & 0xFF); h *= 16777619u; }
            }
            Mix(playBounds.center.x); Mix(playBounds.center.y); Mix(playBounds.center.z);
            Mix(playBounds.size.x); Mix(playBounds.size.y); Mix(playBounds.size.z);
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            string folder = "a_" + Mathf.RoundToInt(s.pitAnchor.x * 1000f).ToString(inv) + "_" +
                            Mathf.RoundToInt(s.pitAnchor.y * 1000f).ToString(inv) + "_" +
                            Mathf.RoundToInt(s.pitAnchor.z * 1000f).ToString(inv) + "_b" + h.ToString("x8", inv);
            return System.IO.Path.Combine(baseDir, "Anchored", folder);
        }

        private void DisposePit()
        {
            buildEpoch++; building = false; // any running build is abandoned; it disposes only its own world
            if (pitWorldReady) { pitWorldReady = false; RaisePitWorldDisposed(world); }
            if (builtEnvironment != null) { builtEnvironment.Teardown(); builtEnvironment = null; }
            DevDisposeLayout();
            bench.Stop();
            authority?.Dispose(); authority = null;
            replica = null;
            gpu?.Dispose(); gpu = null;
            world?.Dispose(); world = null;
            dynamicCount = 0;
        }

        private void OnDestroy()
        {
            destroying = true;
            netPit?.Dispose();
            DisposePit();
            Physics.simulationMode = previousSimMode;
            if (ownedNear && nearMesh != null) Destroy(nearMesh);
            if (ownedFar && farMesh != null) Destroy(farMesh);
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

        private void EnsureDynamic(int n)
        {
            if (dynamicInstances.Length >= n) return;
            int cap = Mathf.NextPowerOfTwo(Mathf.Max(256, n));
            dynamicInstances = new BallInstance[cap];
            dynamicIds = new int[cap];
        }

        // Client build from the host's header (C8). The venue binding and the session client's request fields are applied
        // to the header settings first; NetPit creates PendingSettings fresh and never reads it again.
        private void StartClientBuild()
        {
            PitSettings ps = netPit.PendingSettings;
            prepareClientSettings?.Invoke(ps);
            if (clientRequest != null)
            {
                ps.playerLayer = clientRequest.PlayerLayer;
                if (clientRequest.ClientColliders) ps.maxColliders = Mathf.Max(256, clientRequest.ClientMaxColliders);
            }
            if (SessionDriven) AdoptHostStep(netPit.HostDt);
            StartCoroutine(BuildRoutine(ps, netPit.PendingBounds, false, SessionDriven ? OnClientBuilt : (System.Action<bool, string>)null));
        }

        // ------------------------------------------------------------------ Frame

        private void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            if (!building && authority != null)  // offline, dev host, session host
            {
                DevPreStep(dt);                  // harness only: standalone hand, hover tool source, V/G, bench.PreStep
                netPit?.HostPreStep();           // remote hands and tools, as today
                RunStepListeners(true, dt);      // gameplay: motor, interaction, collider sources
                authority.Step(dt);
                DevPostStep(dt);                 // bench.PostStep
                RunStepListeners(false, dt);
                return;
            }
            if (SessionDriven && mode == Mode.Client && clientPhysicsStepping) // session client: local physics only
            {
                RunStepListeners(true, dt);
                Physics.SyncTransforms();        // autoSyncTransforms is off
                Physics.Simulate(dt);
                RunStepListeners(false, dt);
            }
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            TickFps(dt);
            DevFrame(dt); // bench frame time, dev keys, orbit camera, hover (no-ops while the harness is off)

            if (mode == Mode.Client && netPit != null)
            {
                if (netPit.HeaderPending && !building && world == null && !clientBuildFailed) StartClientBuild();
                PushLocalView(); // the one NetPit.SetView call per frame
                netPit.ClientUpdate(dt);
                if (!pitWorldReady && world != null && !building && netPit.Synced) MarkWorldReady();
            }
            else if (mode == Mode.Host && netPit != null) netPit.HostUpdate(dt);
            if (HarnessActive) EnsureDevHud();

            logTimer += dt;
            if (autoLogInterval > 0f && logTimer >= autoLogInterval) { logTimer = 0f; LogStatus("auto", fullCheck: false); }
        }

        private void LateUpdate()
        {
            Camera rc = ResolveRenderCamera();
            if (building || world == null || gpu == null || rc == null) return;
            gpu.OcclusionEnabled = occlusionCulling;
            gpu.FlipOcclusionY = flipOcclusionY;
            if (ballVisuals != null)
            {
                gpu.LodPixelRadius = ballVisuals.lodPixelRadius;
                gpu.CastShadows = ballVisuals.castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
                gpu.ReceiveShadows = ballVisuals.receiveShadows;
            }
            else
            {
                gpu.LodPixelRadius = lodPixelRadius;
                gpu.CastShadows = ballsCastShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
                gpu.ReceiveShadows = ballsReceiveShadows;
            }
            // A session client shows no resting balls until it is synced (never the stale pre-sync fill).
            if (!(SessionDriven && mode == Mode.Client && !pitWorldReady)) gpu.Render(rc, world.Store);

            dynamicCount = 0;
            if (authority != null)
            {
                EnsureDynamic(authority.Active.Count);
                float alpha = Mathf.Clamp01((Time.time - Time.fixedTime) / Time.fixedDeltaTime);
                dynamicCount = authority.Active.FillInstances(dynamicInstances, dynamicIds, alpha, world.Store);
                DrawLocalHeldAtHoldPoint();
            }
            else if (replica != null && netPit != null && netPit.Synced)
            {
                EnsureDynamic(replica.Count);
                replica.LocalHand = SessionDriven ? localView.HoldPos : DevHandPos();
                dynamicCount = replica.FillInstances(dynamicInstances, dynamicIds, netPit.PlaybackTime);
            }
            gpu.DrawDynamic(rc, dynamicInstances, dynamicCount);

            DevDrawHover(rc); // standalone hover highlight; in a session the player's highlighter calls DrawHighlight
        }

        // ------------------------------------------------------------------ Logging

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
    }
}

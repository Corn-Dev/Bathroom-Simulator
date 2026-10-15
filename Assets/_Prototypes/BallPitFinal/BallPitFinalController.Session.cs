// Kit session attach for BallPitFinalController (new partial; the small edits inside the copied controller file are
// listed in HANDOFF.md). Design: 04 §7 items 1-4 and 6, 06 P2. BallPitSession.BallPitGameAdapter calls AttachSession
// from IGameAdapter.OnSessionLive with the kit-owned NetworkManager; nothing on this path creates a NetworkManager.
// Host: build (fill, or the save's fill) -> Restore -> NetPit.StartHost -> Ready. Client: NetPit.StartClient at once.
// Deliverable A (HANDOFF-A.md): IPitEnvironment and IPitSite, the request's venue and client fields, the stored site
// fields for both roles, the client build-failure path, HostToolFilter, and MarkWorldReady on the host.

using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace BallPitFinal
{
    /// <summary>
    /// The scene colliders a pit needs (venue design b 9.0). Every peer, inside BuildRoutine after FinishFill and the
    /// renderer, before RegisterSceneEnvironment and PitAuthority / PitReplica. An exception fails the build.
    /// </summary>
    public interface IPitEnvironment
    {
        /// <summary>Builds the scene colliders for this world. Throwing fails the build.</summary>
        void Build(PitWorld world);
        /// <summary>From DisposePit (start of every rebuild, detach, OnDestroy). Idempotent.</summary>
        void Teardown();
    }

    /// <summary>Identity of the place the pit is built in (a gameplay PitSite). Identity only.</summary>
    public interface IPitSite
    {
        string SiteId { get; }
        string ProfileKey { get; }
    }

    public sealed partial class BallPitFinalController
    {
        /// <summary>
        /// True while the MultiplayerKit adapter exists (its constructor sets it). Start then waits for AttachSession
        /// instead of building, and the dev-only keys (1-5, T, R, H, J) and the local-UTP path are disabled.
        /// </summary>
        public static bool SessionDriven;

        // Enter Play Mode without a domain reload keeps statics, so a gameplay scene opened standalone after a kit
        // session must get the dev path back.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { SessionDriven = false; }

        /// <summary>One session attach, filled in by the adapter.</summary>
        public sealed class PitSessionRequest
        {
            public NetworkManager Nm;                                   // the kit's NetworkManager; never null
            public bool IsHost;
            public PitSettings Settings;                                // host only: world-shaping fields already applied
            public Bounds Bounds;                                       // host only
            public System.Func<PitWorld, PitAuthority, string> Restore; // host only; null = new game; returns error or null
            public System.Action<NetPit> Ready;                         // host: after NetPit.StartHost; client: after NetPit.StartClient
            public System.Action<string> Failed;                        // build / restore / renderer failure (log detail)
            // Deliverable A. Failed is now also invoked when a session client's build fails.
            public IPitEnvironment Environment;                         // both peers; null = dev layout (ground cube)
            public System.Action<PitSettings> PrepareClientSettings;    // client; applied to the header settings before the build
            public IPitSite Site;                                       // both peers; the site the environment belongs to; null without one
            public Camera OverviewCamera;                               // both peers; the site's loading view; null = fallback camera
            public bool ClientColliders;                                // client builds its PitWorld withColliders; false = today
            public int ClientMaxColliders = 4096;                       // client PitSettings.maxColliders (clamped to >= 256)
            public int PlayerLayer = -1;                                // client PitSettings.playerLayer; -1 = none (today)
        }

        private PitSessionRequest pending; // host request; client requests are never stored here
        private bool sessionStarted;       // the single guard against running a request twice
        private bool startRan;             // Start reached its SessionDriven early return
        private string sessionHud = "";    // HUD line while no world exists (OnGUI)

        // Deliverable A session state (stored for both roles by AttachSession, cleared by DetachSession).
        private PitSessionRequest clientRequest;                 // the session client's request (C3); null on hosts and the dev J client
        private System.Action<PitSettings> prepareClientSettings; // client only
        private IPitSite activeSite;                              // ActiveSite
        private Camera overviewCamera;                            // the loading view's authored camera, or null
        private bool clientBuildFailed;                           // a session client's build failed: never retried
        private bool hostFailed;                                  // FailHost ran (LoadPhase Failed)
        private bool sessionDetached;                             // DetachSession ran: no loading view is created
        private readonly HashSet<int> toolFilterWarned = new HashSet<int>();

        // Read-only accessors for the adapter (save capture, watchdog, hopper ops).
        public PitWorld World => world;
        public PitAuthority Authority => authority;
        public NetPit Net => netPit;
        public bool IsBuilding => building;

        /// <summary>
        /// Host: stores the request and starts it now if Start already ran, else Start starts it. Client: starts
        /// NetPit.StartClient synchronously, because the host sends the header as soon as NGO reports this client
        /// connected. A null NetworkManager is reported through Failed; a second attach is ignored.
        /// Both roles store the environment, site and overview camera here; BeginHost only builds.
        /// </summary>
        public void AttachSession(PitSessionRequest r)
        {
            if (r == null) { Debug.LogError("[BallPitFinal] AttachSession(null) ignored"); return; }
            if (r.Nm == null || (r.IsHost && r.Settings == null))
            {
                string why = r.Nm == null ? "no NetworkManager" : "host request without settings";
                Debug.LogError("[BallPitFinal] AttachSession rejected: " + why);
                r.Failed?.Invoke(why);
                return;
            }
            if (sessionStarted || pending != null)
            {
                Debug.LogError("[BallPitFinal] AttachSession ignored: a session is already attached");
                return;
            }

            sessionDetached = false;
            hostFailed = false;
            clientBuildFailed = false;
            ResetDevToolMarks();
            toolFilterWarned.Clear();
            Environment = r.Environment;
            activeSite = r.Site;
            overviewCamera = r.OverviewCamera;
            prepareClientSettings = r.IsHost ? null : r.PrepareClientSettings;

            if (r.IsHost)
            {
                pending = r;
                RefreshLoadingView();                 // the site's camera replaces the fallback (framed on the request)
                if (startRan) BeginPendingSession(); // else Start calls it
                return;
            }

            // Client. The existing Update path (HeaderPending -> BuildRoutine -> AttachClientWorld) takes over after Start.
            sessionStarted = true;
            DisposePit();
            netPit = NetPit.StartClient(r.Nm, network);
            mode = Mode.Client;
            clientRequest = r;
            sessionHud = "";
            RefreshLoadingView();
            Debug.Log("[BallPitFinal] session client attached, waiting for the host's header");
            r.Ready?.Invoke(netPit);
        }

        /// <summary>Starts a pending host request exactly once; otherwise shows the waiting line.</summary>
        private void BeginPendingSession()
        {
            if (pending != null && pending.IsHost && !sessionStarted) { sessionStarted = true; BeginHost(pending); }
            else if (!sessionStarted) sessionHud = "waiting for session";
        }

        private void BeginHost(PitSessionRequest r)
        {
            settings = r.Settings; // HUD and tools use the session values
            sessionHud = "";
            StartCoroutine(BuildRoutine(r.Settings, r.Bounds, true, (ok, msg) => OnHostBuilt(r, ok, msg)));
        }

        // BuildRoutine's onBuilt for a host request. Restore runs before NetPit.StartHost, while authority.Events is
        // still null, so restored state emits no events; joining clients get it through NetPit's join sync.
        private void OnHostBuilt(PitSessionRequest r, bool ok, string msg)
        {
            if (!ReferenceEquals(pending, r)) return; // DetachSession ran while building: that session is gone
            if (!ok) { FailHost(r, msg ?? "build failed"); return; }
            string err = r.Restore?.Invoke(world, authority);
            if (err != null) { FailHost(r, err); return; }
            netPit = NetPit.StartHost(r.Nm, network, authority);
            mode = Mode.Host;
            Debug.Log($"[BallPitFinal] session host started ({world.Store.Count:n0} balls)");
            authority.ToolFilter = HostToolFilter; // clients' dev tools act only while the host's are on
            MarkWorldReady();                      // PitWorldBuilt: after Restore and NetPit.StartHost, before Ready
            r.Ready?.Invoke(netPit);
        }

        private void FailHost(PitSessionRequest r, string detail)
        {
            hostFailed = true;
            sessionHud = "session failed: " + detail; // the adapter logs the error and ends the session
            r.Failed?.Invoke(detail);
        }

        // BuildRoutine's onBuilt for a session client build. A failure is never retried (clientBuildFailed); the
        // adapter's Failed records it and its watchdog leaves the session on the next frame.
        private void OnClientBuilt(bool ok, string msg)
        {
            if (ok) return;
            clientBuildFailed = true;
            string detail = msg ?? "build failed";
            sessionHud = "session failed: " + detail;
            Debug.LogError("[BallPitFinal] session client world build failed: " + detail);
            clientRequest?.Failed?.Invoke(detail);
        }

        /// <summary>
        /// PitAuthority.ToolFilter on session hosts. In A every session-time Excavate/Dump comes from a dev tool (the
        /// host's V/G/U, the bench, or a client's through NetPit), so they act only while the host's dev tools are on,
        /// and every allowed use marks the session. B's real tools wrap or replace this filter.
        /// </summary>
        private bool HostToolFilter(int holder)
        {
            if (!SessionDevToolsActive)
            {
                if (toolFilterWarned.Add(holder))
                    Debug.LogWarning($"[BallPitFinal] dev tool ignored (holder {holder}): the host's dev tools are off");
                return false;
            }
            MarkDevToolUsed(holder);
            return true;
        }

        /// <summary>
        /// Adapter, from OnSessionEnding, while the NetworkManager is still valid: unregisters NetPit's handlers and
        /// NGO callbacks, drops the pit and returns to the idle (offline) state.
        /// </summary>
        public void DetachSession()
        {
            sessionDetached = true; // first: no loading view may be created while the session tears down
            netPit?.Dispose(); netPit = null;
            DisposePit();
            mode = Mode.Offline;
            pending = null;
            sessionStarted = false;
            sessionHud = "";
            Environment = null;
            activeSite = null;
            overviewCamera = null;
            clientRequest = null;
            prepareClientSettings = null;
            clientBuildFailed = false;
            hostFailed = false;
            ResetDevToolMarks();
            toolFilterWarned.Clear();
        }
    }
}

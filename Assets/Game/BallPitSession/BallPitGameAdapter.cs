// The ball-pit IGameAdapter: identity, scenes, settings schema, capabilities, and the session hooks that attach
// BallPitFinalController to the kit-owned NetworkManager (decision 10; this adapter never creates one). Host: build or
// restore the pit, NetPit.StartHost, then report ready through IGameReadyGate. Client: NetPit.StartClient inside
// OnSessionLive plus a sync watchdog. Saves: capture, and menu-time validation with a one-entry DTO cache.
// Design: 04 §5.5, §6.1-6.3, §7, §8, §11; 02 §2e step 5, §4 step 13; 06 G1.
// Deliverable A (HANDOFF-A.md): the VenueConfig sizes, the PitSite selection and binding (host and client), the
// PlayerSpawner (validated and begun in OnSessionLive, spawns in OnPlayerJoined, despawns in OnPlayerLeft), the
// players' layer, and the client's site and build-failure watchdog exits.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using BallPitFinal;
using BallPitGameplay;
using MultiplayerKit;
using MultiplayerKit.Ngo;
using MultiplayerKit.UI;
using Unity.Netcode;
using UnityEngine;

namespace BallPitSession
{
    public sealed class BallPitGameAdapter : IGameAdapter, IGameReadyGate, IWorldSaveValidator
    {
        public const string Id = "drain_ball_pit";

        /// <summary>Client: the world must be synced this long after its local build completed (04 §8).</summary>
        public const float ClientSyncSeconds = 120f;

        /// <summary>FILL ball-count ceiling of this build (NetPit encodes ball ids in 3 bytes).</summary>
        public const int MaxBallCount = 16777215;

        private const string SyncFailedToast = "Couldn't sync the game world.";

        private readonly MonoBehaviour coroutineHost; // BallPitKitInstaller (DontDestroyOnLoad): runs the watchdog
        private readonly string gameVersion;
        private readonly HopperOwnership ownership = new HopperOwnership();
        private readonly List<IBallPitSaveParticipant> participants = new List<IBallPitSaveParticipant>();
        private BallPitGameState gameState = new BallPitGameState();

        // One session, OnSessionLive .. OnSessionEnding.
        private BallPitFinalController controller;
        private NetworkManager nm;
        private ISessionNetwork network;
        private PlayerId localPlayer;
        private bool isHost, hostReady, restoreFailed;
        private PitFillParams? fillAtBuild; // FILL as built; the inspector can still edit controller.settings (04 §4.1)
        private string failure;
        private PitSlotMirror slotMirror;
        private Coroutine watchdog;
        // Deliverable A, one session (cleared in ResetSession).
        private PlayerSpawner spawner;
        private string clientSiteError;    // client: no usable PitSite; the watchdog leaves one frame later
        private string clientBuildFailure; // client: the world build failed; the watchdog leaves on its next frame

        // The save decoded by ValidateWorldSave, reused by the host's OnSessionLive (04 §6.1 step 2).
        private string cachedKey;
        private BallPitWorldSave cachedSave;

        /// <summary>venue: the pit sizes; null (the installer's three-argument call) resolves Resources/BallPitVenueConfig,
        /// else the built-in five sizes.</summary>
        public BallPitGameAdapter(MonoBehaviour coroutineHost, string gameplaySceneName, string menuSceneName, VenueConfig venue = null)
        {
            this.coroutineHost = coroutineHost;
            GameplaySceneName = string.IsNullOrEmpty(gameplaySceneName) ? "BallPitGame" : gameplaySceneName;
            MenuSceneName = string.IsNullOrEmpty(menuSceneName) ? "MainMenu" : menuSceneName;
            string v = Application.version ?? "";
            gameVersion = v.Length > 32 ? v.Substring(0, 32) : v;
            VenueConfig = VenueConfig.Resolve(venue);
            SiteSelector = new DefaultPitSiteSelector(BallPitSettingsMap.PitSize);
            BallPitFinalController.SessionDriven = true; // the gameplay scene now waits for AttachSession (04 §7 item 2)
        }

        /// <summary>The runtime source of truth for the pit sizes on host and client (venue design b 3.2).</summary>
        public VenueConfig VenueConfig { get; }

        /// <summary>Picks the active PitSite (default: the site whose profileKey matches the lobby pitSize). A selector
        /// whose answer is not a function of the settings must replicate the siteId to clients (DECISIONS.md).</summary>
        public IPitSiteSelector SiteSelector { get; set; }

        /// <summary>This session's site collision proxies, or null in a site-less scene.</summary>
        public PitSiteEnvironment ActiveEnvironment { get; private set; }

        // ------------------------------------------------------------------ Identity, scenes, schema (04 §8)

        public string GameId => Id;
        public string GameVersion => gameVersion;
        public string GameplaySceneName { get; }
        public string MenuSceneName { get; }
        public int MaxPlayersCap => PitAuthority.MaxPlayers;

        /// <summary>Public lobbies stay available; the default privacy is FriendsOnly (Core default).</summary>
        public GameCapabilities Capabilities =>
            GameCapabilities.WorldSaves | GameCapabilities.JoinInProgress | GameCapabilities.PublicLobbies;

        public IReadOnlyList<SettingDefinition> BuildSettingsSchema() => BallPitSettingsMap.BuildDefinitions(VenueConfig);
        public IReadOnlyCollection<string> WorldShapingKeys => BallPitSettingsMap.WorldShapingKeys;

        /// <summary>Every combination is valid: the sizes are the VenueConfig's sanitized profiles, and the kit normalizes a
        /// Choice value to one of the schema's choices.</summary>
        public string ValidateSettings(IReadOnlyHostSettings settings) => null;

        /// <summary>Gameplay placeholders (money, upgrades, containers); future systems read and write it (04 §4.4).</summary>
        public BallPitGameState GameState => gameState;

        // ------------------------------------------------------------------ IGameReadyGate (04 §9.3)

        public bool IsHostWorldReady => hostReady;
        public string FailureMessage => failure;

        // ------------------------------------------------------------------ Session lifecycle

        /// <summary>
        /// Every peer, after NGO scene sync. Host: hands the build/restore to the controller and returns at once
        /// (IsHostWorldReady turns true later). Client: NetPit.StartClient runs before this returns.
        /// </summary>
        public void OnSessionLive(SessionLiveContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            var manager = context.NetworkHandle as NetworkManager;
            if (manager == null) throw new InvalidOperationException("SessionLiveContext.NetworkHandle is not a NetworkManager");
            BallPitFinalController found = UnityEngine.Object.FindObjectOfType<BallPitFinalController>();
            if (found == null) throw new InvalidOperationException("no BallPitFinalController in scene '" + GameplaySceneName + "'");
            // Deliverable A: the scene's PlayerSpawner, checked on every peer before anything starts (setup bugs throw).
            PlayerSpawner foundSpawner = PlayerSpawner.Current != null ? PlayerSpawner.Current : UnityEngine.Object.FindObjectOfType<PlayerSpawner>();
            if (foundSpawner == null) throw new InvalidOperationException("no PlayerSpawner in scene '" + GameplaySceneName + "'");
            foundSpawner.ValidateSetup(manager);
            var layerErrors = new List<string>();
            var layerWarnings = new List<string>();
            PlayerLayers.Validate(found.settings.ballLayer, layerErrors, layerWarnings); // log only: degraded but playable
            foreach (string e in layerErrors) Debug.LogError("[BallPitSession] " + e);
            foreach (string w in layerWarnings) Debug.LogWarning("[BallPitSession] " + w);

            ResetSession();
            controller = found;
            spawner = foundSpawner;
            nm = manager;
            network = context.Network;
            localPlayer = context.LocalPlayer;
            isHost = context.IsHost;
            try
            {
                // Before the attach: on the FCC path the whole host build, PitWorldBuilt and Ready can run inside AttachHost.
                spawner.BeginSession(manager, controller, isHost, localPlayer);
                if (isHost) AttachHost(context); else AttachClient(context);
            }
            catch
            {
                spawner.EndSession(); // after a throw the kit owes no OnSessionEnding
                throw;
            }
        }

        // 04 §6.2 steps 6-7, with the venue site (deliverable A, venue design b 9.1). The controller builds over several
        // frames, then calls Ready or Failed. A site or resume-agreement failure sets failure and returns before the attach.
        private void AttachHost(SessionLiveContext context)
        {
            ownership.Clear();
            ownership.SetHost(localPlayer);
            gameState = new BallPitGameState();

            BallPitWorldSave save = context.ResumeSave != null ? ResolveSave(context.ResumeSave) : null;
            bool resume = save != null;
            // b 9.1 steps 1, 2 and 6: find, select (the save's siteId on resume) and activate. Null without an error: a
            // site-less scene, which runs today's path.
            PitSite site = PitSites.SelectAndActivate(SiteSelector, context.Settings, resume ? save.Fill.SiteId : null,
                                                      out _, out string siteError);
            if (site == null && siteError != null) { FailHostSetup(resume, siteError); return; }
            PitSizeProfile profile = null;
            if (site != null && !VenueConfig.TryGetProfile(site.ProfileKey, out profile))
            { FailHostSetup(resume, $"site '{site.SiteId}' names profile '{site.ProfileKey}', which VenueConfig does not define"); return; }
            if (site == null && resume && (HasAnchor(save.Fill.PitAnchor) || save.Fill.SiteId != null))
            {
                FailHostSetup(true, $"save was made at site '{save.Fill.SiteId ?? "(none)"}' (anchor {Fmt(save.Fill.PitAnchor)}) " +
                                    "but this gameplay scene has no PitSite");
                return;
            }

            PitSettings settings;
            Bounds bounds;
            Func<PitWorld, PitAuthority, string> restore = null;
            if (resume)
            {
                settings = save.Fill.ApplyTo(controller.settings);
                string mismatch = site != null ? ResumeSiteMismatch(save, site, profile) : null; // b 9.1 step 4
                if (mismatch != null) { FailHostSetup(true, mismatch); return; }
            }
            else settings = BallPitSettingsMap.Apply(context.Settings, controller.settings, VenueConfig);
            // b 9.1 step 5: the anchor and the venue bounds (a new game also takes the profile's size and ball diameter).
            if (site != null && !site.TryApplyTo(settings, resume ? null : profile, out string applyError))
            { FailHostSetup(resume, applyError); return; }

            if (resume)
            {
                bounds = save.Fill.Bounds; // the exact bounds the saved Q21 poses were encoded against
                Bounds computed = PitWorld.ComputeBounds(settings);
                if (computed.center != bounds.center || computed.size != bounds.size)
                    Debug.LogWarning($"[BallPitSession] saved play bounds {bounds} differ from this build's {computed}; using the saved bounds");
                restore = (w, a) => RunRestore(save, w, a);
                Debug.Log($"[BallPitSession] host resuming save '{context.ResumeSave.Info.SlotId}' ({save.Fill.BallCount:n0} balls)");
            }
            else
            {
                bounds = PitWorld.ComputeBounds(settings);
                Debug.Log($"[BallPitSession] host new game {settings.pitX}x{settings.pitY}x{settings.pitZ} {settings.fillType} seed {settings.seed}");
            }

            PitSiteEnvironment env = site != null ? new PitSiteEnvironment(site) : null; // the site's invisible collision proxies
            ActiveEnvironment = env;
            settings.playerLayer = PlayerLayers.Player; // every host path (site-less too): players never support balls (d.2)
            controller.AttachSession(new BallPitFinalController.PitSessionRequest
            {
                Nm = nm,
                IsHost = true,
                Settings = settings,
                Bounds = bounds,
                Restore = restore,
                Ready = OnHostPitReady,
                Failed = OnHostPitFailed,
                Environment = env,
                Site = site,
                OverviewCamera = site != null ? site.OverviewCamera : null,
            });
        }

        // b 9.1 step 4: the save's pit must be the site's profile at the site's anchor, exactly (the fill, and every saved
        // ball id, exists only there). Null when they agree, else the Error text.
        private static string ResumeSiteMismatch(BallPitWorldSave save, PitSite site, PitSizeProfile profile)
        {
            PitFillParams f = save.Fill;
            if (f.PitX != profile.pitX || f.PitY != profile.pitY || f.PitZ != profile.pitZ || f.BallDiameter != profile.ballDiameter)
                return $"profile '{profile.key}' is {profile.pitX}x{profile.pitY}x{profile.pitZ} / {Fmt(profile.ballDiameter)} m but the save's pit " +
                       $"is {f.PitX}x{f.PitY}x{f.PitZ} / {Fmt(f.BallDiameter)} m: the profile changed since this save";
            if (site.Anchor == null) return null; // TryApplyTo fails next with "anchor not assigned"
            Vector3 at = site.AnchorPoint;
            if (at.x == f.PitAnchor.x && at.y == f.PitAnchor.y && at.z == f.PitAnchor.z) return null;
            return $"site '{site.SiteId}' is at {Fmt(at)} but the save's pit is at {Fmt(f.PitAnchor)}: the site moved since this save" +
                   (save.Version < 2 ? " (this save predates venues)" : "");
        }

        private void FailHostSetup(bool resume, string detail)
        {
            Debug.LogError("[BallPitSession] host session failed: " + detail);
            failure = resume ? SessionErrorMessages.SaveRestoreFailed : SessionErrorMessages.GameFailedToStart;
        }

        private static bool HasAnchor(Vector3 a) => a.x != 0f || a.y != 0f || a.z != 0f;

        private static string Fmt(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);

        private static string Fmt(Vector3 v) => "(" + Fmt(v.x) + ", " + Fmt(v.y) + ", " + Fmt(v.z) + ")";

        // 04 §6.3. Runs inside the controller's build callback, before NetPit.StartHost.
        private string RunRestore(BallPitWorldSave save, PitWorld w, PitAuthority a)
        {
            string err;
            try
            {
                err = BallPitWorldRestorer.Apply(save, w, a, ownership, gameState, localPlayer, participants);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                err = e.GetType().Name + ": " + e.Message;
            }
            if (err != null) restoreFailed = true;
            return err;
        }

        // 04 §6.2 step 8: NetPit is listening, so clients may connect once Core publishes inGame.
        private void OnHostPitReady(NetPit net)
        {
            if (!isHost || controller == null || controller.Authority == null) return; // session already ended
            slotMirror = new PitSlotMirror(ownership, new PitAuthorityHopperOps(controller.Authority), network, nm.LocalClientId);
            slotMirror.Attach(nm);
            PitFillParams fill = PitFillParams.FromWorld(controller.World);
            fill.SiteId = ActiveEnvironment != null ? ActiveEnvironment.Site.SiteId : null; // FILL v2 (b 9.1 step 8)
            fillAtBuild = fill;
            // The host's player spawned inside PitWorldBuilt, which ran just before this; without it the session ends.
            if (spawner == null || !spawner.HostPlayerSpawned)
            {
                Debug.LogError("[BallPitSession] the host's player object could not be spawned; ending the session");
                failure = SessionErrorMessages.GameFailedToStart; // hostReady stays false
                return;
            }
            hostReady = true;
            Debug.Log($"[BallPitSession] host world ready ({controller.World.Store.Count:n0} balls, {controller.Authority.ParkedHopperCount:n0} parked hopper balls)");
        }

        // A failure leaves a half-built or half-restored world, so it ends the session; the slot is not overwritten
        // because the session never reached InGame (04 §6.3).
        private void OnHostPitFailed(string detail)
        {
            if (!isHost || controller == null) return;
            Debug.LogError("[BallPitSession] " + (restoreFailed ? "restore failed: " : "host world build failed: ") + detail);
            failure = restoreFailed ? SessionErrorMessages.SaveRestoreFailed : SessionErrorMessages.GameFailedToStart;
        }

        // 04 §6.2 step 10 (02 §4 step 13): NetPit.StartClient runs inside this call, so BP.Hdr/BP.Rel/BP.Snap are
        // registered in the frame this client reports SynchronizeComplete, before the host can send its header.
        // Deliverable A (venue design b 9.2): the same site selection as the host (same lobby settings, no save), the
        // site's anchor applied to the header's settings, the client collider shell and the players' layer.
        private void AttachClient(SessionLiveContext context)
        {
            PitSite site = PitSites.SelectAndActivate(SiteSelector, context.Settings, null, out _, out string siteError);
            if (site == null && siteError != null)
            {
                clientSiteError = siteError; // the watchdog leaves one frame later: leaving inside OnSessionLive re-enters the kit
                Debug.LogError("[BallPitSession] client: " + siteError);
                StartWatchdog();
                return;
            }
            PitSizeProfile profile = null;
            if (site != null && !VenueConfig.TryGetProfile(site.ProfileKey, out profile))
                Debug.LogError($"[BallPitSession] client: site '{site.SiteId}' names profile '{site.ProfileKey}', which VenueConfig does not define");
            PitSiteEnvironment env = site != null ? new PitSiteEnvironment(site) : null;
            ActiveEnvironment = env;
            controller.AttachSession(new BallPitFinalController.PitSessionRequest
            {
                Nm = nm,
                IsHost = false,
                Ready = n => Debug.Log("[BallPitSession] client attached, waiting for the host's world"),
                Failed = d => { Debug.LogError("[BallPitSession] client world build failed: " + d); clientBuildFailure = d; },
                Environment = env,
                PrepareClientSettings = site != null ? (Action<PitSettings>)(s => ApplyClientSite(site, profile, s)) : null,
                Site = site,
                OverviewCamera = site != null ? site.OverviewCamera : null,
                ClientColliders = true, // section d, d.5: the local player stands on the pile
                ClientMaxColliders = spawner.Physics.clientShellMaxColliders,
                PlayerLayer = PlayerLayers.Player,
            });
            StartWatchdog();
        }

        // Client, inside StartClientBuild: the site's anchor and bounds on the header's settings (NetPit's header carries no
        // anchor, and NetPit is unchanged). A wrong anchor shows up as a fill checksum mismatch, which NetPit's full resync
        // already covers.
        private static void ApplyClientSite(PitSite site, PitSizeProfile profile, PitSettings s)
        {
            if (!site.TryApplyTo(s, null, out string err))
                Debug.LogError("[BallPitSession] client: " + err + "; building at a zero anchor (the host's full resync delivers the balls)");
            if (profile != null && (s.pitX != profile.pitX || s.pitY != profile.pitY || s.pitZ != profile.pitZ || s.ballDiameter != profile.ballDiameter))
                Debug.LogError($"[BallPitSession] client: the host's pit is {s.pitX}x{s.pitY}x{s.pitZ} / {Fmt(s.ballDiameter)} m, but site " +
                               $"'{site.SiteId}' (profile '{profile.key}') is {profile.pitX}x{profile.pitY}x{profile.pitZ} / {Fmt(profile.ballDiameter)} m: " +
                               "the host is at another site (a selector not derived from the settings must replicate the siteId)");
        }

        // Section d (d.10, d.11). The kit's NGO connect callback runs before NetPit's and the mirror's (they subscribe after
        // the world builds), so the mirror's slot rule, which is NetPit's, is applied early: HandleClientConnected is
        // idempotent, and the mirror's own callback then returns at its "already tracked" check.
        public void OnPlayerJoined(PlayerId player, ulong clientId)
        {
            if (!isHost || !hostReady || slotMirror == null || spawner == null)
            { Debug.LogError($"[BallPitSession] player {player} (client {clientId}) joined before the world was ready"); return; }
            slotMirror.HandleClientConnected(clientId);
            if (!slotMirror.TryGetSlot(clientId, out int slot))
            { Debug.LogWarning($"[BallPitSession] no free pit slot for client {clientId}; NetPit disconnects it"); return; }
            if (spawner.SpawnClientPlayer(clientId, player, slot)) return;
            Debug.LogError($"[BallPitSession] could not spawn player {player} (client {clientId}); disconnecting it");
            network?.DisconnectClient(player, KitError.Unknown);
        }

        public void OnPlayerLeft(PlayerId player, ulong clientId)
        {
            Debug.Log($"[BallPitSession] player {player} left (client {clientId})");
            if (spawner != null) spawner.DespawnPlayer(clientId); // idempotent: NGO also destroys the leaver's player object
        }

        public void OnSettingsChanged(IReadOnlyHostSettings settings, string key) { } // no in-game settings yet

        /// <summary>Every peer, before NGO shutdown, while the NetworkManager is still valid.</summary>
        public void OnSessionEnding(SessionEndReason reason)
        {
            Debug.Log($"[BallPitSession] session ending ({reason})");
            StopWatchdog();
            if (slotMirror != null) { slotMirror.Detach(); slotMirror = null; }
            if (spawner != null) spawner.EndSession(); // before DetachSession: the drivers are disposed while the world exists
            if (controller != null) controller.DetachSession();
            ownership.Clear();
            gameState = new BallPitGameState();
            cachedKey = null;
            cachedSave = null;
            ResetSession();
        }

        private void ResetSession()
        {
            controller = null;
            nm = null;
            network = null;
            localPlayer = PlayerId.None;
            isHost = hostReady = restoreFailed = false;
            fillAtBuild = null;
            failure = null;
            spawner = null;
            ActiveEnvironment = null;
            clientSiteError = null;
            clientBuildFailure = null;
        }

        // ------------------------------------------------------------------ Client sync watchdog (04 §8)

        private void StartWatchdog()
        {
            if (coroutineHost == null) { Debug.LogWarning("[BallPitSession] no coroutine host; client sync watchdog is off"); return; }
            watchdog = coroutineHost.StartCoroutine(ClientWatchdog(controller));
        }

        private void StopWatchdog()
        {
            if (watchdog != null && coroutineHost != null) coroutineHost.StopCoroutine(watchdog);
            watchdog = null;
        }

        // Two deadlines, unscaled time: ClientSyncSeconds after the local build completed (a first-time Random
        // Generate is not charged), and WorldReadySeconds + 60 after OnSessionLive (stuck before or during the build).
        private IEnumerator ClientWatchdog(BallPitFinalController c)
        {
            float start = Time.unscaledTime;
            float cap = start + KitTimeouts.WorldReadySeconds + 60f;
            float builtAt = -1f;
            yield return null; // StartCoroutine runs to the first yield inside OnSessionLive; leaving there would re-enter the kit
            if (clientSiteError != null)
            {
                watchdog = null; // Leave() runs OnSessionEnding, which must not stop this coroutine mid-step
                AbandonClientSync(clientSiteError);
                yield break;
            }
            while (c != null && ReferenceEquals(c, controller))
            {
                if (clientBuildFailure != null) // the controller never retries a failed client build
                {
                    watchdog = null;
                    AbandonClientSync("world build failed: " + clientBuildFailure);
                    yield break;
                }
                NetPit net = c.Net;
                if (net != null && net.Synced)
                {
                    Debug.Log($"[BallPitSession] client synced {Time.unscaledTime - start:0.0}s after going live");
                    watchdog = null;
                    yield break;
                }
                float now = Time.unscaledTime;
                if (builtAt < 0f && c.World != null && !c.IsBuilding) builtAt = now;
                bool late = builtAt >= 0f && now - builtAt > ClientSyncSeconds;
                if (late || now > cap)
                {
                    watchdog = null; // Leave() runs OnSessionEnding, which must not stop this coroutine mid-step
                    AbandonClientSync(late ? $"not synced {ClientSyncSeconds:0}s after the build" : "no synced world before the deadline");
                    yield break;
                }
                yield return null;
            }
            watchdog = null;
        }

        private static void AbandonClientSync(string why)
        {
            Debug.LogError("[BallPitSession] client world not synced (" + why + "); leaving the session");
            MultiplayerKitBootstrap bootstrap = MultiplayerKitBootstrap.Instance;
            SessionController session = bootstrap != null ? bootstrap.Controller : null;
            if (session != null) session.Leave();
            KitOverlay overlay = KitOverlay.Instance;
            if (overlay != null) overlay.ShowToast(ToastSeverity.Error, SyncFailedToast);
        }

        // ------------------------------------------------------------------ Saves (04 §4.2, §6.1, §8)

        /// <summary>
        /// Host in game: the encoded payload, or null while the world is not ready. Exceptions reach Core's catch
        /// (logged at Error, treated as null).
        /// </summary>
        public byte[] CaptureWorldSave()
        {
            if (!isHost || !hostReady || controller == null || controller.IsBuilding) return null;
            PitWorld w = controller.World;
            PitAuthority a = controller.Authority;
            if (w == null || a == null) return null;
            BallPitWorldSave save = BallPitSaveWriter.Capture(w, a, ownership, gameState, localPlayer, participants, fillAtBuild);
            return BallPitSaveWriter.Encode(save);
        }

        /// <summary>
        /// Menu, before any lobby exists: full decode (every 04 §3.2 rule) plus this build's ball-count ceiling. The
        /// decoded save is cached so OnSessionLive does not decode it again. A snapshot that disagrees with FILL is
        /// logged at Warning; FILL wins.
        /// </summary>
        public KitResult ValidateWorldSave(WorldSaveData data)
        {
            if (data == null) return KitResult.Fail(KitError.SaveCorrupt, "no save data");
            KitResult<BallPitWorldSave> decoded = BallPitSaveReader.TryDecode(data.Payload);
            if (!decoded.Ok) return KitResult.Fail(decoded.Error, decoded.Detail);
            BallPitWorldSave save = decoded.Value;
            if (save == null) return KitResult.Fail(KitError.SaveCorrupt, "decoder returned no save");
            if (save.Fill.BallCount < 1 || save.Fill.BallCount > MaxBallCount)
                return KitResult.Fail(KitError.SaveCorrupt, "FILL ball count " + save.Fill.BallCount + " is outside 1.." + MaxBallCount);

            string mismatch = BallPitSettingsMap.DescribeSnapshotMismatch(data.Info.SettingsSnapshot, save.Fill, VenueConfig);
            if (mismatch != null)
                Debug.LogWarning($"[BallPitSession] save '{data.Info.SlotId}': settings snapshot disagrees with its FILL section ({mismatch}); FILL wins");

            cachedKey = CacheKey(data);
            cachedSave = save;
            return KitResult.Success;
        }

        // Cached DTO when the key matches, else a fresh decode. Failure throws: Core ends the session (01 §4.8).
        private BallPitWorldSave ResolveSave(WorldSaveData data)
        {
            string key = CacheKey(data);
            if (cachedSave != null && cachedKey == key) return cachedSave;
            KitResult<BallPitWorldSave> decoded = BallPitSaveReader.TryDecode(data.Payload);
            if (!decoded.Ok || decoded.Value == null)
                throw new InvalidOperationException("the resume save could not be decoded: " + decoded);
            cachedKey = key;
            cachedSave = decoded.Value;
            return cachedSave;
        }

        // SlotId + UpdatedUtc.Ticks + Payload.Length (all exposed by WorldSaveData; 04 §6.1 step 2).
        private static string CacheKey(WorldSaveData data) =>
            data.Info.SlotId + "|" + data.Info.UpdatedUtc.Ticks.ToString(CultureInfo.InvariantCulture) + "|" +
            data.Payload.Length.ToString(CultureInfo.InvariantCulture);

        // ------------------------------------------------------------------ Save participants and tanks (04 §4.4, §5.5)

        /// <summary>A future system's save blob (shop, containers, quests). Tag ^[a-z0-9_.]{1,32}$, unique.</summary>
        public void RegisterSaveParticipant(IBallPitSaveParticipant participant)
        {
            if (participant == null) throw new ArgumentNullException(nameof(participant));
            if (participants.Contains(participant)) return;
            string tag = participant.Tag;
            if (!IsValidParticipantTag(tag))
                throw new ArgumentException("Save participant tag must match ^[a-z0-9_.]{1,32}$: '" + tag + "'", nameof(participant));
            for (int i = 0; i < participants.Count; i++)
                if (participants[i].Tag == tag) throw new ArgumentException("Duplicate save participant tag '" + tag + "'", nameof(participant));
            participants.Add(participant);
        }

        public bool UnregisterSaveParticipant(IBallPitSaveParticipant participant) =>
            participant != null && participants.Remove(participant);

        /// <summary>Host: hands an absent player's parked tank to the host's usable tank. Returns the balls moved.</summary>
        public int ReleaseParkedTank(PlayerId player)
        {
            PitAuthority a = ReadyHostAuthority();
            return a == null ? 0 : ownership.Release(player, new PitAuthorityHopperOps(a));
        }

        /// <summary>Host: hands every parked tank to the host's usable tank. Returns the balls moved.</summary>
        public int ReleaseAllParkedTanks()
        {
            PitAuthority a = ReadyHostAuthority();
            return a == null ? 0 : ownership.ReleaseAll(new PitAuthorityHopperOps(a));
        }

        private PitAuthority ReadyHostAuthority() => isHost && hostReady && controller != null ? controller.Authority : null;

        private static bool IsValidParticipantTag(string tag)
        {
            if (string.IsNullOrEmpty(tag) || tag.Length > 32) return false;
            for (int i = 0; i < tag.Length; i++)
            {
                char c = tag[i];
                if (!((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_' || c == '.')) return false;
            }
            return true;
        }
    }
}

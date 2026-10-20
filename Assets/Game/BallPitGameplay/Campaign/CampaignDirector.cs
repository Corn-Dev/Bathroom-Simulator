// CampaignDirector: the campaign orchestrator (Deliverable D; DESIGN-D, plan item 9). A plain scene MonoBehaviour (NOT a
// NetworkObject). It owns the one live pit INSTANCE (a prefab: pit art + PitSite), picks which entry is live, and on a drain
// advances the run in place: transition -> destroy the old pit instance -> instantiate the next -> BallPitFinalController
// .RebuildActivePit. No scene load, no session detach/attach, no NetPit restart.
//
// Hooks into existing systems (all null-safe, so a scene with no director behaves exactly as before):
//   - adapter.SiteSelector     -> CampaignPitSiteSelector (returns the live instance's PitSite)
//   - adapter AttachHost/Client -> EnsureFirstPit (instantiates the first pit BEFORE PitSites.SelectAndActivate)
//   - adapter AttachHost       -> ApplyEntryColors (new game only, after BallPitSettingsMap.Apply)
//   - GameplaySession          -> OnActivePitDrained when a pit drains and more pits remain (else the unchanged win path)
// Multiplayer: the host replicates the entry index through CampaignNet; a client follows by disposing its world, swapping its
// pit instance, rebinding its site and asking NetPit for the new header (ResetClientWorldForRebuild, LAST).
// Offline / dev-host (SessionDriven == false) the first pit is instantiated in Awake, before the controller's Start.
using System;
using System.Collections;
using System.Collections.Generic;
using BallPitFinal;
using BallPitSession;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BallPitGameplay
{
    [DisallowMultipleComponent]
    public sealed class CampaignDirector : MonoBehaviour
    {
        [Header("Campaign (owner-assigned)")]
        [Tooltip("The ordered list of shipment pits (Tools/Ball Pit/Create Campaign Definition).")]
        [SerializeField] private CampaignDefinition definition;
        [Tooltip("BallPitCampaignNet.prefab (NetworkObject + CampaignNet), also in the kit NetworkPrefabsList. May stay empty offline.")]
        [SerializeField] private NetworkObject campaignNetPrefab;
        [Tooltip("Optional. A component implementing IPitTransition; otherwise a FadeTransition is added automatically.")]
        [SerializeField] private MonoBehaviour transitionOverride;
        [Tooltip("Give up waiting for a rebuild after this many seconds (unscaled).")]
        [SerializeField] private float advanceTimeoutSeconds = 900f;

        private static CampaignDirector instance;
        // Registered once per session; static so a stale one (previous session, same scene lifetime) can be unregistered
        // first and never trips "Duplicate save participant tag".
        private static CampaignSaveParticipant lastParticipant;
        private static BallPitGameAdapter lastParticipantAdapter;

        private BallPitFinalController controller;
        private BallPitGameAdapter adapter;
        private IPitTransition transition;
        private IPitSiteSelector previousSelector;
        private CampaignPitSiteSelector selector;
        private CampaignNet net;
        private GameObject liveInstance;
        private int localIndex;
        private bool firstPitEnsured, netSpawned, advancing, following, tornDown;
        private int pendingFollow = -1;

        /// <summary>The live director, or null. Unity fake-null safe (a destroyed director reads as null).</summary>
        public static CampaignDirector Instance => instance != null ? instance : null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            instance = null;
            lastParticipant = null;
            lastParticipantAdapter = null;
        }

        // ------------------------------------------------------------------ Public API (names are referenced by other files)

        /// <summary>The live entry index (host: authoritative; client: the last index followed).</summary>
        public int CurrentIndex => localIndex;
        /// <summary>True when an entry exists after the current one.</summary>
        public bool HasMorePits => definition != null && CampaignMath.HasMore(localIndex, definition.Count);
        public CampaignEntry CurrentEntry => definition != null ? definition.At(localIndex) : null;
        /// <summary>The PitSite of the live pit instance, or null.</summary>
        public PitSite LiveSite => liveInstance != null ? liveInstance.GetComponentInChildren<PitSite>(true) : null;

        /// <summary>Sets the colour count of the current entry on <paramref name="s"/> (clamped 2..8). No-op without a valid entry.</summary>
        public void ApplyEntryColors(PitSettings s)
        {
            if (s == null) return;
            CampaignEntry entry = CurrentEntry;
            if (entry == null) return;
            s.colorCount = CampaignMath.ClampColorCount(entry.colorCount);
        }

        // ------------------------------------------------------------------ Lifecycle

        private void Awake()
        {
            instance = this;
            controller = FindObjectOfType<BallPitFinalController>();
            adapter = ResolveAdapter();
            ResolveTransition();

            if (adapter != null)
            {
                previousSelector = adapter.SiteSelector;
                selector = new CampaignPitSiteSelector(this, previousSelector);
                adapter.SiteSelector = selector;
            }
            if (CampaignNet.Instance != null) AttachNet(CampaignNet.Instance);

            // Offline / dev-host: no OnSessionLive will ever call EnsureFirstPit, and the controller self-builds in Start
            // (after every Awake), so the first pit must exist now. PitSiteStandalone.TryPrepare then finds its PitSite.
            if (!BallPitFinalController.SessionDriven)
            {
                EnsureFirstPit(null);
                if (controller != null) ApplyEntryColors(controller.settings);
            }
        }

        private void OnDisable() => Teardown();

        private void OnDestroy() => Teardown();

        private void Teardown()
        {
            if (tornDown) return;
            tornDown = true;
            // Only restore if the adapter still holds OUR selector; otherwise a later freeplay session would keep a dead one.
            if (adapter != null && selector != null && ReferenceEquals(adapter.SiteSelector, selector))
                adapter.SiteSelector = previousSelector;
            selector = null;
            UnregisterParticipant();
            if (liveInstance != null) Destroy(liveInstance);
            liveInstance = null;
            net = null;
            if (instance == this) instance = null;
        }

        private static BallPitGameAdapter ResolveAdapter() =>
            BallPitKitInstaller.Instance != null ? BallPitKitInstaller.Instance.Adapter : null;

        private void ResolveTransition()
        {
            transition = transitionOverride as IPitTransition;
            if (transition == null && transitionOverride != null)
                Debug.LogWarning("[CampaignDirector] transitionOverride does not implement IPitTransition; using the default fade", this);
            if (transition == null) transition = GetComponent<IPitTransition>();
            if (transition == null) transition = gameObject.AddComponent<FadeTransition>();
        }

        private static void UnregisterParticipant()
        {
            if (lastParticipant != null && lastParticipantAdapter != null) lastParticipantAdapter.UnregisterSaveParticipant(lastParticipant);
            lastParticipant = null;
            lastParticipantAdapter = null;
        }

        // ------------------------------------------------------------------ First pit

        /// <summary>
        /// Instantiates the entry that starts this session's run and marks it live. Called by the adapter directly before
        /// PitSites.SelectAndActivate (host with the save's siteId on resume; client with null) and, offline/dev, from Awake.
        /// Safe to call again: a live instance of the same entry is kept, a stale one is replaced.
        /// </summary>
        public void EnsureFirstPit(string savedSiteId)
        {
            if (definition == null || definition.Count == 0)
            {
                Debug.LogError("[CampaignDirector] no campaign definition (or it is empty); running the scene's default pit selection", this);
                return;
            }
            if (adapter == null) adapter = ResolveAdapter();

            NetworkManager nm = NetworkManager.Singleton;
            bool listening = nm != null && nm.IsListening;
            bool isHost = listening && nm.IsServer;
            bool isClient = listening && !nm.IsServer && nm.IsClient;

            int startIndex = 0;
            if (isHost && !string.IsNullOrEmpty(savedSiteId))
            {
                startIndex = definition.IndexOfSiteId(savedSiteId);
                if (startIndex < 0)
                {
                    Debug.LogWarning($"[CampaignDirector] resume: no campaign entry has a PitSite '{savedSiteId}'; starting at entry 0", this);
                    startIndex = 0;
                }
            }
            else if (isClient)
            {
                CampaignNet cn = CampaignNet.Instance;
                startIndex = cn != null ? CampaignMath.ClampIndex(cn.Index, definition.Count) : 0;
            }

            CampaignEntry entry = definition.At(startIndex);
            if (entry == null || entry.pitPrefab == null || entry.pitPrefab.GetComponentInChildren<PitSite>(true) == null)
            {
                Debug.LogError($"[CampaignDirector] campaign entry {startIndex} has no pit prefab with a PitSite; running the scene's default pit selection", this);
                return;
            }

            if (liveInstance != null && localIndex == startIndex) { /* same entry already live: keep it */ }
            else
            {
                DestroyLive();
                liveInstance = SpawnPit(entry.pitPrefab);
            }
            localIndex = startIndex;
            firstPitEnsured = true;
            advancing = false;
            following = false;
            pendingFollow = -1;
            if (CampaignNet.Instance == null) netSpawned = false; // a new session needs a new CampaignNet

            // Host + session: one participant for the whole session (not per world); reconcile happens in OnIndexRestored.
            if (isHost && BallPitFinalController.SessionDriven && adapter != null)
            {
                UnregisterParticipant();
                var participant = new CampaignSaveParticipant(this);
                adapter.RegisterSaveParticipant(participant);
                lastParticipant = participant;
                lastParticipantAdapter = adapter;
                CampaignNet existing = CampaignNet.Instance;
                if (existing != null) existing.SetIndex(localIndex);
            }
        }

        private GameObject SpawnPit(GameObject prefab)
        {
            // NOT parented: preserves the prefab's authored world pose (the PitSite anchor must stay unrotated).
            GameObject go = Instantiate(prefab, prefab.transform.position, prefab.transform.rotation);
            go.name = prefab.name;
            // Unload with the gameplay scene. Instantiate lands in the active scene; move only when that is a different scene.
            if (gameObject.scene.IsValid() && go.scene != gameObject.scene) SceneManager.MoveGameObjectToScene(go, gameObject.scene);
            return go;
        }

        // Deactivate before Destroy: Destroy is deferred to the end of the frame, and an active leftover would still be found
        // by anything scanning the scene this frame. Callers never rely on PitSites.FindAll after a swap (they Activate directly).
        private void DestroyLive()
        {
            if (liveInstance == null) return;
            liveInstance.SetActive(false);
            Destroy(liveInstance);
            liveInstance = null;
        }

        // ------------------------------------------------------------------ CampaignNet spawn (host, once per session)

        private void Update()
        {
            if (!netSpawned) TrySpawnNet();
        }

        private void TrySpawnNet()
        {
            if (!firstPitEnsured || !BallPitFinalController.SessionDriven || campaignNetPrefab == null) return;
            NetworkManager nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsServer || !nm.IsListening) return;
            if (CampaignNet.Instance != null) { netSpawned = true; return; }
            netSpawned = true; // one-shot, even on failure (no retry spam)
            NetworkObject no = Instantiate(campaignNetPrefab);
            if (no.GetComponent<CampaignNet>() == null)
            {
                Debug.LogError("[CampaignDirector] campaignNetPrefab has no CampaignNet component; clients will not follow the campaign", this);
                Destroy(no.gameObject);
                return;
            }
            no.Spawn(true); // scene-independent, destroyed on despawn; OnNetworkSpawn publishes the starting index
        }

        // ------------------------------------------------------------------ Host: advance on drain

        /// <summary>Host only. Called by GameplaySession when the live pit is drained and <see cref="HasMorePits"/>.</summary>
        public void OnActivePitDrained()
        {
            if (advancing || !HasMorePits) return;
            if (controller == null || definition == null) return;
            advancing = true;
            StartCoroutine(AdvanceRoutine());
        }

        private IEnumerator AdvanceRoutine()
        {
            // First statement: leave the DepositDriver.CheckWin -> step-listener -> FixedUpdate stack before anything is disposed.
            yield return null;
            bool ok = false;
            IEnumerator work = HostRebuildWork(localIndex + 1, r => ok = r);
            yield return RunWithTransition(work);
            advancing = false;
            if (!ok) Debug.LogError("[CampaignDirector] the next pit did not build; the run cannot continue (v1: no retry)", this);
        }

        // Drives transition.Play(work) by hand so a throwing transition can never strand the rebuild (C# forbids `yield` inside
        // a try that has a catch, so only MoveNext/Current sit in the try). The transition is cosmetic; on any failure the SAME
        // `work` enumerator is stepped to completion. Stepping a finished enumerator again is a harmless no-op.
        private IEnumerator RunWithTransition(IEnumerator work)
        {
            IEnumerator outer = null;
            try { outer = transition != null ? transition.Play(work) : null; }
            catch (Exception e) { Debug.LogError("[CampaignDirector] transition failed to start: " + e); outer = null; }

            bool failed = outer == null;
            while (!failed)
            {
                object current = null;
                bool more = false;
                try
                {
                    more = outer.MoveNext();
                    if (more) current = outer.Current;
                }
                catch (Exception e)
                {
                    Debug.LogError("[CampaignDirector] transition threw; finishing the rebuild without it: " + e);
                    failed = true;
                    continue;
                }
                if (!more) yield break;
                yield return current;
            }
            yield return work;
        }

        // Host work for one advance. Steps 1-8 contain NO yield: SetIndex goes out at the end of the NGO tick, which is after
        // the BeginRebuild that RebuildActivePit performs in this same frame, so a client's reset/Hello can never precede it.
        private IEnumerator HostRebuildWork(int next, Action<bool> finished)
        {
            CampaignEntry entry = definition != null ? definition.At(next) : null;
            if (entry == null || entry.pitPrefab == null || entry.pitPrefab.GetComponentInChildren<PitSite>(true) == null)
            {
                Debug.LogError($"[CampaignDirector] campaign entry {next} has no pit prefab with a PitSite", this);
                finished(false);
                yield break;
            }

            // 1. Replicate first.
            localIndex = next;
            CampaignNet cn = CampaignNet.Instance;
            if (cn != null) cn.SetIndex(next);

            // 2-3. Swap the pit instance (the previous pit is already gone if the new one fails: v1 documented).
            DestroyLive();
            liveInstance = SpawnPit(entry.pitPrefab);
            PitSite site = LiveSite;
            if (site == null) { Debug.LogError($"[CampaignDirector] instantiated pit for entry {next} has no PitSite", this); finished(false); yield break; }

            // 4. Activate directly: PitSites.SelectAndActivate would rescan the scene and could still see the destroyed pit.
            PitSites.Activate(site, new List<PitSite> { site });

            // 5. The size profile.
            VenueConfig cfg = adapter != null ? adapter.VenueConfig : VenueConfig.Load();
            PitSizeProfile profile;
            if (cfg == null || !cfg.TryGetProfile(site.ProfileKey, out profile))
            {
                Debug.LogError($"[CampaignDirector] site '{site.SiteId}' names profile '{site.ProfileKey}', which VenueConfig does not define", this);
                finished(false);
                yield break;
            }

            // 6. Settings for the new pit: clone (fill type, seed, physics, playerLayer carry over), colours from the entry.
            PitSettings s = controller.settings.Clone();
            s.colorCount = CampaignMath.ClampColorCount(entry.colorCount);
            string applyError;
            if (!site.TryApplyTo(s, profile, out applyError))
            {
                Debug.LogError("[CampaignDirector] " + applyError, this);
                finished(false);
                yield break;
            }
            Bounds bounds = PitWorld.ComputeBounds(s);

            // 7-8. Environment + rebuild in place.
            var env = new PitSiteEnvironment(site);
            bool done = false, built = false;
            controller.RebuildActivePit(s, bounds, env, site, site.OverviewCamera, (ok, msg) => { done = true; built = ok; });

            // 9. Wait for the build (a detached/abandoned build never calls back).
            float deadline = Time.unscaledTime + Mathf.Max(1f, advanceTimeoutSeconds);
            while (!done && Time.unscaledTime < deadline) yield return null;
            if (!done) { Debug.LogError("[CampaignDirector] timed out waiting for the next pit to build", this); finished(false); yield break; }
            if (!built) { finished(false); yield break; }

            // 10. Refresh the adapter's build-time snapshot (FILL/siteId, hopper ops) so saves record this pit.
            if (adapter != null && BallPitFinalController.SessionDriven) adapter.OnCampaignPitRebuilt(env);
            finished(true);
        }

        // ------------------------------------------------------------------ Client: follow the replicated index

        internal void AttachNet(CampaignNet n)
        {
            net = n;
            if (n == null) return;
            if (!n.IsServer && firstPitEnsured && !following && n.Index != localIndex && controller != null
                && !controller.IsPitWorldReady && controller.World == null && !controller.IsBuilding)
            {
                // Joined before the netvar arrived: do NOT re-run the reset path here; the header stays authoritative.
                Debug.LogWarning($"[CampaignDirector] campaign index {n.Index} arrived after the first pit (entry {localIndex}) was chosen; " +
                                 "the world still builds from the host's header (art may differ until the next advance)", this);
            }
        }

        internal void DetachNet(CampaignNet n)
        {
            if (ReferenceEquals(net, n)) net = null;
        }

        internal void OnReplicatedIndexChanged(int previous, int current)
        {
            CampaignNet cn = CampaignNet.Instance;
            if (cn != null && cn.IsServer) return; // the host drives its own advance
            if (definition == null || definition.Count == 0 || controller == null) return;
            if (current >= definition.Count)
                Debug.LogWarning($"[CampaignDirector] replicated index {current} is past the last entry ({definition.Count - 1}); using the last entry", this);
            int target = CampaignMath.ClampIndex(current, definition.Count);
            if (following) { pendingFollow = target; return; }
            if (target == localIndex) return; // the initial value, or a joiner that already adopted it
            following = true;
            StartCoroutine(ClientFollow(target));
        }

        private IEnumerator ClientFollow(int target)
        {
            // Do not tear anything down inside NGO's network-update callback.
            yield return null;
            try { yield return RunWithTransition(ClientWork(target)); }
            finally { following = false; }
            if (pendingFollow >= 0 && pendingFollow != localIndex)
            {
                int again = pendingFollow;
                pendingFollow = -1;
                following = true;
                StartCoroutine(ClientFollow(again));
            }
            else pendingFollow = -1;
        }

        // Order is mandatory: dispose the old world -> swap the pit instance -> rebind the controller's site fields ->
        // NetPit.ResetClientWorldForRebuild LAST (it sends Hello; the new header must find a clean client and a bound site).
        private IEnumerator ClientWork(int target)
        {
            CampaignEntry entry = definition.At(target);
            if (entry == null || entry.pitPrefab == null || entry.pitPrefab.GetComponentInChildren<PitSite>(true) == null)
            {
                Debug.LogError($"[CampaignDirector] campaign entry {target} has no pit prefab with a PitSite; this client cannot follow", this);
                yield break;
            }

            localIndex = target;
            controller.DisposeClientPitForRebuild();

            DestroyLive();
            liveInstance = SpawnPit(entry.pitPrefab);
            PitSite site = LiveSite;
            if (site == null) { Debug.LogError($"[CampaignDirector] instantiated pit for entry {target} has no PitSite", this); yield break; }
            PitSites.Activate(site, new List<PitSite> { site });

            // Same effect as the adapter's private ApplyClientSite (its dims diagnostic is not needed: the header is authoritative).
            controller.RebindClientSite(new PitSiteEnvironment(site), site, site.OverviewCamera, s =>
            {
                string err;
                if (!site.TryApplyTo(s, null, out err))
                    Debug.LogError("[CampaignDirector] client: " + err + "; building at a zero anchor (the host's full resync delivers the balls)");
            });

            if (controller.Net != null) controller.Net.ResetClientWorldForRebuild(); // LAST: sends Hello

            float elapsed = 0f;
            while (!controller.IsPitWorldReady && elapsed < BallPitGameAdapter.ClientSyncSeconds
                   && controller.Net != null && !controller.Net.Disconnected)
            {
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }
        }

        // ------------------------------------------------------------------ Resume reconcile (host)

        /// <summary>Host: the save's campaign blob. The siteId-derived index already chose the pit; this confirms it.</summary>
        public void OnIndexRestored(int restored)
        {
            if (definition == null || restored < 0 || restored >= definition.Count)
            {
                Debug.LogWarning($"[CampaignDirector] restored campaign index {restored} is out of range; keeping entry {localIndex}", this);
                return;
            }
            CampaignEntry entry = definition.At(restored);
            PitSite entrySite = entry != null && entry.pitPrefab != null ? entry.pitPrefab.GetComponentInChildren<PitSite>(true) : null;
            PitSite live = LiveSite;
            if (entrySite != null && live != null && string.Equals(entrySite.SiteId, live.SiteId, StringComparison.Ordinal))
            {
                localIndex = restored;
                CampaignNet cn = CampaignNet.Instance;
                if (cn != null) cn.SetIndex(restored);
            }
            else
            {
                Debug.LogWarning($"[CampaignDirector] restored campaign index {restored} does not match the live pit's siteId; keeping entry {localIndex}", this);
            }
        }
    }
}

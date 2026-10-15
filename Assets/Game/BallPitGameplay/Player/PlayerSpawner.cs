// PlayerSpawner: spawns and despawns the networked player objects and owns the per-world player-pit drivers
// (deliverable A, file plan P12; design d.10, a 6.5; brief 6.5, 11.4, 11.5). One instance sits anywhere in the gameplay
// scene. The adapter validates it in OnSessionLive and calls BeginSession before the attach; the host's own player spawns
// on the session's first PitWorldBuilt (after NetPit.StartHost), each client's in OnPlayerJoined, and leaving players
// despawn idempotently. Every build creates the host's HostPlayerPhysics or the client's ClientPitColliders, every dispose
// ends it. It is also the spawn-pose service (IPlayerSpawnPoses) the session gate, respawn and out of bounds use.
using System.Collections.Generic;
using BallPitFinal;
using MultiplayerKit;
using Unity.Netcode;
using UnityEngine;

namespace BallPitGameplay
{
    [DisallowMultipleComponent]
    public sealed class PlayerSpawner : MonoBehaviour, IPlayerSpawnPoses
    {
        [Tooltip("Player.prefab's NetworkObject: the SAME asset as in the NetworkPrefabsList on MultiplayerKitBootstrap." +
                 "networkPrefabs. The kit's playerPrefab stays empty.")]
        [SerializeField] private NetworkObject playerPrefab;
        [Tooltip("Player and pit physics tunables (freeze blocker, held balls, pushers, client colliders, spawning).")]
        [SerializeField] private PlayerPitPhysicsConfig physics = new PlayerPitPhysicsConfig();

        private struct SpawnRecord { public NetworkObject Object; public int Slot; public PlayerId Player; }

        private readonly Dictionary<ulong, SpawnRecord> spawned = new Dictionary<ulong, SpawnRecord>();
        private NetworkManager nm;
        private bool isHost, firstBuildSeen, subscribed;
        private PlayerId localPlayer;
        private SpawnTemplate template = SpawnTemplate.Default;
        private System.Func<Vector3, bool> forbidden;
        private HostPlayerPhysics hostPhysics;
        private ClientPitColliders clientColliders;
#if UNITY_EDITOR
        [System.NonSerialized] private string lastValidation;
#endif

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Current = null;

        public PlayerPitPhysicsConfig Physics => physics;
        /// <summary>The first enabled spawner (OnEnable/OnDisable); a second one logs an Error and is ignored.</summary>
        public static PlayerSpawner Current { get; private set; }
        /// <summary>Set in BeginSession, cleared in EndSession.</summary>
        public BallPitFinalController Controller { get; private set; }
        public bool IsSessionActive { get; private set; }
        /// <summary>Host: the host's own player spawned on the session's first build (the adapter's ready check).</summary>
        public bool HostPlayerSpawned { get; private set; }
        /// <summary>Read only, for the editor tools.</summary>
        public NetworkObject PlayerPrefab => playerPrefab;
        /// <summary>The current world's drivers (null between builds, and on the other role).</summary>
        public HostPlayerPhysics HostPhysics => hostPhysics;
        public ClientPitColliders ClientColliders => clientColliders;

        private void Awake() => forbidden = IsForbidden; // cached: TryGetSpawnPose never allocates a delegate

        private void OnEnable()
        {
            if (Current != null && Current != this)
            {
                Debug.LogError($"[PlayerSpawner] a second PlayerSpawner '{name}' is enabled; '{Current.name}' stays the scene's spawner", this);
                return;
            }
            Current = this;
        }

        private void OnDisable() { if (Current == this) Current = null; }

        private void OnDestroy() { if (IsSessionActive) EndSession(); } // defensive: the adapter's OnSessionEnding ends it first

        // ------------------------------------------------------------------ Setup checks (every peer, OnSessionLive)

        /// <summary>Throws InvalidOperationException (the session fails with GameFailedToStart) with the fix in the message
        /// when the prefab is missing, lacks PlayerAvatar or PlayerSlot, is not a registered network prefab, or the kit's
        /// playerPrefab is set (NGO would auto-spawn a second player object per client).</summary>
        public void ValidateSetup(NetworkManager manager)
        {
            if (playerPrefab == null)
                throw new System.InvalidOperationException("[PlayerSpawner] no player prefab: assign Player.prefab's NetworkObject to PlayerSpawner.playerPrefab");
            if (playerPrefab.GetComponent<PlayerAvatar>() == null || playerPrefab.GetComponent<PlayerSlot>() == null)
                throw new System.InvalidOperationException($"[PlayerSpawner] the player prefab '{playerPrefab.name}' needs PlayerAvatar and PlayerSlot on its root");
            if (manager == null || manager.NetworkConfig == null)
                throw new System.InvalidOperationException("[PlayerSpawner] no NetworkManager to validate the player prefab against");
            if (!IsRegistered(manager.NetworkConfig, playerPrefab.gameObject))
                throw new System.InvalidOperationException($"[PlayerSpawner] '{playerPrefab.name}' is not a registered network prefab: add " +
                                                           "Player.prefab to the NetworkPrefabsList assigned to MultiplayerKitBootstrap.networkPrefabs");
            if (manager.NetworkConfig.PlayerPrefab != null)
                throw new System.InvalidOperationException("[PlayerSpawner] MultiplayerKitBootstrap.playerPrefab must stay empty: with it set, NGO " +
                                                           "auto-spawns a second player object for every client");
        }

        // The runtime list (filled from the prefab lists when NGO starts), then the assigned lists themselves.
        private static bool IsRegistered(NetworkConfig config, GameObject prefab)
        {
            NetworkPrefabs prefabs = config.Prefabs;
            if (prefabs == null) return false;
            IReadOnlyList<NetworkPrefab> runtime = prefabs.Prefabs;
            if (runtime != null)
                for (int i = 0; i < runtime.Count; i++) if (runtime[i] != null && runtime[i].Prefab == prefab) return true;
            List<NetworkPrefabsList> lists = prefabs.NetworkPrefabsLists;
            if (lists == null) return false;
            for (int l = 0; l < lists.Count; l++)
            {
                IReadOnlyList<NetworkPrefab> entries = lists[l] != null ? lists[l].PrefabList : null;
                if (entries == null) continue;
                for (int i = 0; i < entries.Count; i++) if (entries[i] != null && entries[i].Prefab == prefab) return true;
            }
            return false;
        }

        // ------------------------------------------------------------------ Session (adapter)

        /// <summary>Every peer, after ValidateSetup and before the attach: subscribes the world events, caches the spawn
        /// template, and becomes PlayerAvatar.SpawnPoses (the session gate waits for it on host and clients alike).</summary>
        public void BeginSession(NetworkManager manager, BallPitFinalController controller, bool host, PlayerId local)
        {
            if (controller == null) throw new System.ArgumentNullException(nameof(controller));
            if (IsSessionActive) EndSession(); // defensive: a session that was never ended
            nm = manager;
            Controller = controller;
            isHost = host;
            localPlayer = local;
            spawned.Clear();
            firstBuildSeen = false;
            HostPlayerSpawned = false;
            physics.Sanitize();
            if (!TryGetTemplate(out template))
                Debug.LogWarning("[PlayerSpawner] the player prefab has no PlayerConfig asset; spawn checks use PlayerConfig's defaults (0.35 / 1.8 m)", this);
            if (forbidden == null) forbidden = IsForbidden;
            PlayerSpawnResolver.ResetSessionWarnings();
            controller.PitWorldBuilt += OnWorldBuilt;
            controller.PitWorldDisposed += OnWorldDisposed;
            subscribed = true;
            PlayerAvatar.SpawnPoses = this;
            IsSessionActive = true;
            if (controller.IsPitWorldReady && controller.World != null) OnWorldBuilt(controller.World, controller.ActiveSite); // defensive
        }

        /// <summary>Every peer, from OnSessionEnding before controller.DetachSession: disposes the drivers while the world
        /// still exists. It does not despawn: the kit disconnects the clients next and NGO's shutdown destroys every object.</summary>
        public void EndSession()
        {
            BallPitFinalController c = Controller;
            if (subscribed && !ReferenceEquals(c, null))
            {
                c.PitWorldBuilt -= OnWorldBuilt;
                c.PitWorldDisposed -= OnWorldDisposed;
            }
            subscribed = false;
            DisposeDrivers();
            spawned.Clear();
            if (ReferenceEquals(PlayerAvatar.SpawnPoses, this)) PlayerAvatar.SpawnPoses = null;
            IsSessionActive = false;
            HostPlayerSpawned = false;
            firstBuildSeen = false;
            Controller = null;
            nm = null;
        }

        // Every build (brief 11.4): fresh drivers; on the host's first build only, the host's own player.
        private void OnWorldBuilt(PitWorld world, IPitSite site)
        {
            if (!IsSessionActive || world == null) return;
            DisposeDrivers(); // defensive: dispose normally ended them
            BallPitFinalController c = Controller;
            try
            {
                if (isHost)
                {
                    if (c.Authority != null)
                    {
                        hostPhysics = new HostPlayerPhysics(c, c.Authority, world, physics);
                        c.AddStepListener(hostPhysics, PitStepOrder.ColliderSources);
                    }
                    else Debug.LogError("[PlayerSpawner] the host world has no PitAuthority; players get no collider shell", this);
                }
                else if (world.Colliders != null)
                {
                    clientColliders = new ClientPitColliders(c, world, c.Replica, c.Net, physics);
                    c.AddStepListener(clientColliders, PitStepOrder.ColliderSources);
                }
            }
            catch (System.Exception e)
            {
                Debug.LogException(e, this);
                DisposeDrivers();
            }
            if (!isHost || firstBuildSeen) return;
            firstBuildSeen = true;
            // After NetPit.StartHost (the controller raises PitWorldBuilt after it). NGO runs the avatar's OnNetworkSpawn
            // inside this call, so PlayerAvatar.Local exists when it returns.
            HostPlayerSpawned = nm != null && Spawn(nm.LocalClientId, localPlayer, c.LocalSlot);
        }

        // Avatars stay spawned: the local avatar freezes itself, and the gate re-places it after the next build.
        private void OnWorldDisposed(PitWorld world) => DisposeDrivers();

        private void DisposeDrivers()
        {
            BallPitFinalController c = Controller;
            if (hostPhysics != null)
            {
                if (!ReferenceEquals(c, null)) c.RemoveStepListener(hostPhysics);
                hostPhysics.Dispose();
                hostPhysics = null;
            }
            if (clientColliders != null)
            {
                if (!ReferenceEquals(c, null)) c.RemoveStepListener(clientColliders);
                clientColliders.Dispose();
                clientColliders = null;
            }
        }

        // ------------------------------------------------------------------ Spawn and despawn (host)

        /// <summary>Host, from OnPlayerJoined with the mirror's (NetPit's) slot. False (Error) when this peer is not the
        /// host, no session runs, the slot is negative or NGO is shutting down, or the spawn threw; the adapter then
        /// disconnects the client. A client already spawned: true (Warning).</summary>
        public bool SpawnClientPlayer(ulong clientId, PlayerId player, int slot)
        {
            string refusal = !isHost ? "this peer is not the host" : !IsSessionActive ? "no session"
                           : slot < 0 ? "no pit slot" : nm == null || nm.ShutdownInProgress ? "NGO is shutting down" : null;
            if (refusal != null)
            {
                Debug.LogError($"[PlayerSpawner] client {clientId} (player {player}) not spawned: {refusal}", this);
                return false;
            }
            if (spawned.ContainsKey(clientId))
            {
                Debug.LogWarning($"[PlayerSpawner] client {clientId} already has a player object (repeated callback)", this);
                return true;
            }
            return Spawn(clientId, player, slot);
        }

        /// <summary>Host, from OnPlayerLeft. Idempotent: NGO also destroys a disconnected client's player object, and
        /// whichever runs first wins.</summary>
        public void DespawnPlayer(ulong clientId)
        {
            if (!spawned.TryGetValue(clientId, out SpawnRecord r)) return;
            spawned.Remove(clientId);
            NetworkObject no = r.Object;
            if (no == null || !no.IsSpawned) return;
            try { no.Despawn(true); }
            catch (System.Exception e) { Debug.LogException(e, this); } // NGO's own cleanup still destroys the object
        }

        // At the resolved pose, so every peer first sees the player where it will stand. The gate places it again once
        // this peer's world is ready.
        private bool Spawn(ulong owner, PlayerId player, int slot)
        {
            if (!TryGetSpawnPose(slot, null, out Vector3 feet, out float yaw))
            {
                feet = transform.position;
                yaw = transform.eulerAngles.y;
                Debug.LogWarning($"[PlayerSpawner] no spawn pose for slot {slot} yet; spawning at the spawner. The player is placed " +
                                 "once a world is ready", this);
            }
            NetworkObject no = null;
            try
            {
                no = Instantiate(playerPrefab, feet, Quaternion.Euler(0f, yaw, 0f));
                no.GetComponent<PlayerSlot>().ServerInit(slot, player, DisplayNameOf(player, slot)); // written in its OnNetworkSpawn
                no.SpawnAsPlayerObject(owner, destroyWithScene: true); // the gameplay scene owns it: the menu load ends it
            }
            catch (System.Exception e)
            {
                Debug.LogException(e, this);
                Debug.LogError($"[PlayerSpawner] could not spawn player {player} (client {owner}) in slot {slot}", this);
                if (no != null)
                {
                    try { if (no.IsSpawned) no.Despawn(true); else Destroy(no.gameObject); }
                    catch (System.Exception cleanup) { Debug.LogException(cleanup, this); }
                }
                return false;
            }
            spawned[owner] = new SpawnRecord { Object = no, Slot = slot, Player = player };
            Debug.Log($"[PlayerSpawner] spawned player {player} (client {owner}) in slot {slot}", this);
            return true;
        }

        private static string DisplayNameOf(PlayerId player, int slot)
        {
            string name = null;
            try
            {
                KitServices kit = KitRuntime.Current;
                name = kit != null && kit.Identity != null ? kit.Identity.GetDisplayName(player) : null;
            }
            catch (System.Exception e) { Debug.LogException(e); }
            return string.IsNullOrEmpty(name) ? "Player " + (slot + 1) : name;
        }

        // ------------------------------------------------------------------ Spawn poses (IPlayerSpawnPoses, design d.9)

        /// <summary>The pose for a slot under the active site's rules: its own slot's point first, then round-robin over
        /// unblocked points, then the computed ring. False only without a world and without a usable point.</summary>
        public bool TryGetSpawnPose(int slot, Collider ignore, out Vector3 feet, out float yaw)
        {
            BallPitFinalController c = Controller;
            PitWorld w = c != null ? c.World : null;
            IPitSite site = c != null ? c.ActiveSite : null;
            return PlayerSpawnResolver.Resolve(slot, ignore, in template, site, w, physics, forbidden, out feet, out yaw) != SpawnSource.None;
        }

        // Spawn points inside an OutOfBoundsVolume that applies to the active site are skipped (the arrival filter).
        private bool IsForbidden(Vector3 p)
        {
            BallPitFinalController c = Controller;
            return OutOfBoundsVolume.AnyContains(p, c != null ? c.ActiveSite as PitSite : null);
        }

        /// <summary>
        /// The capsule and out-of-bounds rule the player will have at runtime, read from the prefab's PlayerAvatar
        /// ConfigAsset (clamped as PlayerConfig.Sanitize would; the asset is never modified). False = SpawnTemplate.Default,
        /// which equals the avatar's own fallback without a config. Works in edit mode (editor gizmo and validator).
        /// </summary>
        public bool TryGetTemplate(out SpawnTemplate result)
        {
            result = SpawnTemplate.Default;
            PlayerAvatar avatar = playerPrefab != null ? playerPrefab.GetComponent<PlayerAvatar>() : null;
            PlayerConfig c = avatar != null ? avatar.ConfigAsset : null;
            if (c == null) return false;
            result = new SpawnTemplate(Clamp(c.capsuleRadius, 0.2f, 0.6f, 0.35f), Clamp(c.capsuleHeight, 1f, 2.5f, 1.8f),
                                       c.respawnOutsidePlayBounds);
            return true;
        }

        private static float Clamp(float v, float min, float max, float fallback) =>
            float.IsNaN(v) || float.IsInfinity(v) ? fallback : Mathf.Clamp(v, min, max);

#if UNITY_EDITOR
        private void OnValidate()
        {
            string text = playerPrefab == null ? "no player prefab assigned (Player.prefab's NetworkObject)"
                        : playerPrefab.GetComponent<PlayerAvatar>() == null || playerPrefab.GetComponent<PlayerSlot>() == null
                            ? $"the player prefab '{playerPrefab.name}' needs PlayerAvatar and PlayerSlot on its root" : null;
            if (text == lastValidation) return;
            lastValidation = text;
            if (text != null) Debug.LogWarning("[PlayerSpawner] " + text, this);
        }
#endif
    }
}

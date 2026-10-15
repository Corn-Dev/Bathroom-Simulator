// Composition root of the kit and the only code in the delivery that creates a NetworkManager (decision 9). Placed
// once, alone on a root GameObject in the menu scene; no scene may contain a NetworkManager. Design: 03 §9, 02 §1,
// 06 §1 (SetAdapter, execution order, approval callback), 06 N5, 05 §2.1 / §12 (KitRuntime).
// Awake (order -500, after the game's installer at -1000): singleton guard, foreign-NetworkManager check, adapter,
// NetworkManager + Steam transport built in code. Start (after every Awake, so Steam is initialized by then): Steam
// services, NgoSessionNetwork and its approval callback, SessionController, KitRuntime.Publish, launch-argument join.

using System;
using MultiplayerKit.Steam;
using Netcode.Transports;
using Unity.Netcode;
using UnityEngine;

namespace MultiplayerKit.Ngo
{
    [DefaultExecutionOrder(-500)]
    public sealed class MultiplayerKitBootstrap : MonoBehaviour
    {
        private const string NetworkManagerObjectName = "MultiplayerKit.NetworkManager";

        [Tooltip("Optional. A MonoBehaviour implementing IGameAdapter; not needed when the game calls SetAdapter before this Awake.")]
        [SerializeField] private MonoBehaviour gameAdapter;

        [Tooltip("Optional NGO player prefab. Empty: no player objects.")]
        [SerializeField] private GameObject playerPrefab;

        [Tooltip("Optional. The game's network prefabs.")]
        [SerializeField] private NetworkPrefabsList networkPrefabs;

        [Tooltip("Enables KitLog.Debug output.")]
        [SerializeField] private bool verboseLogging;

        private static IGameAdapter pendingAdapter;   // SetAdapter, consumed by the first Awake

        private IGameAdapter adapter;
        private NetworkManager nm;
        private SteamNetworkingSocketsTransport transport;
        private SteamPlayerIdentity identity;
        private SteamLobbyService lobby;
        // Qualified: the input project declares a global SteamInviteService, which would win name lookup here if
        // this assembly is compiled into Assembly-CSharp (HANDOFF 8.3 fallback) while the old script still exists.
        private MultiplayerKit.Steam.SteamInviteService invites;
        private SteamFriendsLobbyDiscovery friends;
        private SteamPublicLobbyBrowser browser;
        private SteamWorldSaveStore saves;            // null without GameCapabilities.WorldSaves
        private NgoSessionNetwork network;
        private SessionController controller;
        private ISteamFlushable[] flushables = new ISteamFlushable[0];
        private bool built;                           // Awake completed and the NetworkManager exists
        private bool shutDown;                        // ShutdownKit ran

        /// <summary>The live bootstrap; null before the first Awake and after it is destroyed.</summary>
        public static MultiplayerKitBootstrap Instance { get; private set; }

        /// <summary>Null until Start. For game code that must end the session itself (04 §8 client watchdog).</summary>
        public SessionController Controller => controller;

        /// <summary>
        /// Registers the game adapter. Valid only before this component's Awake, so call it from an Awake with an
        /// earlier execution order (the game's installer). Ignored with a Warning once a bootstrap exists, so a stray
        /// second adapter can never replace the live one.
        /// </summary>
        public static void SetAdapter(IGameAdapter adapter)
        {
            if (Instance != null)
            {
                KitLog.Warning("MultiplayerKitBootstrap.SetAdapter called after the bootstrap initialized; ignored.");
                return;
            }
            if (adapter == null)
            {
                KitLog.Warning("MultiplayerKitBootstrap.SetAdapter(null) ignored.");
                return;
            }
            pendingAdapter = adapter;
        }

        // 03 §9 Awake. Nothing Steam-dependent here: the project's Steam manager script runs its Awake at the default
        // order (0), after this one, so Steam is not initialized yet.
        private void Awake()
        {
            // 1. The menu scene is loaded again after every session; its copy of this object removes itself.
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            KitLog.Verbose = verboseLogging;

            // 2. Decision 9 enforced at runtime. Scene objects exist before their own Awake runs, so the search also
            // finds a scene NetworkManager whose OnEnable has not set Singleton yet (this Awake runs at -500).
            NetworkManager foreign = NetworkManager.Singleton != null
                ? NetworkManager.Singleton
                : FindFirstObjectByType<NetworkManager>(FindObjectsInactive.Include);
            if (foreign != null)
            {
                throw Fatal("MultiplayerKit owns the NetworkManager; remove the one in scene '" + foreign.gameObject.scene.name + "'.");
            }

            // 3. The SetAdapter value, else the serialized field.
            adapter = ResolveAdapter();

            // 4. NetworkManager and Steam transport in code.
            BuildNetworkManager();
            built = true;
        }

        // 03 §9 Start / 02 §1 step 4. Runs after every Awake, including the Steam manager's.
        private void Start()
        {
            if (Instance != this || !built || controller != null) return;

            // 4.1 Services. Discovery and the browser need the schema for published settings and filters; the
            // controller builds its own from the same adapter, which yields the same definitions.
            GameCapabilities capabilities = adapter.Capabilities;
            HostSettingsSchema schema = HostSettingsSchema.Build(adapter);
            identity = new SteamPlayerIdentity();
            lobby = new SteamLobbyService();
            invites = new MultiplayerKit.Steam.SteamInviteService();
            friends = new SteamFriendsLobbyDiscovery(schema, adapter.GameId, adapter.GameVersion);
            browser = new SteamPublicLobbyBrowser(schema, adapter.GameId, adapter.GameVersion,
                (capabilities & GameCapabilities.PublicLobbies) != 0);
            saves = (capabilities & GameCapabilities.WorldSaves) != 0
                ? new SteamWorldSaveStore(identity, adapter.GameId, persistentDataPath: Application.persistentDataPath)
                : null;
            // Every service that raises coalesced events, checks deadlines or retries late callback registration.
            flushables = new ISteamFlushable[] { lobby, friends, browser, invites, identity };

            network = new NgoSessionNetwork(nm, transport);
            nm.ConnectionApprovalCallback = network.OnApprovalRequested;   // the kit's only assignment; set once, before any StartHost
            controller = new SessionController(adapter, lobby, network, invites, identity, saves);

            // 4.2 The UI binds through Core only (05 §2.1).
            KitRuntime.Publish(new KitServices(controller, friends, browser, saves, identity, adapter.GameVersion));

            // 4.3 No Steam: the menu disables its networked buttons and shows the toast (05 §4); no UI from here.
            if (!SteamPlatform.IsInitialized)
            {
                KitLog.Error("Steam is not initialized; multiplayer is unavailable.");
                return;
            }

            // 4.4 Cold start with "+connect_lobby <id>" (flow 8).
            if (invites.TryConsumeLaunchLobby(out LobbyId launchLobby))
            {
                KitLog.Info("Joining lobby " + launchLobby + " from the launch arguments.");
                KitResult joined = controller.JoinLobby(launchLobby);
                if (!joined.Ok) KitLog.Warning("Joining the launch-argument lobby failed: " + joined);
            }
        }

        private void Update()
        {
            if (shutDown || controller == null) return;
            controller.Tick(Time.unscaledTime);
        }

        // 03 §1: coalesced Steam events and deadlines, after this frame's Steam callbacks.
        private void LateUpdate()
        {
            if (shutDown) return;
            float now = Time.unscaledTime;
            for (int i = 0; i < flushables.Length; i++)
            {
                try
                {
                    flushables[i].Flush(now);
                }
                catch (Exception e)
                {
                    KitLog.Error(flushables[i].GetType().Name + ".Flush threw.", e);
                }
            }
        }

        // 02 §1 step 6: Teardown then skips the menu scene load.
        private void OnApplicationQuit()
        {
            if (Instance != this) return;
            if (controller != null) controller.MarkQuitting();
            ShutdownKit();
        }

        private void OnDestroy()
        {
            if (Instance != this) return;
            ShutdownKit();
            Instance = null;
        }

        // 02 §1 step 6 and 05 §12, once, from OnApplicationQuit or OnDestroy, whichever comes first. Each step is
        // isolated so one failure cannot skip the rest.
        private void ShutdownKit()
        {
            if (shutDown) return;
            shutDown = true;
            if (controller != null) Step("leave the session", controller.Leave);   // teardown, including the host autosave
            if (invites != null) Step("clear rich presence", invites.ClearPresence); // 02 §10 step 5, after teardown's InMenu update
            Step("clear KitRuntime", KitRuntime.Clear);
            Step("dispose the Steam services", DisposeSteamServices);
            if (identity != null) Step("release avatar textures", identity.ReleaseAll);
            Step("shut down NGO", ShutdownNetwork);
        }

        private void DisposeSteamServices()
        {
            flushables = new ISteamFlushable[0];
            DisposeQuietly(browser);
            DisposeQuietly(friends);
            DisposeQuietly(invites);
            DisposeQuietly(lobby);
            DisposeQuietly(identity);
        }

        // NgoSessionNetwork.Shutdown unsubscribes its NGO events, then calls nm.Shutdown; both are idempotent.
        private void ShutdownNetwork()
        {
            if (network != null) network.Shutdown();
            else if (nm != null) nm.Shutdown();
        }

        private IGameAdapter ResolveAdapter()
        {
            IGameAdapter registered = pendingAdapter;
            pendingAdapter = null;
            if (registered != null) return registered;
            if (gameAdapter == null)
            {
                throw Fatal("No game adapter: call MultiplayerKitBootstrap.SetAdapter before this Awake, or assign gameAdapter.");
            }
            if (gameAdapter is IGameAdapter fromField) return fromField;
            throw Fatal("gameAdapter '" + gameAdapter.GetType().Name + "' does not implement IGameAdapter.");
        }

        // 03 §9 config block. Every other NetworkConfig field keeps the NGO defaults NetPit was validated with
        // (tick rate 30, ForceSamePrefabs, client scene sync mode Single).
        private void BuildNetworkManager()
        {
            var root = new GameObject(NetworkManagerObjectName);
            DontDestroyOnLoad(root);
            transport = root.AddComponent<SteamNetworkingSocketsTransport>();
            nm = root.AddComponent<NetworkManager>();
            nm.NetworkConfig = new NetworkConfig
            {
                NetworkTransport = transport,
                ConnectionApproval = true,
                EnableSceneManagement = true,
                PlayerPrefab = playerPrefab,   // may be null
            };
            if (networkPrefabs != null) nm.NetworkConfig.Prefabs.NetworkPrefabsLists.Add(networkPrefabs);
            if (playerPrefab != null) nm.AddNetworkPrefab(playerPrefab);
            // The approval callback is not assigned here: NgoSessionNetwork does not exist until Start.
        }

        private static void Step(string name, Action action)
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                KitLog.Error("Bootstrap shutdown step '" + name + "' threw; continuing.", e);
            }
        }

        private static void DisposeQuietly(IDisposable service)
        {
            if (service == null) return;
            try
            {
                service.Dispose();
            }
            catch (Exception e)
            {
                KitLog.Error(service.GetType().Name + ".Dispose threw.", e);
            }
        }

        // Wiring bugs are fatal (03 §16): Error log, then the exception from Awake.
        private static InvalidOperationException Fatal(string message)
        {
            KitLog.Error(message);
            return new InvalidOperationException(message);
        }
    }
}

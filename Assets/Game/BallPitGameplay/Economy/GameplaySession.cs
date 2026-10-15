// GameplaySession: the scene glue that wires the gameplay loop around each built world (brief §3, §4; design D1-D10).
// On PitWorldBuilt it: locates the kit adapter (null offline), builds the plain-C# GameplayCore bound to
// adapter.GameState (or an internal state offline), builds the read model, attribution and host-only DepositDriver,
// host-spawns BallPitGameplayNet.prefab (session host) or runs the core locally (offline/dev), registers the economy
// save participant (host), and applies team upgrade levels to every player's PlayerCapabilities on each purchase. On
// PitWorldDisposed it tears all of that down. Runs in all three contexts (offline, dev-host, session host/client).
// This component is the single owner of the gameplay runtime; the HUD/shop/results read its ReadModel.
using System.Collections.Generic;
using BallPitFinal;
using Unity.Netcode;
using UnityEngine;

namespace BallPitGameplay
{
    [DisallowMultipleComponent]
    public sealed class GameplaySession : MonoBehaviour
    {
        [Header("Config (owner-assigned)")]
        [SerializeField] private EconomyConfig economyConfig;
        [SerializeField] private UpgradeCatalog upgradeCatalog;
        [Tooltip("The BallPitGameplayNet.prefab (NetworkObject + GameplayNet), also in the kit NetworkPrefabsList.")]
        [SerializeField] private NetworkObject gameplayNetPrefab;

        private BallPitFinalController controller;
        private GameplayCore core;
        private GameplayReadModel readModel;
        private BallAttribution attribution;
        private DepositDriver depositDriver;
        private GameplayNet net;
        private EconomySaveParticipant saveParticipant;
        private BallPitSession.BallPitGameAdapter adapter;
        private PaletteService paletteService;
        private bool subscribed, built, isHost;
        private float startTime;

        /// <summary>The live read model for the HUD/shop/results; null before the first world build.</summary>
        public IGameplayReadModel ReadModel => readModel;
        public GameplayCore Core => core;
        public GameplayNet Net => net;
        public BallAttribution Attribution => attribution;
        /// <summary>True while a networked session hosts this loop (false offline/dev).</summary>
        public bool IsNetworked => net != null;
        /// <summary>True when the local peer is the authority (offline, dev-host, or session host).</summary>
        public bool IsAuthority => controller != null && controller.Authority != null;

        /// <summary>Raised on every peer when the win results arrive (HUD/results panel listen).</summary>
        public event System.Action<ResultsData> WinResults;
        /// <summary>Raised on every peer when the feed changes (HUD listens).</summary>
        public event System.Action FeedChanged;

        private void Awake()
        {
            controller = FindObjectOfType<BallPitFinalController>();
            if (controller == null) { Debug.LogError("[GameplaySession] no BallPitFinalController in the scene", this); enabled = false; return; }
            adapter = BallPitSession.BallPitKitInstaller.Instance != null ? BallPitSession.BallPitKitInstaller.Instance.Adapter : null;
        }

        private void OnEnable()
        {
            if (controller == null) return;
            controller.PitWorldBuilt += OnPitWorldBuilt;
            controller.PitWorldDisposed += OnPitWorldDisposed;
            subscribed = true;
            if (controller.IsPitWorldReady && controller.World != null) OnPitWorldBuilt(controller.World, controller.ActiveSite);
        }

        private void OnDisable()
        {
            if (subscribed && controller != null)
            {
                controller.PitWorldBuilt -= OnPitWorldBuilt;
                controller.PitWorldDisposed -= OnPitWorldDisposed;
            }
            subscribed = false;
            TearDown();
        }

        /// <summary>Lets PaletteApplier share its PaletteService so colour names resolve in the read model.</summary>
        public void SetPaletteService(PaletteService service) => paletteService = service;

        // ------------------------------------------------------------------ Build / teardown

        private void OnPitWorldBuilt(PitWorld world, IPitSite site)
        {
            if (built) TearDown();
            built = true;
            startTime = Time.unscaledTime;
            adapter = BallPitSession.BallPitKitInstaller.Instance != null ? BallPitSession.BallPitKitInstaller.Instance.Adapter : null;

            bool sessionDriven = BallPitFinalController.SessionDriven;
            isHost = controller.Authority != null; // authority exists on host + offline

            // Core: bound to the adapter's persisted GameState when a session runs, else internal (offline).
            BallPitSession.BallPitGameState gameState = sessionDriven && adapter != null ? adapter.GameState : null;
            core = new GameplayCore(economyConfig, upgradeCatalog, gameState);
            core.DevToolsUsed |= controller.DevToolsUsedInSession;
            core.PeakPlayers = Mathf.Max(core.PeakPlayers, PlayerAvatar.All.Count);

            attribution = new BallAttribution();
            readModel = new GameplayReadModel(controller, core, paletteService, () => net);

            // Host-only: deposit/reject/return/win driver, save participant, capability application on purchase.
            if (isHost)
            {
                depositDriver = new DepositDriver(controller, core, attribution, () => controller.ActiveSite as PitSite, OnWinHost);
                controller.AddStepListener(depositDriver, PitStepOrder.ColliderSources);
                core.UpgradePurchased += OnUpgradePurchasedHost;
                core.BallSold += OnSaleHost;
                controller.DevToolsUsed += OnDevToolUsed;
                if (sessionDriven && adapter != null)
                {
                    saveParticipant = new EconomySaveParticipant(core);
                    adapter.RegisterSaveParticipant(saveParticipant);
                }
                ApplyAllCapabilities(); // restored levels take effect immediately
            }

            // Networked: host spawns the gameplay NetworkObject; clients find it via GameplayNet.Instance.
            if (sessionDriven)
            {
                if (isHost) SpawnGameplayNet();
                else BindClientNet();
            }
        }

        private void OnPitWorldDisposed(PitWorld world) => TearDown();

        private void TearDown()
        {
            if (!built) return;
            built = false;
            if (depositDriver != null)
            {
                controller.RemoveStepListener(depositDriver);
                depositDriver.Detach();
                depositDriver = null;
            }
            if (core != null)
            {
                core.UpgradePurchased -= OnUpgradePurchasedHost;
                core.BallSold -= OnSaleHost;
            }
            if (controller != null) controller.DevToolsUsed -= OnDevToolUsed;
            if (net != null) { net.Feed -= OnNetFeed; net.WinResults -= OnNetWin; }
            if (isHost && net != null && net.IsSpawned)
            {
                try { net.NetworkObject.Despawn(true); } catch (System.Exception e) { Debug.LogException(e, this); }
            }
            net = null;
            if (saveParticipant != null && adapter != null) adapter.UnregisterSaveParticipant(saveParticipant);
            saveParticipant = null;
            attribution?.Clear();
            attribution = null;
            readModel = null;
            core = null;
        }

        // ------------------------------------------------------------------ Networked object wiring

        private void SpawnGameplayNet()
        {
            if (gameplayNetPrefab == null) { Debug.LogError("[GameplaySession] no gameplayNetPrefab assigned; networked money/upgrades will not replicate", this); return; }
            NetworkManager nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsServer) return;
            NetworkObject no = Instantiate(gameplayNetPrefab);
            net = no.GetComponent<GameplayNet>();
            if (net == null) { Debug.LogError("[GameplaySession] gameplayNetPrefab has no GameplayNet component", this); Destroy(no.gameObject); return; }
            net.BindHost(core);
            no.Spawn(true); // scene-independent, destroyed on despawn
            net.Feed += OnNetFeed;
            net.WinResults += OnNetWin;
        }

        private void BindClientNet()
        {
            net = GameplayNet.Instance;
            if (net != null)
            {
                net.SetCatalog(upgradeCatalog);
                net.Feed += OnNetFeed;
                net.WinResults += OnNetWin;
            }
            else StartCoroutine(WaitForClientNet()); // the host may spawn it a frame later
        }

        private System.Collections.IEnumerator WaitForClientNet()
        {
            float deadline = Time.unscaledTime + 30f;
            while (built && net == null && Time.unscaledTime < deadline)
            {
                net = GameplayNet.Instance;
                if (net != null)
                {
                    net.SetCatalog(upgradeCatalog);
                    net.Feed += OnNetFeed;
                    net.WinResults += OnNetWin;
                    yield break;
                }
                yield return null;
            }
        }

        // ------------------------------------------------------------------ Buy intent (from the shop UI)

        /// <summary>Routes a buy intent: networked → GameplayNet ServerRpc; offline/dev → the core directly.</summary>
        public void RequestBuy(string upgradeId)
        {
            string buyer = LocalPlayerName();
            if (net != null) net.RequestBuy(upgradeId, buyer);
            else if (core != null && core.TryBuy(upgradeId, buyer, out _)) { /* OnUpgradePurchasedHost applies + feeds */ }
        }

        // ------------------------------------------------------------------ Host callbacks

        private void OnUpgradePurchasedHost(PurchaseInfo info)
        {
            ApplyAllCapabilities();
            ApplyFunnelToContainers();
            // Offline: feed locally (no GameplayNet to fan it out). Networked: GameplayNet's ClientRpc feeds every peer.
            if (net == null)
            {
                readModel?.PushFeed($"{Blank(info.BuyerName, "Someone")} bought {UpgradeName(info.UpgradeId)} (lv {info.NewLevel})");
                FeedChanged?.Invoke();
            }
        }

        private void OnSaleHost(SaleInfo info)
        {
            if (net == null && !string.IsNullOrEmpty(info.Seller))
            {
                readModel?.PushFeed($"{info.Seller} sold a ball (+{info.Value})");
                FeedChanged?.Invoke();
            }
        }

        private void OnDevToolUsed(int holder) { if (core != null) core.DevToolsUsed = true; }

        private void OnWinHost()
        {
            if (core == null) return;
            core.SiteCompleted = true;
            var data = BuildResults();
            if (net != null) net.HostBroadcastWin(in data);
            else { WinResults?.Invoke(data); } // offline: raise locally
        }

        // ------------------------------------------------------------------ Networked feed/win

        private void OnNetFeed(string line) { readModel?.PushFeed(line); FeedChanged?.Invoke(); }
        private void OnNetWin(ResultsData data) => WinResults?.Invoke(data);

        // ------------------------------------------------------------------ Capability application (brief §4.4)

        private void ApplyAllCapabilities()
        {
            if (core == null || controller == null || controller.Authority == null) return; // host only
            IReadOnlyList<PlayerAvatar> avatars = PlayerAvatar.All;
            core.PeakPlayers = Mathf.Max(core.PeakPlayers, avatars.Count);
            foreach (PlayerStat stat in core.AffectedStats())
            {
                float baseValue = BaseStat(stat);
                float effective = core.EffectiveStat(stat, baseValue);
                for (int i = 0; i < avatars.Count; i++)
                {
                    PlayerCapabilities caps = avatars[i] != null ? avatars[i].Capabilities : null;
                    if (caps != null) caps.ServerSet(stat, effective);
                }
            }
        }

        // The PlayerCapabilities base is 1 for the multiplier stats and 1 ball for carry (its serialized defaults; the
        // catalog's additive/multiplicative effects combine onto that base through GameplayCore.EffectiveStat).
        private static float BaseStat(PlayerStat stat) => stat == PlayerStat.CarryCapacity ? 1f : 1f;

        // The "funnel" named hook scales container intake openings (brief §4.3). 1 + accumulated amount.
        private void ApplyFunnelToContainers()
        {
            if (core == null) return;
            float scale = 1f + core.NamedHookAmount("funnel");
            IReadOnlyList<BallContainer> containers = ContainerRegistry.All;
            for (int i = 0; i < containers.Count; i++) if (containers[i] != null) containers[i].IntakeScale = scale;
        }

        // ------------------------------------------------------------------ Helpers

        private ResultsData BuildResults()
        {
            int ballsSold = 0;
            BallStore store = controller != null && controller.World != null ? controller.World.Store : null;
            if (store != null) ballsSold = store.StateTotal(BallStore.State.Stored);
            int levels = core != null ? core.UpgradeLevelsBought : 0;
            long money = core != null ? core.Money : 0;
            int rejects = core != null ? core.RejectsTotal : 0;
            return new ResultsData(Time.unscaledTime - startTime, ballsSold, money, levels, rejects);
        }

        private string UpgradeName(string id)
        {
            UpgradeDefinition u = upgradeCatalog != null ? upgradeCatalog.Find(id) : null;
            return u != null ? u.DisplayName : id;
        }

        private static string Blank(string s, string fallback) => string.IsNullOrEmpty(s) ? fallback : s;

        private static string LocalPlayerName()
        {
            PlayerAvatar local = PlayerAvatar.Local;
            PlayerSlot slot = local != null ? local.Slot : null;
            string name = slot != null ? slot.DisplayName : null;
            return string.IsNullOrEmpty(name) ? "You" : name;
        }
    }
}

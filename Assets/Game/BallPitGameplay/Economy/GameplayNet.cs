// GameplayNet: the thin NGO adapter that carries GameplayCore state over ONE gameplay NetworkObject, SEPARATE from
// NetPit (brief §3; design D1, D2). On the host it binds a GameplayCore and mirrors money / upgrade levels / win into
// NetworkVariables (late joiners get them automatically). Purchases arrive as a RequireOwnership=false ServerRpc and
// run the SAME core.TryBuy validation for every buyer including the host. Sales, rejects, purchases and win fan out as
// ClientRpcs for the HUD feed and the results panel. Offline (no GameplayNet) gameplay calls the core directly; a
// static Instance lets callers detect which path is live. Host-spawned by GameplaySession and registered in the kit
// NetworkPrefabsList (prefab BallPitGameplayNet.prefab).
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace BallPitGameplay
{
    public sealed class GameplayNet : NetworkBehaviour
    {
        /// <summary>The live instance while a networked gameplay session runs; null offline.</summary>
        public static GameplayNet Instance { get; private set; }

        private readonly NetworkVariable<long> money = new NetworkVariable<long>(0,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<bool> won = new NetworkVariable<bool>(false,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkList<int> levels = new NetworkList<int>(
            null, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private GameplayCore core;
        private UpgradeCatalog catalog;

        /// <summary>Replicated money pot (host core value, mirrored to clients).</summary>
        public long Money => money.Value;
        public bool Won => won.Value;

        /// <summary>Raised on every peer when money changes.</summary>
        public event System.Action MoneyChanged;
        /// <summary>Raised on every peer with a human-readable feed line (sale / reject / purchase).</summary>
        public event System.Action<string> Feed;
        /// <summary>Raised on every peer when the win results arrive.</summary>
        public event System.Action<ResultsData> WinResults;

        /// <summary>Host only: bind the core before/at spawn so its values seed the NetworkVariables.</summary>
        public void BindHost(GameplayCore gameplayCore)
        {
            core = gameplayCore;
            catalog = gameplayCore != null ? gameplayCore.Catalog : null;
        }

        public override void OnNetworkSpawn()
        {
            Instance = this;
            money.OnValueChanged += OnMoneyChanged;
            if (IsServer)
            {
                int count = catalog != null ? catalog.Count : 0;
                levels.Clear();
                for (int i = 0; i < count; i++) levels.Add(core != null ? core.LevelOf(catalog.At(i)?.Id) : 0);
                money.Value = core != null ? core.Money : 0;
                won.Value = core != null && core.SiteCompleted;
                if (core != null)
                {
                    core.MoneyChanged += HostPushMoney;
                    core.UpgradePurchased += HostPushPurchase;
                    core.BallSold += HostPushSale;
                }
            }
        }

        public override void OnNetworkDespawn()
        {
            money.OnValueChanged -= OnMoneyChanged;
            if (IsServer && core != null)
            {
                core.MoneyChanged -= HostPushMoney;
                core.UpgradePurchased -= HostPushPurchase;
                core.BallSold -= HostPushSale;
            }
            if (Instance == this) Instance = null;
        }

        private void OnMoneyChanged(long previous, long current) => MoneyChanged?.Invoke();

        // ------------------------------------------------------------------ Replicated level reads

        /// <summary>The team level of a catalog index as replicated (clients read this; the host could read the core).</summary>
        public int LevelAt(int catalogIndex) => (uint)catalogIndex < (uint)levels.Count ? levels[catalogIndex] : 0;

        /// <summary>The team level of an upgrade id as replicated.</summary>
        public int LevelOf(string id)
        {
            int i = catalog != null ? catalog.IndexOf(id) : -1;
            return i >= 0 ? LevelAt(i) : 0;
        }

        /// <summary>Clients learn the catalog from GameplaySession; the host already has it.</summary>
        public void SetCatalog(UpgradeCatalog c) { if (catalog == null) catalog = c; }

        // ------------------------------------------------------------------ Purchases

        /// <summary>Any peer: send a buy intent. The host validates and applies with core.TryBuy.</summary>
        public void RequestBuy(string upgradeId, string buyerName)
        {
            BuyUpgradeServerRpc(new FixedString64Bytes(Trim(upgradeId, 60)), SlotNames.ToFixed64(buyerName));
        }

        [ServerRpc(RequireOwnership = false)]
        private void BuyUpgradeServerRpc(FixedString64Bytes id, FixedString64Bytes buyerName)
        {
            if (core == null) return;
            string upgradeId = id.ToString();
            if (core.TryBuy(upgradeId, buyerName.ToString(), out _))
            {
                // HostPushPurchase already updated the levels list + money + broadcast the feed line.
            }
        }

        // ------------------------------------------------------------------ Host → clients mirroring + feed

        private void HostPushMoney()
        {
            if (!IsServer || core == null) return;
            money.Value = core.Money;
        }

        private void HostPushPurchase(PurchaseInfo info)
        {
            if (!IsServer) return;
            int i = catalog != null ? catalog.IndexOf(info.UpgradeId) : -1;
            if (i >= 0 && i < levels.Count) levels[i] = info.NewLevel;
            string label = catalog != null && catalog.Find(info.UpgradeId) != null ? catalog.Find(info.UpgradeId).DisplayName : info.UpgradeId;
            FeedClientRpc(new FixedString128Bytes(Trim($"{Blank(info.BuyerName, "Someone")} bought {label} (lv {info.NewLevel})", 128)));
        }

        private void HostPushSale(SaleInfo info)
        {
            if (!IsServer) return;
            // Sales fan out as a feed line only when attributed, to keep the feed readable at high sell rates.
            if (!string.IsNullOrEmpty(info.Seller))
                FeedClientRpc(new FixedString128Bytes(Trim($"{info.Seller} sold a ball (+{info.Value})", 128)));
        }

        [ClientRpc]
        private void FeedClientRpc(FixedString128Bytes line) => Feed?.Invoke(line.ToString());

        // ------------------------------------------------------------------ Win / results

        /// <summary>Host only: mark won and broadcast the results.</summary>
        public void HostBroadcastWin(in ResultsData data)
        {
            if (!IsServer) return;
            won.Value = true;
            WinClientRpc(data.TimeSeconds, data.BallsSold, data.Money, data.UpgradeLevels, data.Rejects);
        }

        [ClientRpc]
        private void WinClientRpc(float timeSeconds, int ballsSold, long moneyTotal, int upgradeLevels, int rejects)
        {
            WinResults?.Invoke(new ResultsData(timeSeconds, ballsSold, moneyTotal, upgradeLevels, rejects));
        }

        private static string Trim(string s, int max) => string.IsNullOrEmpty(s) ? "" : (s.Length > max ? s.Substring(0, max) : s);
        private static string Blank(string s, string fallback) => string.IsNullOrEmpty(s) ? fallback : s;
    }

    /// <summary>The win summary shown by the results panel (brief §4.6). Kept flat so it rides a ClientRpc.</summary>
    public readonly struct ResultsData
    {
        public readonly float TimeSeconds;
        public readonly int BallsSold;
        public readonly long Money;
        public readonly int UpgradeLevels;
        public readonly int Rejects;

        public ResultsData(float timeSeconds, int ballsSold, long money, int upgradeLevels, int rejects)
        {
            TimeSeconds = timeSeconds; BallsSold = ballsSold; Money = money; UpgradeLevels = upgradeLevels; Rejects = rejects;
        }
    }
}

// GameplayCore: the plain-C# host authority for the gameplay loop (brief §3; design D2). Holds the team money pot and
// the team-shared upgrade levels, and owns the pure rules: sell a ball, buy an upgrade (identical validation for every
// buyer including the host), and the integrity counters the later achievements framework reads. It runs IDENTICALLY
// offline, dev-host and session-host; the thin GameplayNet adapter mirrors its state over NGO. No Unity scene types, no
// NGO. When a BallPitGameState (adapter.GameState) is supplied, money + levels are stored THROUGH it so the existing
// save format persists them; offline it keeps its own fields.
using System.Collections.Generic;

namespace BallPitGameplay
{
    /// <summary>A completed sale, for the feed + achievements.</summary>
    public readonly struct SaleInfo
    {
        public readonly int Color;
        public readonly long Value;
        public readonly string Seller;     // display name of the last holder/thrower, or null
        public readonly bool Thrown;       // thrown (vs dropped)
        public readonly float ThrowDistance;

        public SaleInfo(int color, long value, string seller, bool thrown, float throwDistance)
        {
            Color = color; Value = value; Seller = seller; Thrown = thrown; ThrowDistance = throwDistance;
        }
    }

    /// <summary>A completed purchase, for the feed + capability application.</summary>
    public readonly struct PurchaseInfo
    {
        public readonly string UpgradeId;
        public readonly int NewLevel;
        public readonly long Cost;
        public readonly string BuyerName;

        public PurchaseInfo(string upgradeId, int newLevel, long cost, string buyerName)
        {
            UpgradeId = upgradeId; NewLevel = newLevel; Cost = cost; BuyerName = buyerName;
        }
    }

    public sealed partial class GameplayCore
    {
        private readonly EconomyConfig economy;
        private readonly UpgradeCatalog catalog;

        // Backing store: a BallPitGameState (persisted) when supplied, else internal fields (offline).
        private readonly BallPitSession.BallPitGameState state;
        private long offlineMoney;
        private readonly Dictionary<string, int> offlineLevels = new Dictionary<string, int>(System.StringComparer.Ordinal);

        // Integrity flags (brief §4.3) — persisted through the economy save participant blob.
        public bool DevToolsUsed;
        public int PeakPlayers;
        public int UpgradeLevelsBought;
        public int RejectsTotal;
        public bool SiteCompleted;

        /// <summary>Raised after money changes (sale or purchase).</summary>
        public event System.Action MoneyChanged;
        /// <summary>Raised after a successful sale.</summary>
        public event System.Action<SaleInfo> BallSold;
        /// <summary>Raised after a successful purchase (feed + apply team levels to players).</summary>
        public event System.Action<PurchaseInfo> UpgradePurchased;

        /// <summary>
        /// <paramref name="gameState"/> null = offline (internal money/levels). economy/catalog may be null (then
        /// sales award 0 and no upgrade is buyable), so the loop still runs for a bare-bones dev scene.
        /// </summary>
        public GameplayCore(EconomyConfig economy, UpgradeCatalog catalog, BallPitSession.BallPitGameState gameState)
        {
            this.economy = economy;
            this.catalog = catalog;
            state = gameState;
        }

        public UpgradeCatalog Catalog => catalog;
        public EconomyConfig Economy => economy;

        /// <summary>The team money pot.</summary>
        public long Money
        {
            get => state != null ? state.MoneyPot : offlineMoney;
            private set { if (state != null) state.MoneyPot = value; else offlineMoney = value; }
        }

        /// <summary>The team-shared level of an upgrade id (0 = not bought).</summary>
        public int LevelOf(string upgradeId)
        {
            if (string.IsNullOrEmpty(upgradeId)) return 0;
            if (state != null)
            {
                for (int i = 0; i < state.Upgrades.Count; i++)
                    if (string.Equals(state.Upgrades[i].Id, upgradeId, System.StringComparison.Ordinal))
                        return state.Upgrades[i].Level;
                return 0;
            }
            return offlineLevels.TryGetValue(upgradeId, out int l) ? l : 0;
        }

        // ------------------------------------------------------------------ Selling

        /// <summary>A correctly-sorted ball of <paramref name="color"/> sells for its configured value into the pot.</summary>
        public void AddSale(int color, string seller, bool thrown, float throwDistance)
        {
            long value = economy != null ? economy.Value(color) : 0;
            Money += value;
            BallSold?.Invoke(new SaleInfo(color, value, seller, thrown, throwDistance));
            MoneyChanged?.Invoke();
        }

        /// <summary>A wrong-colour or stray ball returned to the pit (integrity + feed; no money change).</summary>
        public void AddReject() => RejectsTotal++;

        // ------------------------------------------------------------------ Buying

        /// <summary>True if the upgrade can be bought now; out params explain the next cost and (on false) why not.</summary>
        public bool CanBuy(string upgradeId, out long cost, out string reason)
        {
            cost = 0; reason = null;
            UpgradeDefinition u = catalog != null ? catalog.Find(upgradeId) : null;
            if (u == null) { reason = "unknown upgrade"; return false; }
            int level = LevelOf(upgradeId);
            if (level >= u.MaxLevel) { reason = "already at max level"; return false; }
            cost = u.CostAt(level);
            if (!PrereqsMet(u, out string missing)) { reason = missing; return false; }
            if (Money < cost) { reason = "not enough money"; return false; }
            return true;
        }

        /// <summary>
        /// Attempts a purchase with the SAME validation for every buyer including the host (brief §3). On success:
        /// spends the cost, bumps the team level, bumps the integrity counter, raises UpgradePurchased + MoneyChanged.
        /// </summary>
        public bool TryBuy(string upgradeId, string buyerName, out string reason)
        {
            if (!CanBuy(upgradeId, out long cost, out reason)) return false;
            int newLevel = LevelOf(upgradeId) + 1;
            Money -= cost;
            SetLevel(upgradeId, newLevel);
            UpgradeLevelsBought++;
            UpgradePurchased?.Invoke(new PurchaseInfo(upgradeId, newLevel, cost, buyerName));
            MoneyChanged?.Invoke();
            return true;
        }

        private bool PrereqsMet(UpgradeDefinition u, out string missing)
        {
            missing = null;
            IReadOnlyList<UpgradePrereq> prereqs = u.Prereqs;
            if (prereqs == null) return true;
            for (int i = 0; i < prereqs.Count; i++)
            {
                UpgradePrereq pr = prereqs[i];
                if (pr.HasUpgrade && LevelOf(pr.upgradeId) < pr.minLevel)
                {
                    UpgradeDefinition dep = catalog != null ? catalog.Find(pr.upgradeId) : null;
                    missing = "requires " + (dep != null ? dep.DisplayName : pr.upgradeId) + " level " + pr.minLevel;
                    return false;
                }
                if (pr.requiresSiteCompleted && !SiteCompleted) { missing = "requires the pit completed"; return false; }
            }
            return true;
        }

        private void SetLevel(string upgradeId, int level)
        {
            if (state != null)
            {
                for (int i = 0; i < state.Upgrades.Count; i++)
                    if (string.Equals(state.Upgrades[i].Id, upgradeId, System.StringComparison.Ordinal))
                    { state.Upgrades[i] = new BallPitSession.UpgradeEntry(upgradeId, level); return; }
                state.Upgrades.Add(new BallPitSession.UpgradeEntry(upgradeId, level));
                return;
            }
            offlineLevels[upgradeId] = level;
        }
    }
}

// GameplayReadModel: the concrete read-only facade over the live gameplay context (brief §4.1, §4.6). Reads money and
// upgrade levels from the host core when offline/host, or from the replicated GameplayNet on a client; reads per-colour
// ball counts straight from the controller's PitWorld store (exact on every peer); resolves colour names from the
// palette service; and surfaces the local player's carry count + tool. It owns the bounded feed list that GameplaySession
// pushes lines into. Pure reads; it never mutates gameplay state and never touches the kit.
using System.Collections.Generic;
using BallPitFinal;
using UnityEngine;

namespace BallPitGameplay
{
    public sealed class GameplayReadModel : IGameplayReadModel
    {
        private const int MaxFeed = 8;

        private readonly BallPitFinalController controller;
        private readonly GameplayCore core;            // non-null offline + host
        private readonly PaletteService palette;
        private readonly System.Func<GameplayNet> net; // the live networked adapter, or null offline

        private readonly List<string> feed = new List<string>(MaxFeed);
        private readonly List<UpgradeView> upgradeRows = new List<UpgradeView>();

        public GameplayReadModel(BallPitFinalController controller, GameplayCore core, PaletteService palette, System.Func<GameplayNet> net)
        {
            this.controller = controller;
            this.core = core;
            this.palette = palette;
            this.net = net;
        }

        private GameplayNet Net => net != null ? net() : null;
        private BallStore Store => controller != null && controller.World != null ? controller.World.Store : null;

        public bool IsLive => controller != null && controller.IsPitWorldReady && Store != null;

        public long Money
        {
            get
            {
                GameplayNet n = Net;
                if (n != null) return n.Money;
                return core != null ? core.Money : 0;
            }
        }

        public bool Won
        {
            get
            {
                GameplayNet n = Net;
                if (n != null) return n.Won;
                return core != null && core.SiteCompleted;
            }
        }

        public int ColorCount => Store != null ? Store.Colors : 0;
        public int InPlay(int color) { var s = Store; return s != null && color >= 0 && color < s.Colors ? s.InPlay(color) : 0; }
        public int Sold(int color) { var s = Store; return s != null && color >= 0 && color < s.Colors ? s.CountOf(BallStore.State.Stored, color) : 0; }
        public int Total(int color) { var s = Store; return s != null && color >= 0 && color < s.Colors ? s.ColorTotal(color) : 0; }

        public float SoldFraction(int color)
        {
            int total = Total(color);
            return total <= 0 ? 0f : (float)Sold(color) / total;
        }

        public string ColorName(int color) => palette != null ? palette.NameOf(color) : "Color " + (color + 1);

        public int CarryCount
        {
            get
            {
                PlayerAvatar local = PlayerAvatar.Local;
                PlayerToolState tool = local != null ? local.GetComponent<PlayerToolState>() : null;
                if (tool != null) return tool.CarriedCount;
                // Offline/host fallback: held + pocketed in the local slot.
                PitAuthority a = controller != null ? controller.Authority : null;
                int slot = controller != null ? controller.LocalSlot : -1;
                if (a == null || (uint)slot >= PitAuthority.MaxPlayers) return controller != null && controller.LocalHeldBall >= 0 ? 1 : 0;
                int held = a.HeldId[slot] >= 0 ? 1 : 0;
                return held + a.HopperCount(slot);
            }
        }

        public string CurrentToolName
        {
            get
            {
                PlayerAvatar local = PlayerAvatar.Local;
                PlayerInteractor interactor = local != null ? local.GetComponent<PlayerInteractor>() : null;
                IPlayerTool t = interactor != null ? interactor.ActiveTool : null;
                return t is ArmsTool ? "Hands" : t != null ? t.GetType().Name : "Hands";
            }
        }

        public IReadOnlyList<UpgradeView> Upgrades
        {
            get
            {
                upgradeRows.Clear();
                UpgradeCatalog catalog = core != null ? core.Catalog : null;
                if (catalog == null) return upgradeRows;
                long money = Money; // replicated on a client, core value offline/host
                for (int i = 0; i < catalog.Count; i++)
                {
                    UpgradeDefinition u = catalog.At(i);
                    if (u == null) continue;
                    int level = LevelOf(u.Id); // replicated on a client (Net.LevelOf), core offline/host
                    long cost = u.CostAt(level);
                    // Buyability computed from the LOCALLY-VISIBLE money + levels, so the client shows the real state
                    // (its own core is not the authority; the host re-validates on the ServerRpc regardless).
                    string reason = null;
                    bool canBuy;
                    if (level >= u.MaxLevel) { reason = "already at max level"; canBuy = false; }
                    else if (!PrereqsMet(catalog, u, out reason)) canBuy = false;
                    else if (money < cost) { reason = "not enough money"; canBuy = false; }
                    else canBuy = true;
                    upgradeRows.Add(new UpgradeView(u.Id, u.DisplayName, u.Description, level, u.MaxLevel, cost, canBuy, reason));
                }
                return upgradeRows;
            }
        }

        // Prereq check against the locally-visible levels (mirrors GameplayCore.PrereqsMet; the host still re-validates).
        private bool PrereqsMet(UpgradeCatalog catalog, UpgradeDefinition u, out string missing)
        {
            missing = null;
            IReadOnlyList<UpgradePrereq> prereqs = u.Prereqs;
            if (prereqs == null) return true;
            for (int i = 0; i < prereqs.Count; i++)
            {
                UpgradePrereq pr = prereqs[i];
                if (pr.HasUpgrade && LevelOf(pr.upgradeId) < pr.minLevel)
                {
                    UpgradeDefinition dep = catalog.Find(pr.upgradeId);
                    missing = "requires " + (dep != null ? dep.DisplayName : pr.upgradeId) + " level " + pr.minLevel;
                    return false;
                }
                if (pr.requiresSiteCompleted && !Won) { missing = "requires the pit completed"; return false; }
            }
            return true;
        }

        private int LevelOf(string id)
        {
            GameplayNet n = Net;
            if (n != null) return n.LevelOf(id);
            return core != null ? core.LevelOf(id) : 0;
        }

        public IReadOnlyList<string> Feed => feed;

        /// <summary>GameplaySession pushes feed lines here (from core events offline, or GameplayNet.Feed networked).</summary>
        public void PushFeed(string line)
        {
            if (string.IsNullOrEmpty(line)) return;
            feed.Add(line);
            while (feed.Count > MaxFeed) feed.RemoveAt(0);
        }
    }
}

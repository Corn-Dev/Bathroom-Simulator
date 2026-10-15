// DepositDriver: the host-only deposit / reject / return / win driver (brief §4.2, §4.6; design D4, D6, D7). An
// IPitStepListener registered at ColliderSources order; its AfterPhysicsStep runs AFTER PitAuthority.Step on the host
// only (authority is non-null only on the host). Each step it tests every active ball against each container's top
// opening: right colour → Deposit + sell; wrong colour → ReturnToPit + reject. It also installs PitAuthority.LossHook
// so strays (below the kill height, or inside an OutOfBoundsVolume) return to the pit instead of being marked Lost
// (which would block the win). After deposits it checks the win condition (all balls Stored) and fires once.
// Created and owned by GameplaySession, which injects the core + attribution and registers/unregisters it with the
// controller around a world's lifetime. Host only; clients never run it.
using System.Collections.Generic;
using BallPitFinal;
using UnityEngine;

namespace BallPitGameplay
{
    public sealed class DepositDriver : IPitStepListener
    {
        private readonly BallPitFinalController controller;
        private readonly PitAuthority authority;
        private readonly PitWorld world;
        private readonly GameplayCore core;
        private readonly BallAttribution attribution;
        private readonly DropPointPicker dropPicker;
        private readonly System.Func<PitSite> activeSite;
        private readonly System.Action onWin;

        private struct Pending { public int Id; public int Color; public Vector3 Pos; public BallContainer Container; public bool Sell; }
        private readonly List<Pending> pending = new List<Pending>(64);
        private bool won;

        /// <summary>onWin fires exactly once, on the host, the step the last ball is sold.</summary>
        public DepositDriver(BallPitFinalController controller, GameplayCore core, BallAttribution attribution,
                             System.Func<PitSite> activeSite, System.Action onWin)
        {
            this.controller = controller;
            authority = controller != null ? controller.Authority : null;
            world = controller != null ? controller.World : null;
            this.core = core;
            this.attribution = attribution;
            this.activeSite = activeSite;
            this.onWin = onWin;
            if (world != null) dropPicker = new DropPointPicker(world);
            if (authority != null) authority.LossHook = HandleLoss; // strays return instead of becoming Lost
        }

        /// <summary>Removes the loss hook (called by GameplaySession on dispose) so the collapse sim is untouched.</summary>
        public void Detach()
        {
            if (authority != null && authority.LossHook == HandleLoss) authority.LossHook = null;
        }

        public void BeforePhysicsStep(float dt) { }

        public void AfterPhysicsStep(float dt)
        {
            if (authority == null || world == null || core == null) return; // host only
            IReadOnlyList<BallContainer> containers = ContainerRegistry.All;
            if (containers.Count == 0) { CheckWin(); return; }
            PitSite site = activeSite != null ? activeSite() : null;

            // Phase 1: decide, reading the live pool without mutating it (Deposit/ReturnToPit swap-remove from the pool).
            pending.Clear();
            bool anyOob = OutOfBoundsVolume.Active.Count > 0; // the stray sweep only runs when a volume exists
            ActivePhysics active = authority.Active;
            int n = active.Count;
            for (int i = 0; i < n; i++)
            {
                if (active.CarriedAt(i)) continue; // a held ball is not deposited
                int id = active.IdAt(i);
                Vector3 pos = active.PosAt(i);
                int ballColor = world.Store.color[id];
                bool claimed = false;
                for (int c = 0; c < containers.Count; c++)
                {
                    BallContainer container = containers[c];
                    if (container == null || !container.AppliesTo(site) || !container.TryIntake(pos)) continue;
                    pending.Add(new Pending { Id = id, Color = ballColor, Pos = pos, Container = container, Sell = container.ColorIndex == ballColor });
                    claimed = true;
                    break; // one container claims the ball
                }
                // Stray sweep (brief §4.2): an active ball resting inside an out-of-bounds volume returns to the pit.
                // Balls below the kill height are handled by LossHook instead (PitAuthority marks them Lost → we hook it).
                if (!claimed && anyOob && OutOfBoundsVolume.AnyContains(pos, site))
                    pending.Add(new Pending { Id = id, Color = ballColor, Pos = pos, Container = null, Sell = false });
            }

            // Phase 2: apply. Safe now because the forward read above is complete.
            for (int p = 0; p < pending.Count; p++)
            {
                Pending d = pending[p];
                if (d.Sell) Sell(d.Id, d.Color, d.Pos, d.Container);
                else Reject(d.Id, d.Pos, d.Container);
            }
            CheckWin();
        }

        private void Sell(int id, int color, Vector3 pos, BallContainer container)
        {
            authority.Deposit(id);
            string seller = null; bool thrown = false; float dist = 0f;
            attribution?.Consume(id, pos, out seller, out thrown, out dist);
            core.AddSale(color, seller, thrown, dist);
            container.NotifySold(pos);
        }

        private void Reject(int id, Vector3 pos, BallContainer container)
        {
            authority.ReturnToPit(id, dropPicker != null ? dropPicker.Pick() : pos);
            core.AddReject();
            container?.NotifyRejected(pos); // null for a stray-volume return (no container involved)
        }

        // PitAuthority.LossHook: a stray about to be marked Lost. Return it to the pit and report handled, so the win
        // condition (every ball Stored) remains reachable (game-design §10). Also returns balls inside an
        // OutOfBoundsVolume on the active site (checked by GameplaySession's stray sweep, which also calls ReturnToPit).
        private bool HandleLoss(int id)
        {
            if (world == null || dropPicker == null) return false;
            authority.ReturnToPit(id, dropPicker.Pick());
            core.AddReject();
            return true;
        }

        private void CheckWin()
        {
            if (won || world == null) return;
            if (world.Store.StateTotal(BallStore.State.Stored) != world.Store.Count) return;
            won = true;
            onWin?.Invoke();
        }
    }
}

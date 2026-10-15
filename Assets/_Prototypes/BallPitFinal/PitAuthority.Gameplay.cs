// PitAuthority.Gameplay: additive, host-only gameplay primitives for deliverable B (brief §4.2, §4.4; design D5, D6).
// ALL members here are additive to the existing partial class and default off; none change the collapse simulation.
//   PocketHeld       — carry capacity > 1: move the held ball into the player's OWN hopper (slot 0-3, so NetPit join
//                      sync stays correct) and clear the hand.
//   PromoteFromHopper— throw/release the next: pour one of the player's hopper balls back into the hand as a held ball.
//   ReturnToPit      — wrong colour / stray: Demote → ToGone(Hopper) → promote back to Active at a drop point, all
//                      within ONE call, so no ball EVER lingers in State.Hopper across a step except a player hopper.
// Every transition raises the matching Events? call exactly as the existing methods do, so clients mirror correctly.
using UnityEngine;

namespace BallPitFinal
{
    public sealed partial class PitAuthority
    {
        /// <summary>
        /// Carry-capacity support (host): moves the ball the player currently holds into that player's own hopper
        /// (0-3) and frees the hand, so the player can grab another. Returns false when the player holds nothing or the
        /// hopper is at the ledger's limit. The ball stays in a PLAYER hopper, which NetPit's join sync enumerates.
        /// </summary>
        public bool PocketHeld(int player)
        {
            if ((uint)player >= MaxPlayers) return false;
            int id = HeldId[player];
            if (id < 0) return false;
            HeldId[player] = -1;
            if (Active.IsActive(id)) Active.Demote(id); // a held ball is a kinematic active body
            W.ToGone(id, BallStore.State.Hopper);
            hopper[player].Add(id);
            Events?.Gone(id, BallStore.State.Hopper, player);
            return true;
        }

        /// <summary>
        /// Promotes one ball from the player's hopper back into the hand as a Held ball at <paramref name="at"/> (host).
        /// Used when a held ball is thrown and the next carried ball should take its place. Returns false when the hand
        /// is occupied, the hopper is empty, or the active pool is full. Slot 0 keeps its parked prefix untouched.
        /// </summary>
        public bool PromoteFromHopper(int player, Vector3 at)
        {
            if ((uint)player >= MaxPlayers || HeldId[player] >= 0) return false;
            var h = hopper[player];
            int floor = player == 0 ? parkedCount : 0;
            if (h.Count <= floor) return false;
            Active.MaxActive = S.maxActive;
            if (!Active.CanPromote) return false;
            int id = h[h.Count - 1];
            Vector3 p = W.Q.Snap(at);
            if (!Active.Promote(id, p, Vector3.zero, carried: true)) return false;
            h.RemoveAt(h.Count - 1);
            W.ToLive(id, BallStore.State.Held, p);
            HeldId[player] = id;
            Events?.Held(id, player, W.Q.Encode(p));
            return true;
        }

        /// <summary>
        /// Returns a ball to the pit at <paramref name="dropPoint"/> in ONE step (host): Demote (if active) →
        /// ToGone(Hopper) → promote back to Active at the drop point. Because the whole sequence runs inside this call,
        /// the ball is never left in State.Hopper across a physics step (NetPit join sync only enumerates player
        /// hoppers 0-3). Used for wrong-colour deposits and out-of-bounds strays (via LossHook). Falls back to a resting
        /// placement if the active pool is full, so the ball is never dropped from the ledger. Clears any hand holding it.
        /// </summary>
        public void ReturnToPit(int id, Vector3 dropPoint)
        {
            if ((uint)id >= (uint)Store.Count) return;
            for (int p = 0; p < MaxPlayers; p++) if (HeldId[p] == id) HeldId[p] = -1;
            if (Active.IsActive(id)) Active.Demote(id);
            // Transient Hopper state within this call only (removed before the next step by the promote below).
            W.ToGone(id, BallStore.State.Hopper);
            Vector3 p0 = W.Q.Snap(dropPoint);
            Active.MaxActive = S.maxActive;
            if (Active.CanPromote && Active.Promote(id, p0, new Vector3(0f, -1f, 0f), carried: false))
            {
                W.ToLive(id, BallStore.State.Active, p0);
                Events?.Spawned(id, W.Q.Encode(p0));
                TotalSpawned++;
                return;
            }
            // Pool full: settle it as a resting ball at the drop point rather than leaving it in Hopper.
            W.ToResting(id, p0);
            Events?.Froze(id, W.Q.Encode(p0));
        }
    }
}

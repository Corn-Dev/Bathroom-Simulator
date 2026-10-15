// Restore and hopper-parking API for PitAuthority (new partial; the three edits inside the copied PitAuthority.cs are
// listed in HANDOFF.md). Design: 04 §9.1, §5, §6.3; 06 P4. RestoreActive / RestoreHopperBall run only on a freshly
// built world before NetPit.StartHost (Events == null). Parked tanks of absent players live in slot 0's protected
// prefix hopper[0][0 .. parkedCount), which Dump never pours from; their state stays Hopper, so NetPit's join sync and
// VerifyAll (both iterate whole lists) are unchanged.

using System.Collections.Generic;
using UnityEngine;

namespace BallPitFinal
{
    public sealed partial class PitAuthority
    {
        /// <summary>Length of slot 0's protected prefix (parked tanks of absent players).</summary>
        public int ParkedHopperCount => parkedCount;

        /// <summary>Balls the player can dump: the whole list, minus the parked prefix for slot 0. Out of range: 0.</summary>
        public int UsableHopperCount(int player)
        {
            if ((uint)player >= MaxPlayers) return 0;
            return hopper[player].Count - (player == 0 ? parkedCount : 0);
        }

        /// <summary>
        /// Restore only. A saved Active ball (still Resting at its fill pose) resumes as a free body at pose, from rest
        /// (velocity is not saved). Pool full (maxActive lower than at save time): it rests at pose and is queued for
        /// a support test, so it thaws once budget frees. True when applied either way; false for a bad id or a ball
        /// that is not Resting.
        /// </summary>
        public bool RestoreActive(int id, Vector3 pose)
        {
            RequireRestorePhase(nameof(RestoreActive));
            if ((uint)id >= (uint)Store.Count || Store.state[id] != BallStore.State.Resting) return false;
            if (Active.CanPromote && Active.Promote(id, pose, Vector3.zero, carried: false))
            {
                W.ToLive(id, BallStore.State.Active, pose);
                return true;
            }
            W.ToResting(id, pose);
            queue.Push(id, pose, StepIndex);
            return true;
        }

        /// <summary>
        /// Restore only. A saved Hopper ball (still Resting at its fill pose) goes into the player's hopper list.
        /// parked: appended to slot 0's protected prefix; the player must be 0 and slot 0 must not hold own balls yet,
        /// so parked balls are restored first and the host's own after them (both pure appends). False for a bad
        /// player, id or state; out-of-order parking throws.
        /// </summary>
        public bool RestoreHopperBall(int player, int id, bool parked)
        {
            RequireRestorePhase(nameof(RestoreHopperBall));
            if ((uint)player >= MaxPlayers || (parked && player != 0)) return false;
            if ((uint)id >= (uint)Store.Count || Store.state[id] != BallStore.State.Resting) return false;
            if (parked && hopper[0].Count != parkedCount)
                throw new System.InvalidOperationException("RestoreHopperBall: parked balls must be restored before slot 0's own balls");
            W.ToGone(id, BallStore.State.Hopper);
            hopper[player].Add(id);
            if (parked) parkedCount++;
            return true;
        }

        /// <summary>
        /// Moves all of hopper[fromPlayer] (1..3) to the end of slot 0's protected prefix in one O(n) rebuild
        /// (prefix + from + own), keeping their order, and appends the moved ids to moved. States stay Hopper and no
        /// events are emitted: clients do not track which list a hopper ball is in. fromPlayer 0 or out of range: 0.
        /// </summary>
        public int ParkHopper(int fromPlayer, List<int> moved)
        {
            if (fromPlayer <= 0 || fromPlayer >= MaxPlayers) return 0;
            List<int> from = hopper[fromPlayer];
            int n = from.Count;
            if (n == 0) return 0;
            hopper[0].InsertRange(parkedCount, from);
            parkedCount += n;
            moved?.AddRange(from);
            from.Clear();
            return n;
        }

        /// <summary>
        /// Moves the parked ids that select accepts to the end of hopper[toPlayer] (for slot 0: after the host's own
        /// balls), keeping their order, in one O(n) pass over slot 0, and appends them to moved. No events.
        /// toPlayer out of range or select null: 0.
        /// </summary>
        public int UnparkHopper(int toPlayer, System.Predicate<int> select, List<int> moved)
        {
            if ((uint)toPlayer >= MaxPlayers || select == null || parkedCount == 0) return 0;
            List<int> h0 = hopper[0];
            var picked = new List<int>();
            int keep = 0;
            for (int i = 0; i < parkedCount; i++)
            {
                int id = h0[i];
                if (select(id)) picked.Add(id); else h0[keep++] = id; // keep <= i: compaction never overwrites unread ids
            }
            int n = picked.Count;
            if (n == 0) return 0;
            int own = h0.Count - parkedCount;
            for (int i = 0; i < own; i++) h0[keep + i] = h0[parkedCount + i]; // own balls follow the kept prefix
            h0.RemoveRange(keep + own, n);
            parkedCount = keep;
            hopper[toPlayer].AddRange(picked);
            moved?.AddRange(picked);
            return n;
        }

        /// <summary>Whether a support test is pending for this ball (save capture). Bad id: false.</summary>
        public bool IsQueuedForSupport(int id) => (uint)id < (uint)Store.Count && queue.Contains(id);

        /// <summary>Queues a support test for a Resting ball (restores the saved pending queue). Otherwise a no-op.</summary>
        public void QueueSupportTest(int id)
        {
            if ((uint)id >= (uint)Store.Count || Store.state[id] != BallStore.State.Resting) return;
            queue.Push(id, Store.pos[id], StepIndex);
        }

        private void RequireRestorePhase(string member)
        {
            if (Events != null)
                throw new System.InvalidOperationException(member + " is restore-only: call it before NetPit.StartHost (Events must be null)");
        }
    }
}

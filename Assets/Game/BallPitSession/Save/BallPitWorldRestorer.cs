// Applies a decoded world save to a freshly built host world, before NetPit.StartHost: authority.Events is still null,
// so nothing is replicated, and joining clients get the restored state through NetPit's join sync over ModifiedAt.
// Order (04 §6.3): 1 fill check, 2 records (ToResting / RestoreActive / ToGone), 3 hoppers (HopperOwnership.Restore),
// 4 pending support tests, 5 ledger cross-check + VerifyAll, 6 gameplay state and participants. A failure in steps 1-5
// leaves a half-restored world, so it is fatal for the session (returned as log detail; the slot on disk is not
// overwritten because the session never reaches InGame); step 6 problems are only logged. Design: 04 §6.3, §5.4,
// §4.4, §11; 06 G10.

using System;
using System.Collections.Generic;
using BallPitFinal;
using MultiplayerKit;
using UnityEngine;

namespace BallPitSession
{
    public static class BallPitWorldRestorer
    {
        /// <summary>
        /// Null on success, else a log detail (BallPitGameAdapter fails the session with "This save couldn't be
        /// restored."). game is the adapter's live state, filled in place; participants get their blobs in step 6.
        /// </summary>
        public static string Apply(BallPitWorldSave save, PitWorld w, PitAuthority a, HopperOwnership ownership,
                                   BallPitGameState game, PlayerId localPlayer, List<IBallPitSaveParticipant> participants)
        {
            if (save == null || w == null || a == null || ownership == null)
                return "restore: missing save, world, authority or hopper ownership";
            int restedActive;
            try
            {
                string error = CheckFill(save, w, a);                                // 1
                if (error != null) return error;
                error = ApplyRecords(save, w, a);                                    // 2
                if (error != null) return error;
                error = ownership.Restore(save.Hoppers, localPlayer, a);             // 3
                if (error != null) return error;
                int[] pending = save.PendingSupport;                                 // 4: no-op unless Resting
                for (int i = 0; pending != null && i < pending.Length; i++) a.QueueSupportTest(pending[i]);
                error = CrossCheck(save, w, a, out restedActive);                    // 5
                if (error != null) return error;
            }
            catch (InvalidOperationException e) // RestoreActive / RestoreHopperBall misuse (04 §11): fatal like a mismatch
            {
                return e.Message;
            }
            if (restedActive != 0)
                Debug.LogWarning($"[BallPitSession] restore: {restedActive} saved-active balls resumed resting (active pool full)");

            RestoreGame(save, w.Store, game, participants);                          // 6
            Debug.Log($"[BallPitSession] restored {save.RecordCount:n0} modified balls, " +
                      $"{w.Store.StateTotal(BallStore.State.Hopper):n0} hopper balls ({a.ParkedHopperCount:n0} parked), " +
                      $"{a.QueueLength:n0} pending support tests");
            return null;
        }

        // 1, plus the preconditions. Ball ids are only meaningful against the identical fill.
        private static string CheckFill(BallPitWorldSave save, PitWorld w, PitAuthority a)
        {
            if (a.Events != null) return "restore must run before NetPit.StartHost (authority events are attached)";
            if (w.ModifiedCount != 0) return "the world is not freshly built (" + w.ModifiedCount + " balls already modified)";
            if (w.Store.Count != save.Fill.BallCount || w.FillChecksum != save.Fill.FillChecksum)
                return $"fill mismatch: this build filled {w.Store.Count} balls (checksum {w.FillChecksum:X16}), the save has " +
                       $"{save.Fill.BallCount} ({save.Fill.FillChecksum:X16}); the fill code or the baked Random fill differs from the saving build";
            int colors = w.Store.Colors;
            if (save.LedgerStates != BallStore.StateCount || save.LedgerColors != colors ||
                save.Ledger == null || save.Ledger.Length != BallStore.StateCount * colors)
                return "ledger shape " + save.LedgerStates + "x" + save.LedgerColors + " does not match " +
                       BallStore.StateCount + " states x " + colors + " colors";
            return null;
        }

        // 2, in file order. Hopper records wait for step 3, which needs their owner.
        private static string ApplyRecords(BallPitWorldSave save, PitWorld w, PitAuthority a)
        {
            int count = w.Store.Count;
            for (int i = 0; i < save.RecordCount; i++)
            {
                int id = save.RecordIds[i];
                var state = (BallStore.State)save.RecordStates[i];
                if ((uint)id >= (uint)count) return "record " + i + ": ball " + id + " is out of range";
                switch (state)
                {
                    case BallStore.State.Resting:
                        w.ToResting(id, w.Q.Decode(save.RecordPoses[i]));
                        break;
                    case BallStore.State.Active:
                        if (!a.RestoreActive(id, w.Q.Decode(save.RecordPoses[i])))
                            return "record " + i + ": ball " + id + " could not resume active (not a fresh Resting ball)";
                        break;
                    case BallStore.State.Stored:
                    case BallStore.State.Lost:
                        w.ToGone(id, state);
                        break;
                    case BallStore.State.Hopper:
                        break;
                    default:
                        return "record " + i + ": ball " + id + " has state " + save.RecordStates[i];
                }
            }
            return null;
        }

        // 5. The saved ledger has Held folded into Active, and RestoreActive rests a saved-active ball when the pool is
        // smaller than at save time (maxActive is not saved), so only the Resting + Active sum must match exactly.
        // restedActive counts those pool-full fallbacks.
        private static string CrossCheck(BallPitWorldSave save, PitWorld w, PitAuthority a, out int restedActive)
        {
            BallStore store = w.Store;
            int[] ledger = save.Ledger;
            int colors = save.LedgerColors;
            restedActive = 0;
            for (int c = 0; c < colors; c++)
            {
                string error = Exact(store, ledger, colors, BallStore.State.Stored, c) ??
                               Exact(store, ledger, colors, BallStore.State.Lost, c) ??
                               Exact(store, ledger, colors, BallStore.State.Hopper, c);
                if (error != null) return error;
                int savedActive = ledger[(int)BallStore.State.Active * colors + c];
                int savedPile = ledger[(int)BallStore.State.Resting * colors + c] + savedActive;
                int active = store.CountOf(BallStore.State.Active, c);
                int pile = store.CountOf(BallStore.State.Resting, c) + active;
                if (pile != savedPile) return "ledger mismatch: Resting + Active of color " + c + " restored " + pile + ", saved " + savedPile;
                int held = store.CountOf(BallStore.State.Held, c);
                if (held != 0) return "ledger mismatch: " + held + " Held balls of color " + c + " after the restore";
                restedActive += savedActive - active;
            }
            return a.VerifyAll(out string verify) ? null : "VerifyAll: " + verify;
        }

        private static string Exact(BallStore store, int[] ledger, int colors, BallStore.State s, int c)
        {
            int restored = store.CountOf(s, c), saved = ledger[(int)s * colors + c];
            return restored == saved ? null : "ledger mismatch: " + s + " of color " + c + " restored " + restored + ", saved " + saved;
        }

        // 6, never fatal. The placeholders go into the adapter's live state (a deep copy, so it does not share arrays
        // with the decoded save). Each blob goes to its registered participant, or stays unclaimed (no participant, or
        // its Restore threw) so the next save writes it back.
        private static void RestoreGame(BallPitWorldSave save, BallStore store, BallPitGameState game,
                                        List<IBallPitSaveParticipant> participants)
        {
            if (game == null) return;
            BallPitGameState saved = save.Game != null ? save.Game.Clone() : new BallPitGameState();
            game.MoneyPot = saved.MoneyPot;
            game.Upgrades.Clear();
            game.Upgrades.AddRange(saved.Upgrades);
            game.Containers.Clear();
            game.Containers.AddRange(saved.Containers);
            game.UnclaimedExtensions.Clear();
            for (int i = 0; i < saved.UnclaimedExtensions.Count; i++)
            {
                (string tag, byte[] data) = saved.UnclaimedExtensions[i];
                IBallPitSaveParticipant p = FindParticipant(participants, tag);
                if (p != null)
                {
                    try
                    {
                        p.Restore(data ?? Array.Empty<byte>());
                        continue;
                    }
                    catch (Exception e)
                    {
                        Debug.LogError("[BallPitSession] restore: save participant '" + tag + "' threw; its data is kept for the next save");
                        Debug.LogException(e);
                    }
                }
                game.UnclaimedExtensions.Add((tag, data));
            }
            CheckContainers(game, store);
        }

        private static IBallPitSaveParticipant FindParticipant(List<IBallPitSaveParticipant> participants, string tag)
        {
            for (int i = 0; participants != null && i < participants.Count; i++)
                if (participants[i] != null && string.Equals(participants[i].Tag, tag, StringComparison.Ordinal)) return participants[i];
            return null;
        }

        // Container bookkeeping is gameplay data and the ledger is the truth, so a mismatch is one Warning only. Skipped
        // until a container system keeps entries at all (today Stored balls have no container entries).
        private static void CheckContainers(BallPitGameState game, BallStore store)
        {
            if (game.Containers.Count == 0) return;
            var perColor = new long[store.Colors];
            for (int i = 0; i < game.Containers.Count; i++)
            {
                ContainerEntry e = game.Containers[i];
                if (e.Color < perColor.Length) perColor[e.Color] += e.Stored;
            }
            var diff = new List<string>();
            for (int c = 0; c < perColor.Length; c++)
            {
                int stored = store.CountOf(BallStore.State.Stored, c);
                if (perColor[c] != stored) diff.Add("color " + c + ": containers " + perColor[c] + ", ledger " + stored);
            }
            if (diff.Count > 0)
                Debug.LogWarning("[BallPitSession] restore: container contents disagree with the Stored ledger (" + string.Join("; ", diff) + "); the ledger is the truth");
        }
    }
}

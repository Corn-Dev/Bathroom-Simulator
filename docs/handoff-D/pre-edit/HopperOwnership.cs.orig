// PlayerId <-> hopper tank bookkeeping, host only. The single owner of "which player owns which hopper balls":
// connected players' tanks sit in their NetPit slot list, absent players' tanks are parked in slot 0's protected prefix
// (PitAuthority stays identity-agnostic). Design: 04 §5.2-5.5, §6.3 step 3, §4.2 step 4; 06 G4. Driven by
// PitSlotMirror (join/leave), BallPitWorldRestorer (Restore), BallPitSaveWriter (BuildEntries) and the adapter
// (Release/ReleaseAll). Every list change goes through IHopperOps, so tests run it against a fake (06 T20).

using System;
using System.Collections.Generic;
using BallPitFinal;
using MultiplayerKit;
using UnityEngine;

namespace BallPitSession
{
    public sealed class HopperOwnership
    {
        /// <summary>Absent owners that keep a parked tank; one more releases the owner seen longest ago (04 §5.5).</summary>
        public const int MaxParkedOwners = 16;

        private readonly PlayerId[] slotOwner = new PlayerId[PitAuthority.MaxPlayers];              // [0] = host
        private readonly Dictionary<int, PlayerId> parkedOwner = new Dictionary<int, PlayerId>();    // every parked ball
        private readonly Dictionary<PlayerId, long> lastSeenUtc = new Dictionary<PlayerId, long>();  // owners with parked balls
        private readonly List<int> moved = new List<int>(256);

        /// <summary>PlayerId in a slot 0..3 (None when free or unknown).</summary>
        public PlayerId SlotOwner(int slot) => (uint)slot < (uint)slotOwner.Length ? slotOwner[slot] : PlayerId.None;

        /// <summary>Absent players that currently have a parked tank.</summary>
        public int ParkedOwnerCount => lastSeenUtc.Count;

        /// <summary>Slot 0's owner, the host. Set at session start (Restore sets it too).</summary>
        public void SetHost(PlayerId host) { slotOwner[0] = host; }

        public void Clear()
        {
            for (int i = 0; i < slotOwner.Length; i++) slotOwner[i] = PlayerId.None;
            parkedOwner.Clear();
            lastSeenUtc.Clear();
            moved.Clear();
        }

        // ------------------------------------------------------------------ Restore (04 §5.4 row 1, §6.3 step 3)

        public string Restore(IReadOnlyList<HopperEntry> entries, PlayerId localHost, PitAuthority authority)
        {
            if (authority == null) return "hopper restore: no authority";
            return Restore(entries, localHost, new PitAuthorityHopperOps(authority));
        }

        /// <summary>
        /// Before NetPit.StartHost. The local host's entry, the save's FlagHost entry when no entry matches the local
        /// host (save loaded by another account), and orphans (player 0) become the host's own tank; every other entry
        /// is parked under its player. Parked balls are restored first, then own balls, so both are pure appends to
        /// slot 0. Returns null, or a log detail on the first failure (fatal for the load).
        /// </summary>
        public string Restore(IReadOnlyList<HopperEntry> entries, PlayerId localHost, IHopperOps ops)
        {
            Clear();
            slotOwner[0] = localHost;
            if (entries == null || entries.Count == 0) return null;

            bool hostHasEntry = false;
            for (int i = 0; i < entries.Count && localHost.IsValid; i++)
                if (entries[i] != null && entries[i].PlayerId == localHost.Value) { hostHasEntry = true; break; }

            for (int pass = 0; pass < 2; pass++)
            {
                bool parkedPass = pass == 0;
                for (int i = 0; i < entries.Count; i++)
                {
                    HopperEntry e = entries[i];
                    if (e == null || e.Ids == null) continue;
                    if (IsOwnOnRestore(e, localHost, hostHasEntry) == parkedPass) continue;
                    var owner = new PlayerId(e.PlayerId);
                    for (int k = 0; k < e.Ids.Length; k++)
                    {
                        int id = e.Ids[k];
                        if (!ops.RestoreHopperBall(0, id, parkedPass))
                            return "hopper restore: ball " + id + " (entry " + i + ") is not a restorable Resting ball";
                        if (parkedPass) parkedOwner[id] = owner;
                    }
                    if (parkedPass && e.Ids.Length > 0) lastSeenUtc[owner] = e.LastSeenUtcTicks;
                }
            }
            EnforceCap(ops);
            return null;
        }

        private static bool IsOwnOnRestore(HopperEntry e, PlayerId localHost, bool hostHasEntry)
        {
            if (e.PlayerId == 0UL) return true;                                  // orphaned contents
            if (localHost.IsValid && e.PlayerId == localHost.Value) return true; // the host's own tank
            return !hostHasEntry && (e.Flags & HopperEntry.FlagHost) != 0;       // saved by another account
        }

        // ------------------------------------------------------------------ Join / leave (04 §5.4 rows 2-3)

        /// <summary>
        /// A client got NetPit slot 1..3. A returning player gets exactly their parked tank back, in order; anyone
        /// else starts with an empty tank. Returns the number of balls handed back.
        /// </summary>
        public int OnSlotJoined(int slot, PlayerId player, IHopperOps ops)
        {
            if (slot <= 0 || slot >= slotOwner.Length) { Debug.LogError($"[BallPitSession] join into invalid hopper slot {slot}"); return 0; }
            slotOwner[slot] = player;
            int leftover = ops.Hopper(slot).Count;
            if (leftover > 0)
                Debug.LogWarning($"[BallPitSession] slot {slot} still holds {leftover:n0} hopper balls as player {player} joins; they stay in that tank");
            if (!player.IsValid || !lastSeenUtc.ContainsKey(player)) return 0;

            moved.Clear();
            int n = ops.UnparkHopper(slot, id => parkedOwner.TryGetValue(id, out PlayerId o) && o == player, moved);
            for (int i = 0; i < moved.Count; i++) parkedOwner.Remove(moved[i]);
            lastSeenUtc.Remove(player);
            Debug.Log($"[BallPitSession] player {player} rejoined in slot {slot}: {n:n0} parked hopper balls returned");
            return n;
        }

        /// <summary>
        /// The client in slot 1..3 left: its tank is parked under its player, so nobody else inherits it. Balls of an
        /// unknown owner go straight to the host's usable tank. Past MaxParkedOwners the owner seen longest ago is
        /// released to the host. Returns the number of balls moved out of the slot.
        /// </summary>
        public int OnSlotLeft(int slot, long nowUtcTicks, IHopperOps ops)
        {
            if (slot <= 0 || slot >= slotOwner.Length) { Debug.LogError($"[BallPitSession] leave from invalid hopper slot {slot}"); return 0; }
            PlayerId owner = slotOwner[slot];
            slotOwner[slot] = PlayerId.None;
            moved.Clear();
            int n = ops.ParkHopper(slot, moved);
            if (n == 0) return 0;

            if (!owner.IsValid)
            {
                // Nobody to return them to. The just-parked ids are the only prefix ids without an owner entry.
                ops.UnparkHopper(0, id => !parkedOwner.ContainsKey(id), null);
                Debug.Log($"[BallPitSession] slot {slot} left without a known player: {n:n0} hopper balls moved to the host's tank");
                return n;
            }
            for (int i = 0; i < moved.Count; i++) parkedOwner[moved[i]] = owner;
            lastSeenUtc[owner] = nowUtcTicks;
            Debug.Log($"[BallPitSession] player {owner} left slot {slot}: {n:n0} hopper balls parked");
            EnforceCap(ops);
            return n;
        }

        // ------------------------------------------------------------------ Release (04 §5.5)

        /// <summary>Hands an absent player's parked tank to the host's usable tank. Returns the count moved.</summary>
        public int Release(PlayerId player, IHopperOps ops)
        {
            if (!lastSeenUtc.ContainsKey(player)) return 0;
            moved.Clear();
            int n = ops.UnparkHopper(0, id => parkedOwner.TryGetValue(id, out PlayerId o) && o == player, moved);
            for (int i = 0; i < moved.Count; i++) parkedOwner.Remove(moved[i]);
            lastSeenUtc.Remove(player);
            return n;
        }

        /// <summary>Hands every parked tank to the host's usable tank. Returns the count moved.</summary>
        public int ReleaseAll(IHopperOps ops)
        {
            int n = ops.UnparkHopper(0, _ => true, null);
            parkedOwner.Clear();
            lastSeenUtc.Clear();
            return n;
        }

        private void EnforceCap(IHopperOps ops)
        {
            while (lastSeenUtc.Count > MaxParkedOwners)
            {
                PlayerId oldest = PlayerId.None;
                long oldestSeen = long.MaxValue;
                bool found = false;
                foreach (KeyValuePair<PlayerId, long> kv in lastSeenUtc)
                {
                    if (found && kv.Value >= oldestSeen) continue;
                    oldest = kv.Key; oldestSeen = kv.Value; found = true;
                }
                int n = Release(oldest, ops);
                Debug.Log($"[BallPitSession] more than {MaxParkedOwners} absent players with tanks: released {n:n0} parked balls of player {oldest} to the host's tank");
            }
        }

        // ------------------------------------------------------------------ Save (04 §4.2 step 4, §5.4 row 4)

        public List<HopperEntry> BuildEntries(PitAuthority authority, long nowUtcTicks) =>
            BuildEntries(new PitAuthorityHopperOps(authority), nowUtcTicks);

        /// <summary>
        /// One entry for the host (FlagHost: its own balls after the parked prefix), one per occupied client slot, one
        /// per parked owner (FlagParked, prefix order). Every Hopper-state ball is written exactly once. Unowned slot
        /// contents and prefix balls without an owner (a bug, logged at Error) go into the host's entry; a repeated
        /// player id is merged into one entry, so the HOPR read rules (04 §3.2) always hold.
        /// </summary>
        public List<HopperEntry> BuildEntries(IHopperOps ops, long nowUtcTicks)
        {
            var result = new List<HopperEntry>();
            var lists = new List<List<int>>();
            var index = new Dictionary<ulong, int>();
            ulong host = slotOwner[0].Value;

            result.Add(new HopperEntry { PlayerId = host, Flags = HopperEntry.FlagHost, LastSeenUtcTicks = nowUtcTicks });
            lists.Add(new List<int>());
            index[host] = 0;
            index[0UL] = 0; // player 0 means "the host's" on load

            int EntryFor(ulong player, byte flags, long seen)
            {
                if (index.TryGetValue(player, out int i))
                {
                    HopperEntry e = result[i];
                    if (i != 0) e.Flags |= flags; // FlagHost stays unique to entry 0
                    if (seen > e.LastSeenUtcTicks) e.LastSeenUtcTicks = seen;
                    return i;
                }
                index[player] = result.Count;
                result.Add(new HopperEntry { PlayerId = player, Flags = flags, LastSeenUtcTicks = seen });
                lists.Add(new List<int>());
                return result.Count - 1;
            }

            IReadOnlyList<int> h0 = ops.Hopper(0);
            int parked = Math.Min(ops.ParkedHopperCount, h0.Count);
            for (int i = parked; i < h0.Count; i++) lists[0].Add(h0[i]);

            for (int s = 1; s < slotOwner.Length; s++)
            {
                IReadOnlyList<int> h = ops.Hopper(s);
                if (!slotOwner[s].IsValid && h.Count == 0) continue;
                int e = EntryFor(slotOwner[s].Value, 0, nowUtcTicks);
                for (int i = 0; i < h.Count; i++) lists[e].Add(h[i]);
            }

            int ownerless = 0;
            for (int i = 0; i < parked; i++)
            {
                int id = h0[i];
                if (parkedOwner.TryGetValue(id, out PlayerId owner))
                {
                    long seen = lastSeenUtc.TryGetValue(owner, out long t) ? t : nowUtcTicks;
                    lists[EntryFor(owner.Value, HopperEntry.FlagParked, seen)].Add(id);
                }
                else { lists[0].Add(id); ownerless++; }
            }
            if (ownerless > 0)
                Debug.LogError($"[BallPitSession] {ownerless:n0} parked hopper balls had no owner; saved into the host's tank");

            for (int i = 0; i < result.Count; i++) result[i].Ids = lists[i].ToArray();
            return result;
        }
    }
}

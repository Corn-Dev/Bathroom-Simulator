// Ball-pit world save writer. Capture (04 §4.2 steps 1-6) builds the DTO from the live host world between two physics
// steps; Encode serializes it: the 12-byte header, then the body through raw Deflate at CompressionLevel.Fastest (04
// §3.1). Capture only produces DTOs that pass every BallPitSaveReader rule (FILL and HOPR are checked, GAME is
// sanitized), so it never writes a save that cannot be loaded; Encode writes any DTO exactly as given, which the codec
// tests rely on. Design: 04 §3, §4.2, §4.4; 06 G8. Strings use BinaryWriter.Write(string) (UTF-8); valid keys are
// 1..64 ASCII bytes, so their length prefix is a single byte, the form BallPitSaveReader decodes.
// DeflateStream and CompressionLevel are in netstandard and System.dll, referenced by default under both API levels
// (no csc.rsp). CompressionLevel is fully qualified: UnityEngine declares its own (CS0104).
//
// ---- Field order (04 §4.3). Identical block in BallPitSaveWriter.cs and BallPitSaveReader.cs. ----
// Header        magic, version, compression, reserved, bodyLength
// FILL          tag, fillType, pitX, pitY, pitZ, ballDiameter, colorCount, seed,
//               colliderRadiusScale, jitter, maxSupportGapDegrees, pitWalls, center.x/y/z, size.x/y/z,
//               fillChecksum, ballCount, [v2+] anchor.x/y/z, siteId
// SESS          tag, hostPlayer, savedUtcTicks, stepIndex, netProtocol
// LEDG          tag, stateCount, colorCount, counts[S*C]   (Held row always 0; Held folded into Active)
// BALL          tag, recordCount, { id, state, [q] } * R
// HOPR          tag, entryCount, { playerId, flags, lastSeenUtcTicks, count, ids[count] } * E
// SUPQ          tag, count, ids[count]
// GAME          tag, moneyPot, upgradeCount, { id, level } * U,
//               containerCount, { id, color, stored } * K,
//               extCount, { tag, length, bytes[length] } * X
// BPND          tag, (assert end of stream)
// ---- End of field order. ----

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using BallPitFinal;
using MultiplayerKit;
using UnityEngine;

namespace BallPitSession
{
    public static class BallPitSaveWriter
    {
        // Bit 63 of a Q21 pose is never set for a finite pose. The mask keeps one NaN physics pose from producing a
        // record the reader rejects, which would make the whole slot unloadable.
        private const ulong PoseMask = 0x7FFFFFFFFFFFFFFFUL;

        /// <summary>
        /// Host main thread, between two PitAuthority.Step calls (BallPitGameAdapter.CaptureWorldSave from
        /// SessionController.Tick). Throws InvalidOperationException when the world breaks a FILL or HOPR rule: Core logs
        /// it and skips this save, so the slot keeps its last loadable copy. fill: the PitFillParams.FromWorld snapshot
        /// taken when the world was built (BallPitGameAdapter.OnHostPitReady); null reads w now.
        /// </summary>
        public static BallPitWorldSave Capture(PitWorld w, PitAuthority a, HopperOwnership owners, BallPitGameState game,
                                               PlayerId host, List<IBallPitSaveParticipant> participants, PitFillParams? fill = null)
        {
            if (w == null || a == null || owners == null)
                throw new ArgumentNullException(w == null ? nameof(w) : a == null ? nameof(a) : nameof(owners));
            BallStore store = w.Store;
            long now = DateTime.UtcNow.Ticks;
            var save = new BallPitWorldSave();

            // 1. FILL (the build-time snapshot: w.S is the inspector-editable settings object) and SESS.
            save.Fill = fill ?? PitFillParams.FromWorld(w);
            string error = save.Fill.Validate();
            if (error != null) throw new InvalidOperationException("this world cannot be saved: FILL " + error);
            save.HostPlayer = host.Value;
            save.SavedUtcTicks = now;
            save.StepIndex = a.StepIndex;
            save.NetProtocol = NetPit.Protocol;

            // 2. Ledger with Held folded into Active (step 3 writes Held balls as Active records).
            CaptureLedger(save, store);

            // 3. Records, in ModifiedAt order.
            int hopperRecords = CaptureRecords(save, w);

            // 4. Hoppers per PlayerId, derived from the PitAuthority lists (04 §5.4).
            save.Hoppers.AddRange(owners.BuildEntries(a, now));
            error = CheckHoppers(save.Hoppers, store, hopperRecords);
            if (error != null) throw new InvalidOperationException("this world cannot be saved: HOPR " + error);

            // 5. Pending support tests (one bool-array read per ball).
            var pending = new List<int>(a.QueueLength);
            for (int id = 0; id < store.Count; id++)
                if (a.IsQueuedForSupport(id)) pending.Add(id);
            save.PendingSupport = pending.ToArray();

            // 6. Gameplay state and participant blobs.
            save.Game = CaptureGame(game, store.Colors, participants);
            return save;
        }

        private static void CaptureLedger(BallPitWorldSave save, BallStore store)
        {
            int colors = store.Colors;
            var ledger = new int[BallStore.StateCount * colors];
            for (int s = 0; s < BallStore.StateCount; s++)
                for (int c = 0; c < colors; c++)
                    ledger[s * colors + c] = store.CountOf((BallStore.State)s, c);
            int activeRow = (int)BallStore.State.Active * colors, heldRow = (int)BallStore.State.Held * colors;
            for (int c = 0; c < colors; c++) { ledger[activeRow + c] += ledger[heldRow + c]; ledger[heldRow + c] = 0; }
            save.LedgerStates = BallStore.StateCount;
            save.LedgerColors = (byte)colors;
            save.Ledger = ledger;
        }

        // Resting poses are already Q21-snapped, so Encode is exact; Active poses are quantized here (< 0.1 mm). A Held
        // ball is written as Active at the hand pose (the hand is not saved) and resumes as a free body. Returns the
        // number of Hopper records.
        private static int CaptureRecords(BallPitWorldSave save, PitWorld w)
        {
            BallStore store = w.Store;
            int n = w.ModifiedCount, hopperRecords = 0;
            int[] ids = save.RecordIds = new int[n];
            byte[] states = save.RecordStates = new byte[n];
            ulong[] poses = save.RecordPoses = new ulong[n];
            save.RecordCount = n;
            for (int i = 0; i < n; i++)
            {
                int id = w.ModifiedAt(i);
                BallStore.State st = store.state[id];
                if (st == BallStore.State.Held) st = BallStore.State.Active;
                ulong q = 0UL;
                if (st == BallStore.State.Resting || st == BallStore.State.Active) q = w.Q.Encode(store.pos[id]) & PoseMask;
                else if (st == BallStore.State.Hopper) hopperRecords++;
                ids[i] = id;
                states[i] = (byte)st;
                poses[i] = q;
            }
            return hopperRecords;
        }

        // The HOPR read rules on BuildEntries' result. A violation means the hopper lists and the ledger disagree (a
        // bug); writing it anyway would replace the slot's last loadable copy with one the reader rejects.
        private static string CheckHoppers(List<HopperEntry> entries, BallStore store, int hopperRecords)
        {
            if (entries.Count > BallPitWorldSave.MaxHopperEntries)
                return entries.Count + " entries, more than " + BallPitWorldSave.MaxHopperEntries;
            var players = new HashSet<ulong>();
            var listed = new HashSet<int>();
            bool hostSeen = false;
            for (int k = 0; k < entries.Count; k++)
            {
                HopperEntry e = entries[k];
                if (e == null || e.Ids == null) return "entry " + k + " has no id list";
                if ((e.Flags & ~(HopperEntry.FlagHost | HopperEntry.FlagParked)) != 0) return "entry " + k + " flags " + e.Flags;
                bool isHost = (e.Flags & HopperEntry.FlagHost) != 0;
                if (isHost && hostSeen) return "entry " + k + " is a second host entry";
                hostSeen |= isHost;
                if (e.PlayerId != 0UL && !players.Add(e.PlayerId)) return "player " + e.PlayerId + " has two entries";
                for (int j = 0; j < e.Ids.Length; j++)
                {
                    int id = e.Ids[j];
                    if ((uint)id >= (uint)store.Count || store.state[id] != BallStore.State.Hopper || !listed.Add(id))
                        return "entry " + k + " lists ball " + id + ", which is not a unique Hopper-state ball";
                }
            }
            if (listed.Count != hopperRecords) return listed.Count + " hopper ids listed for " + hopperRecords + " Hopper-state balls";
            return null;
        }

        // 04 §4.2 step 6: a deep copy of the gameplay state, then one blob per registered participant. Entries that break
        // a GAME rule are dropped (Error), and a participant that throws is skipped, so the rest of the save stays valid.
        // A participant whose tag already has a blob is skipped too: that blob is unclaimed data from the load (its
        // Restore threw, or it registered after the restore), kept verbatim, and the reader allows each tag once.
        private static BallPitGameState CaptureGame(BallPitGameState game, int colors, List<IBallPitSaveParticipant> participants)
        {
            BallPitGameState g = game != null ? game.Clone() : new BallPitGameState();
            if (g.MoneyPot < 0)
            {
                Debug.LogError("[BallPitSession] save: money pot " + g.MoneyPot + " is negative; saved as 0");
                g.MoneyPot = 0;
            }

            var keys = new HashSet<string>(StringComparer.Ordinal);
            int dropped = KeepValid(g.Upgrades, BallPitGameState.MaxUpgrades,
                u => u.Level >= 0 && BallPitGameState.IsValidKey(u.Id, BallPitGameState.MaxIdLength) && keys.Add(u.Id));
            keys.Clear();
            dropped += KeepValid(g.Containers, BallPitGameState.MaxContainers,
                c => c.Color < colors && c.Stored >= 0 && BallPitGameState.IsValidKey(c.Id, BallPitGameState.MaxIdLength) && keys.Add(c.Id));
            keys.Clear();
            dropped += KeepValid(g.UnclaimedExtensions, BallPitGameState.MaxExtensions,
                x => (x.Data == null || x.Data.Length <= BallPitGameState.MaxExtensionLength) &&
                     BallPitGameState.IsValidKey(x.Tag, BallPitGameState.MaxTagLength) && keys.Add(x.Tag));
            if (dropped > 0) Debug.LogError($"[BallPitSession] save: dropped {dropped} game-state entries that break the GAME rules");

            for (int i = 0; participants != null && i < participants.Count; i++)
            {
                IBallPitSaveParticipant p = participants[i];
                if (p == null) continue;
                string tag = p.Tag;
                if (keys.Contains(tag))
                {
                    Debug.LogWarning("[BallPitSession] save: participant '" + tag + "' skipped; the save keeps the unclaimed blob with that tag");
                    continue;
                }
                if (!BallPitGameState.IsValidKey(tag, BallPitGameState.MaxTagLength) || g.UnclaimedExtensions.Count >= BallPitGameState.MaxExtensions)
                {
                    Debug.LogError("[BallPitSession] save: participant '" + tag + "' skipped (invalid tag, or more than " + BallPitGameState.MaxExtensions + " blobs)");
                    continue;
                }
                byte[] data;
                try
                {
                    data = p.Capture() ?? Array.Empty<byte>();
                }
                catch (Exception e)
                {
                    Debug.LogError("[BallPitSession] save: participant '" + tag + "' threw in Capture; saved without its data");
                    Debug.LogException(e);
                    continue;
                }
                if (data.Length > BallPitGameState.MaxExtensionLength)
                {
                    Debug.LogError("[BallPitSession] save: participant '" + tag + "' blob is " + data.Length + " bytes, over the limit; skipped");
                    continue;
                }
                keys.Add(tag);
                g.UnclaimedExtensions.Add((tag, data));
            }
            return g;
        }

        // Compacts list to its first max entries that pass ok, in order; returns how many were dropped.
        private static int KeepValid<T>(List<T> list, int max, Func<T, bool> ok)
        {
            int kept = 0;
            for (int i = 0; i < list.Count; i++)
                if (kept < max && ok(list[i])) list[kept++] = list[i];
            int dropped = list.Count - kept;
            list.RemoveRange(kept, dropped);
            return dropped;
        }

        /// <summary>
        /// Serializes the DTO as given. The body goes through a BinaryWriter (UTF-8) into an uncompressed stream so its
        /// length B is known, then the 12-byte header and the body through DeflateStream(Fastest), disposed before
        /// ToArray so the final block is flushed. Throws ArgumentException for a structurally incomplete DTO.
        /// </summary>
        public static byte[] Encode(BallPitWorldSave save)
        {
            if (save == null) throw new ArgumentNullException(nameof(save));
            Require(save.Ledger != null && save.Ledger.Length == save.LedgerStates * save.LedgerColors,
                    "Ledger must hold LedgerStates * LedgerColors counts");
            int r = save.RecordCount;
            Require(r == 0 || (r > 0 && save.RecordIds != null && save.RecordStates != null && save.RecordPoses != null &&
                               save.RecordIds.Length >= r && save.RecordStates.Length >= r && save.RecordPoses.Length >= r),
                    "the record arrays must hold RecordCount entries");
            Vector3 anchor = save.Fill.PitAnchor;
            if (save.Version < 2 && (anchor.x != 0f || anchor.y != 0f || anchor.z != 0f || save.Fill.SiteId != null))
                throw new ArgumentException("a version-1 save cannot carry a pit anchor or site id");

            using (var body = new MemoryStream(EstimateBodyLength(save)))
            {
                using (var bw = new BinaryWriter(body, Encoding.UTF8, true))
                {
                    WriteFill(bw, save.Fill, save.Version);
                    WriteSess(bw, save);
                    WriteLedg(bw, save);
                    WriteBall(bw, save);
                    WriteHopr(bw, save.Hoppers);
                    WriteSupq(bw, save.PendingSupport);
                    WriteGame(bw, save.Game ?? new BallPitGameState());
                    WriteTag(bw, BallPitWorldSave.TagEnd);
                }
                if (body.Length > BallPitWorldSave.MaxBodyLength)
                    throw new InvalidOperationException("save body is " + body.Length + " bytes, over the " + BallPitWorldSave.MaxBodyLength + "-byte limit");
                int bodyLength = (int)body.Length;

                using (var output = new MemoryStream(bodyLength / 4 + 64))
                {
                    using (var hw = new BinaryWriter(output, Encoding.UTF8, true))
                    {
                        WriteTag(hw, BallPitWorldSave.Magic);          // 4 bytes
                        hw.Write(save.Version);                        // u16
                        hw.Write(BallPitWorldSave.CompressionDeflate); // u8
                        hw.Write((byte)0);                             // u8 reserved
                        hw.Write(bodyLength);                          // i32
                    }
                    using (var deflate = new DeflateStream(output, System.IO.Compression.CompressionLevel.Fastest, true))
                        deflate.Write(body.GetBuffer(), 0, bodyLength);
                    return output.ToArray();
                }
            }
        }

        private static void Require(bool ok, string what)
        {
            if (!ok) throw new ArgumentException("BallPitWorldSave is incomplete: " + what);
        }

        // Initial capacity, so a ~31 MB worst-case body is not grown by repeated doubling.
        private static int EstimateBodyLength(BallPitWorldSave s)
        {
            long n = 512L + 4L * s.Ledger.Length + 13L * s.RecordCount + 4L * (s.PendingSupport?.Length ?? 0);
            for (int k = 0; k < s.Hoppers.Count; k++) n += 25L + 4L * (s.Hoppers[k]?.Ids?.Length ?? 0);
            return (int)Math.Min(n, BallPitWorldSave.MaxBodyLength);
        }

        // Four ASCII bytes: a section tag or the header magic.
        private static void WriteTag(BinaryWriter bw, string tag)
        {
            for (int i = 0; i < 4; i++) bw.Write((byte)tag[i]);
        }

        private static void WriteFill(BinaryWriter bw, PitFillParams f, ushort version)
        {
            WriteTag(bw, BallPitWorldSave.TagFill);
            bw.Write(f.FillType);                        // u8
            bw.Write(f.PitX); bw.Write(f.PitY); bw.Write(f.PitZ); // i32 x3
            bw.Write(f.BallDiameter);                    // f32
            bw.Write(f.ColorCount);                      // i32
            bw.Write(f.Seed);                            // i32
            bw.Write(f.ColliderRadiusScale);             // f32
            bw.Write(f.Jitter);                          // f32
            bw.Write(f.MaxSupportGapDegrees);            // f32
            bw.Write((byte)(f.PitWalls ? 1 : 0));        // u8
            bw.Write(f.BoundsCenter.x); bw.Write(f.BoundsCenter.y); bw.Write(f.BoundsCenter.z); // f32 x3
            bw.Write(f.BoundsSize.x); bw.Write(f.BoundsSize.y); bw.Write(f.BoundsSize.z);       // f32 x3
            bw.Write(f.FillChecksum);                    // u64
            bw.Write(f.BallCount);                       // i32
            if (version < 2) return;
            bw.Write(f.PitAnchor.x); bw.Write(f.PitAnchor.y); bw.Write(f.PitAnchor.z); // f32 x3
            // Site id: one length byte (0 = none) plus that many ASCII bytes. BinaryWriter.Write(string) writes exactly this
            // for a valid id (1..32 characters of [a-z0-9_.]: its 7-bit length prefix is a single byte below 128).
            if (f.SiteId == null) bw.Write((byte)0);
            else bw.Write(f.SiteId);
        }

        private static void WriteSess(BinaryWriter bw, BallPitWorldSave s)
        {
            WriteTag(bw, BallPitWorldSave.TagSess);
            bw.Write(s.HostPlayer);                      // u64
            bw.Write(s.SavedUtcTicks);                   // i64
            bw.Write(s.StepIndex);                       // i32
            bw.Write(s.NetProtocol);                     // u8
        }

        private static void WriteLedg(BinaryWriter bw, BallPitWorldSave s)
        {
            WriteTag(bw, BallPitWorldSave.TagLedg);
            bw.Write(s.LedgerStates);                    // u8
            bw.Write(s.LedgerColors);                    // u8
            for (int i = 0; i < s.Ledger.Length; i++) bw.Write(s.Ledger[i]); // i32 at [state * C + color]
        }

        private static void WriteBall(BinaryWriter bw, BallPitWorldSave s)
        {
            WriteTag(bw, BallPitWorldSave.TagBall);
            bw.Write(s.RecordCount);                     // i32
            for (int i = 0; i < s.RecordCount; i++)
            {
                byte st = s.RecordStates[i];
                bw.Write((uint)s.RecordIds[i]);          // u32
                bw.Write(st);                            // u8
                if (st == 0 || st == 1) bw.Write(s.RecordPoses[i]); // u64 Q21, Resting and Active only
            }
        }

        private static void WriteHopr(BinaryWriter bw, List<HopperEntry> hoppers)
        {
            WriteTag(bw, BallPitWorldSave.TagHopr);
            bw.Write(hoppers.Count);                     // i32
            for (int k = 0; k < hoppers.Count; k++)
            {
                HopperEntry e = hoppers[k] ?? throw new ArgumentException("BallPitWorldSave is incomplete: Hoppers[" + k + "] is null");
                int[] ids = e.Ids ?? Array.Empty<int>();
                bw.Write(e.PlayerId);                    // u64
                bw.Write(e.Flags);                       // u8
                bw.Write(e.LastSeenUtcTicks);            // i64
                bw.Write(ids.Length);                    // i32
                for (int j = 0; j < ids.Length; j++) bw.Write((uint)ids[j]); // u32
            }
        }

        private static void WriteSupq(BinaryWriter bw, int[] ids)
        {
            WriteTag(bw, BallPitWorldSave.TagSupq);
            ids = ids ?? Array.Empty<int>();
            bw.Write(ids.Length);                        // i32
            for (int i = 0; i < ids.Length; i++) bw.Write((uint)ids[i]); // u32
        }

        private static void WriteGame(BinaryWriter bw, BallPitGameState g)
        {
            WriteTag(bw, BallPitWorldSave.TagGame);
            bw.Write(g.MoneyPot);                        // i64
            bw.Write(g.Upgrades.Count);                  // i32
            for (int i = 0; i < g.Upgrades.Count; i++)
            {
                bw.Write(g.Upgrades[i].Id);              // string
                bw.Write(g.Upgrades[i].Level);           // i32
            }
            bw.Write(g.Containers.Count);                // i32
            for (int i = 0; i < g.Containers.Count; i++)
            {
                bw.Write(g.Containers[i].Id);            // string
                bw.Write(g.Containers[i].Color);         // u8
                bw.Write(g.Containers[i].Stored);        // i32
            }
            bw.Write(g.UnclaimedExtensions.Count);       // i32
            for (int i = 0; i < g.UnclaimedExtensions.Count; i++)
            {
                (string tag, byte[] data) = g.UnclaimedExtensions[i];
                data = data ?? Array.Empty<byte>();
                bw.Write(tag);                           // string
                bw.Write(data.Length);                   // i32
                bw.Write(data);                          // bytes
            }
        }
    }
}

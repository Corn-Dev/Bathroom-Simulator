// Ball-pit world save reader. TryDecode checks the 12-byte header, inflates the body and reads every section in the
// writer's order, applying each 04 §3.2 read rule. Every count and length is checked against its limit and against
// the body bytes left before anything it sizes is allocated, so a corrupted field cannot trigger a huge allocation.
// Never throws: rule violations, truncation, bad Deflate and malformed fields return SaveCorrupt with the section tag
// and body offset in Detail; a newer payload version returns IncompatibleVersion. Design: 04 §3, §4.3; 06 G9.
// Split for the 400-line rule: HOPR, SUPQ, GAME and the key reader are in BallPitSaveReader.Sections.cs.
// Ids and tags are read by decoding BinaryWriter.Write(string)'s length prefix directly (valid keys are 1..64 ASCII
// bytes, so it is one byte), so BinaryReader.ReadString never sizes a buffer from untrusted data.
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
using System.IO;
using System.IO.Compression;
using System.Text;
using BallPitFinal;
using MultiplayerKit;
using UnityEngine;

namespace BallPitSession
{
    public static partial class BallPitSaveReader
    {
        /// <summary>Header, inflate, every section and the end-of-body assert. Ok carries the decoded save; never throws.</summary>
        public static KitResult<BallPitWorldSave> TryDecode(byte[] payload)
        {
            const int headerLength = BallPitWorldSave.HeaderLength;
            if (payload == null || payload.Length < headerLength)
                return Corrupt("header: the payload has " + (payload == null ? 0 : payload.Length) + " bytes, less than the " + headerLength + "-byte header");
            for (int i = 0; i < 4; i++)
                if (payload[i] != (byte)BallPitWorldSave.Magic[i]) return Corrupt("header: magic is not " + BallPitWorldSave.Magic);
            int version = payload[4] | (payload[5] << 8);
            if (version == 0) return Corrupt("header: payload version 0");
            if (version > BallPitWorldSave.CurrentVersion)
                return KitResult<BallPitWorldSave>.Fail(KitError.IncompatibleVersion,
                    "saved by a newer version (payload version " + version + ", this build reads up to " + BallPitWorldSave.CurrentVersion + ")");
            if (payload[6] != BallPitWorldSave.CompressionDeflate)
                return Corrupt("header: compression " + payload[6] + " is not " + BallPitWorldSave.CompressionDeflate + " (raw Deflate)");
            // payload[7] is reserved and ignored.
            int bodyLength = payload[8] | (payload[9] << 8) | (payload[10] << 16) | (payload[11] << 24);
            if (bodyLength < 1 || bodyLength > BallPitWorldSave.MaxBodyLength)
                return Corrupt("header: body length " + bodyLength + " outside 1.." + BallPitWorldSave.MaxBodyLength);

            SectionReader reader = null;
            try
            {
                using (var input = new MemoryStream(payload, headerLength, payload.Length - headerLength, false))
                using (var inflate = new DeflateStream(input, CompressionMode.Decompress))
                using (var br = new BinaryReader(new BodyStream(inflate, bodyLength), Encoding.UTF8))
                {
                    reader = new SectionReader(br, bodyLength);
                    return KitResult<BallPitWorldSave>.From(reader.Run((ushort)version));
                }
            }
            catch (CorruptSaveException e)
            {
                return Corrupt(e.Message);
            }
            catch (Exception e) when (e is IOException || e is InvalidDataException || e is ArgumentException ||
                                      e is OverflowException || e is FormatException || e is NotSupportedException)
            {
                // EndOfStreamException (truncated body) is an IOException; InvalidDataException is bad Deflate data.
                return Corrupt(Where(reader) + e.GetType().Name + ": " + e.Message);
            }
            catch (Exception e)
            {
                Debug.LogException(e); // a reader bug rather than a damaged file; still reported as SaveCorrupt
                return Corrupt(Where(reader) + "unexpected " + e.GetType().Name + ": " + e.Message);
            }
        }

        private static KitResult<BallPitWorldSave> Corrupt(string detail) =>
            KitResult<BallPitWorldSave>.Fail(KitError.SaveCorrupt, detail);

        private static string Where(SectionReader reader) => reader == null ? "inflate: " : reader.Where;

        // A read rule failed. Carries the whole Detail text; thrown and caught only inside TryDecode.
        private sealed class CorruptSaveException : Exception
        {
            public CorruptSaveException(string detail) : base(detail) { }
        }

        // One decode, sections in the writer's order. Every rule failure throws CorruptSaveException naming the section
        // and the body offset. bits is one bitset over ball ids, reused by BALL (duplicate ids), HOPR (unclaimed Hopper
        // records) and SUPQ (duplicate ids).
        private sealed partial class SectionReader
        {
            private readonly BinaryReader br;
            private readonly BodyStream body;
            private readonly int bodyLength;
            private readonly BallPitWorldSave save = new BallPitWorldSave();
            private string section = BallPitWorldSave.TagFill;
            private int ballCount, colors, hopperRecords;
            private ulong[] bits;

            public SectionReader(BinaryReader br, int bodyLength)
            {
                this.br = br;
                body = (BodyStream)br.BaseStream;
                this.bodyLength = bodyLength;
            }

            public string Where => section + " at body offset " + body.Consumed + ": ";

            public BallPitWorldSave Run(ushort version)
            {
                save.Version = version;
                ReadFill();
                ReadSess();
                ReadLedg();
                ReadBall();
                ReadHopr();
                ReadSupq();
                ReadGame();
                ReadEnd();
                return save;
            }

            private void ExpectTag(string tag)
            {
                long at = body.Consumed;
                section = tag;
                byte b0 = br.ReadByte(), b1 = br.ReadByte(), b2 = br.ReadByte(), b3 = br.ReadByte();
                if (b0 != tag[0] || b1 != tag[1] || b2 != tag[2] || b3 != tag[3])
                    throw new CorruptSaveException("expected tag " + tag + " at body offset " + at);
            }

            private void Fail(string what) => throw new CorruptSaveException(Where + what);

            private int ReadCount(int max, string what)
            {
                int n = br.ReadInt32();
                if (n < 0 || n > max) Fail(what + " " + n + " outside 0.." + max);
                return n;
            }

            // Before allocating for a count: the body must still hold at least the bytes those entries take.
            private void NeedBytes(long bytes, string what)
            {
                long left = bodyLength - body.Consumed;
                if (bytes > left) Fail(what + " need " + bytes + " bytes, the body has " + left + " left");
            }

            private static bool TestAndSet(ulong[] b, int i)
            {
                ulong m = 1UL << (i & 63);
                bool was = (b[i >> 6] & m) != 0;
                b[i >> 6] |= m;
                return was;
            }

            private static bool TestAndClear(ulong[] b, int i)
            {
                ulong m = 1UL << (i & 63);
                bool was = (b[i >> 6] & m) != 0;
                b[i >> 6] &= ~m;
                return was;
            }

            private void ReadFill()
            {
                ExpectTag(BallPitWorldSave.TagFill);
                var f = new PitFillParams();
                f.FillType = br.ReadByte();
                f.PitX = br.ReadInt32();
                f.PitY = br.ReadInt32();
                f.PitZ = br.ReadInt32();
                f.BallDiameter = br.ReadSingle();
                f.ColorCount = br.ReadInt32();
                f.Seed = br.ReadInt32();
                f.ColliderRadiusScale = br.ReadSingle();
                f.Jitter = br.ReadSingle();
                f.MaxSupportGapDegrees = br.ReadSingle();
                byte walls = br.ReadByte();
                if (walls > 1) Fail("pitWalls " + walls + " is not 0 or 1");
                f.PitWalls = walls == 1;
                float cx = br.ReadSingle(), cy = br.ReadSingle(), cz = br.ReadSingle();
                float sx = br.ReadSingle(), sy = br.ReadSingle(), sz = br.ReadSingle();
                f.BoundsCenter = new Vector3(cx, cy, cz);
                f.BoundsSize = new Vector3(sx, sy, sz);
                f.FillChecksum = br.ReadUInt64();
                f.BallCount = br.ReadInt32();
                if (save.Version >= 2) // Run(version) set it; v1 keeps the zero anchor and the null site id of new PitFillParams()
                {
                    float ax = br.ReadSingle(), ay = br.ReadSingle(), az = br.ReadSingle();
                    f.PitAnchor = new Vector3(ax, ay, az);
                    f.SiteId = ReadOptionalKey(BallPitGameState.MaxTagLength, "site id");
                }
                string error = f.Validate();
                if (error != null) Fail(error);
                save.Fill = f;
                ballCount = f.BallCount;
                colors = f.ColorCount;
            }

            // The FILL site id (v2+): one length byte, 0 = none, otherwise ReadKey's rules (BallPitSaveReader.Sections.cs),
            // so a corrupt length cannot allocate more than maxLength bytes.
            private string ReadOptionalKey(int maxLength, string what)
            {
                int n = br.ReadByte();
                if (n == 0) return null;
                if (n > maxLength) Fail(what + " length " + n + " outside 0.." + maxLength);
                byte[] bytes = br.ReadBytes(n);
                if (bytes.Length != n) throw new EndOfStreamException(what + " is truncated");
                var chars = new char[n];
                for (int i = 0; i < n; i++)
                {
                    char c = (char)bytes[i];
                    if (!BallPitGameState.IsKeyChar(c)) Fail(what + " has a character outside [a-z0-9_.]");
                    chars[i] = c;
                }
                return new string(chars);
            }

            private void ReadSess()
            {
                ExpectTag(BallPitWorldSave.TagSess);
                save.HostPlayer = br.ReadUInt64(); // any; 0 for offline dev saves
                save.SavedUtcTicks = br.ReadInt64();
                if (save.SavedUtcTicks < 0) Fail("saved UTC ticks " + save.SavedUtcTicks + " < 0");
                save.StepIndex = br.ReadInt32();   // diagnostic only
                save.NetProtocol = br.ReadByte();  // diagnostic only
            }

            // Counts per [state * C + color]. Held balls are saved as Active, so the Held row must be all zero.
            private void ReadLedg()
            {
                ExpectTag(BallPitWorldSave.TagLedg);
                byte states = br.ReadByte(), ledgerColors = br.ReadByte();
                if (states != BallStore.StateCount) Fail("state count " + states + " != " + BallStore.StateCount);
                if (ledgerColors != colors) Fail("color count " + ledgerColors + " != FILL colorCount " + colors);
                var ledger = new int[states * ledgerColors];
                int heldRow = (int)BallStore.State.Held * ledgerColors;
                long total = 0;
                for (int i = 0; i < ledger.Length; i++)
                {
                    int v = br.ReadInt32();
                    if (v < 0) Fail("count " + v + " < 0 for state " + i / ledgerColors + ", color " + i % ledgerColors);
                    if (v != 0 && i >= heldRow && i < heldRow + ledgerColors)
                        Fail("Held count " + v + " for color " + (i - heldRow) + " (Held balls are saved as Active)");
                    ledger[i] = v;
                    total += v;
                }
                if (total != ballCount) Fail("counts sum to " + total + ", FILL has " + ballCount + " balls");
                save.LedgerStates = states;
                save.LedgerColors = ledgerColors;
                save.Ledger = ledger;
            }

            // Records: u32 id, u8 state, u64 Q21 pose for Resting (0) / Active (1) only. Ids unique and in range; Held (2)
            // is never written; bit 63 of a pose must be 0.
            private void ReadBall()
            {
                ExpectTag(BallPitWorldSave.TagBall);
                int r = ReadCount(ballCount, "record count");
                NeedBytes(5L * r, "records");
                var ids = new int[r];
                var states = new byte[r];
                var poses = new ulong[r];
                bits = new ulong[(ballCount + 63) >> 6];
                for (int i = 0; i < r; i++)
                {
                    uint id = br.ReadUInt32();
                    byte st = br.ReadByte();
                    if (id >= (uint)ballCount) Fail("record " + i + " id " + id + " >= ball count " + ballCount);
                    if (TestAndSet(bits, (int)id)) Fail("record " + i + " repeats id " + id);
                    ulong q = 0UL;
                    if (st == 0 || st == 1)
                    {
                        q = br.ReadUInt64();
                        if ((q >> 63) != 0UL) Fail("record " + i + " pose has bit 63 set");
                    }
                    else if (st == 5) hopperRecords++;
                    else if (st != 3 && st != 4) Fail("record " + i + " state " + st + (st == 2 ? " (Held is never written)" : " is unknown"));
                    ids[i] = (int)id;
                    states[i] = st;
                    poses[i] = q;
                }
                save.RecordCount = r;
                save.RecordIds = ids;
                save.RecordStates = states;
                save.RecordPoses = poses;
            }

            // Exactly B body bytes, then nothing more on the inflated stream.
            private void ReadEnd()
            {
                ExpectTag(BallPitWorldSave.TagEnd);
                if (body.Consumed != bodyLength)
                    Fail("the sections end at " + body.Consumed + " but the header declares " + bodyLength + " body bytes");
                if (body.HasTrailingData()) Fail("the inflated stream continues past the declared body length");
            }
        }

        // Forward-only view of the inflated body. Buffers the DeflateStream (one inflate call per 64 KiB, not one per
        // field), never takes more than the declared body length from it (past that, reads return 0 and BinaryReader
        // throws EndOfStreamException), and reports the consumed offset.
        private sealed class BodyStream : Stream
        {
            private readonly Stream inner;
            private readonly long limit;
            private readonly byte[] buffer = new byte[65536];
            private int start, end;
            private long taken;

            public BodyStream(Stream inner, long limit)
            {
                this.inner = inner;
                this.limit = limit;
            }

            /// <summary>Body bytes handed to the reader so far.</summary>
            public long Consumed => taken - (end - start);

            /// <summary>Once the whole body was consumed: whether the inflated stream holds anything after it.</summary>
            public bool HasTrailingData() => start != end || inner.ReadByte() >= 0;

            public override int Read(byte[] dst, int offset, int count)
            {
                if (count <= 0 || (start == end && !Fill())) return 0;
                int k = Math.Min(count, end - start);
                Buffer.BlockCopy(buffer, start, dst, offset, k);
                start += k;
                return k;
            }

            // BinaryReader.ReadByte calls this; Stream's default would allocate a byte[1] per call.
            public override int ReadByte() => start == end && !Fill() ? -1 : buffer[start++];

            private bool Fill()
            {
                long left = limit - taken;
                if (left <= 0) return false;
                int n = inner.Read(buffer, 0, (int)Math.Min(buffer.Length, left));
                if (n <= 0) return false;
                taken += n;
                start = 0;
                end = n;
                return true;
            }

            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => Consumed; set => throw new NotSupportedException(); }
            public override void Flush() { }
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] src, int offset, int count) => throw new NotSupportedException();
        }
    }
}

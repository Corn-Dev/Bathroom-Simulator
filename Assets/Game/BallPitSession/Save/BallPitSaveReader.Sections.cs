// BallPitSaveReader, part 2: sections HOPR, SUPQ and GAME and the key reader (split from BallPitSaveReader.cs for the
// 400-line rule; the field-order block, TryDecode and FILL..BALL are there). Design: 04 §3.2, §4.3; 06 G9.

using System;
using System.Collections.Generic;
using System.IO;
using BallPitFinal;

namespace BallPitSession
{
    public static partial class BallPitSaveReader
    {
        private sealed partial class SectionReader
        {
            // Tanks per PlayerId. At most one host entry, no player id repeats except 0, flag bits 0-1 only. Every id
            // must be a BALL record in state Hopper not claimed by an earlier id, and every Hopper record is claimed.
            private void ReadHopr()
            {
                ExpectTag(BallPitWorldSave.TagHopr);
                int entries = ReadCount(BallPitWorldSave.MaxHopperEntries, "entry count");
                Array.Clear(bits, 0, bits.Length);
                for (int i = 0; i < save.RecordCount; i++)
                    if (save.RecordStates[i] == (byte)BallStore.State.Hopper) TestAndSet(bits, save.RecordIds[i]);

                var players = new HashSet<ulong>();
                bool hostSeen = false;
                int claimed = 0;
                for (int k = 0; k < entries; k++)
                {
                    ulong player = br.ReadUInt64();
                    byte flags = br.ReadByte();
                    long lastSeen = br.ReadInt64();
                    if ((flags & ~(HopperEntry.FlagHost | HopperEntry.FlagParked)) != 0) Fail("entry " + k + " flags " + flags + " use unknown bits");
                    bool isHost = (flags & HopperEntry.FlagHost) != 0;
                    if (isHost && hostSeen) Fail("entry " + k + " is a second host entry");
                    hostSeen |= isHost;
                    if (player != 0UL && !players.Add(player)) Fail("entry " + k + " repeats player " + player);

                    int n = ReadCount(ballCount, "entry " + k + " id count");
                    NeedBytes(4L * n, "entry " + k + " ids");
                    var ids = new int[n];
                    for (int j = 0; j < n; j++)
                    {
                        uint id = br.ReadUInt32();
                        if (id >= (uint)ballCount || !TestAndClear(bits, (int)id))
                            Fail("entry " + k + " id " + id + " is not an unclaimed Hopper record");
                        ids[j] = (int)id;
                    }
                    claimed += n;
                    save.Hoppers.Add(new HopperEntry { PlayerId = player, Flags = flags, LastSeenUtcTicks = lastSeen, Ids = ids });
                }
                if (claimed != hopperRecords) Fail(claimed + " hopper ids for " + hopperRecords + " Hopper records");
            }

            // Pending support tests. Ids in range and unique; they may be unmodified fill balls (no BALL record needed).
            private void ReadSupq()
            {
                ExpectTag(BallPitWorldSave.TagSupq);
                int n = ReadCount(ballCount, "count");
                NeedBytes(4L * n, "ids");
                Array.Clear(bits, 0, bits.Length);
                var ids = new int[n];
                for (int i = 0; i < n; i++)
                {
                    uint id = br.ReadUInt32();
                    if (id >= (uint)ballCount || TestAndSet(bits, (int)id)) Fail("id " + id + " is out of range or repeats");
                    ids[i] = (int)id;
                }
                save.PendingSupport = ids;
            }

            // Gameplay placeholders (04 §4.4). Every extension blob lands in Game.UnclaimedExtensions; the restorer hands
            // each one to its registered participant.
            private void ReadGame()
            {
                ExpectTag(BallPitWorldSave.TagGame);
                BallPitGameState g = save.Game;
                g.MoneyPot = br.ReadInt64();
                if (g.MoneyPot < 0) Fail("money pot " + g.MoneyPot + " < 0");
                var keys = new HashSet<string>(StringComparer.Ordinal);

                int upgrades = ReadCount(BallPitGameState.MaxUpgrades, "upgrade count");
                for (int i = 0; i < upgrades; i++)
                {
                    string id = ReadKey(BallPitGameState.MaxIdLength, "upgrade id");
                    int level = br.ReadInt32();
                    if (level < 0) Fail("upgrade '" + id + "' level " + level + " < 0");
                    if (!keys.Add(id)) Fail("upgrade id '" + id + "' repeats");
                    g.Upgrades.Add(new UpgradeEntry(id, level));
                }

                keys.Clear();
                int containers = ReadCount(BallPitGameState.MaxContainers, "container count");
                for (int i = 0; i < containers; i++)
                {
                    string id = ReadKey(BallPitGameState.MaxIdLength, "container id");
                    byte color = br.ReadByte();
                    int stored = br.ReadInt32();
                    if (color >= colors) Fail("container '" + id + "' color " + color + " >= colorCount " + colors);
                    if (stored < 0) Fail("container '" + id + "' stored " + stored + " < 0");
                    if (!keys.Add(id)) Fail("container id '" + id + "' repeats");
                    g.Containers.Add(new ContainerEntry(id, color, stored));
                }

                keys.Clear();
                int extensions = ReadCount(BallPitGameState.MaxExtensions, "extension count");
                for (int i = 0; i < extensions; i++)
                {
                    string tag = ReadKey(BallPitGameState.MaxTagLength, "extension tag");
                    int length = br.ReadInt32();
                    if (length < 0 || length > BallPitGameState.MaxExtensionLength)
                        Fail("extension '" + tag + "' length " + length + " outside 0.." + BallPitGameState.MaxExtensionLength);
                    NeedBytes(length, "extension '" + tag + "'");
                    byte[] data = br.ReadBytes(length);
                    if (data.Length != length) throw new EndOfStreamException("extension '" + tag + "' is truncated");
                    if (!keys.Add(tag)) Fail("extension tag '" + tag + "' repeats");
                    g.UnclaimedExtensions.Add((tag, data));
                }
            }

            // A key written by BinaryWriter.Write(string): 7-bit length prefix, then UTF-8 bytes. A valid key is 1..max
            // ASCII bytes, so the prefix is one byte; a set continuation bit means 128+ bytes, which is already too long.
            private string ReadKey(int maxLength, string what)
            {
                int n = br.ReadByte();
                if (n < 1 || n > maxLength) Fail(what + " length " + n + " outside 1.." + maxLength);
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
        }
    }
}

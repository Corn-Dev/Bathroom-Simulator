// Complete IWorldSaveStore over System.IO: one directory, two files per slot (ws_<id>.bin envelope + payload,
// ws_<id>.json metadata). Design: 04 §2.1-§2.4, 01 §4.7 (validation), 06 C44. Non-Steam builds use it directly;
// SteamWorldSaveStore owns one as its fallback and reuses the raw helpers (TryReadMeta, TryReadBin, OpenPayload,
// WriteFiles, DeleteFiles). Write order (04 §2.4 step 6): bin.tmp, json.tmp, then copy+delete for bin first and
// json second; the CRC in both files turns a crash between the two copies into SaveCorrupt instead of a wrong
// world. The root directory is passed in, so this file has no UnityEngine use. Never throws: I/O exceptions
// become SaveIoFailed and are logged at Warning.

using System;
using System.Collections.Generic;
using System.IO;

namespace MultiplayerKit
{
    public sealed class LocalWorldSaveStore : IWorldSaveStore
    {
        private const int NewIdAttempts = 8;

        private readonly string root;
        private readonly Random random = new Random();

        public LocalWorldSaveStore(string rootDirectory, int maxSlots = 16)
        {
            if (string.IsNullOrWhiteSpace(rootDirectory))
            {
                throw new ArgumentException("A root directory is required.", nameof(rootDirectory));
            }
            if (maxSlots < 1) throw new ArgumentOutOfRangeException(nameof(maxSlots), "maxSlots must be at least 1.");
            root = rootDirectory;
            MaxSlots = maxSlots;
        }

        public int MaxSlots { get; }

        public string RootDirectory => root;

        public KitResult<IReadOnlyList<WorldSaveSlotInfo>> ListSlots()
        {
            var slots = new List<WorldSaveSlotInfo>();
            string[] files;
            try
            {
                if (!Directory.Exists(root)) return KitResult<IReadOnlyList<WorldSaveSlotInfo>>.From(slots);
                files = Directory.GetFiles(root, WorldSaveNames.MetaSearchPattern);
            }
            catch (Exception e)
            {
                KitLog.Warning("[WorldSave] listing local saves failed: " + e.GetType().Name);
                return KitResult<IReadOnlyList<WorldSaveSlotInfo>>.Fail(KitError.SaveIoFailed, e.Message);
            }

            foreach (string path in files)
            {
                string name = Path.GetFileName(path);
                if (!WorldSaveNames.TrySlotIdFromMetaFileName(name, out string slotId)) continue;
                KitResult<WorldSaveMetaJson> meta = TryReadMeta(slotId);
                if (!meta.Ok)
                {
                    KitLog.Warning("[WorldSave] skipped " + name + ": " + meta);
                    continue;
                }
                slots.Add(meta.Value.ToSlotInfo());
            }
            SortNewestFirst(slots);
            return KitResult<IReadOnlyList<WorldSaveSlotInfo>>.From(slots);
        }

        public KitResult<WorldSaveSlotInfo> GetLatest()
        {
            KitResult<IReadOnlyList<WorldSaveSlotInfo>> list = ListSlots();
            if (!list.Ok) return KitResult<WorldSaveSlotInfo>.Fail(list.Error, list.Detail);
            if (list.Value.Count == 0) return KitResult<WorldSaveSlotInfo>.Fail(KitError.SaveNotFound, "no saves");
            return KitResult<WorldSaveSlotInfo>.From(list.Value[0]);
        }

        public KitResult<WorldSaveSlotInfo> Save(string slotId, string displayName, double playtimeSeconds,
            string gameVersion, IReadOnlyDictionary<string, string> settingsSnapshot, byte[] payload)
        {
            if (slotId != null && !WorldSaveNames.IsValidSlotId(slotId))
            {
                return KitResult<WorldSaveSlotInfo>.Fail(KitError.InvalidArgument, "invalid slot id");
            }
            if (!WorldSaveEnvelope.IsValidPayload(payload))
            {
                return KitResult<WorldSaveSlotInfo>.Fail(KitError.InvalidArgument, "payload must be 1.."
                    + WorldSaveEnvelope.MaxPayloadBytes + " bytes");
            }

            DateTime now = DateTime.UtcNow;
            DateTime created = now;
            if (slotId == null)
            {
                KitResult<string> id = AllocateSlotId(now);
                if (!id.Ok) return KitResult<WorldSaveSlotInfo>.Fail(id.Error, id.Detail);
                slotId = id.Value;
            }
            else
            {
                // Overwrite keeps CreatedUtc; missing or unreadable metadata is treated as a new slot with this id.
                KitResult<WorldSaveMetaJson> existing = TryReadMeta(slotId);
                if (existing.Ok) created = new DateTime(existing.Value.createdUtcTicks, DateTimeKind.Utc);
            }

            byte[] envelope = WorldSaveEnvelope.Wrap(payload, out uint crc);
            WorldSaveMetaJson meta = WorldSaveMetaJson.Create(slotId, displayName, created, now, playtimeSeconds,
                gameVersion, settingsSnapshot, payload.Length, crc);

            KitResult written = WriteFiles(slotId, envelope, meta.ToUtf8Bytes());
            if (!written.Ok) return KitResult<WorldSaveSlotInfo>.Fail(written.Error, written.Detail);

            KitLog.Info("[WorldSave] saved " + slotId + " (" + (envelope.Length + 1023) / 1024 + " KB, local)");
            return KitResult<WorldSaveSlotInfo>.From(meta.ToSlotInfo());
        }

        public KitResult<WorldSaveData> Load(string slotId)
        {
            if (!WorldSaveNames.IsValidSlotId(slotId))
            {
                return KitResult<WorldSaveData>.Fail(KitError.InvalidArgument, "invalid slot id");
            }

            KitResult<WorldSaveMetaJson> meta = TryReadMeta(slotId);
            if (!meta.Ok)
            {
                KitLog.Warning("[WorldSave] load " + slotId + ": " + meta);
                return KitResult<WorldSaveData>.Fail(meta.Error, meta.Detail);
            }
            KitResult<byte[]> bin = TryReadBin(slotId);
            if (!bin.Ok)
            {
                KitLog.Warning("[WorldSave] load " + slotId + ": " + bin);
                return KitResult<WorldSaveData>.Fail(bin.Error, bin.Detail);
            }

            KitResult<WorldSaveData> data = OpenPayload(meta.Value, bin.Value);
            if (!data.Ok) KitLog.Warning("[WorldSave] load " + slotId + ": " + data);
            return data;
        }

        public KitResult Delete(string slotId)
        {
            if (!WorldSaveNames.IsValidSlotId(slotId)) return KitResult.Fail(KitError.InvalidArgument, "invalid slot id");
            return DeleteFiles(slotId);
        }

        // Raw helpers (also used by SteamWorldSaveStore). All validate the slot id and never throw.

        /// <summary>
        /// Reads and validates ws_&lt;id&gt;.json. SaveNotFound when the file is missing, SaveCorrupt when it does
        /// not parse, fails Validate or names another slot, SaveIoFailed on an I/O error.
        /// </summary>
        public KitResult<WorldSaveMetaJson> TryReadMeta(string slotId)
        {
            if (!WorldSaveNames.IsValidSlotId(slotId))
            {
                return KitResult<WorldSaveMetaJson>.Fail(KitError.InvalidArgument, "invalid slot id");
            }

            string path = MetaPath(slotId);
            byte[] bytes;
            try
            {
                if (!File.Exists(path)) return KitResult<WorldSaveMetaJson>.Fail(KitError.SaveNotFound, "no metadata file");
                if (new FileInfo(path).Length > WorldSaveMetaJson.MaxFileBytes)
                {
                    return KitResult<WorldSaveMetaJson>.Fail(KitError.SaveCorrupt, "metadata file too large");
                }
                bytes = File.ReadAllBytes(path);
            }
            catch (Exception e)
            {
                return KitResult<WorldSaveMetaJson>.Fail(KitError.SaveIoFailed, e.GetType().Name + ": " + e.Message);
            }

            if (!WorldSaveMetaJson.TryParseUtf8(bytes, out WorldSaveMetaJson meta, out string failure))
            {
                return KitResult<WorldSaveMetaJson>.Fail(KitError.SaveCorrupt, failure);
            }
            if (!string.Equals(meta.slotId, slotId, StringComparison.Ordinal))
            {
                return KitResult<WorldSaveMetaJson>.Fail(KitError.SaveCorrupt, "metadata names another slot");
            }
            return KitResult<WorldSaveMetaJson>.From(meta);
        }

        /// <summary>Raw ws_&lt;id&gt;.bin bytes. A missing file is SaveCorrupt (its metadata exists, 04 §2.4).</summary>
        public KitResult<byte[]> TryReadBin(string slotId)
        {
            if (!WorldSaveNames.IsValidSlotId(slotId)) return KitResult<byte[]>.Fail(KitError.InvalidArgument, "invalid slot id");

            string path = BinPath(slotId);
            try
            {
                if (!File.Exists(path)) return KitResult<byte[]>.Fail(KitError.SaveCorrupt, "payload file missing");
                if (new FileInfo(path).Length > (long)WorldSaveEnvelope.HeaderSize + WorldSaveEnvelope.MaxPayloadBytes)
                {
                    return KitResult<byte[]>.Fail(KitError.SaveCorrupt, "payload file too large");
                }
                return KitResult<byte[]>.From(File.ReadAllBytes(path));
            }
            catch (Exception e)
            {
                return KitResult<byte[]>.Fail(KitError.SaveIoFailed, e.GetType().Name + ": " + e.Message);
            }
        }

        /// <summary>Pairs validated metadata with envelope bytes (04 §2.3 checks). Failure is SaveCorrupt.</summary>
        public static KitResult<WorldSaveData> OpenPayload(WorldSaveMetaJson meta, byte[] envelopeBytes)
        {
            if (meta == null) return KitResult<WorldSaveData>.Fail(KitError.SaveCorrupt, "no metadata");
            if (!meta.Validate(out string invalid)) return KitResult<WorldSaveData>.Fail(KitError.SaveCorrupt, invalid);
            if (!WorldSaveEnvelope.TryUnwrap(envelopeBytes, meta.payloadBytes, meta.payloadCrc32, out byte[] payload,
                    out string failure))
            {
                return KitResult<WorldSaveData>.Fail(KitError.SaveCorrupt, failure);
            }
            return KitResult<WorldSaveData>.From(new WorldSaveData(meta.ToSlotInfo(), payload));
        }

        /// <summary>
        /// Writes both files of a slot through ".tmp" copies, bin first, json second (04 §2.4 step 6), creating the
        /// directory if needed. Leftover temporaries of a failed write are removed best effort.
        /// </summary>
        public KitResult WriteFiles(string slotId, byte[] envelopeBytes, byte[] metaUtf8)
        {
            if (!WorldSaveNames.IsValidSlotId(slotId)) return KitResult.Fail(KitError.InvalidArgument, "invalid slot id");
            if (envelopeBytes == null || metaUtf8 == null) return KitResult.Fail(KitError.InvalidArgument, "missing file bytes");

            string bin = BinPath(slotId);
            string json = MetaPath(slotId);
            string binTmp = bin + WorldSaveNames.TempSuffix;
            string jsonTmp = json + WorldSaveNames.TempSuffix;
            try
            {
                Directory.CreateDirectory(root);
                File.WriteAllBytes(binTmp, envelopeBytes);
                File.WriteAllBytes(jsonTmp, metaUtf8);
                File.Copy(binTmp, bin, true);
                File.Delete(binTmp);
                File.Copy(jsonTmp, json, true);
                File.Delete(jsonTmp);
                return KitResult.Success;
            }
            catch (Exception e)
            {
                KitLog.Warning("[WorldSave] local write of " + slotId + " failed: " + e.GetType().Name + ": " + e.Message);
                TryDeleteQuietly(binTmp);
                TryDeleteQuietly(jsonTmp);
                return KitResult.Fail(KitError.SaveIoFailed, e.GetType().Name + ": " + e.Message);
            }
        }

        /// <summary>
        /// Deletes metadata first (an interrupted delete leaves only an unlisted .bin), then the payload and any
        /// temporaries. Ok if either file existed, SaveNotFound if neither did, SaveIoFailed on an I/O error.
        /// </summary>
        public KitResult DeleteFiles(string slotId)
        {
            if (!WorldSaveNames.IsValidSlotId(slotId)) return KitResult.Fail(KitError.InvalidArgument, "invalid slot id");
            try
            {
                bool existed = DeleteIfExists(MetaPath(slotId));
                existed |= DeleteIfExists(BinPath(slotId));
                DeleteIfExists(MetaPath(slotId) + WorldSaveNames.TempSuffix);
                DeleteIfExists(BinPath(slotId) + WorldSaveNames.TempSuffix);
                return existed ? KitResult.Success : KitResult.Fail(KitError.SaveNotFound, "no such slot");
            }
            catch (Exception e)
            {
                KitLog.Warning("[WorldSave] local delete of " + slotId + " failed: " + e.GetType().Name);
                return KitResult.Fail(KitError.SaveIoFailed, e.GetType().Name + ": " + e.Message);
            }
        }

        /// <summary>True if either file of the slot exists locally. Never throws.</summary>
        public bool SlotExists(string slotId)
        {
            if (!WorldSaveNames.IsValidSlotId(slotId)) return false;
            try
            {
                return File.Exists(MetaPath(slotId)) || File.Exists(BinPath(slotId));
            }
            catch (Exception)
            {
                return false;
            }
        }

        // Internals.

        // 04 §2.4 step 2: slots-full check, then up to 8 random suffixes for the generated id.
        private KitResult<string> AllocateSlotId(DateTime utcNow)
        {
            KitResult<IReadOnlyList<WorldSaveSlotInfo>> existing = ListSlots();
            if (!existing.Ok) return KitResult<string>.Fail(existing.Error, existing.Detail);
            if (existing.Value.Count >= MaxSlots)
            {
                KitLog.Warning("[WorldSave] all " + MaxSlots + " slots are used.");
                return KitResult<string>.Fail(KitError.SaveIoFailed, WorldSaveNames.SlotsFullDetail);
            }
            for (int attempt = 0; attempt < NewIdAttempts; attempt++)
            {
                string id = WorldSaveNames.NewSlotId(utcNow, random);
                if (!SlotExists(id)) return KitResult<string>.From(id);
            }
            return KitResult<string>.Fail(KitError.SaveIoFailed, "could not allocate a slot id");
        }

        private string MetaPath(string slotId)
        {
            return Path.Combine(root, WorldSaveNames.MetaFileName(slotId));
        }

        private string BinPath(string slotId)
        {
            return Path.Combine(root, WorldSaveNames.PayloadFileName(slotId));
        }

        private static bool DeleteIfExists(string path)
        {
            if (!File.Exists(path)) return false;
            File.Delete(path);
            return true;
        }

        private static void TryDeleteQuietly(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception)
            {
                // Best effort; a leftover .tmp is never listed.
            }
        }

        private static void SortNewestFirst(List<WorldSaveSlotInfo> slots)
        {
            slots.Sort((a, b) =>
            {
                int byTime = b.UpdatedUtc.CompareTo(a.UpdatedUtc);
                return byTime != 0 ? byTime : string.CompareOrdinal(a.SlotId, b.SlotId);
            });
        }
    }
}

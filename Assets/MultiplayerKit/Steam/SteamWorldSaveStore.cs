// IWorldSaveStore over Steam Remote Storage with an owned LocalWorldSaveStore fallback, following the SaveManager
// pattern (cloud first, local on failure). Design: 04 §2.1-§2.4, §11, 01 §4.7, 06 S12.
// Both backends use the same two files per slot (ws_<id>.bin envelope, ws_<id>.json metadata). Cloud is usable when
// Steam is initialized and cloud is enabled for account and app. Save: cloud (quota permitting) then local; a cloud
// success removes the stale local copy. ListSlots/Load: the newer copy per slot wins (ties keep the cloud copy);
// Load falls back to the other backend's copy when the newest one is unreadable. Delete: both backends.
// File I/O reuses LocalWorldSaveStore's raw helpers; Steam calls go through SteamCloudFiles. Never throws.

using System;
using System.Collections.Generic;
using System.IO;

namespace MultiplayerKit.Steam
{
    public sealed class SteamWorldSaveStore : IWorldSaveStore
    {
        private const int NewIdAttempts = 8;
        private const long QuotaMarginBytes = 64 * 1024;
        private const long MaxBinFileBytes = (long)WorldSaveEnvelope.HeaderSize + WorldSaveEnvelope.MaxPayloadBytes;

        private enum Backend { Cloud, Local }

        // One backend's valid metadata for a slot.
        private readonly struct Copy
        {
            public readonly WorldSaveMetaJson Meta;
            public readonly Backend From;

            public Copy(WorldSaveMetaJson meta, Backend from)
            {
                Meta = meta;
                From = from;
            }
        }

        private readonly IPlayerIdentity identity;
        private readonly string gameRoot;
        private readonly SteamCloudFiles cloud = new SteamCloudFiles();
        private readonly Random random = new Random();
        private LocalWorldSaveStore local;
        private bool localKeyed;

        /// <summary>
        /// Local fallback root: persistentDataPath/MultiplayerKit/WorldSaves/gameId/identity.LocalIdentityKey (04 §2.1).
        /// persistentDataPath null → Application.persistentDataPath, read here on the main thread.
        /// </summary>
        public SteamWorldSaveStore(IPlayerIdentity identity, string gameId, int maxSlots = 16, string persistentDataPath = null)
        {
            this.identity = identity ?? throw new ArgumentNullException(nameof(identity));
            if (string.IsNullOrWhiteSpace(gameId)) throw new ArgumentException("A game id is required.", nameof(gameId));
            if (maxSlots < 1) throw new ArgumentOutOfRangeException(nameof(maxSlots), "maxSlots must be at least 1.");
            string basePath = string.IsNullOrEmpty(persistentDataPath) ? UnityEngine.Application.persistentDataPath : persistentDataPath;
            gameRoot = Path.Combine(basePath, "MultiplayerKit", "WorldSaves", gameId);
            MaxSlots = maxSlots;
        }

        public int MaxSlots { get; }

        // The identity key keeps two Steam accounts on one PC apart. Resolved on first use, and once more when the
        // key becomes known if the store was first used while Steam was unavailable (no saves are written then).
        private LocalWorldSaveStore Local
        {
            get
            {
                if (local != null && localKeyed) return local;
                string key = identity.LocalIdentityKey ?? "";
                if (local == null || key.Length > 0)
                {
                    local = new LocalWorldSaveStore(key.Length > 0 ? Path.Combine(gameRoot, key) : gameRoot, MaxSlots);
                    localKeyed = key.Length > 0;
                }
                return local;
            }
        }

        /// <summary>Metadata only (never payloads), newest first. Unreadable files are skipped with a Warning.</summary>
        public KitResult<IReadOnlyList<WorldSaveSlotInfo>> ListSlots()
        {
            var newest = new Dictionary<string, WorldSaveSlotInfo>(StringComparer.Ordinal);
            bool cloudUsable = cloud.IsUsable;
            if (cloudUsable)
            {
                foreach (string name in cloud.ListNames(WorldSaveNames.MetaFileNameRegex))
                {
                    if (!WorldSaveNames.TrySlotIdFromMetaFileName(name, out string slotId)) continue;
                    KitResult<WorldSaveMetaJson> meta = ReadCloudMeta(slotId);
                    if (meta.Ok) newest[slotId] = meta.Value.ToSlotInfo();
                    else KitLog.Warning("[WorldSave] skipped cloud " + name + ": " + meta.Error);
                }
            }

            KitResult<IReadOnlyList<WorldSaveSlotInfo>> locals = Local.ListSlots();
            if (!locals.Ok)
            {
                if (!cloudUsable) return locals;
                KitLog.Warning("[WorldSave] local saves not listed: " + locals);
            }
            else
            {
                foreach (WorldSaveSlotInfo info in locals.Value)
                {
                    bool known = newest.TryGetValue(info.SlotId, out WorldSaveSlotInfo cloudInfo);
                    if (!known || info.UpdatedUtc > cloudInfo.UpdatedUtc) newest[info.SlotId] = info;
                }
            }

            var slots = new List<WorldSaveSlotInfo>(newest.Values);
            slots.Sort(NewestFirst);
            return KitResult<IReadOnlyList<WorldSaveSlotInfo>>.From(slots);
        }

        public KitResult<WorldSaveSlotInfo> GetLatest()
        {
            KitResult<IReadOnlyList<WorldSaveSlotInfo>> list = ListSlots();
            if (!list.Ok) return KitResult<WorldSaveSlotInfo>.Fail(list.Error, list.Detail);
            if (list.Value.Count == 0) return KitResult<WorldSaveSlotInfo>.Fail(KitError.SaveNotFound, "no saves");
            return KitResult<WorldSaveSlotInfo>.From(list.Value[0]);
        }

        /// <summary>
        /// 04 §2.4 Save. The password is stripped from the snapshot by WorldSaveMetaJson.Create. A cloud failure
        /// falls back to the local files and is still a success; only a local I/O failure returns SaveIoFailed.
        /// </summary>
        public KitResult<WorldSaveSlotInfo> Save(string slotId, string displayName, double playtimeSeconds,
            string gameVersion, IReadOnlyDictionary<string, string> settingsSnapshot, byte[] payload)
        {
            if (slotId != null && !WorldSaveNames.IsValidSlotId(slotId))
            {
                KitLog.Warning("[WorldSave] save refused: invalid slot id '" + KitLog.Truncate(slotId) + "'.");
                return KitResult<WorldSaveSlotInfo>.Fail(KitError.InvalidArgument, "invalid slot id");
            }
            if (!WorldSaveEnvelope.IsValidPayload(payload))
            {
                KitLog.Warning("[WorldSave] save refused: payload must be 1.." + WorldSaveEnvelope.MaxPayloadBytes + " bytes.");
                return KitResult<WorldSaveSlotInfo>.Fail(KitError.InvalidArgument, "payload size");
            }

            bool cloudUsable = cloud.IsUsable;
            DateTime now = DateTime.UtcNow;
            DateTime created = now;
            if (slotId == null)
            {
                KitResult<string> id = AllocateSlotId(now, cloudUsable);
                if (!id.Ok) return KitResult<WorldSaveSlotInfo>.Fail(id.Error, id.Detail);
                slotId = id.Value;
            }
            else
            {
                // Overwrite keeps CreatedUtc; without readable metadata the slot is treated as new with this id.
                List<Copy> existing = ReadCopies(slotId, cloudUsable, out _);
                if (existing.Count > 0) created = new DateTime(existing[0].Meta.createdUtcTicks, DateTimeKind.Utc);
            }

            byte[] envelope = WorldSaveEnvelope.Wrap(payload, out uint crc);
            WorldSaveMetaJson meta = WorldSaveMetaJson.Create(slotId, displayName, created, now, playtimeSeconds,
                gameVersion, settingsSnapshot, payload.Length, crc);
            byte[] metaBytes = meta.ToUtf8Bytes();
            long kilobytes = (envelope.Length + 1023L) / 1024L;

            if (cloudUsable && WriteCloud(slotId, envelope, metaBytes))
            {
                Local.DeleteFiles(slotId);   // best effort; a missing local copy is the normal case
                KitLog.Info("[WorldSave] saved " + slotId + " (" + kilobytes + " KB, cloud)");
                return KitResult<WorldSaveSlotInfo>.From(meta.ToSlotInfo());
            }

            KitResult written = Local.WriteFiles(slotId, envelope, metaBytes);
            if (!written.Ok) return KitResult<WorldSaveSlotInfo>.Fail(written.Error, written.Detail);
            KitLog.Info("[WorldSave] saved " + slotId + " (" + kilobytes + " KB, local)");
            return KitResult<WorldSaveSlotInfo>.From(meta.ToSlotInfo());
        }

        /// <summary>SaveNotFound, SaveCorrupt or SaveIoFailed on failure; the older copy is used if the newest is unreadable.</summary>
        public KitResult<WorldSaveData> Load(string slotId)
        {
            if (!WorldSaveNames.IsValidSlotId(slotId)) return KitResult<WorldSaveData>.Fail(KitError.InvalidArgument, "invalid slot id");

            List<Copy> copies = ReadCopies(slotId, cloud.IsUsable, out KitResult metaFailure);
            if (copies.Count == 0)
            {
                KitLog.Warning("[WorldSave] load " + slotId + ": " + metaFailure);
                return KitResult<WorldSaveData>.Fail(metaFailure.Error, metaFailure.Detail);
            }

            KitResult<WorldSaveData> newest = Open(slotId, copies[0]);
            if (newest.Ok) return newest;
            if (copies.Count > 1)
            {
                KitResult<WorldSaveData> older = Open(slotId, copies[1]);
                if (older.Ok)
                {
                    KitLog.Warning("[WorldSave] " + slotId + ": newest copy corrupt (" + newest + "), loaded older "
                        + Name(copies[1].From) + " copy");
                    return older;
                }
                KitLog.Warning("[WorldSave] load " + slotId + ": older " + Name(copies[1].From) + " copy failed too: " + older);
            }
            KitLog.Warning("[WorldSave] load " + slotId + ": " + newest);
            return newest;
        }

        /// <summary>Deletes from both backends: Ok if any file existed, SaveNotFound if none did, SaveIoFailed on a failed delete.</summary>
        public KitResult Delete(string slotId)
        {
            if (!WorldSaveNames.IsValidSlotId(slotId)) return KitResult.Fail(KitError.InvalidArgument, "invalid slot id");

            bool existed = false;
            bool failed = false;
            if (cloud.IsUsable)
            {
                // Metadata first, so an interrupted delete leaves only an unlisted payload (as the local store does).
                string[] names = { WorldSaveNames.MetaFileName(slotId), WorldSaveNames.PayloadFileName(slotId) };
                foreach (string name in names)
                {
                    if (!cloud.Exists(name)) continue;
                    existed = true;
                    if (cloud.Delete(name)) continue;
                    failed = true;
                    KitLog.Warning("[WorldSave] Steam Cloud delete of " + name + " failed.");
                }
            }

            KitResult localResult = Local.DeleteFiles(slotId);
            if (localResult.Ok) existed = true;
            else if (localResult.Error != KitError.SaveNotFound) failed = true;

            if (failed) return KitResult.Fail(KitError.SaveIoFailed, "delete of " + slotId + " incomplete");
            if (!existed) return KitResult.Fail(KitError.SaveNotFound, "no such slot");
            KitLog.Info("[WorldSave] deleted " + slotId);
            return KitResult.Success;
        }

        // Internals.

        private bool WriteCloud(string slotId, byte[] envelope, byte[] metaBytes)
        {
            if (!cloud.HasQuota(envelope.Length + metaBytes.Length + QuotaMarginBytes))
            {
                KitLog.Warning("[WorldSave] cloud quota too low for " + slotId + ", saving locally");
                return false;
            }
            // Payload first, metadata second: the metadata file is the commit record (04 §2.2).
            if (cloud.TryWrite(WorldSaveNames.PayloadFileName(slotId), envelope)
                && cloud.TryWrite(WorldSaveNames.MetaFileName(slotId), metaBytes))
            {
                return true;
            }
            KitLog.Warning("[WorldSave] Steam Cloud write failed, saving locally");
            return false;
        }

        // 04 §2.4 step 2 over both backends: slots-full check, then up to 8 random suffixes for the generated id.
        private KitResult<string> AllocateSlotId(DateTime utcNow, bool cloudUsable)
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
                bool inCloud = cloudUsable && (cloud.Exists(WorldSaveNames.MetaFileName(id))
                    || cloud.Exists(WorldSaveNames.PayloadFileName(id)));
                if (!inCloud && !Local.SlotExists(id)) return KitResult<string>.From(id);
            }
            return KitResult<string>.Fail(KitError.SaveIoFailed, "could not allocate a slot id");
        }

        // Valid metadata copies of one slot, newest first (a tie keeps the cloud copy first). With no valid copy,
        // failure is the most telling read error: SaveCorrupt or SaveIoFailed before SaveNotFound.
        private List<Copy> ReadCopies(string slotId, bool cloudUsable, out KitResult failure)
        {
            var copies = new List<Copy>(2);
            failure = KitResult.Fail(KitError.SaveNotFound, "no such slot");
            if (cloudUsable) Consider(copies, ReadCloudMeta(slotId), Backend.Cloud, ref failure);
            Consider(copies, Local.TryReadMeta(slotId), Backend.Local, ref failure);
            if (copies.Count == 2 && copies[1].Meta.updatedUtcTicks > copies[0].Meta.updatedUtcTicks)
            {
                copies.Reverse();
            }
            return copies;
        }

        private static void Consider(List<Copy> copies, KitResult<WorldSaveMetaJson> meta, Backend from, ref KitResult failure)
        {
            if (meta.Ok)
            {
                copies.Add(new Copy(meta.Value, from));
                return;
            }
            if (meta.Error != KitError.SaveNotFound && failure.Error == KitError.SaveNotFound)
            {
                failure = KitResult.Fail(meta.Error, Name(from) + " metadata: " + meta.Detail);
            }
        }

        // Cloud twin of LocalWorldSaveStore.TryReadMeta: same validation and error codes.
        private KitResult<WorldSaveMetaJson> ReadCloudMeta(string slotId)
        {
            KitResult<byte[]> bytes = cloud.TryRead(WorldSaveNames.MetaFileName(slotId), WorldSaveMetaJson.MaxFileBytes);
            if (!bytes.Ok) return KitResult<WorldSaveMetaJson>.Fail(bytes.Error, bytes.Detail);
            if (!WorldSaveMetaJson.TryParseUtf8(bytes.Value, out WorldSaveMetaJson meta, out string failure))
            {
                return KitResult<WorldSaveMetaJson>.Fail(KitError.SaveCorrupt, failure);
            }
            if (!string.Equals(meta.slotId, slotId, StringComparison.Ordinal))
            {
                return KitResult<WorldSaveMetaJson>.Fail(KitError.SaveCorrupt, "metadata names another slot");
            }
            return KitResult<WorldSaveMetaJson>.From(meta);
        }

        // Reads the payload file from the copy's backend and checks the envelope against the metadata (04 §2.3).
        private KitResult<WorldSaveData> Open(string slotId, Copy copy)
        {
            KitResult<byte[]> bin = copy.From == Backend.Cloud
                ? cloud.TryRead(WorldSaveNames.PayloadFileName(slotId), MaxBinFileBytes)
                : Local.TryReadBin(slotId);
            if (!bin.Ok)
            {
                // A missing payload next to existing metadata is corruption (04 §2.4).
                KitError error = bin.Error == KitError.SaveNotFound ? KitError.SaveCorrupt : bin.Error;
                return KitResult<WorldSaveData>.Fail(error, Name(copy.From) + " payload: " + bin.Detail);
            }
            return LocalWorldSaveStore.OpenPayload(copy.Meta, bin.Value);
        }

        private static string Name(Backend backend)
        {
            return backend == Backend.Cloud ? "cloud" : "local";
        }

        private static int NewestFirst(WorldSaveSlotInfo a, WorldSaveSlotInfo b)
        {
            int byTime = b.UpdatedUtc.CompareTo(a.UpdatedUtc);
            return byTime != 0 ? byTime : string.CompareOrdinal(a.SlotId, b.SlotId);
        }
    }
}

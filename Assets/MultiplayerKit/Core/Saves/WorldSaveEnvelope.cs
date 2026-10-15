// Envelope of the world save payload file "ws_<slotId>.bin", plus the slot-id and file-name rules shared by
// LocalWorldSaveStore and SteamWorldSaveStore. Design: 04 §2.2-§2.4, 01 §4.7 (validation), 06 C42.
// Envelope field order (little-endian, 20-byte header):
//   0  4 bytes  magic 4D 4B 57 53 ("MKWS")
//   4  u16      envelope format = 1
//   6  u16      reserved = 0 (ignored on read)
//   8  i64      payload length L (= file length - 20 = metadata payloadBytes)
//  16  u32      CRC-32 of the payload (= metadata payloadCrc32)
//  20  L bytes  payload, adapter-defined, passed through unchanged

using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace MultiplayerKit
{
    public static class WorldSaveEnvelope
    {
        public const int HeaderSize = 20;
        public const ushort Format = 1;

        /// <summary>64 MiB (01 §4.7); Steam Cloud's per-file limit is 100 MiB.</summary>
        public const int MaxPayloadBytes = 64 * 1024 * 1024;

        private const byte Magic0 = 0x4D, Magic1 = 0x4B, Magic2 = 0x57, Magic3 = 0x53;

        /// <summary>True for a non-null, non-empty payload of at most MaxPayloadBytes.</summary>
        public static bool IsValidPayload(byte[] payload)
        {
            return payload != null && payload.Length > 0 && payload.Length <= MaxPayloadBytes;
        }

        /// <summary>Header + payload. Throws ArgumentException when IsValidPayload is false.</summary>
        public static byte[] Wrap(byte[] payload)
        {
            return Wrap(payload, out _);
        }

        /// <summary>Header + payload, and the payload CRC the metadata file must carry.</summary>
        public static byte[] Wrap(byte[] payload, out uint payloadCrc)
        {
            if (!IsValidPayload(payload))
            {
                throw new ArgumentException("World save payload must be 1.." + MaxPayloadBytes + " bytes.", nameof(payload));
            }

            payloadCrc = Crc32.Compute(payload, 0, payload.Length);
            var bytes = new byte[HeaderSize + payload.Length];
            bytes[0] = Magic0;
            bytes[1] = Magic1;
            bytes[2] = Magic2;
            bytes[3] = Magic3;
            WriteUInt16(bytes, 4, Format);
            WriteUInt16(bytes, 6, 0);
            WriteInt64(bytes, 8, payload.Length);
            WriteUInt32(bytes, 16, payloadCrc);
            Buffer.BlockCopy(payload, 0, bytes, HeaderSize, payload.Length);
            return bytes;
        }

        /// <summary>
        /// Checks the envelope against itself and against the metadata (expectedLength = payloadBytes,
        /// expectedCrc = payloadCrc32) and copies the payload out. False means SaveCorrupt. Never throws.
        /// </summary>
        public static bool TryUnwrap(byte[] bytes, long expectedLength, uint expectedCrc, out byte[] payload)
        {
            return TryUnwrap(bytes, expectedLength, expectedCrc, out payload, out _);
        }

        /// <summary>As above; failure names the first rule that failed (log text only).</summary>
        public static bool TryUnwrap(byte[] bytes, long expectedLength, uint expectedCrc, out byte[] payload,
                                     out string failure)
        {
            payload = null;
            if (bytes == null || bytes.Length < HeaderSize)
            {
                failure = "file shorter than the envelope header";
                return false;
            }
            if (bytes[0] != Magic0 || bytes[1] != Magic1 || bytes[2] != Magic2 || bytes[3] != Magic3)
            {
                failure = "bad magic";
                return false;
            }
            ushort format = ReadUInt16(bytes, 4);
            if (format != Format)
            {
                failure = "envelope format " + format.ToString(CultureInfo.InvariantCulture);
                return false;
            }

            long length = ReadInt64(bytes, 8);
            if (length <= 0 || length > MaxPayloadBytes || length != bytes.Length - HeaderSize)
            {
                failure = "payload length " + length.ToString(CultureInfo.InvariantCulture)
                    + " does not match the file length";
                return false;
            }
            if (length != expectedLength)
            {
                failure = "payload length does not match the metadata";
                return false;
            }

            uint stored = ReadUInt32(bytes, 16);
            uint computed = Crc32.Compute(bytes, HeaderSize, (int)length);
            if (stored != computed)
            {
                failure = "payload CRC does not match the envelope";
                return false;
            }
            if (stored != expectedCrc)
            {
                failure = "payload CRC does not match the metadata";
                return false;
            }

            payload = new byte[length];
            Buffer.BlockCopy(bytes, HeaderSize, payload, 0, (int)length);
            failure = null;
            return true;
        }

        private static void WriteUInt16(byte[] b, int o, ushort v)
        {
            b[o] = (byte)v;
            b[o + 1] = (byte)(v >> 8);
        }

        private static void WriteUInt32(byte[] b, int o, uint v)
        {
            for (int i = 0; i < 4; i++) b[o + i] = (byte)(v >> (8 * i));
        }

        private static void WriteInt64(byte[] b, int o, long v)
        {
            ulong u = (ulong)v;
            for (int i = 0; i < 8; i++) b[o + i] = (byte)(u >> (8 * i));
        }

        private static ushort ReadUInt16(byte[] b, int o)
        {
            return (ushort)(b[o] | (b[o + 1] << 8));
        }

        private static uint ReadUInt32(byte[] b, int o)
        {
            uint v = 0;
            for (int i = 0; i < 4; i++) v |= (uint)b[o + i] << (8 * i);
            return v;
        }

        private static long ReadInt64(byte[] b, int o)
        {
            ulong v = 0;
            for (int i = 0; i < 8; i++) v |= (ulong)b[o + i] << (8 * i);
            return (long)v;
        }
    }

    /// <summary>Slot ids and the file names used identically in Steam Remote Storage and the local directory.</summary>
    public static class WorldSaveNames
    {
        public const int MaxSlotIdLength = 40;
        public const string FilePrefix = "ws_";
        public const string PayloadExtension = ".bin";
        public const string MetaExtension = ".json";

        /// <summary>Suffix of the local write-then-copy temporaries (04 §2.4 step 6); never listed.</summary>
        public const string TempSuffix = ".tmp";

        /// <summary>Directory.GetFiles pattern for metadata files; results are re-checked with TrySlotIdFromMetaFileName.</summary>
        public const string MetaSearchPattern = "ws_*.json";

        /// <summary>KitResult.Detail of Save when a new slot is requested at MaxSlots (01 §4.7).</summary>
        public const string SlotsFullDetail = "slots full";

        /// <summary>Cloud listing filter (04 §2.4). \z rather than $, so a trailing newline never matches.</summary>
        public static readonly Regex MetaFileNameRegex =
            new Regex(@"^ws_[a-z0-9\-]{1,40}\.json\z", RegexOptions.CultureInvariant);

        /// <summary>^[a-z0-9\-]{1,40}$, checked character by character (ASCII only).</summary>
        public static bool IsValidSlotId(string slotId)
        {
            if (string.IsNullOrEmpty(slotId) || slotId.Length > MaxSlotIdLength) return false;
            for (int i = 0; i < slotId.Length; i++)
            {
                char c = slotId[i];
                bool ok = (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '-';
                if (!ok) return false;
            }
            return true;
        }

        public static string PayloadFileName(string slotId)
        {
            return FilePrefix + slotId + PayloadExtension;
        }

        public static string MetaFileName(string slotId)
        {
            return FilePrefix + slotId + MetaExtension;
        }

        /// <summary>"ws_&lt;id&gt;.json" (file name only, no directory) → id. False for anything else, including ".tmp".</summary>
        public static bool TrySlotIdFromMetaFileName(string fileName, out string slotId)
        {
            slotId = null;
            if (fileName == null
                || fileName.Length <= FilePrefix.Length + MetaExtension.Length
                || !fileName.StartsWith(FilePrefix, StringComparison.Ordinal)
                || !fileName.EndsWith(MetaExtension, StringComparison.Ordinal))
            {
                return false;
            }
            string id = fileName.Substring(FilePrefix.Length, fileName.Length - FilePrefix.Length - MetaExtension.Length);
            if (!IsValidSlotId(id)) return false;
            slotId = id;
            return true;
        }

        /// <summary>New slot id "w-yyyyMMdd-HHmmss-xxxx": UTC time plus 4 lowercase hex digits from random.</summary>
        public static string NewSlotId(DateTime utcNow, Random random)
        {
            if (random == null) throw new ArgumentNullException(nameof(random));
            return "w-" + utcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-"
                + random.Next(0, 0x10000).ToString("x4", CultureInfo.InvariantCulture);
        }
    }
}

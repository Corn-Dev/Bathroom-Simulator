// Byte layout of the NGO connection-approval payload (decision 5) and the KitError text form carried as NGO's
// disconnect reason. Design: 01 §4.6 (layout, limits), 03 §10.1 (encode), §10.2 (decode in approval), §10.3.
// Field order (little-endian; Encode writes and TryDecode reads exactly this; total <= 256 bytes, no trailing bytes):
//   1. ushort  magic 0x4D4B ("MK"; on the wire 0x4B 0x4D)
//   2. int32   KitProtocol
//   3. uint64  Player (PlayerId.Value)
//   4. ushort  GameVersion UTF-8 byte count, then those bytes
//   5. ushort  PasswordHash UTF-8 byte count, then those bytes

using System;
using System.Globalization;
using System.Text;

namespace MultiplayerKit
{
    public static class ConnectionPayloadCodec
    {
        public const ushort Magic = 0x4D4B;

        /// <summary>Upper bound of an encoded payload; longer input fails TryDecode.</summary>
        public const int MaxBytes = 256;

        // magic + proto + player + two length prefixes
        private const int FixedBytes = 2 + 4 + 8 + 2 + 2;

        // Throws on invalid UTF-16/UTF-8 instead of silently substituting characters.
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        /// <summary>
        /// Field order above. Null strings (default(ConnectionPayload)) are written as "". Throws ArgumentException
        /// if the result would exceed 256 bytes or a string is not valid UTF-16; neither happens for a 32-char
        /// game version and a 64-char hash (at most 18 + 96 + 64 bytes).
        /// </summary>
        public static byte[] Encode(ConnectionPayload payload)
        {
            byte[] version = StrictUtf8.GetBytes(payload.GameVersion ?? "");
            byte[] hash = StrictUtf8.GetBytes(payload.PasswordHash ?? "");
            int total = FixedBytes + version.Length + hash.Length;
            if (total > MaxBytes)
            {
                throw new ArgumentException("Connection payload is " + total + " bytes; the limit is " + MaxBytes + ".", nameof(payload));
            }

            var bytes = new byte[total];
            int o = 0;
            o = WriteUInt16(bytes, o, Magic);
            o = WriteInt32(bytes, o, payload.KitProtocol);
            o = WriteUInt64(bytes, o, payload.Player.Value);
            o = WriteString(bytes, o, version);
            WriteString(bytes, o, hash);
            return bytes;
        }

        /// <summary>
        /// False (payload = default) on null, fewer than 18 or more than 256 bytes, wrong magic, a length prefix
        /// past the end, invalid UTF-8, or trailing bytes. Never throws. Protocol, version, player and hash are
        /// not judged here; approval does that (01 §6.3).
        /// </summary>
        public static bool TryDecode(byte[] data, out ConnectionPayload payload)
        {
            payload = default(ConnectionPayload);
            if (data == null || data.Length < FixedBytes || data.Length > MaxBytes) return false;

            int o = 0;
            if (ReadUInt16(data, ref o) != Magic) return false;
            int proto = ReadInt32(data, ref o);
            ulong player = ReadUInt64(data, ref o);
            if (!TryReadString(data, ref o, out string version)) return false;
            if (!TryReadString(data, ref o, out string hash)) return false;
            if (o != data.Length) return false;

            payload = new ConnectionPayload(proto, version, new PlayerId(player), hash);
            return true;
        }

        /// <summary>Disconnect / rejection reason text: the decimal KitError value (03 §10.2 step 5, §13).</summary>
        public static string EncodeReason(KitError reason)
        {
            return ((int)reason).ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Client side of 03 §10.3: decimal digits naming a defined KitError other than None; anything else (null,
        /// "", text, unknown numbers, "0") is ConnectionLost. Never returns None.
        /// </summary>
        public static KitError DecodeReason(string text)
        {
            if (!string.IsNullOrEmpty(text)
                && int.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int code)
                && code != (int)KitError.None
                && Enum.IsDefined(typeof(KitError), code))
            {
                return (KitError)code;
            }
            return KitError.ConnectionLost;
        }

        // Little-endian primitives (explicit shifts, independent of the platform's byte order).

        private static int WriteUInt16(byte[] b, int o, ushort v)
        {
            b[o] = (byte)v;
            b[o + 1] = (byte)(v >> 8);
            return o + 2;
        }

        private static int WriteInt32(byte[] b, int o, int v)
        {
            for (int i = 0; i < 4; i++) b[o + i] = (byte)(v >> (8 * i));
            return o + 4;
        }

        private static int WriteUInt64(byte[] b, int o, ulong v)
        {
            for (int i = 0; i < 8; i++) b[o + i] = (byte)(v >> (8 * i));
            return o + 8;
        }

        private static int WriteString(byte[] b, int o, byte[] utf8)
        {
            o = WriteUInt16(b, o, (ushort)utf8.Length);
            Buffer.BlockCopy(utf8, 0, b, o, utf8.Length);
            return o + utf8.Length;
        }

        private static ushort ReadUInt16(byte[] b, ref int o)
        {
            ushort v = (ushort)(b[o] | (b[o + 1] << 8));
            o += 2;
            return v;
        }

        private static int ReadInt32(byte[] b, ref int o)
        {
            int v = 0;
            for (int i = 0; i < 4; i++) v |= b[o + i] << (8 * i);
            o += 4;
            return v;
        }

        private static ulong ReadUInt64(byte[] b, ref int o)
        {
            ulong v = 0;
            for (int i = 0; i < 8; i++) v |= (ulong)b[o + i] << (8 * i);
            o += 8;
            return v;
        }

        // Length prefix, then that many UTF-8 bytes; false if either runs past the end or the bytes are invalid.
        private static bool TryReadString(byte[] b, ref int o, out string value)
        {
            value = null;
            if (b.Length - o < 2) return false;
            int length = ReadUInt16(b, ref o);
            if (length > b.Length - o) return false;
            try
            {
                value = StrictUtf8.GetString(b, o, length);
            }
            catch (ArgumentException)
            {
                return false;
            }
            o += length;
            return true;
        }
    }
}

// Table-driven CRC-32 (IEEE 802.3: reflected, polynomial 0xEDB88320, initial value and final XOR 0xFFFFFFFF).
// It binds a world save's payload file to its metadata file. Design: 04 §2.2 (payloadCrc32), 04 §2.3, 06 C41.
// Check value: the CRC of the ASCII bytes "123456789" is 0xCBF43926. Pure C#, no UnityEngine.

using System;

namespace MultiplayerKit
{
    public static class Crc32
    {
        private const uint Polynomial = 0xEDB88320u;

        private static readonly uint[] Table = BuildTable();

        /// <summary>CRC of the whole array. Throws ArgumentNullException for null.</summary>
        public static uint Compute(byte[] data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            return Compute(data, 0, data.Length);
        }

        /// <summary>CRC of data[offset .. offset + count). An empty range returns 0.</summary>
        public static uint Compute(byte[] data, int offset, int count)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (offset < 0 || count < 0 || offset > data.Length - count)
            {
                throw new ArgumentOutOfRangeException(nameof(count), "Range is outside the array.");
            }

            uint crc = 0xFFFFFFFFu;
            int end = offset + count;
            for (int i = offset; i < end; i++)
            {
                crc = Table[(crc ^ data[i]) & 0xFFu] ^ (crc >> 8);
            }
            return ~crc;
        }

        private static uint[] BuildTable()
        {
            var table = new uint[256];
            for (uint n = 0; n < 256; n++)
            {
                uint c = n;
                for (int k = 0; k < 8; k++)
                {
                    c = (c & 1u) != 0 ? Polynomial ^ (c >> 1) : c >> 1;
                }
                table[n] = c;
            }
            return table;
        }
    }
}

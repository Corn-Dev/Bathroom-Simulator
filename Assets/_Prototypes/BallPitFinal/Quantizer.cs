using UnityEngine;

namespace BallPitFinal
{
    /// <summary>
    /// Fixed-point positions relative to the play bounds, identical on every peer (the host sends the bounds).
    /// Q21 (21 bits per axis in a ulong, ~0.1 mm on a 200 m world) is THE stored form of every resting pose:
    /// a pose is snapped once (at fill or freeze) and the collider, renderer, save and network all use that value.
    /// Encode(Decode(q)) == q, so snapping is idempotent. Q16 (16 bits per axis) is only for moving-ball snapshots.
    /// </summary>
    public sealed class Quantizer
    {
        public readonly Vector3 Min, Size;
        private const float Max21 = 2097151f, Max16 = 65535f;
        private const ulong M21 = 0x1FFFFF;
        private readonly float sx, sy, sz, ix, iy, iz;
        private readonly float sx16, sy16, sz16, ix16, iy16, iz16;

        public Quantizer(Bounds b)
        {
            Min = b.min; Size = b.size;
            sx = Size.x / Max21; sy = Size.y / Max21; sz = Size.z / Max21;
            ix = Max21 / Size.x; iy = Max21 / Size.y; iz = Max21 / Size.z;
            sx16 = Size.x / Max16; sy16 = Size.y / Max16; sz16 = Size.z / Max16;
            ix16 = Max16 / Size.x; iy16 = Max16 / Size.y; iz16 = Max16 / Size.z;
        }

        private static uint Q(float v, float inv, float max)
        {
            float t = v * inv + 0.5f;
            if (t <= 0f) return 0u;
            if (t >= max) return (uint)max;
            return (uint)t;
        }

        public ulong Encode(Vector3 p)
        {
            ulong x = Q(p.x - Min.x, ix, Max21), y = Q(p.y - Min.y, iy, Max21), z = Q(p.z - Min.z, iz, Max21);
            return x | (y << 21) | (z << 42);
        }

        public Vector3 Decode(ulong q)
        {
            return new Vector3(Min.x + (float)(q & M21) * sx,
                               Min.y + (float)((q >> 21) & M21) * sy,
                               Min.z + (float)((q >> 42) & M21) * sz);
        }

        public Vector3 Snap(Vector3 p) => Decode(Encode(p));

        public void Encode16(Vector3 p, out ushort x, out ushort y, out ushort z)
        {
            x = (ushort)Q(p.x - Min.x, ix16, Max16);
            y = (ushort)Q(p.y - Min.y, iy16, Max16);
            z = (ushort)Q(p.z - Min.z, iz16, Max16);
        }

        public Vector3 Decode16(ushort x, ushort y, ushort z) =>
            new Vector3(Min.x + x * sx16, Min.y + y * sy16, Min.z + z * sz16);
    }
}

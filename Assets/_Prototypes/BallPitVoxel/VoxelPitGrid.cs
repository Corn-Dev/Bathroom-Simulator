using UnityEngine;

namespace BallPitProto
{
    /// <summary>
    /// PROTOTYPE — not final. Local, single-player, no networking.
    ///
    /// The authoritative pit data: a 3D grid of cells. Each cell is either
    /// empty or holds a ball of a given color index. This is the "voxel"
    /// source of truth described in the networking-and-pit steering doc.
    ///
    /// No cell is a GameObject. Rendering and physics live elsewhere; this is
    /// pure data + the dig/settle rules operating on it.
    ///
    /// Coordinate convention:
    ///   x = width, y = height (up), z = depth.
    ///   Cell (x,y,z) world center = origin + (x+0.5, y+0.5, z+0.5) * cellSize.
    /// </summary>
    public sealed class VoxelPitGrid
    {
        public const int Empty = -1;

        public readonly int SizeX;
        public readonly int SizeY;
        public readonly int SizeZ;
        public readonly float CellSize;
        public readonly Vector3 Origin;

        // Flattened grid. Value is a color index, or Empty.
        private readonly sbyte[] cells;

        public int FilledCount { get; private set; }

        public VoxelPitGrid(int sizeX, int sizeY, int sizeZ, float cellSize, Vector3 origin)
        {
            SizeX = sizeX;
            SizeY = sizeY;
            SizeZ = sizeZ;
            CellSize = cellSize;
            Origin = origin;
            cells = new sbyte[sizeX * sizeY * sizeZ];
            for (int i = 0; i < cells.Length; i++) cells[i] = (sbyte)Empty;
        }

        public int Index(int x, int y, int z) => (y * SizeZ + z) * SizeX + x;

        public bool InBounds(int x, int y, int z) =>
            x >= 0 && x < SizeX && y >= 0 && y < SizeY && z >= 0 && z < SizeZ;

        public int Get(int x, int y, int z) => cells[Index(x, y, z)];

        public bool IsEmpty(int x, int y, int z) => cells[Index(x, y, z)] == Empty;

        public void Set(int x, int y, int z, int color)
        {
            int i = Index(x, y, z);
            bool wasFilled = cells[i] != Empty;
            bool willFill = color != Empty;
            if (wasFilled && !willFill) FilledCount--;
            else if (!wasFilled && willFill) FilledCount++;
            cells[i] = (sbyte)color;
        }

        /// <summary>Grid-center of a cell (no packing offset / no jitter).</summary>
        public Vector3 CellToWorld(int x, int y, int z) =>
            Origin + new Vector3((x + 0.5f) * CellSize, (y + 0.5f) * CellSize, (z + 0.5f) * CellSize);

        // --- Packing tunables (set from the controller) ---
        // How much to shift alternate rows/layers so balls nestle into valleys
        // (0 = cubic stacking, 0.5 = balls sit over the gap between two below).
        public float PackOffset = 0.5f;
        // Vertical layer spacing as a fraction of CellSize. Real close-packed
        // sphere layers sit ~0.816 of a diameter apart (sqrt(2/3)), so layers
        // interlock instead of stacking straight up. Lower = denser/deeper look.
        public float LayerSpacing = 0.82f;
        // Random jitter magnitude as a fraction of CellSize.
        public float Jitter = 0.08f;

        /// <summary>
        /// World position for the VISUAL ball in a cell. Approximates how real
        /// dumped spheres settle (random close packing): alternate rows/layers
        /// are offset so each ball nestles into the valley of the layer beneath,
        /// vertical spacing is compressed so layers interlock, and a small
        /// deterministic jitter breaks up the lattice. The result reads as a
        /// dense, deep pile with no see-through gaps. Deterministic per cell.
        /// </summary>
        public Vector3 CellToWorldVisual(int x, int y, int z)
        {
            float cs = CellSize;

            // Base position with compressed vertical spacing (layers interlock).
            float px = Origin.x + (x + 0.5f) * cs;
            float py = Origin.y + (y + 0.5f) * LayerSpacing * cs;
            float pz = Origin.z + (z + 0.5f) * cs;

            // Hexagonal-close-packing style offsets: each successive layer and
            // row shifts so balls sit over the gaps below, not directly on top.
            float off = PackOffset * cs * 0.5f;
            if ((y & 1) == 1) { px += off; pz += off; }   // every other layer
            if ((z & 1) == 1) px += off;                   // every other row
            if ((y % 3) == 2) { px -= off; pz -= off; }    // 3-layer ABC cycle

            // Deterministic per-cell jitter.
            float j = Jitter * cs;
            px += (Hash01(x * 73856093 ^ y * 19349663 ^ z * 83492791) - 0.5f) * 2f * j;
            py += (Hash01(x * 19349663 ^ y * 83492791 ^ z * 73856093) - 0.5f) * 2f * j;
            pz += (Hash01(x * 83492791 ^ y * 73856093 ^ z * 19349663) - 0.5f) * 2f * j;

            return new Vector3(px, py, pz);
        }

        private static float Hash01(int n)
        {
            n = (n << 13) ^ n;
            int m = (n * (n * n * 15731 + 789221) + 1376312589) & 0x7fffffff;
            return m / 2147483647f;
        }

        /// <summary>Fill the whole grid with random colors (a packed pit).</summary>
        public void FillSolid(int colorCount, int seed)
        {
            var rng = new System.Random(seed);
            for (int i = 0; i < cells.Length; i++)
                cells[i] = (sbyte)rng.Next(0, colorCount);
            FilledCount = cells.Length;
        }

        /// <summary>
        /// Clear everything, then fill only a sub-region [x0..x1) x [0..height) x
        /// [z0..z1) with random colors — the "pit" sitting inside a larger play-
        /// area grid whose surrounding columns start empty (the floor around the
        /// pit, where dumped balls accumulate as cells).
        /// </summary>
        public void FillRegion(int x0, int x1, int z0, int z1, int height, int colorCount, int seed)
        {
            var rng = new System.Random(seed);
            int filled = 0;
            for (int i = 0; i < cells.Length; i++) cells[i] = (sbyte)Empty;
            for (int y = 0; y < height && y < SizeY; y++)
            for (int z = Mathf.Max(0, z0); z < Mathf.Min(SizeZ, z1); z++)
            for (int x = Mathf.Max(0, x0); x < Mathf.Min(SizeX, x1); x++)
            {
                cells[Index(x, y, z)] = (sbyte)rng.Next(0, colorCount);
                filled++;
            }
            FilledCount = filled;
        }

        /// <summary>
        /// Topmost filled cell y at column (x,z), or -1 if the column is empty.
        /// This is "the surface" at that column.
        /// </summary>
        public int TopFilledY(int x, int z)
        {
            for (int y = SizeY - 1; y >= 0; y--)
                if (cells[Index(x, y, z)] != Empty) return y;
            return -1;
        }
    }
}

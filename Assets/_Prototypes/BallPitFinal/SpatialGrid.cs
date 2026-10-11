using System.Collections.Generic;
using UnityEngine;

namespace BallPitFinal
{
    /// <summary>
    /// Index only (never a position source). Bounded uniform grid, lazily allocated in 8x8x8-cell chunks,
    /// with intrusive doubly-linked lists through flat per-ball arrays: Insert/Remove/Move are O(1) and
    /// allocation-free; queries are plain array walks. Positions outside the bounds are clamped into the
    /// border cells, which stays correct because every query does an exact distance test.
    /// </summary>
    public sealed class SpatialGrid
    {
        public readonly float CellSize;
        public readonly Vector3 Min;
        public readonly int Nx, Ny, Nz;
        public int Count { get; private set; }

        private const int CB = 3, CM = 7, CCells = 512;
        private readonly float inv;
        private readonly int cnx, cny, cnz;
        private readonly int[][] chunks;
        private readonly int[] next, prev, cellOf; // per ball id; cellOf = linear cell index or -1

        public SpatialGrid(Bounds bounds, float cellSize, int capacity)
        {
            CellSize = cellSize; inv = 1f / cellSize; Min = bounds.min;
            Nx = Mathf.Max(1, Mathf.CeilToInt(bounds.size.x * inv));
            Ny = Mathf.Max(1, Mathf.CeilToInt(bounds.size.y * inv));
            Nz = Mathf.Max(1, Mathf.CeilToInt(bounds.size.z * inv));
            cnx = (Nx + CM) >> CB; cny = (Ny + CM) >> CB; cnz = (Nz + CM) >> CB;
            chunks = new int[cnx * cny * cnz][];
            next = new int[capacity]; prev = new int[capacity]; cellOf = new int[capacity];
            for (int i = 0; i < capacity; i++) cellOf[i] = -1;
        }

        public int CellX(float x) { int c = (int)System.Math.Floor((x - Min.x) * inv); return c < 0 ? 0 : (c >= Nx ? Nx - 1 : c); }
        public int CellY(float y) { int c = (int)System.Math.Floor((y - Min.y) * inv); return c < 0 ? 0 : (c >= Ny ? Ny - 1 : c); }
        public int CellZ(float z) { int c = (int)System.Math.Floor((z - Min.z) * inv); return c < 0 ? 0 : (c >= Nz ? Nz - 1 : c); }

        private int ChunkIndex(int x, int y, int z) => ((z >> CB) * cny + (y >> CB)) * cnx + (x >> CB);
        private static int Local(int x, int y, int z) => ((z & CM) << 6) | ((y & CM) << 3) | (x & CM);

        /// <summary>First ball id in a cell or -1. Cell coords must be in range.</summary>
        public int Head(int x, int y, int z)
        {
            int[] ch = chunks[ChunkIndex(x, y, z)];
            return ch == null ? -1 : ch[Local(x, y, z)];
        }

        public int Next(int id) => next[id];
        public bool Contains(int id) => cellOf[id] >= 0;

        public void Insert(int id, Vector3 p)
        {
            if (cellOf[id] >= 0) Remove(id);
            int x = CellX(p.x), y = CellY(p.y), z = CellZ(p.z);
            int ci = ChunkIndex(x, y, z);
            int[] ch = chunks[ci];
            if (ch == null)
            {
                ch = new int[CCells];
                for (int i = 0; i < CCells; i++) ch[i] = -1;
                chunks[ci] = ch;
            }
            int l = Local(x, y, z);
            int h = ch[l];
            next[id] = h; prev[id] = -1;
            if (h >= 0) prev[h] = id;
            ch[l] = id;
            cellOf[id] = (z * Ny + y) * Nx + x;
            Count++;
        }

        public void Remove(int id)
        {
            int lin = cellOf[id];
            if (lin < 0) return;
            int x = lin % Nx, t = lin / Nx, y = t % Ny, z = t / Ny;
            int p = prev[id], n = next[id];
            if (p >= 0) next[p] = n; else chunks[ChunkIndex(x, y, z)][Local(x, y, z)] = n;
            if (n >= 0) prev[n] = p;
            cellOf[id] = -1;
            Count--;
        }

        public void Move(int id, Vector3 p)
        {
            int lin = (CellZ(p.z) * Ny + CellY(p.y)) * Nx + CellX(p.x);
            if (cellOf[id] == lin) return;
            Insert(id, p);
        }

        /// <summary>Clears and fills results with ids whose position is within radius of p (exact test).</summary>
        public void Gather(Vector3 p, float radius, Vector3[] positions, List<int> results, int exclude = -1)
        {
            results.Clear();
            float r2 = radius * radius;
            int x0 = CellX(p.x - radius), x1 = CellX(p.x + radius);
            int y0 = CellY(p.y - radius), y1 = CellY(p.y + radius);
            int z0 = CellZ(p.z - radius), z1 = CellZ(p.z + radius);
            for (int z = z0; z <= z1; z++)
            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                int id = Head(x, y, z);
                while (id >= 0)
                {
                    if (id != exclude)
                    {
                        Vector3 d = positions[id] - p;
                        if (d.x * d.x + d.y * d.y + d.z * d.z <= r2) results.Add(id);
                    }
                    id = next[id];
                }
            }
        }

        public void Clear()
        {
            for (int i = 0; i < chunks.Length; i++) chunks[i] = null;
            for (int i = 0; i < cellOf.Length; i++) cellOf[i] = -1;
            Count = 0;
        }
    }
}

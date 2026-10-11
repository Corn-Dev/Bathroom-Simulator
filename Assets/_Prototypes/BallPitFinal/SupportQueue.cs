using System.Collections.Generic;
using UnityEngine;

namespace BallPitFinal
{
    /// <summary>
    /// Collapse scheduler. Resting balls waiting for a support test are bucketed into vertical columns ("regions",
    /// regionSize x regionSize in XZ). Each region is its own min-heap by height (lowest first = local void migration
    /// inside that column), and the drain visits regions round-robin with a fair share each. Independent collapse
    /// fronts (every stretch of a long tunnel, several craters, a dump pile) therefore progress concurrently instead of
    /// being serialized by one global height order. Deduplicated per ball; remembers the first queued step (hang metric).
    /// </summary>
    public sealed class SupportQueue
    {
        private sealed class Heap
        {
            public float[] keys = new float[64];
            public int[] ids = new int[64];
            public int count;
        }

        private readonly Heap[] heaps;
        private readonly int nx, nz;
        private readonly float minX, minZ, inv;
        private readonly bool[] queued;
        private readonly int[] firstStep;
        private readonly List<int> active = new List<int>(256);
        private readonly bool[] inActive;
        private int rr, total;

        public int Count => total;
        public int ActiveRegions => active.Count;

        public SupportQueue(Bounds bounds, float regionSize, int ballCapacity)
        {
            regionSize = Mathf.Max(0.1f, regionSize);
            inv = 1f / regionSize;
            minX = bounds.min.x; minZ = bounds.min.z;
            nx = Mathf.Max(1, Mathf.CeilToInt(bounds.size.x * inv));
            nz = Mathf.Max(1, Mathf.CeilToInt(bounds.size.z * inv));
            heaps = new Heap[nx * nz];
            inActive = new bool[nx * nz];
            queued = new bool[ballCapacity];
            firstStep = new int[ballCapacity];
        }

        public bool Contains(int id) => queued[id];

        private int RegionOf(Vector3 p)
        {
            int x = (int)System.Math.Floor((p.x - minX) * inv), z = (int)System.Math.Floor((p.z - minZ) * inv);
            x = x < 0 ? 0 : (x >= nx ? nx - 1 : x);
            z = z < 0 ? 0 : (z >= nz ? nz - 1 : z);
            return z * nx + x;
        }

        public void Push(int id, Vector3 p, int step)
        {
            if (queued[id]) return;
            queued[id] = true;
            firstStep[id] = step;
            int r = RegionOf(p);
            Heap h = heaps[r] ?? (heaps[r] = new Heap());
            if (h.count == h.keys.Length)
            {
                System.Array.Resize(ref h.keys, h.count * 2);
                System.Array.Resize(ref h.ids, h.count * 2);
            }
            float key = p.y;
            int i = h.count++;
            while (i > 0)
            {
                int parent = (i - 1) >> 1;
                if (h.keys[parent] <= key) break;
                h.keys[i] = h.keys[parent]; h.ids[i] = h.ids[parent];
                i = parent;
            }
            h.keys[i] = key; h.ids[i] = id;
            total++;
            if (!inActive[r]) { inActive[r] = true; active.Add(r); }
        }

        /// <summary>Next non-empty region in round-robin order, or -1 if the queue is empty.</summary>
        public int NextRegion()
        {
            while (active.Count > 0)
            {
                if (rr >= active.Count) rr = 0;
                int r = active[rr];
                if (heaps[r].count == 0)
                {
                    inActive[r] = false;
                    active[rr] = active[active.Count - 1];
                    active.RemoveAt(active.Count - 1);
                    continue;
                }
                rr++;
                return r;
            }
            return -1;
        }

        /// <summary>Pops the lowest ball of a region.</summary>
        public bool TryPop(int region, out int id, out int queuedAtStep)
        {
            Heap h = heaps[region];
            if (h == null || h.count == 0) { id = -1; queuedAtStep = 0; return false; }
            id = h.ids[0];
            queued[id] = false;
            queuedAtStep = firstStep[id];
            total--;
            int n = --h.count;
            if (n > 0)
            {
                float key = h.keys[n];
                int last = h.ids[n];
                int i = 0;
                while (true)
                {
                    int l = 2 * i + 1;
                    if (l >= n) break;
                    int r = l + 1;
                    int m = (r < n && h.keys[r] < h.keys[l]) ? r : l;
                    if (h.keys[m] >= key) break;
                    h.keys[i] = h.keys[m]; h.ids[i] = h.ids[m];
                    i = m;
                }
                h.keys[i] = key; h.ids[i] = last;
            }
            return true;
        }

        public void Clear()
        {
            for (int k = 0; k < active.Count; k++)
            {
                Heap h = heaps[active[k]];
                for (int i = 0; i < h.count; i++) queued[h.ids[i]] = false;
                h.count = 0;
                inActive[active[k]] = false;
            }
            active.Clear();
            total = 0; rr = 0;
        }
    }
}

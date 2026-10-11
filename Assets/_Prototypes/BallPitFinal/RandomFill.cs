using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;

namespace BallPitFinal
{
    /// <summary>
    /// Random loose packing by sequential deposition ("pour"): each ball drops at a random spot in the pit and slides
    /// under gravity (projected descent against the pile, frictionless) until it sits in a stable pocket: the floor,
    /// or contacts whose horizontal directions surround its center (the same rule PitAuthority uses to decide support).
    /// Gives ~0.58 packing like a real poured pit, so crater walls slump instead of standing as FCC terraces.
    /// Pure C# (worker-thread safe). The result is cached as a file so every peer loads identical bytes:
    /// generate once in the editor (written to StreamingAssets/BallPitFills) and ship it with the build.
    /// </summary>
    public static class RandomFill
    {
        public const int Version = 1;
        private const uint Magic = 0x31465042; // "BPF1"

        public static string FileName(PitSettings s)
        {
            ulong h = 14695981039346656037UL;
            void Mix(long v) { for (int i = 0; i < 8; i++) { h ^= (ulong)((v >> (i * 8)) & 0xFF); h *= 1099511628211UL; } }
            Mix(Version); Mix(s.pitX); Mix(s.pitY); Mix(s.pitZ); Mix(s.colorCount); Mix(s.seed);
            Mix(Mathf.RoundToInt(s.ballDiameter * 100000f)); Mix(Mathf.RoundToInt(s.colliderRadiusScale * 100000f));
            Mix(Mathf.RoundToInt(s.jitter * 100000f)); Mix(Mathf.RoundToInt(s.maxSupportGapDegrees * 1000f));
            return $"fill_{h:x16}.bin";
        }

        public static Task<string> LoadOrGenerateAsync(PitWorld w, string streamingDir, string persistentDir,
                                                       bool writeToStreaming, System.Action<float> progress)
        {
            return Task.Run(() => LoadOrGenerate(w, streamingDir, persistentDir, writeToStreaming, progress));
        }

        public static string LoadOrGenerate(PitWorld w, string streamingDir, string persistentDir,
                                            bool writeToStreaming, System.Action<float> progress)
        {
            string name = FileName(w.S);
            string a = Path.Combine(streamingDir, "BallPitFills", name);
            string b = Path.Combine(persistentDir, "BallPitFills", name);
            if (File.Exists(a) && TryLoad(w, a)) return "loaded " + a;
            if (File.Exists(b) && TryLoad(w, b)) return "loaded " + b;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            Generate(w, progress);
            string outPath = writeToStreaming ? a : b;
            try { Save(w, outPath); }
            catch (System.Exception e) { return $"generated in {sw.Elapsed.TotalSeconds:0.0}s (save failed: {e.Message})"; }
            return $"generated in {sw.Elapsed.TotalSeconds:0.0}s -> {outPath}";
        }

        public static void Generate(PitWorld w, System.Action<float> progress)
        {
            PitSettings S = w.S;
            int total = S.pitX * S.pitY * S.pitZ;
            float r = w.ColliderRadius * 1.001f, d = 2f * r, d2 = d * d;
            float minX = w.WallMinX + r, maxX = w.WallMaxX - r, minZ = w.WallMinZ + r, maxZ = w.WallMaxZ - r;
            float floor = w.FloorY + r;
            SpatialGrid grid = w.Resting;
            Vector3[] pos = w.Store.pos;
            var rng = new System.Random(S.seed);
            int nx = grid.Nx, nz = grid.Nz;
            var top = new float[nx * nz];
            for (int i = 0; i < top.Length; i++) top[i] = float.MinValue;
            var cache = new List<int>(128);
            var cpos = new Vector3[256];
            int cn = 0;
            var angles = new float[64];
            float maxGap = S.maxSupportGapDegrees * Mathf.Deg2Rad;
            float cacheMove2 = (0.5f * r) * (0.5f * r);

            void Refill(Vector3 c)
            {
                grid.Gather(c, 3f * r, pos, cache);
                if (cache.Count > cpos.Length) cpos = new Vector3[cache.Count * 2];
                cn = cache.Count;
                for (int k = 0; k < cn; k++) cpos[k] = pos[cache[k]];
            }

            float DropTo(Vector3 p)
            {
                float best = floor;
                int cx = grid.CellX(p.x), cz = grid.CellZ(p.z);
                for (int cy = grid.CellY(p.y); cy >= 0; cy--)
                {
                    float cellTop = grid.Min.y + (cy + 1) * grid.CellSize;
                    if (cellTop + d < best) break;
                    for (int dz = -1; dz <= 1; dz++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int ux = cx + dx, uz = cz + dz;
                        if (ux < 0 || uz < 0 || ux >= nx || uz >= nz) continue;
                        for (int id = grid.Head(ux, cy, uz); id >= 0; id = grid.Next(id))
                        {
                            Vector3 q = pos[id];
                            float hx = q.x - p.x, hz = q.z - p.z, h2 = hx * hx + hz * hz;
                            if (h2 >= d2) continue;
                            float yc = q.y + Mathf.Sqrt(d2 - h2);
                            if (yc <= p.y + 1e-4f && yc > best) best = yc;
                        }
                    }
                }
                return best;
            }

            Vector3 Settle(Vector3 p)
            {
                p.y = DropTo(p);
                Vector3 cc = p;
                Refill(cc);
                float h = 0.25f * r, tol2 = (d * 1.002f) * (d * 1.002f), direct2 = (0.05f * r) * (0.05f * r);
                for (int it = 0; it < 300; it++)
                {
                    if ((p - cc).sqrMagnitude > cacheMove2) { cc = p; Refill(cc); }
                    p.y -= h;
                    for (int pass = 0; pass < 3; pass++)
                    {
                        for (int k = 0; k < cn; k++)
                        {
                            Vector3 dv = p - cpos[k];
                            float dd = dv.sqrMagnitude;
                            if (dd >= d2) continue;
                            float len = Mathf.Sqrt(dd);
                            if (len < 1e-6f) { dv = Vector3.up; len = 1f; }
                            p = cpos[k] + dv * (d / len);
                        }
                        if (p.y < floor) p.y = floor;
                        p.x = Mathf.Clamp(p.x, minX, maxX);
                        p.z = Mathf.Clamp(p.z, minZ, maxZ);
                    }
                    if (p.y <= floor + 1e-5f) return p;

                    int na = 0, below = 0;
                    bool any = false;
                    for (int k = 0; k < cn && na < angles.Length - 4; k++)
                    {
                        Vector3 dv = cpos[k] - p;
                        if (dv.sqrMagnitude > tol2) continue;
                        any = true;
                        if (dv.y < -0.1f * r)
                        {
                            below++;
                            float hh = dv.x * dv.x + dv.z * dv.z;
                            if (hh < direct2) return p;
                            angles[na++] = Mathf.Atan2(dv.z, dv.x);
                        }
                    }
                    if (p.x <= minX + 1e-4f) { angles[na++] = Mathf.PI; any = true; }
                    if (p.x >= maxX - 1e-4f) { angles[na++] = 0f; any = true; }
                    if (p.z <= minZ + 1e-4f) { angles[na++] = -Mathf.PI * 0.5f; any = true; }
                    if (p.z >= maxZ - 1e-4f) { angles[na++] = Mathf.PI * 0.5f; any = true; }
                    if (below > 0 && na >= 2 && MaxGap(angles, na) < maxGap) return p;
                    if (!any) { p.y = DropTo(p); cc = p; Refill(cc); } // free fall: jump to the next contact
                }
                return p;
            }

            for (int i = 0; i < total; i++)
            {
                float x = minX + (float)rng.NextDouble() * (maxX - minX);
                float z = minZ + (float)rng.NextDouble() * (maxZ - minZ);
                int colorIndex = rng.Next(0, S.colorCount);
                int cx = grid.CellX(x), cz = grid.CellZ(z);
                float startY = floor;
                for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int ux = cx + dx, uz = cz + dz;
                    if (ux < 0 || uz < 0 || ux >= nx || uz >= nz) continue;
                    float tv = top[uz * nx + ux];
                    if (tv > float.MinValue && tv + d > startY) startY = tv + d;
                }
                Vector3 p = Settle(new Vector3(x, startY + 0.01f, z));
                p = w.Q.Snap(p);
                int id = w.Store.Add(p, colorIndex, BallStore.State.Resting);
                grid.Insert(id, p);
                int col = grid.CellZ(p.z) * nx + grid.CellX(p.x);
                if (p.y > top[col]) top[col] = p.y;
                if ((i & 4095) == 0) progress?.Invoke((float)i / total);
            }
            progress?.Invoke(1f);
        }

        private static float MaxGap(float[] a, int n)
        {
            for (int i = 1; i < n; i++)
            {
                float v = a[i]; int k = i - 1;
                while (k >= 0 && a[k] > v) { a[k + 1] = a[k]; k--; }
                a[k + 1] = v;
            }
            float gap = a[0] + 2f * Mathf.PI - a[n - 1];
            for (int i = 1; i < n; i++) gap = Mathf.Max(gap, a[i] - a[i - 1]);
            return gap;
        }

        public static void Save(PitWorld w, string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
            using (var bw = new BinaryWriter(fs))
            {
                bw.Write(Magic); bw.Write(Version); bw.Write(w.Store.Count);
                for (int i = 0; i < w.Store.Count; i++)
                {
                    Vector3 p = w.Store.pos[i];
                    bw.Write(p.x); bw.Write(p.y); bw.Write(p.z); bw.Write(w.Store.color[i]);
                }
            }
        }

        public static bool TryLoad(PitWorld w, string path)
        {
            try
            {
                Vector3[] p; byte[] c; int n;
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read))
                using (var br = new BinaryReader(fs))
                {
                    if (br.ReadUInt32() != Magic || br.ReadInt32() != Version) return false;
                    n = br.ReadInt32();
                    if (n != w.Store.Capacity || w.Store.Count != 0) return false;
                    p = new Vector3[n]; c = new byte[n];
                    for (int i = 0; i < n; i++)
                    {
                        p[i] = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
                        c[i] = br.ReadByte();
                        if (c[i] >= w.Store.Colors) return false;
                    }
                }
                for (int i = 0; i < n; i++)
                {
                    Vector3 q = w.Q.Snap(p[i]);
                    int id = w.Store.Add(q, c[i], BallStore.State.Resting);
                    w.Resting.Insert(id, q);
                }
                return true;
            }
            catch (System.Exception) { return false; }
        }
    }
}

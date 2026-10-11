using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BallPitFinal
{
    /// <summary>True icosphere (icosahedron base), shared vertices, radius 0.5, outward winding guaranteed.</summary>
    public static class BallMeshes
    {
        public static Mesh IcoSphere(int subdivisions, string name)
        {
            float t = (1f + Mathf.Sqrt(5f)) * 0.5f;
            var verts = new List<Vector3>
            {
                new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
                new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
                new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1)
            };
            for (int i = 0; i < verts.Count; i++) verts[i] = verts[i].normalized;
            var tris = new List<int>
            {
                0,11,5, 0,5,1, 0,1,7, 0,7,10, 0,10,11, 1,5,9, 5,11,4, 11,10,2, 10,7,6, 7,1,8,
                3,9,4, 3,4,2, 3,2,6, 3,6,8, 3,8,9, 4,9,5, 2,4,11, 6,2,10, 8,6,7, 9,8,1
            };
            var cache = new Dictionary<long, int>();

            int Mid(int i0, int i1)
            {
                long key = i0 < i1 ? ((long)i0 << 32) | (uint)i1 : ((long)i1 << 32) | (uint)i0;
                if (cache.TryGetValue(key, out int idx)) return idx;
                idx = verts.Count;
                verts.Add(((verts[i0] + verts[i1]) * 0.5f).normalized);
                cache[key] = idx;
                return idx;
            }

            for (int s = 0; s < subdivisions; s++)
            {
                var nt = new List<int>(tris.Count * 4);
                cache.Clear();
                for (int i = 0; i < tris.Count; i += 3)
                {
                    int a = tris[i], b = tris[i + 1], c = tris[i + 2];
                    int ab = Mid(a, b), bc = Mid(b, c), ca = Mid(c, a);
                    nt.Add(a); nt.Add(ab); nt.Add(ca);
                    nt.Add(b); nt.Add(bc); nt.Add(ab);
                    nt.Add(c); nt.Add(ca); nt.Add(bc);
                    nt.Add(ab); nt.Add(bc); nt.Add(ca);
                }
                tris = nt;
            }

            // Unity front faces: Cross(b - a, c - a) points toward the viewer. Force outward for every triangle.
            for (int i = 0; i < tris.Count; i += 3)
            {
                Vector3 a = verts[tris[i]], b = verts[tris[i + 1]], c = verts[tris[i + 2]];
                if (Vector3.Dot(Vector3.Cross(b - a, c - a), a + b + c) < 0f)
                {
                    int tmp = tris[i + 1]; tris[i + 1] = tris[i + 2]; tris[i + 2] = tmp;
                }
            }

            var v = new Vector3[verts.Count];
            var n = new Vector3[verts.Count];
            for (int i = 0; i < verts.Count; i++) { n[i] = verts[i]; v[i] = verts[i] * 0.5f; }
            var m = new Mesh { name = name };
            m.indexFormat = v.Length > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            m.SetVertices(v);
            m.SetNormals(n);
            m.SetTriangles(tris, 0);
            m.RecalculateBounds();
            return m;
        }
    }
}

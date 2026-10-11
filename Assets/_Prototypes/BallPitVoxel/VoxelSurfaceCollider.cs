using System.Collections.Generic;
using UnityEngine;

namespace BallPitProto
{
    /// <summary>
    /// PROTOTYPE — not final.
    ///
    /// Builds ONE collider mesh for the whole pit whose top surface follows the
    /// height of each column (a heightfield). Promoted balls roll/rest on this
    /// instead of us giving 10,000 balls individual colliders.
    ///
    /// Regenerated whenever the grid surface changes. The pit interior is solid
    /// (balls rest on top and roll into craters; they don't tunnel through the
    /// mass) — a deliberate simplification for the ball-pit feel.
    ///
    /// The mesh is a grid of quads at each column's surface height, with simple
    /// side skirts down to the floor so there are no open edges to fall through.
    /// </summary>
    public sealed class VoxelSurfaceCollider
    {
        private readonly VoxelPitGrid grid;
        private readonly MeshCollider meshCollider;
        private readonly Mesh mesh;

        private readonly List<Vector3> verts = new List<Vector3>();
        private readonly List<int> tris = new List<int>();

        /// <summary>The collider's GameObject, so callers can destroy it on rebuild.</summary>
        public GameObject GameObject { get; }

        public VoxelSurfaceCollider(VoxelPitGrid grid, Transform parent, PhysicMaterial physMat)
        {
            this.grid = grid;

            var go = new GameObject("Proto_PitSurfaceCollider");
            GameObject = go;
            // IMPORTANT: the mesh verts are in WORLD space, so the collider's
            // transform MUST be identity at the origin — otherwise it's offset
            // from the balls (grab misses, thrown balls land in the wrong place).
            // We deliberately do NOT parent it under the (possibly offset)
            // controller object.
            go.transform.SetParent(parent, true);
            go.transform.position = Vector3.zero;
            go.transform.rotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;

            meshCollider = go.AddComponent<MeshCollider>();
            if (physMat != null) meshCollider.sharedMaterial = physMat;

            mesh = new Mesh { name = "PitSurfaceMesh" };
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32; // may exceed 65k verts
        }

        /// <summary>
        /// Add a visible dark "backing" surface just below the ball tops. This
        /// is the bulletproof no-see-through fix: even if a sliver of gap exists
        /// between balls, behind it is this opaque surface (reading as pit depth)
        /// instead of the far wall or empty space. Call once after construction.
        /// </summary>
        public void EnableVisibleBacking(Material backingMat, float sinkBelowSurface)
        {
            if (backingMat == null) return;

            // Child object nudged DOWN so the balls poke above it and it only
            // shows through the gaps between them.
            var vis = new GameObject("Proto_PitBackingVisual");
            vis.transform.SetParent(GameObject.transform, false);
            vis.transform.localPosition = new Vector3(0f, -sinkBelowSurface, 0f);

            backingFilter = vis.AddComponent<MeshFilter>();
            var mr = vis.AddComponent<MeshRenderer>();
            mr.sharedMaterial = backingMat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            backingFilter.sharedMesh = mesh; // shares the heightfield mesh
        }

        private MeshFilter backingFilter;

        /// <summary>
        /// Surface world height at a column = the actual VISUAL top-ball height.
        /// Must match CellToWorldVisual's packing (compressed LayerSpacing),
        /// otherwise the collider/backing float above or sink below the balls.
        /// </summary>
        private float ColumnHeight(int x, int z)
        {
            int topY = grid.TopFilledY(x, z);
            if (topY < 0) return grid.Origin.y; // empty column = floor
            // Center of the top visual ball, plus half a cell (its radius) to
            // reach the top of that ball.
            float centerY = grid.CellToWorldVisual(x, topY, z).y;
            return centerY + grid.CellSize * 0.5f;
        }

        /// <summary>Rebuild the heightfield mesh + collider from the grid.</summary>
        public void Rebuild()
        {
            verts.Clear();
            tris.Clear();

            float cs = grid.CellSize;
            float ox = grid.Origin.x;
            float oz = grid.Origin.z;

            float floorY = grid.Origin.y;

            // One quad per column at its surface height, PLUS vertical skirts
            // from each surface edge down to the floor where a neighbor column
            // is lower. This makes crater walls solid so balls can't tunnel
            // through the side of a hole, and the whole thing is a closed solid
            // (no infinitely-thin sheet to fall through).
            for (int x = 0; x < grid.SizeX; x++)
            for (int z = 0; z < grid.SizeZ; z++)
            {
                float x0 = ox + x * cs;
                float x1 = ox + (x + 1) * cs;
                float z0 = oz + z * cs;
                float z1 = oz + (z + 1) * cs;

                float h00 = CornerHeight(x, z);
                float h10 = CornerHeight(x + 1, z);
                float h01 = CornerHeight(x, z + 1);
                float h11 = CornerHeight(x + 1, z + 1);

                // Top surface quad (normal up).
                AddQuad(
                    new Vector3(x0, h00, z0),
                    new Vector3(x0, h01, z1),
                    new Vector3(x1, h10, z0),
                    new Vector3(x1, h11, z1));

                // Skirt walls down to the floor on each side. Double-sided so a
                // ball inside a crater is blocked regardless of approach.
                AddWall(new Vector3(x0, h00, z0), new Vector3(x1, h10, z0), floorY); // -Z edge
                AddWall(new Vector3(x1, h11, z1), new Vector3(x0, h01, z1), floorY); // +Z edge
                AddWall(new Vector3(x0, h01, z1), new Vector3(x0, h00, z0), floorY); // -X edge
                AddWall(new Vector3(x1, h10, z0), new Vector3(x1, h11, z1), floorY); // +X edge
            }

            mesh.Clear();
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            // Reassign to force the collider to recook.
            meshCollider.sharedMesh = null;
            meshCollider.sharedMesh = mesh;
        }

        /// <summary>Add an upward-facing quad from 4 corners (a,b,c,d).</summary>
        private void AddQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            int i = verts.Count;
            verts.Add(a); verts.Add(b); verts.Add(c); verts.Add(d);
            // a=(x0,z0) b=(x0,z1) c=(x1,z0) d=(x1,z1)
            tris.Add(i + 0); tris.Add(i + 1); tris.Add(i + 2);
            tris.Add(i + 2); tris.Add(i + 1); tris.Add(i + 3);
        }

        /// <summary>
        /// Add a vertical double-sided wall from the segment (top0 -> top1) down
        /// to floorY. Double-sided so it blocks from inside a crater and outside.
        /// </summary>
        private void AddWall(Vector3 top0, Vector3 top1, float floorY)
        {
            Vector3 b0 = new Vector3(top0.x, floorY, top0.z);
            Vector3 b1 = new Vector3(top1.x, floorY, top1.z);
            int i = verts.Count;
            verts.Add(top0); verts.Add(top1); verts.Add(b0); verts.Add(b1);
            // Front faces.
            tris.Add(i + 0); tris.Add(i + 2); tris.Add(i + 1);
            tris.Add(i + 1); tris.Add(i + 2); tris.Add(i + 3);
            // Back faces (reverse winding) for double-sided collision.
            tris.Add(i + 0); tris.Add(i + 1); tris.Add(i + 2);
            tris.Add(i + 2); tris.Add(i + 1); tris.Add(i + 3);
        }

        /// <summary>
        /// Height at a shared corner between columns = max of the up-to-4 columns
        /// touching that corner. Max (not average) keeps balls resting on top of
        /// the tallest neighbor so they don't sink into a cliff edge.
        /// </summary>
        private float CornerHeight(int cx, int cz)
        {
            float h = grid.Origin.y;
            bool any = false;
            for (int dx = -1; dx <= 0; dx++)
            for (int dz = -1; dz <= 0; dz++)
            {
                int x = cx + dx, z = cz + dz;
                if (x < 0 || x >= grid.SizeX || z < 0 || z >= grid.SizeZ) continue;
                float ch = ColumnHeight(x, z);
                if (!any || ch > h) h = ch;
                any = true;
            }
            return h;
        }
    }
}

using System.Collections.Generic;
using UnityEngine;

namespace BallPitProto
{
    /// <summary>
    /// PROTOTYPE — not final.
    ///
    /// Renders the whole voxel grid with GPU instancing. The entire pit is a
    /// handful of DrawMeshInstanced calls (batched in groups of 1023, Unity's
    /// per-call cap) instead of thousands of GameObjects. No colliders, no
    /// Rigidbodies — this is pure visual, driven by the grid data.
    ///
    /// Rebuilds its instance buffers only when the grid actually changes.
    ///
    /// VISIBILITY CULLING: we only emit instances for cells that are actually
    /// visible — a filled cell with at least one empty (or out-of-bounds)
    /// face-neighbor. Fully-buried cells (all 6 neighbors filled) are never
    /// drawn because the player can't see them. This is the Minecraft
    /// "don't render buried blocks" idea. The grid DATA stays complete so
    /// dig/collapse still operate on the full volume; we just skip invisible
    /// balls at draw time. For a packed cube this cuts the drawn count from the
    /// full volume down to roughly its surface area.
    /// </summary>
    public sealed class VoxelPitRenderer
    {
        private const int BatchSize = 1023; // Graphics.DrawMeshInstanced cap

        private readonly VoxelPitGrid grid;
        private readonly Mesh mesh;
        private readonly Material[] materialsPerColor;
        private readonly float ballScale;

        // One list of matrices per color, chunked into batches of 1023.
        private readonly List<Matrix4x4>[] matricesPerColor;
        private readonly List<Matrix4x4[]>[] batchesPerColor;

        /// <summary>How many balls were actually emitted (visible) last Rebuild.</summary>
        public int VisibleCount { get; private set; }

        /// <summary>
        /// How many layers deep to draw behind an exposed cell. 1 = just the
        /// shell (hollow look through gaps); 2 = a backing layer that plugs the
        /// front layer's gaps so you can't see through — like a real pile.
        /// </summary>
        public int ShellDepth = 2;

        public VoxelPitRenderer(VoxelPitGrid grid, Mesh mesh, Material[] materialsPerColor, float ballScale)
        {
            this.grid = grid;
            this.mesh = mesh;
            this.materialsPerColor = materialsPerColor;
            this.ballScale = ballScale;

            int colors = materialsPerColor.Length;
            matricesPerColor = new List<Matrix4x4>[colors];
            batchesPerColor = new List<Matrix4x4[]>[colors];
            for (int c = 0; c < colors; c++)
            {
                matricesPerColor[c] = new List<Matrix4x4>(1024);
                batchesPerColor[c] = new List<Matrix4x4[]>();
            }
        }

        /// <summary>Rebuild instance matrices from the current grid state.</summary>
        public void Rebuild()
        {
            for (int c = 0; c < matricesPerColor.Length; c++)
                matricesPerColor[c].Clear();

            Vector3 scale = Vector3.one * (grid.CellSize * ballScale);

            int visible = 0;
            for (int y = 0; y < grid.SizeY; y++)
            for (int z = 0; z < grid.SizeZ; z++)
            for (int x = 0; x < grid.SizeX; x++)
            {
                int color = grid.Get(x, y, z);
                if (color == VoxelPitGrid.Empty) continue;
                if (color < 0 || color >= matricesPerColor.Length) continue;

                // Draw only cells within ShellDepth of an exposed face. The
                // front layer is the visible surface; the layer(s) behind it
                // plug the gaps so you never see through to empty space.
                if (!WithinShell(x, y, z, ShellDepth)) continue;

                Vector3 pos = grid.CellToWorldVisual(x, y, z);
                Quaternion rot = DeterministicRotation(x, y, z);
                matricesPerColor[color].Add(Matrix4x4.TRS(pos, rot, scale));
                visible++;
            }
            VisibleCount = visible;

            // Pre-chunk into fixed-size arrays for DrawMeshInstanced.
            for (int c = 0; c < matricesPerColor.Length; c++)
            {
                var batches = batchesPerColor[c];
                batches.Clear();
                var list = matricesPerColor[c];
                for (int i = 0; i < list.Count; i += BatchSize)
                {
                    int count = Mathf.Min(BatchSize, list.Count - i);
                    var arr = new Matrix4x4[count];
                    list.CopyTo(i, arr, 0, count);
                    batches.Add(arr);
                }
            }
        }

        /// <summary>Call every frame from Update to issue the draw calls.</summary>
        public void Draw()
        {
            for (int c = 0; c < batchesPerColor.Length; c++)
            {
                var mat = materialsPerColor[c];
                var batches = batchesPerColor[c];
                for (int b = 0; b < batches.Count; b++)
                    Graphics.DrawMeshInstanced(mesh, 0, mat, batches[b], batches[b].Length);
            }
        }

        public int TotalDrawCalls
        {
            get
            {
                int n = 0;
                for (int c = 0; c < batchesPerColor.Length; c++) n += batchesPerColor[c].Count;
                return n;
            }
        }

        /// <summary>
        /// True if an empty/out-of-bounds cell lies anywhere within a box of
        /// radius `depth` (ALL directions, including diagonals). Diagonals
        /// matter: in close packing the hole between surface balls looks
        /// *diagonally* into the pile, so the ball that plugs it sits at a
        /// diagonal offset — an axis-only scan misses it and you see through.
        ///
        /// A cell is drawn when it is close to the exposed surface; cells deep
        /// inside the mass (no empty cell within `depth` in any direction) are
        /// culled. This keeps a solid band of balls behind every surface gap.
        /// </summary>
        private bool WithinShell(int x, int y, int z, int depth)
        {
            for (int dy = -depth; dy <= depth; dy++)
            for (int dz = -depth; dz <= depth; dz++)
            for (int dx = -depth; dx <= depth; dx++)
            {
                if (dx == 0 && dy == 0 && dz == 0) continue;
                if (NeighborEmpty(x + dx, y + dy, z + dz)) return true;
            }
            return false;
        }

        private bool NeighborEmpty(int x, int y, int z)
        {
            if (!grid.InBounds(x, y, z)) return true; // outer face is exposed
            return grid.IsEmpty(x, y, z);
        }

        private static Quaternion DeterministicRotation(int x, int y, int z)
        {
            // Stable pseudo-random orientation per cell so spheres (and later,
            // textured balls) don't all face the same way.
            int h = x * 73856093 ^ y * 19349663 ^ z * 83492791;
            float ax = ((h & 0xFF) / 255f) * 360f;
            float ay = (((h >> 8) & 0xFF) / 255f) * 360f;
            float az = (((h >> 16) & 0xFF) / 255f) * 360f;
            return Quaternion.Euler(ax, ay, az);
        }
    }
}

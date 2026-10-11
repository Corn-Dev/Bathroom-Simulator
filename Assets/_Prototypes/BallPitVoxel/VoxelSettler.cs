using System.Collections.Generic;
using UnityEngine;

namespace BallPitProto
{
    /// <summary>
    /// PROTOTYPE — not final.
    ///
    /// The settling rule: balls fall into empty space below them, and slump
    /// sideways into holes (angle-of-repose-ish). No structural integrity like
    /// 7 Days to Die — anything unsupported falls. Cheap direction on purpose.
    ///
    /// Works off a "dirty cell" queue so we only ever process cells near a
    /// change, not the whole pit. StepFrame() does a bounded amount of work per
    /// frame so a huge collapse spreads over several frames (and we can watch
    /// it) instead of locking up in one spike.
    ///
    /// This is exactly the cost we want to observe: how big do cascades get,
    /// and does bounding the per-frame work keep it smooth?
    /// </summary>
    public sealed class VoxelSettler
    {
        private readonly VoxelPitGrid grid;

        // Cells that might need to fall/slump. We use a HashSet to dedupe plus
        // a queue for ordering. Processed bottom-up-ish via repeated passes.
        private readonly HashSet<int> dirtySet = new HashSet<int>();
        private readonly Queue<Vector3Int> dirtyQueue = new Queue<Vector3Int>();

        /// <summary>Set true whenever the grid changed this frame (so renderer rebuilds).</summary>
        public bool ChangedThisStep { get; private set; }

        /// <summary>How many cells moved last StepFrame — useful as a cascade-size readout.</summary>
        public int LastStepMoves { get; private set; }

        /// <summary>Pending dirty cells still queued to resolve.</summary>
        public int PendingCount => dirtyQueue.Count;

        public VoxelSettler(VoxelPitGrid grid)
        {
            this.grid = grid;
        }

        public void MarkDirty(int x, int y, int z)
        {
            if (!grid.InBounds(x, y, z)) return;
            int idx = grid.Index(x, y, z);
            if (dirtySet.Add(idx))
                dirtyQueue.Enqueue(new Vector3Int(x, y, z));
        }

        /// <summary>Mark a cell and its neighbors dirty (used around a removed cell).</summary>
        public void MarkNeighborhoodDirty(int x, int y, int z)
        {
            // The removed cell + the cell above (which should fall) + lateral
            // neighbors above (which may slump in) are the candidates.
            MarkDirty(x, y, z);
            MarkDirty(x, y + 1, z);
            MarkDirty(x + 1, y + 1, z);
            MarkDirty(x - 1, y + 1, z);
            MarkDirty(x, y + 1, z + 1);
            MarkDirty(x, y + 1, z - 1);
        }

        /// <summary>
        /// Resolve up to maxMoves cell movements this frame. Returns when the
        /// budget is spent or the queue drains. Re-queues follow-on cells so the
        /// cascade continues next frame.
        /// </summary>
        public void StepFrame(int maxMoves)
        {
            ChangedThisStep = false;
            LastStepMoves = 0;

            int budget = maxMoves;
            // Process a snapshot of the current queue size so newly-added cells
            // wait for the next frame (bounds the cascade per frame).
            int toProcess = dirtyQueue.Count;

            while (budget > 0 && toProcess > 0 && dirtyQueue.Count > 0)
            {
                var c = dirtyQueue.Dequeue();
                dirtySet.Remove(grid.Index(c.x, c.y, c.z));
                toProcess--;

                if (TryResolveCell(c.x, c.y, c.z))
                {
                    budget--;
                    LastStepMoves++;
                    ChangedThisStep = true;
                }
            }
        }

        /// <summary>
        /// If the cell at (x,y,z) is filled and unsupported, move it down (or
        /// slump into a lower diagonal gap). Returns true if something moved.
        /// </summary>
        private bool TryResolveCell(int x, int y, int z)
        {
            if (!grid.InBounds(x, y, z)) return false;
            if (grid.IsEmpty(x, y, z)) return false;
            if (y == 0) return false; // resting on the floor

            // 1) Straight down if the cell below is empty.
            if (grid.IsEmpty(x, y - 1, z))
            {
                MoveCell(x, y, z, x, y - 1, z);
                return true;
            }

            // 2) Slump diagonally if directly below is blocked but a diagonal-
            //    down neighbor is open (and the cell above that neighbor is open
            //    so it can actually roll in). This gives the ball-pit slope.
            if (TrySlump(x, y, z, 1, 0)) return true;
            if (TrySlump(x, y, z, -1, 0)) return true;
            if (TrySlump(x, y, z, 0, 1)) return true;
            if (TrySlump(x, y, z, 0, -1)) return true;

            return false;
        }

        private bool TrySlump(int x, int y, int z, int dx, int dz)
        {
            int nx = x + dx, nz = z + dz;
            if (!grid.InBounds(nx, y - 1, nz)) return false;
            // Target lower-diagonal cell must be empty, and the side cell at the
            // current level must be empty too (so the ball has somewhere to roll
            // through rather than tunneling through a solid wall).
            if (grid.IsEmpty(nx, y - 1, nz) && grid.IsEmpty(nx, y, nz))
            {
                MoveCell(x, y, z, nx, y - 1, nz);
                return true;
            }
            return false;
        }

        private void MoveCell(int fromX, int fromY, int fromZ, int toX, int toY, int toZ)
        {
            int color = grid.Get(fromX, fromY, fromZ);
            grid.Set(fromX, fromY, fromZ, VoxelPitGrid.Empty);
            grid.Set(toX, toY, toZ, color);

            // The cell we just vacated may let the cell above it fall; the cell
            // we moved into may still be able to fall further. Re-queue both,
            // plus lateral neighbors around the vacated spot.
            MarkDirty(toX, toY, toZ);
            MarkDirty(fromX, fromY + 1, fromZ);
            MarkDirty(fromX + 1, fromY, fromZ);
            MarkDirty(fromX - 1, fromY, fromZ);
            MarkDirty(fromX, fromY, fromZ + 1);
            MarkDirty(fromX, fromY, fromZ - 1);
        }
    }
}

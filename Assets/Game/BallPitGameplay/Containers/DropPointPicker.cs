// DropPointPicker: picks a safe drop point inside the pit for a ball returned to the pile (brief §4.2). A random XZ
// inside the pit interior (with a margin), raised above the pile by raycasting down from above the rim, so a returned
// ball falls onto the top of the pile and settles with real physics rather than spawning inside the mass. Pure helper
// over PitWorld; deterministic enough for a scatter (uses System.Random seeded from the world's seed).
using BallPitFinal;
using UnityEngine;

namespace BallPitGameplay
{
    public sealed class DropPointPicker
    {
        private readonly PitWorld world;
        private readonly System.Random rng;
        private readonly float margin;

        public DropPointPicker(PitWorld world)
        {
            this.world = world;
            rng = new System.Random(world != null ? world.S.seed ^ 0x1a2b3c4d : 12345);
            margin = world != null ? world.Diameter * 2f : 1f;
        }

        /// <summary>A drop point: random XZ inside the interior, Y just above the pile surface (or the rim). Never below
        /// the floor; always finite.</summary>
        public Vector3 Pick()
        {
            if (world == null) return Vector3.zero;
            float x0 = world.WallMinX + margin, x1 = world.WallMaxX - margin;
            float z0 = world.WallMinZ + margin, z1 = world.WallMaxZ - margin;
            if (x1 < x0) { float m = (world.WallMinX + world.WallMaxX) * 0.5f; x0 = x1 = m; }
            if (z1 < z0) { float m = (world.WallMinZ + world.WallMaxZ) * 0.5f; z0 = z1 = m; }
            float x = Mathf.Lerp(x0, x1, (float)rng.NextDouble());
            float z = Mathf.Lerp(z0, z1, (float)rng.NextDouble());

            float top = world.WallTop + world.Diameter; // start above the rim
            float y = top;
            var ray = new Ray(new Vector3(x, top, z), Vector3.down);
            // Prefer the resting-ball surface (the pile); fall back to the environment (floor/walls).
            if (world.Raycast(ray, top - world.FloorY, out _, out float tBall)) y = top - tBall + world.Diameter;
            else if (world.RaycastEnvironment(ray, top - world.FloorY, out RaycastHit hit)) y = hit.point.y + world.Diameter;
            else y = world.FloorY + world.Diameter;

            y = Mathf.Clamp(y, world.FloorY + world.VisualRadius, top);
            return new Vector3(x, y, z);
        }
    }
}

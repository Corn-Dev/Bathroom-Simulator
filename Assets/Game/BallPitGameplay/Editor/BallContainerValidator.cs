// BallContainerValidator: Tools/Ball Pit/Validate Containers (brief §4.1 validator rules). Checks the loaded scene's
// BallContainers against the pit the controller would build: every active colour has at least one container; every
// intake box sits above the pit floor; no intake overlaps the pit interior box (a container must be beside/above the
// pit, not inside the ball mass). Uses PitWorld.ComputeInterior so it agrees with the simulation. Logs one clickable
// entry per issue plus a summary; never modifies the scene.
using System.Collections.Generic;
using BallPitFinal;
using UnityEditor;
using UnityEngine;

namespace BallPitGameplay
{
    internal static class BallContainerValidator
    {
        [MenuItem("Tools/Ball Pit/Validate Containers")]
        private static void Run()
        {
            int errors = 0, warnings = 0;
            Physics.SyncTransforms();
            var controller = Object.FindObjectOfType<BallPitFinalController>(true);
            if (controller == null) { Debug.LogWarning("[Validate Containers] no BallPitFinalController in the scene"); }
            PitSettings s = controller != null && controller.settings != null ? controller.settings : new PitSettings();
            int colorCount = Mathf.Clamp(s.colorCount, 1, 16);

            PitWorld.ComputeInterior(s, out float x0, out float x1, out float z0, out float z1, out float floorY);
            float fillTop = PitWorld.PredictFccFillTop(s);
            var interior = new Bounds();
            interior.SetMinMax(new Vector3(x0, floorY, z0), new Vector3(x1, fillTop, z1));

            BallContainer[] containers = Object.FindObjectsOfType<BallContainer>(true);
            var byColor = new Dictionary<int, int>();
            var ids = new HashSet<string>(System.StringComparer.Ordinal);

            foreach (BallContainer c in containers)
            {
                if (c == null) continue;
                if (!byColor.ContainsKey(c.ColorIndex)) byColor[c.ColorIndex] = 0;
                byColor[c.ColorIndex]++;
                if (!ids.Add(c.Id)) { Debug.LogError($"[Validate Containers] duplicate container id '{c.Id}'", c); errors++; }
                if (c.ColorIndex < 0 || c.ColorIndex >= colorCount)
                { Debug.LogError($"[Validate Containers] '{c.name}' colour {c.ColorIndex} is outside 0..{colorCount - 1}", c); errors++; }

                Bounds localBox = c.IntakeLocalBounds;
                Bounds world = TransformBounds(c.transform, localBox);
                if (world.min.y < floorY - 0.001f)
                { Debug.LogWarning($"[Validate Containers] '{c.name}' intake dips below the pit floor (y {world.min.y:0.00} < {floorY:0.00})", c); warnings++; }
                if (world.Intersects(interior) && OverlapVolume(world, interior) > 0.001f)
                { Debug.LogError($"[Validate Containers] '{c.name}' intake overlaps the pit interior; move it beside/above the pit", c); errors++; }
            }

            for (int color = 0; color < colorCount; color++)
                if (!byColor.ContainsKey(color))
                { Debug.LogError($"[Validate Containers] no container for active colour {color}", controller); errors++; }

            Debug.Log($"[Validate Containers] done: {errors} error(s), {warnings} warning(s), {containers.Length} container(s), {colorCount} colour(s)");
        }

        // The axis-aligned world bounds of a local box under a transform (covers rotation/scale).
        private static Bounds TransformBounds(Transform t, Bounds local)
        {
            Vector3 c = t.TransformPoint(local.center);
            Vector3 e = local.extents;
            Vector3 x = t.TransformVector(e.x, 0, 0), y = t.TransformVector(0, e.y, 0), z = t.TransformVector(0, 0, e.z);
            Vector3 worldExtent = new Vector3(
                Mathf.Abs(x.x) + Mathf.Abs(y.x) + Mathf.Abs(z.x),
                Mathf.Abs(x.y) + Mathf.Abs(y.y) + Mathf.Abs(z.y),
                Mathf.Abs(x.z) + Mathf.Abs(y.z) + Mathf.Abs(z.z));
            return new Bounds(c, worldExtent * 2f);
        }

        private static float OverlapVolume(Bounds a, Bounds b)
        {
            float dx = Mathf.Min(a.max.x, b.max.x) - Mathf.Max(a.min.x, b.min.x);
            float dy = Mathf.Min(a.max.y, b.max.y) - Mathf.Max(a.min.y, b.min.y);
            float dz = Mathf.Min(a.max.z, b.max.z) - Mathf.Max(a.min.z, b.min.z);
            return dx <= 0f || dy <= 0f || dz <= 0f ? 0f : dx * dy * dz;
        }
    }
}

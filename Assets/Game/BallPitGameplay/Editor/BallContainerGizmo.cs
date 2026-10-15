// BallContainerGizmo: editor-only authoring aid for BallContainer (brief §4.1). Draws the top-opening intake box (the
// exact oriented box TryIntake tests) and a colour swatch label, so the owner can place and size the opening visually.
// Boxes only, never per ball; nothing here runs in a build.
using UnityEditor;
using UnityEngine;

namespace BallPitGameplay
{
    internal static class BallContainerGizmo
    {
        [DrawGizmo(GizmoType.NonSelected | GizmoType.Selected | GizmoType.Pickable)]
        private static void DrawContainer(BallContainer container, GizmoType type)
        {
            bool selected = (type & GizmoType.Selected) != 0;
            Bounds local = container.IntakeLocalBounds;
            Matrix4x4 old = Gizmos.matrix;
            Gizmos.matrix = container.transform.localToWorldMatrix;
            Color c = SwatchColor(container.ColorIndex);
            Gizmos.color = new Color(c.r, c.g, c.b, selected ? 0.25f : 0.1f);
            Gizmos.DrawCube(local.center, local.size);
            Gizmos.color = new Color(c.r, c.g, c.b, 0.95f);
            Gizmos.DrawWireCube(local.center, local.size);
            Gizmos.matrix = old;

            Handles.color = Color.white;
            Handles.Label(container.IntakeWorldCenter, $"{container.name}  colour {container.ColorIndex}");
        }

        // A stable, distinguishable swatch per index for authoring (NOT the runtime palette).
        private static Color SwatchColor(int index) => Color.HSVToRGB((Mathf.Abs(index) % 8) / 8f, 0.7f, 0.95f);
    }
}

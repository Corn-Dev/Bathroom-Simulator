// VenueGizmos: editor-only authoring aids for the venue (deliverable A, file plan E1; design b 11.2, 11.5, 13; c 14;
// d.9). Gizmos: each PitSite's exact interior (computed with the same PitWorld and VenueMath functions the simulation
// uses), its venue bounds and overview-camera frustum; spawn-point capsules; out-of-bounds volumes; climbable colliders.
// Menu items: the default VenueConfig, BallVisualConfig and PlayerConfig assets, Apply PlayerConfig to Selected Prefab,
// and the PitSite context item Fit Venue Bounds To Pit. Boxes and planes only, never per ball. Nothing here runs in a build.
using System.Collections.Generic;
using System.Globalization;
using BallPitFinal;
using UnityEditor;
using UnityEngine;

namespace BallPitGameplay
{
    [InitializeOnLoad]
    internal static class VenueGizmos
    {
        private static readonly Color InteriorColor = new Color(0f, 0.9f, 1f, 0.9f);
        private static readonly Color FccColor = new Color(0.2f, 0.9f, 0.3f, 0.9f);
        private static readonly Color RandomColor = new Color(1f, 0.6f, 0.1f, 0.9f);
        private static readonly Color ProxyColor = new Color(0.6f, 0.6f, 0.6f, 0.8f);
        private static readonly Color BadColor = new Color(1f, 0.2f, 0.2f, 1f);
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        // Scene lookups are cached and cleared on hierarchy changes, so FindObjectOfType never runs per repaint.
        private static bool sceneCached;
        private static BallPitFinalController controller;
        private static PlayerSpawner spawner;
        private static GUIStyle labelStyle, badStyle;

        static VenueGizmos()
        {
            EditorApplication.hierarchyChanged += () => sceneCached = false;
            EditorApplication.projectChanged += () => sceneCached = false;
        }

        private static void CacheScene()
        {
            if (sceneCached) return;
            sceneCached = true;
            controller = Object.FindObjectOfType<BallPitFinalController>(true);
            spawner = Object.FindObjectOfType<PlayerSpawner>(true);
        }

        /// <summary>The scene controller's inspector settings, else defaults: what a new session's preview starts from.</summary>
        internal static PitSettings BaseSettings()
        {
            CacheScene();
            return controller != null && controller.settings != null ? controller.settings : new PitSettings();
        }

        /// <summary>The spawn capsule and bounds rule from the scene spawner's prefab (PlayerConfig), else the defaults.</summary>
        internal static SpawnTemplate Template()
        {
            CacheScene();
            return spawner != null && spawner.TryGetTemplate(out SpawnTemplate t) ? t : SpawnTemplate.Default;
        }

        // ------------------------------------------------------------------ PitSite (b 11.2)

        [DrawGizmo(GizmoType.NonSelected | GizmoType.Selected | GizmoType.Pickable)]
        private static void DrawSite(PitSite site, GizmoType type)
        {
            bool selected = (type & GizmoType.Selected) != 0;
            DrawOverviewFrustum(site.OverviewCamera, selected);
            if (site.Anchor == null) { Label(site.transform.position, $"PitSite '{site.SiteId}': anchor not assigned", true); return; }
            if (!VenueConfig.Load().TryGetProfile(site.ProfileKey, out PitSizeProfile profile))
            {
                Label(site.Anchor.position, $"PitSite '{site.SiteId}': profile '{site.ProfileKey}' is not in the VenueConfig", true);
                return;
            }
            PitSettings s = VenueMath.PreviewSettings(site.AnchorPoint, profile, BaseSettings());
            PitWorld.ComputeInterior(s, out float x0, out float x1, out float z0, out float z1, out float floorY);
            float rim = site.RimHeight;
            Bounds proxies = VenueMath.PredictProxyBox(s, rim);
            float rimTop = proxies.max.y;

            Gizmos.color = InteriorColor;
            WireBox(new Vector3(x0, floorY, z0), new Vector3(x1, rimTop, z1));
            Label(new Vector3((x0 + x1) * 0.5f, rimTop, (z0 + z1) * 0.5f), $"{site.SiteId} ({site.ProfileKey})", false);
            bool rotated = Quaternion.Angle(site.Anchor.rotation, Quaternion.identity) > 0.01f;
            if (rotated) Label(site.Anchor.position, "anchor rotated", true);
            if (!selected) return;

            float d = s.ballDiameter, r = PitWorld.ColliderRadiusOf(s);
            float fccTop = PitWorld.PredictFccFillTop(s), randomTop = VenueMath.EstimateRandomFillTop(s);
            Rect(x0, x1, z0, z1, floorY, new Color(0f, 0.9f, 1f, 0.08f), InteriorColor);
            Rect(x0, x1, z0, z1, fccTop + r, new Color(0.2f, 0.9f, 0.3f, 0.12f), FccColor);
            DottedRect(x0, x1, z0, z1, fccTop + 2f * d, FccColor);
            DottedRect(x0, x1, z0, z1, randomTop + r, RandomColor);
            DottedRect(x0, x1, z0, z1, randomTop + 2f * d, RandomColor);
            if (rim > 0f) Rect(x0, x1, z0, z1, floorY + rim, new Color(1f, 1f, 1f, 0.04f), Color.white);
            Gizmos.color = ProxyColor;
            Gizmos.DrawWireCube(proxies.center, proxies.size);
            Bounds venue = site.WorldVenueBounds;
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireCube(venue.center, venue.size);

            Vector3 gridSize = venue.size.x > 0f && venue.size.y > 0f && venue.size.z > 0f ? venue.size : PitWorld.ComputeBounds(s).size;
            double gridMb = VenueMath.GridIndexBytes(gridSize, d) / (1024.0 * 1024.0);
            string text =
                $"{profile.key} | {profile.pitX}x{profile.pitY}x{profile.pitZ} = {profile.BallCount.ToString("n0", Inv)} balls | " +
                $"d {F(d)} m | floor y {floorY.ToString("0.000", Inv)} | interior {F2(x1 - x0)} x {F2(z1 - z0)} m\n" +
                $"FCC WallTop +{F1(fccTop + 2f * d - floorY)} | Random WallTop ~ +{F1(randomTop + 2f * d - floorY)} | " +
                (rim > 0f ? $"rim +{F1(rim)}" : "no rim") + $" | grid {gridMb.ToString("0.0", Inv)} MB";
            Label(new Vector3(x1, rimTop, z1), text, false);
        }

        private static void DrawOverviewFrustum(Camera cam, bool selected)
        {
            if (cam == null) return;
            Matrix4x4 old = Gizmos.matrix;
            Gizmos.matrix = Matrix4x4.TRS(cam.transform.position, cam.transform.rotation, Vector3.one);
            Gizmos.color = selected ? new Color(1f, 1f, 1f, 0.8f) : new Color(1f, 1f, 1f, 0.25f);
            Gizmos.DrawFrustum(Vector3.zero, cam.fieldOfView, Mathf.Min(cam.farClipPlane, 30f), cam.nearClipPlane, cam.aspect);
            Gizmos.matrix = old;
        }

        // ------------------------------------------------------------------ Spawn points, volumes, climbables (d.9, c 8)

        [DrawGizmo(GizmoType.NonSelected | GizmoType.Selected | GizmoType.Pickable)]
        private static void DrawSpawnPoint(PlayerSpawnPoint point, GizmoType type)
        {
            SpawnTemplate t = Template();
            Vector3 feet = point.Feet;
            PitSite site = point.site;
            bool outside = t.RequireInsidePlayBounds && site != null && site.Anchor != null &&
                           !site.WorldVenueBounds.Contains(feet + Vector3.up * MotorMath.BoundsProbeLift);
            Color c = outside ? BadColor : new Color(0.3f, 0.8f, 1f, 1f);
            Gizmos.color = new Color(c.r, c.g, c.b, 0.35f);
            Gizmos.DrawSphere(feet + Vector3.up * t.Radius, t.Radius * 0.35f); // pickable handle
            Handles.color = c;
            WireCapsule(feet, t.Radius, t.Height);
            Quaternion facing = Quaternion.Euler(0f, point.Yaw, 0f);
            Handles.ArrowHandleCap(0, feet + Vector3.up * 0.05f, facing, Mathf.Max(0.5f, t.Radius * 2f), EventType.Repaint);
            Label(feet + Vector3.up * (t.Height + 0.15f), (point.slot >= 0 ? "slot " + point.slot : "any slot") +
                  (outside ? " (outside the site's venue bounds)" : ""), outside);
            if ((type & GizmoType.Selected) != 0 && site != null && site.Anchor != null)
                Handles.DrawDottedLine(feet, site.Anchor.position, 4f);
        }

        [DrawGizmo(GizmoType.NonSelected | GizmoType.Selected)]
        private static void DrawOutOfBounds(OutOfBoundsVolume volume, GizmoType type)
        {
            bool selected = (type & GizmoType.Selected) != 0;
            foreach (Collider col in volume.GetComponents<Collider>())
            {
                Bounds b = col.bounds;
                Gizmos.color = new Color(1f, 0.15f, 0.15f, selected ? 0.25f : 0.08f);
                Gizmos.DrawCube(b.center, b.size);
                Gizmos.color = new Color(1f, 0.15f, 0.15f, 0.9f);
                Gizmos.DrawWireCube(b.center, b.size);
            }
        }

        private static readonly List<Collider> climbScratch = new List<Collider>();

        [DrawGizmo(GizmoType.Selected | GizmoType.InSelectionHierarchy)]
        private static void DrawClimbable(ClimbableSurface surface, GizmoType type)
        {
            surface.GetColliders(climbScratch);
            Gizmos.color = new Color(0.4f, 1f, 0.4f, 0.9f);
            for (int i = 0; i < climbScratch.Count; i++)
                if (climbScratch[i] != null) Gizmos.DrawWireCube(climbScratch[i].bounds.center, climbScratch[i].bounds.size);
            climbScratch.Clear();
        }

        // ------------------------------------------------------------------ Default assets (b 13, c 14)

        private const string VenueConfigPath = "Assets/Game/BallPitGameplay/Resources/BallPitVenueConfig.asset";
        private const string BallVisualPath = "Assets/Game/BallPitGameplay/Config/BallVisualConfig.asset";
        private const string PlayerConfigPath = "Assets/Game/BallPitGameplay/Player/PlayerConfig.asset";

        [MenuItem("Tools/Ball Pit/Create Default Venue Config")]
        private static void CreateVenueConfig()
        {
            var existing = Resources.Load<VenueConfig>(VenueConfig.ResourcePath); // the one the runtime would load
            CreateOnce(VenueConfigPath, existing, () => VenueConfig.CreateDefault());
        }

        [MenuItem("Tools/Ball Pit/Create Default Ball Visual Config")]
        private static void CreateBallVisualConfig() => CreateOnce(BallVisualPath, null, () => BallVisualConfig.CreateDefault());

        [MenuItem("Ball Pit/Player/Create Default PlayerConfig")]
        private static void CreatePlayerConfig() => CreateOnce(PlayerConfigPath, null, () => ScriptableObject.CreateInstance<PlayerConfig>());

        // Never overwrites: an existing asset is pinged and selected instead.
        private static void CreateOnce(string path, Object existing, System.Func<ScriptableObject> make)
        {
            if (existing == null) existing = AssetDatabase.LoadAssetAtPath<Object>(path);
            if (existing != null)
            {
                Debug.Log($"[VenueGizmos] '{AssetDatabase.GetAssetPath(existing)}' already exists; it was not overwritten", existing);
                Select(existing);
                return;
            }
            EnsureFolder(path.Substring(0, path.LastIndexOf('/')));
            ScriptableObject asset = make();
            asset.hideFlags = HideFlags.None; // CreateDefault instances may carry runtime flags
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();
            Debug.Log($"[VenueGizmos] created '{path}'", asset);
            Select(asset);
        }

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            int slash = folder.LastIndexOf('/');
            string parent = folder.Substring(0, slash);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, folder.Substring(slash + 1));
        }

        private static void Select(Object o)
        {
            EditorGUIUtility.PingObject(o);
            Selection.activeObject = o;
        }

        // ------------------------------------------------------------------ Apply PlayerConfig (c 2)

        [MenuItem("Ball Pit/Player/Apply PlayerConfig to Selected Prefab", true)]
        private static bool CanApplyPlayerConfig() => Selection.activeGameObject != null && Selection.activeGameObject.GetComponent<PlayerAvatar>() != null;

        /// <summary>Writes the PlayerConfig dimensions into the selected player (prefab asset, prefab stage or scene object),
        /// as PlayerAvatar.Awake does at runtime: capsule height, radius and center, Head height, HoldPoint offset.</summary>
        [MenuItem("Ball Pit/Player/Apply PlayerConfig to Selected Prefab")]
        private static void ApplyPlayerConfig()
        {
            GameObject go = Selection.activeGameObject;
            if (go == null) return;
            string path = AssetDatabase.GetAssetPath(go);
            if (string.IsNullOrEmpty(path)) { if (Apply(go, true)) Debug.Log($"[VenueGizmos] PlayerConfig applied to '{go.name}'", go); return; }
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                if (Apply(root, false)) { PrefabUtility.SaveAsPrefabAsset(root, path); Debug.Log($"[VenueGizmos] PlayerConfig applied to '{path}'", go); }
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        private static bool Apply(GameObject go, bool undo)
        {
            var avatar = go.GetComponent<PlayerAvatar>();
            PlayerConfig c = avatar != null ? avatar.ConfigAsset : null;
            if (c == null) { Debug.LogError($"[VenueGizmos] '{go.name}': assign a PlayerConfig to its PlayerAvatar first", go); return false; }
            var capsule = go.GetComponent<CapsuleCollider>();
            var view = go.GetComponent<PlayerView>();
            var targets = new List<Object>();
            if (capsule != null) targets.Add(capsule);
            if (view != null && view.Head != null) targets.Add(view.Head);
            if (view != null && view.HoldPoint != null) targets.Add(view.HoldPoint);
            if (undo) Undo.RecordObjects(targets.ToArray(), "Apply PlayerConfig");
            if (capsule != null)
            {
                capsule.height = c.capsuleHeight;
                capsule.radius = c.capsuleRadius;
                capsule.center = new Vector3(0f, c.capsuleHeight * 0.5f, 0f);
            }
            if (view != null && view.Head != null) view.Head.localPosition = new Vector3(0f, c.eyeHeight, 0f);
            if (view != null && view.HoldPoint != null) view.HoldPoint.localPosition = c.holdOffset;
            foreach (Object o in targets) { PrefabUtility.RecordPrefabInstancePropertyModifications(o); EditorUtility.SetDirty(o); }
            return true;
        }

        // ------------------------------------------------------------------ Fit Venue Bounds To Pit (b 11.5)

        /// <summary>A starting venueBounds that passes the validator's bounds checks: the computed play bounds and the
        /// predicted proxy box, grown 2 m sideways and to 10 m above the rim, relative to the anchor. Undo through
        /// SerializedObject (venueBounds is private).</summary>
        [MenuItem("CONTEXT/PitSite/Fit Venue Bounds To Pit")]
        private static void FitVenueBounds(MenuCommand command)
        {
            var site = command.context as PitSite;
            if (site == null) return;
            if (site.Anchor == null || !VenueConfig.Load().TryGetProfile(site.ProfileKey, out PitSizeProfile profile))
            {
                Debug.LogError($"[VenueGizmos] PitSite '{site.SiteId}': " + (site.Anchor == null ? "assign the anchor"
                               : $"profile '{site.ProfileKey}' is not in the VenueConfig") + " before fitting the venue bounds", site);
                return;
            }
            PitSettings s = VenueMath.PreviewSettings(site.AnchorPoint, profile, BaseSettings());
            Bounds proxies = VenueMath.PredictProxyBox(s, site.RimHeight);
            Bounds b = PitWorld.ComputeBounds(s); // PreviewSettings turns the override off
            b.Encapsulate(proxies);
            Vector3 min = b.min - new Vector3(2f, 0f, 2f), max = b.max + new Vector3(2f, 0f, 2f);
            max.y = Mathf.Max(max.y, proxies.max.y + 10f); // throw arcs above the rim
            b.SetMinMax(min - site.AnchorPoint, max - site.AnchorPoint);
            var so = new SerializedObject(site);
            so.FindProperty("venueBounds").boundsValue = b;
            so.ApplyModifiedProperties(); // records Undo, dirties the scene, handles prefab overrides
            Debug.Log($"[VenueGizmos] PitSite '{site.SiteId}': venueBounds fitted to {b.size} around the pit", site);
        }

        // ------------------------------------------------------------------ Drawing helpers

        private static void WireBox(Vector3 min, Vector3 max) => Gizmos.DrawWireCube((min + max) * 0.5f, max - min);

        private static void Rect(float x0, float x1, float z0, float z1, float y, Color fill, Color outline) =>
            Handles.DrawSolidRectangleWithOutline(new[] { new Vector3(x0, y, z0), new Vector3(x1, y, z0), new Vector3(x1, y, z1), new Vector3(x0, y, z1) },
                                                  fill, outline);

        private static void DottedRect(float x0, float x1, float z0, float z1, float y, Color color)
        {
            Handles.color = color;
            Vector3 a = new Vector3(x0, y, z0), b = new Vector3(x1, y, z0), c = new Vector3(x1, y, z1), d = new Vector3(x0, y, z1);
            Handles.DrawDottedLine(a, b, 4f); Handles.DrawDottedLine(b, c, 4f);
            Handles.DrawDottedLine(c, d, 4f); Handles.DrawDottedLine(d, a, 4f);
        }

        // Two rings, four sides and the two hemisphere arcs per side plane; Handles.color is set by the caller.
        private static void WireCapsule(Vector3 feet, float r, float h)
        {
            Vector3 lo = feet + Vector3.up * r, hi = feet + Vector3.up * Mathf.Max(r, h - r);
            Handles.DrawWireDisc(lo, Vector3.up, r);
            Handles.DrawWireDisc(hi, Vector3.up, r);
            Vector3[] sides = { Vector3.forward, Vector3.right, Vector3.back, Vector3.left };
            foreach (Vector3 s in sides) Handles.DrawLine(lo + s * r, hi + s * r);
            Handles.DrawWireArc(hi, Vector3.forward, Vector3.right, 180f, r);
            Handles.DrawWireArc(hi, Vector3.right, Vector3.back, 180f, r);
            Handles.DrawWireArc(lo, Vector3.forward, Vector3.left, 180f, r);
            Handles.DrawWireArc(lo, Vector3.right, Vector3.forward, 180f, r);
        }

        private static void Label(Vector3 at, string text, bool bad)
        {
            if (labelStyle == null)
            {
                labelStyle = new GUIStyle(EditorStyles.miniLabel) { richText = false };
                labelStyle.normal.textColor = new Color(0.85f, 0.95f, 1f);
                badStyle = new GUIStyle(EditorStyles.boldLabel);
                badStyle.normal.textColor = BadColor;
            }
            Handles.Label(at, text, bad ? badStyle : labelStyle);
        }

        private static string F(float v) => v.ToString("0.###", Inv);
        private static string F1(float v) => v.ToString("0.0", Inv);
        private static string F2(float v) => v.ToString("0.00", Inv);
    }
}

// GameplayAssetMenu: Tools/Ball Pit/Create Default Gameplay Assets (brief §4.3, §4.5). One-click generation of the
// bite-(a) defaults: an EconomyConfig, an UpgradeCatalog with the four starter upgrades (Second Hand, Strong Arm,
// Wider Funnels, Sneakers) and their UpgradeDefinition assets, and the six palette presets (Classic + Pastel authored,
// Candy/Neon/Ocean/Sunset placeholder). Never overwrites an existing asset; it pings and selects it instead. The owner
// then assigns these to GameplaySession / PaletteApplier. Editor only.
//
// Deliverable C (brief §3.7; DESIGN-C §7) adds two more items:
//   - Create Placeholder Container: a prefab assembling the CONTAINER-3-PARTS (placeholder vessel mesh + child intake
//     box sized to BallContainer.intake* + an Interactable collider so PlayerInteractor can aim) plus the BallContainer
//     component and a MassFillView whose fill-volume child mesh is framed to the vessel. Rough art the owner replaces.
//   - Create Kiddie Profile: appends a tiny "kiddie" PitSizeProfile (10x2x10 = 200 balls at 0.5 m; §9b/§2b) to the
//     active VenueConfig resource (never overwrites an existing "kiddie" key), else logs how to create the asset.
// Both reuse the existing EnsureFolder + never-overwrite + ping/select pattern; the kiddie size is DATA (no code
// constant hardcodes it), keeping "code never assumes a fixed layout" (game-design §2b).
using UnityEditor;
using UnityEngine;

namespace BallPitGameplay
{
    internal static class GameplayAssetMenu
    {
        private const string Root = "Assets/Game/BallPitGameplay";
        private const string EconomyPath = Root + "/Economy/Defaults/EconomyConfig.asset";
        private const string CatalogPath = Root + "/Economy/Defaults/UpgradeCatalog.asset";
        private const string UpgradeDir = Root + "/Economy/Defaults/Upgrades";
        private const string PresetDir = Root + "/Colors/Presets";
        private const string PlaceholderDir = Root + "/Containers/Placeholders";
        private const string PlaceholderContainerPath = PlaceholderDir + "/PlaceholderContainer.prefab";
        // Must match VenueGizmos.VenueConfigPath (the Resources asset the runtime loads).
        private const string VenueConfigPath = Root + "/Resources/BallPitVenueConfig.asset";

        [MenuItem("Tools/Ball Pit/Create Default Gameplay Assets")]
        private static void CreateAll()
        {
            CreateEconomy();
            CreateCatalog();
            CreatePalettes();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[GameplayAssetMenu] default gameplay assets ready under " + Root);
        }

        // ------------------------------------------------------------------ Placeholder container prefab (C §3.7, §7)

        /// <summary>
        /// Creates a placeholder container prefab (never overwrites: pings + selects if present). It assembles the three
        /// CONTAINER-3-PARTS the brief calls out: (1) a placeholder vessel mesh (a Unity primitive the owner replaces),
        /// (2) a child intake box sized to BallContainer.intakeCenterLocal/intakeSizeLocal — the "trigger" rectangle
        /// TryIntake tests, NOT a physics trigger, so its collider is left as an isTrigger box purely as an authoring
        /// marker that the gizmo also draws, (3) an Interactable collider so PlayerInteractor can aim at it. The
        /// BallContainer + a MassFillView (its fill-volume child framed to the vessel interior) complete the loop.
        /// The owner sets colorIndex + the Interactable layer per the handoff, drops 3-4, and sees sort -> sell -> fill.
        /// </summary>
        [MenuItem("Tools/Ball Pit/Create Placeholder Container")]
        private static void CreatePlaceholderContainer()
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(PlaceholderContainerPath);
            if (existing != null)
            {
                Debug.Log($"[GameplayAssetMenu] '{PlaceholderContainerPath}' already exists; it was not overwritten", existing);
                EditorGUIUtility.PingObject(existing);
                Selection.activeObject = existing;
                return;
            }
            EnsureFolder(PlaceholderDir);

            // Root carries BallContainer + MassFillView; children are the three authoring parts.
            var root = new GameObject("PlaceholderContainer");
            try
            {
                // (1) Placeholder vessel body: a primitive cube (open-topped vessel is the owner's real art later).
                GameObject body = GameObject.CreatePrimitive(PrimitiveType.Cube);
                body.name = "VesselBody (placeholder)";
                body.transform.SetParent(root.transform, false);
                body.transform.localPosition = new Vector3(0f, 0.5f, 0f);
                body.transform.localScale = new Vector3(1f, 1f, 1f);
                // The primitive's own BoxCollider is the Interactable collider (part 3): the player aims at the vessel.
                // Left on whatever layer the owner later assigns to PlayerInteractor.interactableMask (see LAYERS).

                // (2) Child intake box ("trigger" rectangle): matches BallContainer defaults (centre y=1, size 1x0.5x1).
                // Not a physics trigger — TryIntake tests geometry. The box collider here is an isTrigger authoring
                // marker only (the gizmo draws the real tested box from BallContainer.IntakeLocalBounds).
                var intake = new GameObject("IntakeBox (geometry marker)");
                intake.transform.SetParent(root.transform, false);
                intake.transform.localPosition = new Vector3(0f, 1f, 0f);
                var intakeBox = intake.AddComponent<BoxCollider>();
                intakeBox.isTrigger = true;
                intakeBox.size = new Vector3(1f, 0.5f, 1f);

                // (3b) Fill-volume child mesh for the MassFillView: a thin colored cube that rises inside the vessel.
                // MassFillView drives THIS child's local Y scale/position (never the root), so the vessel/intake/art
                // are never flattened (the regression the owner saw).
                GameObject fill = GameObject.CreatePrimitive(PrimitiveType.Cube);
                fill.name = "FillVolume";
                fill.transform.SetParent(root.transform, false);
                fill.transform.localPosition = new Vector3(0f, 0.05f, 0f);
                fill.transform.localScale = new Vector3(0.9f, 0.001f, 0.9f);
                Object.DestroyImmediate(fill.GetComponent<BoxCollider>()); // the fill is purely visual

                var container = root.AddComponent<BallContainer>();
                var cso = new SerializedObject(container);
                cso.FindProperty("id").stringValue = "placeholder";
                cso.FindProperty("colorIndex").intValue = 0;
                cso.FindProperty("intakeCenterLocal").vector3Value = new Vector3(0f, 1f, 0f);
                cso.FindProperty("intakeSizeLocal").vector3Value = new Vector3(1f, 0.5f, 1f);
                cso.ApplyModifiedPropertiesWithoutUndo();

                var mass = root.AddComponent<MassFillView>();
                var mso = new SerializedObject(mass);
                mso.FindProperty("fillVolume").objectReferenceValue = fill.transform;
                mso.FindProperty("fillRenderer").objectReferenceValue = fill.GetComponent<MeshRenderer>();
                // Frame the fill inside the vessel: empty almost flat, full ~ the vessel interior height.
                mso.FindProperty("emptyScaleY").floatValue = 0.001f;
                mso.FindProperty("fullScaleY").floatValue = 0.9f;
                mso.FindProperty("emptyPosY").floatValue = 0.05f;
                mso.FindProperty("fullPosY").floatValue = 0.5f;
                mso.ApplyModifiedPropertiesWithoutUndo();

                PrefabUtility.SaveAsPrefabAsset(root, PlaceholderContainerPath, out bool ok);
                if (!ok) { Debug.LogError($"[GameplayAssetMenu] failed to save '{PlaceholderContainerPath}'"); return; }
            }
            finally { Object.DestroyImmediate(root); }

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlaceholderContainerPath);
            Debug.Log($"[GameplayAssetMenu] created '{PlaceholderContainerPath}'. Set colorIndex per container and put the " +
                      "root on your Interactable layer (see HANDOFF-C LAYERS); run Tools/Ball Pit/Validate Containers.", prefab);
            EditorGUIUtility.PingObject(prefab);
            Selection.activeObject = prefab;
        }

        // ------------------------------------------------------------------ Kiddie PitSizeProfile (C §3.7, §7; §9b)

        /// <summary>
        /// Appends a tiny "kiddie" PitSizeProfile (10x2x10 = 200 balls at 0.5 m, owner Q4) to the active VenueConfig
        /// resource so the owner doesn't hand-type it. Never overwrites an existing "kiddie" key (pings the asset
        /// instead); logs how to create the VenueConfig if none exists. The size is DATA, not a code constant.
        /// </summary>
        [MenuItem("Tools/Ball Pit/Create Kiddie Profile")]
        private static void CreateKiddieProfile()
        {
            var config = AssetDatabase.LoadAssetAtPath<VenueConfig>(VenueConfigPath);
            if (config == null) config = Resources.Load<VenueConfig>(VenueConfig.ResourcePath);
            if (config == null)
            {
                Debug.LogWarning("[GameplayAssetMenu] no VenueConfig asset found. Run Tools > Ball Pit > Create Default " +
                                 "Venue Config first, then run this again to append the 'kiddie' profile.");
                return;
            }

            var so = new SerializedObject(config);
            SerializedProperty profiles = so.FindProperty("profiles");
            for (int i = 0; i < profiles.arraySize; i++)
            {
                SerializedProperty key = profiles.GetArrayElementAtIndex(i).FindPropertyRelative("key");
                if (key != null && key.stringValue == "kiddie")
                {
                    Debug.Log("[GameplayAssetMenu] VenueConfig already has a 'kiddie' profile; it was not changed.", config);
                    EditorGUIUtility.PingObject(config);
                    Selection.activeObject = config;
                    return;
                }
            }

            int idx = profiles.arraySize;
            profiles.arraySize = idx + 1;
            SerializedProperty p = profiles.GetArrayElementAtIndex(idx);
            p.FindPropertyRelative("key").stringValue = "kiddie";
            p.FindPropertyRelative("displayName").stringValue = "Kiddie Pool";
            p.FindPropertyRelative("pitX").intValue = 10;
            p.FindPropertyRelative("pitY").intValue = 2;
            p.FindPropertyRelative("pitZ").intValue = 10;
            p.FindPropertyRelative("ballDiameter").floatValue = 0.5f;
            p.FindPropertyRelative("recommendedPlayersMin").intValue = 1;
            p.FindPropertyRelative("recommendedPlayersMax").intValue = 4;
            p.FindPropertyRelative("estimatedPlayTime").stringValue = "tutorial";
            p.FindPropertyRelative("notes").stringValue = "Tiny above-ground tutorial pool (200 balls). Author anchor at " +
                                                          "floor level; model the visible pool wall to the ball surface.";
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();

            Debug.Log($"[GameplayAssetMenu] appended the 'kiddie' profile (10x2x10 = 200 balls, 0.5 m) to '{AssetDatabase.GetAssetPath(config)}'.", config);
            EditorGUIUtility.PingObject(config);
            Selection.activeObject = config;
        }

        private static void CreateEconomy()
        {
            if (AssetDatabase.LoadAssetAtPath<EconomyConfig>(EconomyPath) != null) return;
            EnsureFolder(UpgradeDir); // also creates Economy/Defaults
            var cfg = ScriptableObject.CreateInstance<EconomyConfig>();
            cfg.baseValue = 1;
            cfg.combinedCap = 2f;
            AssetDatabase.CreateAsset(cfg, EconomyPath);
        }

        private static void CreateCatalog()
        {
            if (AssetDatabase.LoadAssetAtPath<UpgradeCatalog>(CatalogPath) != null) return;
            EnsureFolder(UpgradeDir);

            UpgradeDefinition secondHand = MakeUpgrade("second_hand", "Second Hand", "Carry an extra ball.",
                1, 3, 40, 2.0f, PlayerStat.CarryCapacity, UpgradeEffectKind.Additive, 1f, null);
            UpgradeDefinition strongArm = MakeUpgrade("strong_arm", "Strong Arm", "Throw balls harder and farther.",
                1, 2, 30, 1.8f, PlayerStat.ThrowPower, UpgradeEffectKind.Multiplicative, 0.25f, null);
            UpgradeDefinition widerFunnels = MakeUpgrade("wider_funnels", "Wider Funnels", "Container openings catch balls more easily.",
                1, 1, 25, 1.8f, PlayerStat.CarryCapacity, UpgradeEffectKind.Additive, 0f, "funnel", funnelPerLevel: 0.5f);
            UpgradeDefinition sneakers = MakeUpgrade("sneakers", "Sneakers", "Move a little faster.",
                1, 2, 35, 1.8f, PlayerStat.MoveSpeed, UpgradeEffectKind.Multiplicative, 0.15f, null);

            var catalog = ScriptableObject.CreateInstance<UpgradeCatalog>();
            SetArray(catalog, "upgrades", new[] { secondHand, strongArm, widerFunnels, sneakers });
            AssetDatabase.CreateAsset(catalog, CatalogPath);
        }

        private static UpgradeDefinition MakeUpgrade(string id, string name, string desc, int phase, int maxLevel,
            long costBase, float costGrowth, PlayerStat stat, UpgradeEffectKind kind, float perLevel, string namedHook,
            float funnelPerLevel = 0f)
        {
            string path = UpgradeDir + "/" + id + ".asset";
            var existing = AssetDatabase.LoadAssetAtPath<UpgradeDefinition>(path);
            if (existing != null) return existing;

            var u = ScriptableObject.CreateInstance<UpgradeDefinition>();
            var so = new SerializedObject(u);
            so.FindProperty("id").stringValue = id;
            so.FindProperty("displayName").stringValue = name;
            so.FindProperty("description").stringValue = desc;
            so.FindProperty("phase").intValue = phase;
            so.FindProperty("maxLevel").intValue = maxLevel;
            so.FindProperty("costBase").longValue = costBase;
            so.FindProperty("costGrowth").floatValue = costGrowth;

            SerializedProperty effects = so.FindProperty("effects");
            effects.arraySize = 1;
            SerializedProperty e0 = effects.GetArrayElementAtIndex(0);
            if (string.IsNullOrEmpty(namedHook))
            {
                e0.FindPropertyRelative("stat").enumValueIndex = (int)stat;
                e0.FindPropertyRelative("kind").enumValueIndex = (int)kind;
                e0.FindPropertyRelative("perLevel").floatValue = perLevel;
                e0.FindPropertyRelative("namedHook").stringValue = "";
            }
            else
            {
                e0.FindPropertyRelative("stat").enumValueIndex = (int)stat;
                e0.FindPropertyRelative("kind").enumValueIndex = (int)UpgradeEffectKind.Additive;
                e0.FindPropertyRelative("perLevel").floatValue = funnelPerLevel;
                e0.FindPropertyRelative("namedHook").stringValue = namedHook;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.CreateAsset(u, path);
            return u;
        }

        private static void CreatePalettes()
        {
            EnsureFolder(PresetDir);
            // Classic (authored)
            MakePreset("classic", "Classic",
                new[] { Red(), Blue(), new Color(0.2f, 0.8f, 0.2f), new Color(1f, 0.85f, 0.1f), new Color(0.7f, 0.3f, 0.9f),
                        new Color(1f, 0.5f, 0.1f), new Color(0.1f, 0.8f, 0.85f), new Color(0.95f, 0.4f, 0.6f) },
                new[] { "Red", "Blue", "Green", "Yellow", "Purple", "Orange", "Cyan", "Pink" });
            // Pastel (authored)
            MakePreset("pastel", "Pastel",
                new[] { new Color(1f, 0.7f, 0.7f), new Color(0.7f, 0.8f, 1f), new Color(0.75f, 1f, 0.78f),
                        new Color(1f, 0.95f, 0.7f), new Color(0.86f, 0.76f, 1f), new Color(1f, 0.82f, 0.65f),
                        new Color(0.72f, 0.95f, 0.95f), new Color(1f, 0.78f, 0.88f) },
                new[] { "Rose", "Sky", "Mint", "Butter", "Lilac", "Peach", "Aqua", "Blossom" });
            // Placeholders (no colours => HSV fallback at runtime); the owner authors them later.
            MakePreset("candy", "Candy", new Color[0], new string[0]);
            MakePreset("neon", "Neon", new Color[0], new string[0]);
            MakePreset("ocean", "Ocean", new Color[0], new string[0]);
            MakePreset("sunset", "Sunset", new Color[0], new string[0]);
        }

        private static void MakePreset(string key, string name, Color[] colors, string[] names)
        {
            string path = PresetDir + "/" + key + ".asset";
            if (AssetDatabase.LoadAssetAtPath<PalettePreset>(path) != null) return;
            var preset = ScriptableObject.CreateInstance<PalettePreset>();
            preset.Author(key, name, colors, names);
            AssetDatabase.CreateAsset(preset, path);
        }

        private static Color Red() => new Color(0.9f, 0.2f, 0.2f);
        private static Color Blue() => new Color(0.2f, 0.4f, 0.95f);

        private static void SetArray(Object obj, string field, UpgradeDefinition[] values)
        {
            var so = new SerializedObject(obj);
            SerializedProperty arr = so.FindProperty(field);
            arr.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++) arr.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            int slash = folder.LastIndexOf('/');
            string parent = folder.Substring(0, slash);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, folder.Substring(slash + 1));
        }
    }
}

// GameplayAssetMenu: Tools/Ball Pit/Create Default Gameplay Assets (brief §4.3, §4.5). One-click generation of the
// bite-(a) defaults: an EconomyConfig, an UpgradeCatalog with the four starter upgrades (Second Hand, Strong Arm,
// Wider Funnels, Sneakers) and their UpgradeDefinition assets, and the six palette presets (Classic + Pastel authored,
// Candy/Neon/Ocean/Sunset placeholder). Never overwrites an existing asset; it pings and selects it instead. The owner
// then assigns these to GameplaySession / PaletteApplier. Editor only.
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

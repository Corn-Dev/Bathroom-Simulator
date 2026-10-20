// CampaignEditorTools: the editor menus for Deliverable D (CampaignDirector). Editor only.
//   Tools/Ball Pit/Create Campaign Definition : MainCampaign.asset with two empty entries (colorCount 3 and 4).
//   Tools/Ball Pit/Create Campaign Net Prefab : BallPitCampaignNet.prefab (NetworkObject + CampaignNet).
//   Tools/Ball Pit/Validate Campaign          : checks the campaign list, the pit prefabs and the open scene.
// Follows GameplayAssetMenu: private EnsureFolder, never overwrites (pings + selects instead).
using System.Collections.Generic;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;

namespace BallPitGameplay
{
    internal static class CampaignEditorTools
    {
        private const string Root = "Assets/Game/BallPitGameplay/Campaign";
        private const string DefinitionPath = Root + "/MainCampaign.asset";
        private const string NetPrefabPath = Root + "/BallPitCampaignNet.prefab";

        // ------------------------------------------------------------------ Create Campaign Definition

        [MenuItem("Tools/Ball Pit/Create Campaign Definition")]
        private static void CreateDefinition()
        {
            var existing = AssetDatabase.LoadAssetAtPath<CampaignDefinition>(DefinitionPath);
            if (existing != null)
            {
                Debug.Log($"[CampaignEditorTools] '{DefinitionPath}' already exists; it was not overwritten", existing);
                EditorGUIUtility.PingObject(existing);
                Selection.activeObject = existing;
                return;
            }
            EnsureFolder(Root);

            var def = ScriptableObject.CreateInstance<CampaignDefinition>();
            AssetDatabase.CreateAsset(def, DefinitionPath);

            // Two empty entries (CAMPAIGN-SCENE-SETUP Part A step 6): entry 0 = 3 colours, entry 1 = 4 colours. Prefabs are the owner's.
            var so = new SerializedObject(def);
            SerializedProperty entries = so.FindProperty("entries");
            entries.arraySize = 2;
            entries.GetArrayElementAtIndex(0).FindPropertyRelative("colorCount").intValue = 3;
            entries.GetArrayElementAtIndex(1).FindPropertyRelative("colorCount").intValue = 4;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(def);
            AssetDatabase.SaveAssets();

            Debug.Log($"[CampaignEditorTools] created '{DefinitionPath}' with 2 entries (colorCount 3 and 4). Assign each entry's " +
                      "pitPrefab (a Pit_*.prefab with a PitSite), then run Tools/Ball Pit/Validate Campaign.", def);
            EditorGUIUtility.PingObject(def);
            Selection.activeObject = def;
        }

        // ------------------------------------------------------------------ Create Campaign Net Prefab

        [MenuItem("Tools/Ball Pit/Create Campaign Net Prefab")]
        private static void CreateNetPrefab()
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(NetPrefabPath);
            if (existing != null)
            {
                Debug.Log($"[CampaignEditorTools] '{NetPrefabPath}' already exists; it was not overwritten", existing);
                EditorGUIUtility.PingObject(existing);
                Selection.activeObject = existing;
                return;
            }
            EnsureFolder(Root);

            var temp = new GameObject("BallPitCampaignNet");
            try
            {
                temp.AddComponent<NetworkObject>();
                temp.AddComponent<CampaignNet>();
                PrefabUtility.SaveAsPrefabAsset(temp, NetPrefabPath, out bool ok);
                if (!ok) { Debug.LogError($"[CampaignEditorTools] failed to save '{NetPrefabPath}'"); return; }
            }
            finally { Object.DestroyImmediate(temp); }

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(NetPrefabPath);
            Debug.Log($"[CampaignEditorTools] created '{NetPrefabPath}'. Add it to the kit NetworkPrefabsList (the same list that holds " +
                      "BallPitGameplayNet.prefab) and assign it to CampaignDirector.campaignNetPrefab.", prefab);
            EditorGUIUtility.PingObject(prefab);
            Selection.activeObject = prefab;
        }

        // ------------------------------------------------------------------ Validate Campaign

        [MenuItem("Tools/Ball Pit/Validate Campaign")]
        private static void ValidateCampaign()
        {
            var errors = new List<string>();
            var warnings = new List<string>();

            // Which definition(s): the open scene's director first, else every CampaignDefinition asset.
            CampaignDirector director = Object.FindObjectOfType<CampaignDirector>(true);
            var definitions = new List<CampaignDefinition>();
            if (director != null)
            {
                var dso = new SerializedObject(director);
                var def = dso.FindProperty("definition").objectReferenceValue as CampaignDefinition;
                if (def == null) errors.Add("CampaignDirector has no CampaignDefinition assigned.");
                else definitions.Add(def);
            }
            else
            {
                warnings.Add("No CampaignDirector in the open scene; validating every CampaignDefinition asset and skipping scene checks that need it.");
                foreach (string guid in AssetDatabase.FindAssets("t:CampaignDefinition"))
                {
                    var def = AssetDatabase.LoadAssetAtPath<CampaignDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                    if (def != null) definitions.Add(def);
                }
                if (definitions.Count == 0) errors.Add("No CampaignDefinition asset found. Run Tools/Ball Pit/Create Campaign Definition.");
            }

            VenueConfig venue = VenueConfig.Load();
            if (venue == null) errors.Add("No VenueConfig could be loaded (Tools/Ball Pit/Create Default Venue Config).");

            int maxColors = 0;
            for (int d = 0; d < definitions.Count; d++)
            {
                CampaignDefinition def = definitions[d];
                string dn = def.name;
                if (def.Count == 0) errors.Add($"[{dn}] the campaign has no entries.");

                var seenSiteIds = new Dictionary<string, int>();
                for (int i = 0; i < def.Count; i++)
                {
                    CampaignEntry entry = def.Entries[i];
                    string tag = $"[{dn}] entry {i}";
                    if (entry == null) { errors.Add($"{tag}: null entry."); continue; }

                    if (entry.colorCount < CampaignMath.MinColors || entry.colorCount > CampaignMath.MaxColors)
                        warnings.Add($"{tag}: colorCount {entry.colorCount} is outside {CampaignMath.MinColors}..{CampaignMath.MaxColors}; it is clamped at runtime.");
                    int clamped = CampaignMath.ClampColorCount(entry.colorCount);
                    if (clamped > maxColors) maxColors = clamped;

                    if (entry.pitPrefab == null) { errors.Add($"{tag}: pitPrefab is not assigned."); continue; }
                    PitSite site = entry.pitPrefab.GetComponentInChildren<PitSite>(true);
                    if (site == null) { errors.Add($"{tag}: prefab '{entry.pitPrefab.name}' has no PitSite on the root or any child."); continue; }

                    // The site's own cheap rules (siteId format, anchor, venueBounds, ...); relayed with the entry prefix.
                    if (venue != null)
                    {
                        var siteErrors = new List<string>();
                        var siteWarnings = new List<string>();
                        site.Validate(venue, siteErrors, siteWarnings);
                        foreach (string e in siteErrors) errors.Add($"{tag} ('{entry.pitPrefab.name}'): {e}");
                        foreach (string w in siteWarnings) warnings.Add($"{tag} ('{entry.pitPrefab.name}'): {w}");

                        PitSizeProfile profile;
                        if (!venue.TryGetProfile(site.ProfileKey, out profile))
                            errors.Add($"{tag} ('{entry.pitPrefab.name}'): profileKey '{site.ProfileKey}' is not defined in the VenueConfig (the host build fails).");
                    }

                    if (seenSiteIds.TryGetValue(site.SiteId, out int firstIndex))
                        warnings.Add($"{tag}: siteId '{site.SiteId}' is also used by entry {firstIndex}. Legal, but a resume then relies on the saved campaign index to tell them apart; unique siteIds are safer.");
                    else seenSiteIds[site.SiteId] = i;

                    Transform root = entry.pitPrefab.transform;
                    if (root.rotation != Quaternion.identity || root.localScale != Vector3.one)
                        warnings.Add($"{tag}: prefab root '{entry.pitPrefab.name}' has a non-identity rotation or scale; it is instantiated at its authored pose and the PitSite anchor must stay unrotated.");
                }
            }

            // Scene checks (the open scene).
            BallContainer[] containers = Object.FindObjectsOfType<BallContainer>(true);
            if (maxColors > 0)
            {
                if (containers.Length < maxColors)
                    warnings.Add($"Only {containers.Length} BallContainer(s) in the open scene but a campaign entry has {maxColors} colours.");
                for (int c = 0; c < maxColors; c++)
                {
                    bool found = false;
                    for (int k = 0; k < containers.Length; k++)
                        if (containers[k].ColorIndex == c) { found = true; break; }
                    if (!found) errors.Add($"No BallContainer with colorIndex {c} in the open scene; a pit with {c + 1}+ colours can never fully drain.");
                }
            }
            for (int k = 0; k < containers.Length; k++)
                if (containers[k].Site != null)
                    warnings.Add($"BallContainer '{containers[k].name}' has its Site field set. A scene-resident PitSite link breaks once pits are prefabs; clear it so the container applies to every pit.");

            foreach (PitSite resident in Object.FindObjectsOfType<PitSite>(true))
                warnings.Add($"PitSite '{resident.name}' is resident in the open scene. The campaign instantiates pits at runtime; no pit should sit in the scene.");

            if (director != null)
            {
                var dso = new SerializedObject(director);
                var netPrefab = dso.FindProperty("campaignNetPrefab").objectReferenceValue as NetworkObject;
                if (netPrefab == null)
                    errors.Add("CampaignDirector.campaignNetPrefab is not assigned (run Create Campaign Net Prefab). Required for the networked advance; offline play does not need it.");
                else if (netPrefab.GetComponent<CampaignNet>() == null)
                    errors.Add("CampaignDirector.campaignNetPrefab has no CampaignNet component.");
            }

            foreach (string w in warnings) Debug.LogWarning("[CampaignEditorTools] " + w);
            foreach (string e in errors) Debug.LogError("[CampaignEditorTools] " + e);
            Debug.Log($"[CampaignEditorTools] Validate Campaign: {errors.Count} error(s), {warnings.Count} warning(s).");
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

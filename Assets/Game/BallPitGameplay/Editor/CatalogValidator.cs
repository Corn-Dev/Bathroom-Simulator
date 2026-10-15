// CatalogValidator: Tools/Ball Pit/Validate Upgrade Catalogs (brief §4.3). Runs UpgradeCatalog.Validate on every
// catalog asset in the project (unique valid ids, monotonic costs, resolvable prerequisites) and logs each problem.
// Pure asset check; never modifies anything.
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace BallPitGameplay
{
    internal static class CatalogValidator
    {
        [MenuItem("Tools/Ball Pit/Validate Upgrade Catalogs")]
        private static void Run()
        {
            string[] guids = AssetDatabase.FindAssets("t:UpgradeCatalog");
            int problems = 0;
            var list = new List<string>();
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var catalog = AssetDatabase.LoadAssetAtPath<UpgradeCatalog>(path);
                if (catalog == null) continue;
                list.Clear();
                if (!catalog.Validate(list))
                {
                    problems += list.Count;
                    foreach (string p in list) Debug.LogError($"[Validate Catalog] {path}: {p}", catalog);
                }
            }
            Debug.Log($"[Validate Catalog] checked {guids.Length} catalog(s): {problems} problem(s)");
        }
    }
}

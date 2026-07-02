using System;
using System.Collections.Generic;
using Steamworks;
using UnityEngine;

/// <summary>
/// Development/testing backend that runs entirely client-side.
/// Rolls fish from a weighted table and grants via Steam Inventory client API.
/// Replace with RemoteFishingBackend for production.
/// </summary>
public class MockFishingBackend : IFishingBackend
{
    private readonly FishSpeciesData[] allFish;
    private readonly HashSet<string> caughtSpecies;

    public MockFishingBackend(FishSpeciesData[] allFish)
    {
        this.allFish = allFish;
        caughtSpecies = new HashSet<string>();

        // Load previously caught species from PlayerPrefs (dev persistence)
        string saved = UnityEngine.PlayerPrefs.GetString("Fishdex_Caught", "");
        if (!string.IsNullOrEmpty(saved))
        {
            foreach (string id in saved.Split(','))
            {
                if (!string.IsNullOrEmpty(id))
                    caughtSpecies.Add(id);
            }
        }
    }

    public void RequestCatch(FishingContext context, Action<CatchResult> onResult, Action<string> onError)
    {
        if (allFish == null || allFish.Length == 0)
        {
            onError?.Invoke("No fish species configured.");
            return;
        }

        // Roll which fish was caught
        FishSpeciesData fish = RollFish(context);
        if (fish == null)
        {
            onError?.Invoke("Fish roll failed.");
            return;
        }

        // Grant via Steam Inventory
        bool grantSuccess = GrantFishToInventory(fish);
        if (!grantSuccess)
        {
            onError?.Invoke("Steam Inventory grant failed.");
            return;
        }

        // Check if this is a new species for the Fishdex
        bool isNew = !caughtSpecies.Contains(fish.fishId);
        if (isNew)
        {
            caughtSpecies.Add(fish.fishId);
            SaveFishdex();
        }

        onResult?.Invoke(new CatchResult
        {
            fish = fish,
            isNewSpecies = isNew
        });
    }

    private FishSpeciesData RollFish(FishingContext context)
    {
        float totalWeight = 0f;
        float[] weights = new float[allFish.Length];

        for (int i = 0; i < allFish.Length; i++)
        {
            float weight = allFish[i].dropWeight;

            // Rod rarity bonus: boost rare+ fish weights
            if (context.rod != null && allFish[i].rarity >= FishRarity.Rare)
            {
                weight *= (1f + context.rod.rarityBonus * 10f);
            }

            weights[i] = weight;
            totalWeight += weight;
        }

        float roll = UnityEngine.Random.Range(0f, totalWeight);
        float cumulative = 0f;

        for (int i = 0; i < allFish.Length; i++)
        {
            cumulative += weights[i];
            if (roll <= cumulative)
            {
                return allFish[i];
            }
        }

        return allFish[allFish.Length - 1];
    }

    private bool GrantFishToInventory(FishSpeciesData fish)
    {
        if (!SteamManager.Initialized)
        {
            Debug.LogWarning("[MockFishingBackend] Steam not initialized — skipping inventory grant.");
            return true; // Still return true so the flow works in editor without Steam
        }

        if (fish.steamItemDefId <= 0)
        {
            Debug.LogWarning($"[MockFishingBackend] Fish '{fish.displayName}' has no Steam item def ID — skipping grant.");
            return true; // Allow flow to continue for fish without Steam integration
        }

        SteamItemDef_t[] itemDefs = new SteamItemDef_t[] { (SteamItemDef_t)fish.steamItemDefId };
        uint[] quantities = new uint[] { 1 };

        bool success = SteamInventory.GenerateItems(
            out _,
            itemDefs,
            quantities,
            (uint)itemDefs.Length
        );

        if (!success)
        {
            Debug.LogError($"[MockFishingBackend] GenerateItems failed for '{fish.displayName}' (defId: {fish.steamItemDefId})");
        }

        return success;
    }

    /// <summary>
    /// Check if a species has been caught before (for Fishdex display).
    /// </summary>
    public bool HasCaughtSpecies(string fishId) => caughtSpecies.Contains(fishId);

    /// <summary>
    /// Total number of unique species caught.
    /// </summary>
    public int GetCaughtCount() => caughtSpecies.Count;

    private void SaveFishdex()
    {
        string data = string.Join(",", caughtSpecies);
        UnityEngine.PlayerPrefs.SetString("Fishdex_Caught", data);
        UnityEngine.PlayerPrefs.Save();
    }
}

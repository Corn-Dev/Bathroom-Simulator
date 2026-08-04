using System;
using UnityEngine;

/// <summary>
/// Development backend that runs entirely client-side.
/// Rolls fish from a weighted table, rolls a size, and saves to PlayerSaveData.
/// No Steam Inventory grants — all data is in the save file.
/// </summary>
public class MockFishingBackend : IFishingBackend
{
    private readonly FishSpeciesData[] allFish;

    public MockFishingBackend(FishSpeciesData[] allFish)
    {
        this.allFish = allFish;
    }

    public void RequestCatch(FishingContext context, Action<CatchResult> onResult, Action<string> onError)
    {
        if (allFish == null || allFish.Length == 0)
        {
            onError?.Invoke("No fish species configured.");
            return;
        }

        if (SaveManager.Instance == null)
        {
            onError?.Invoke("SaveManager not initialized.");
            return;
        }

        // Roll which fish was caught
        FishSpeciesData fish = RollFish(context);
        if (fish == null)
        {
            onError?.Invoke("Fish roll failed.");
            return;
        }

        // Roll size on a bell curve
        float size = RollSize(fish);

        // Check if this is a new species
        bool isNew = !SaveManager.Instance.Data.HasCaughtSpecies(fish.fishId);

        // Check if this is a new personal best
        float previousBest = SaveManager.Instance.Data.GetPersonalBest(fish.fishId);
        bool isPersonalBest = size > previousBest;

        // Register the catch in save data
        SaveManager.Instance.Data.RegisterCatch(fish.fishId, size);
        SaveManager.Instance.Save();

        onResult?.Invoke(new CatchResult
        {
            fish = fish,
            isNewSpecies = isNew,
            size = size,
            isPersonalBest = isPersonalBest
        });
    }

    private FishSpeciesData RollFish(FishingContext context)
    {
        float totalWeight = 0f;
        float[] weights = new float[allFish.Length];

        for (int i = 0; i < allFish.Length; i++)
        {
            float weight = allFish[i].dropWeight;

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
                return allFish[i];
        }

        return allFish[allFish.Length - 1];
    }

    private float RollSize(FishSpeciesData fish)
    {
        // Bell curve centered on averageWeight, clamped to min/max
        float mean = fish.averageWeight;
        float stdDev = (fish.maxWeight - fish.minWeight) * 0.15f; // Most catches cluster near average

        // Box-Muller transform for gaussian distribution
        float u1 = UnityEngine.Random.Range(0.0001f, 1f);
        float u2 = UnityEngine.Random.Range(0.0001f, 1f);
        float gaussian = Mathf.Sqrt(-2f * Mathf.Log(u1)) * Mathf.Cos(2f * Mathf.PI * u2);

        float size = mean + gaussian * stdDev;
        size = Mathf.Clamp(size, fish.minWeight, fish.maxWeight);
        return Mathf.Round(size * 10f) / 10f; // Round to 1 decimal
    }

    /// <summary>Check if a species has been caught (reads from save).</summary>
    public bool HasCaughtSpecies(string fishId)
    {
        return SaveManager.Instance != null && SaveManager.Instance.Data.HasCaughtSpecies(fishId);
    }

    /// <summary>Total unique species caught.</summary>
    public int GetCaughtCount()
    {
        return SaveManager.Instance != null ? SaveManager.Instance.Data.GetCaughtCount() : 0;
    }
}

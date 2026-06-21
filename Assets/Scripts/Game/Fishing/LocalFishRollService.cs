using UnityEngine;

/// <summary>
/// Local (client-side) implementation of IFishRollService.
/// Rolls a fish from a weighted table based on rarity and drop weights.
/// Used for development/testing. In production, the server does this roll.
/// </summary>
public class LocalFishRollService : IFishRollService
{
    private readonly FishSpeciesData[] allFish;

    public LocalFishRollService(FishSpeciesData[] allFish)
    {
        this.allFish = allFish;
    }

    public FishSpeciesData RollFish(FishingContext context)
    {
        if (allFish == null || allFish.Length == 0)
        {
            Debug.LogError("[LocalFishRollService] No fish species configured!");
            return null;
        }

        // Calculate total weight, applying rod's rarity bonus to rarer fish
        float totalWeight = 0f;
        float[] weights = new float[allFish.Length];

        for (int i = 0; i < allFish.Length; i++)
        {
            float weight = allFish[i].dropWeight;

            // Rod rarity bonus: multiply rare+ fish weights by (1 + bonus)
            if (context.rod != null && allFish[i].rarity >= FishRarity.Rare)
            {
                weight *= (1f + context.rod.rarityBonus * 10f);
            }

            weights[i] = weight;
            totalWeight += weight;
        }

        // Weighted random selection
        float roll = Random.Range(0f, totalWeight);
        float cumulative = 0f;

        for (int i = 0; i < allFish.Length; i++)
        {
            cumulative += weights[i];
            if (roll <= cumulative)
            {
                return allFish[i];
            }
        }

        // Fallback (shouldn't happen)
        return allFish[allFish.Length - 1];
    }
}

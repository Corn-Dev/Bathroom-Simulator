using System;
using System.Collections.Generic;

/// <summary>
/// All persistent player data. Serialized to JSON and stored in Steam Cloud.
/// Add new fields as needed — existing saves without the field will get the default value.
/// </summary>
[Serializable]
public class PlayerSaveData
{
    // Currency
    public int currency = 0;

    // Fishdex — which species have been caught at least once
    public List<string> caughtSpecies = new List<string>();

    // Personal bests — biggest size caught per species
    public List<PersonalBestEntry> personalBests = new List<PersonalBestEntry>();

    // Fish inventory — fish the player is holding (to sell or keep)
    public List<CaughtFishEntry> fishInventory = new List<CaughtFishEntry>();

    // Gear
    public string equippedRodId = "starter";
    public string equippedBaitId = "basic";
    public List<string> ownedRods = new List<string> { "starter" };
    public List<string> ownedBait = new List<string> { "basic" };

    // Zones unlocked
    public List<string> unlockedZones = new List<string> { "pond" };

    // --- Helper methods ---

    public bool HasCaughtSpecies(string fishId)
    {
        return caughtSpecies.Contains(fishId);
    }

    public void RegisterCatch(string fishId, float size)
    {
        // Mark species as caught
        if (!caughtSpecies.Contains(fishId))
            caughtSpecies.Add(fishId);

        // Update personal best
        var pb = personalBests.Find(p => p.fishId == fishId);
        if (pb == null)
        {
            personalBests.Add(new PersonalBestEntry { fishId = fishId, bestSize = size });
        }
        else if (size > pb.bestSize)
        {
            pb.bestSize = size;
        }

        // Add to inventory
        fishInventory.Add(new CaughtFishEntry { fishId = fishId, size = size });
    }

    public float GetPersonalBest(string fishId)
    {
        var pb = personalBests.Find(p => p.fishId == fishId);
        return pb != null ? pb.bestSize : 0f;
    }

    public int GetCaughtCount()
    {
        return caughtSpecies.Count;
    }
}

[Serializable]
public class PersonalBestEntry
{
    public string fishId;
    public float bestSize;
}

[Serializable]
public class CaughtFishEntry
{
    public string fishId;
    public float size;
}

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

    // Achievements the player has unlocked (Steam achievement API names).
    public List<string> unlockedAchievements = new List<string>();

    // Generic unlockables (cosmetics, areas, etc.) keyed by id.
    public List<string> unlockedItems = new List<string>();

    // Arbitrary counters for tracking stats that drive achievements
    // (e.g. "flushes", "handsWashed"). Keyed by stat id.
    public List<StatEntry> stats = new List<StatEntry>();

    // --- Helper methods ---

    public bool HasAchievement(string achievementId)
    {
        return unlockedAchievements.Contains(achievementId);
    }

    public void UnlockAchievement(string achievementId)
    {
        if (!unlockedAchievements.Contains(achievementId))
            unlockedAchievements.Add(achievementId);
    }

    public bool HasItem(string itemId)
    {
        return unlockedItems.Contains(itemId);
    }

    public void UnlockItem(string itemId)
    {
        if (!unlockedItems.Contains(itemId))
            unlockedItems.Add(itemId);
    }

    public int GetStat(string statId)
    {
        var entry = stats.Find(s => s.statId == statId);
        return entry != null ? entry.value : 0;
    }

    public int IncrementStat(string statId, int amount = 1)
    {
        var entry = stats.Find(s => s.statId == statId);
        if (entry == null)
        {
            entry = new StatEntry { statId = statId, value = 0 };
            stats.Add(entry);
        }

        entry.value += amount;
        return entry.value;
    }
}

[Serializable]
public class StatEntry
{
    public string statId;
    public int value;
}

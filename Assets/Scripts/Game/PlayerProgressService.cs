using System;
using UnityEngine;

/// <summary>
/// Loads the local player's persistent progress (achievements, unlocks, etc.)
/// before gameplay is enabled. Replace the placeholder body with a real
/// cloud/back-end load when ready.
/// </summary>
public sealed class PlayerProgressService : IPlayerProgressService
{
    private const string ProgressVersionSuffix = "_progress_version";

    public void BeginLoadProgress(string identityKey, Action<bool> onCompleted)
    {
        if (string.IsNullOrWhiteSpace(identityKey))
        {
            onCompleted?.Invoke(false);
            return;
        }

        // Placeholder for cloud/back-end load. For now we validate local profile availability.
        string key = identityKey + ProgressVersionSuffix;
        if (!PlayerPrefs.HasKey(key))
        {
            PlayerPrefs.SetInt(key, 1);
            PlayerPrefs.Save();
        }

        onCompleted?.Invoke(true);
    }
}

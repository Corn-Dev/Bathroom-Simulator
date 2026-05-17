using System;
using UnityEngine;

public sealed class FishingPlayerProgressService : IPlayerProgressService
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

using Steamworks;
using UnityEngine;

/// <summary>
/// Manages player save data via Steam Cloud.
/// Call Load() on game start, Save() after any meaningful change.
/// Access current data via SaveManager.Instance.Data.
///
/// Falls back to local file if Steam isn't available (editor testing).
/// </summary>
public class SaveManager : MonoBehaviour
{
    private const string SAVE_FILE_NAME = "phish_save.json";

    public static SaveManager Instance { get; private set; }
    public PlayerSaveData Data { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
        Load();
    }

    public void Save()
    {
        string json = JsonUtility.ToJson(Data, true);

        if (SteamManager.Initialized)
        {
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(json);
            bool success = SteamRemoteStorage.FileWrite(SAVE_FILE_NAME, bytes, bytes.Length);
            if (!success)
                Debug.LogWarning("[SaveManager] Steam Cloud write failed — saving locally as fallback.");
            else
            {
                Debug.Log("[SaveManager] Saved to Steam Cloud.");
                return;
            }
        }

        // Fallback: local PlayerPrefs
        PlayerPrefs.SetString("PhishSave", json);
        PlayerPrefs.Save();
        Debug.Log("[SaveManager] Saved locally (Steam unavailable).");
    }

    public void Load()
    {
        string json = null;

        try
        {
            if (SteamManager.Initialized && SteamRemoteStorage.IsCloudEnabledForAccount() && SteamRemoteStorage.FileExists(SAVE_FILE_NAME))
            {
                int fileSize = SteamRemoteStorage.GetFileSize(SAVE_FILE_NAME);
                if (fileSize > 0)
                {
                    byte[] bytes = new byte[fileSize];
                    int bytesRead = SteamRemoteStorage.FileRead(SAVE_FILE_NAME, bytes, fileSize);

                    if (bytesRead > 0)
                    {
                        json = System.Text.Encoding.UTF8.GetString(bytes, 0, bytesRead);
                        Debug.Log("[SaveManager] Loaded from Steam Cloud.");
                    }
                }
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[SaveManager] Steam Cloud read failed: {e.Message}");
        }

        // Fallback: local
        if (string.IsNullOrEmpty(json))
        {
            json = PlayerPrefs.GetString("PhishSave", "");
            if (!string.IsNullOrEmpty(json))
                Debug.Log("[SaveManager] Loaded from local storage.");
        }

        if (!string.IsNullOrEmpty(json))
        {
            Data = JsonUtility.FromJson<PlayerSaveData>(json);
        }
        else
        {
            Data = new PlayerSaveData();
            Debug.Log("[SaveManager] No save found — starting fresh.");
        }
    }

    /// <summary>
    /// Wipe save data (for testing). Call from dev console.
    /// </summary>
    public void ResetSave()
    {
        Data = new PlayerSaveData();
        Save();
        Debug.Log("[SaveManager] Save reset.");
    }
}

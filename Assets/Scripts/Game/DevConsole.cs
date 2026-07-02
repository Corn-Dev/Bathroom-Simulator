using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// In-game developer console with two modes:
///   - Passive: top-left, low opacity, shows recent messages, fades after inactivity
///   - Focused: higher opacity, text input active, cursor unlocked
///
/// F1 toggles focus mode. Escape or Enter (after sending) exits focus mode.
/// Messages auto-show when new logs arrive and fade after a timeout.
/// Remove before shipping.
/// </summary>
public class DevConsole : MonoBehaviour
{
    [Header("Config")]
    [SerializeField] private KeyCode focusKey = KeyCode.F1;
    [SerializeField] private int maxLines = 30;
    [SerializeField] private int fontSize = 13;
    [SerializeField] private float fadeTimeout = 5f;
    [SerializeField] private float passiveOpacity = 0.4f;
    [SerializeField] private float focusedOpacity = 0.85f;

    private bool isFocused = false;
    private float lastMessageTime;
    private readonly List<LogEntry> logEntries = new List<LogEntry>();
    private Vector2 scrollPosition;
    private string inputText = "";
    private bool stylesInitialized = false;

    private GUIStyle logStyle;
    private GUIStyle inputStyle;
    private Texture2D bgTexture;

    /// <summary>
    /// Returns true if the console is focused and capturing input.
    /// </summary>
    public bool IsOpen => isFocused;

    private struct LogEntry
    {
        public string message;
        public LogType type;
        public float time;
    }

    private void OnEnable()
    {
        Application.logMessageReceived += HandleLog;
    }

    private void OnDisable()
    {
        Application.logMessageReceived -= HandleLog;
    }

    private void HandleLog(string message, string stackTrace, LogType type)
    {
        logEntries.Add(new LogEntry { message = message, type = type, time = Time.unscaledTime });
        if (logEntries.Count > maxLines)
            logEntries.RemoveAt(0);

        lastMessageTime = Time.unscaledTime;
        scrollPosition.y = float.MaxValue;
    }

    private void Update()
    {
        if (Input.GetKeyDown(focusKey))
        {
            isFocused = !isFocused;
            if (isFocused)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            else
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }

        if (isFocused && Input.GetKeyDown(KeyCode.Escape))
        {
            isFocused = false;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    private void InitStyles()
    {
        if (stylesInitialized) return;

        logStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = fontSize,
            wordWrap = true,
            richText = true
        };

        inputStyle = new GUIStyle(GUI.skin.textField)
        {
            fontSize = fontSize
        };

        bgTexture = new Texture2D(1, 1);
        bgTexture.SetPixel(0, 0, Color.black);
        bgTexture.Apply();

        stylesInitialized = true;
    }

    private void OnGUI()
    {
        // Don't render over full-screen UI panels
        var fishdex = FindFirstObjectByType<FishdexUI>();
        if (fishdex != null && fishdex.IsOpen) return;

        InitStyles();

        // Determine visibility: show if focused, or if a recent message exists
        float timeSinceLastMessage = Time.unscaledTime - lastMessageTime;
        bool hasRecentMessage = timeSinceLastMessage < fadeTimeout;

        if (!isFocused && !hasRecentMessage) return;

        // Calculate opacity
        float opacity;
        if (isFocused)
        {
            opacity = focusedOpacity;
        }
        else
        {
            // Fade out over the last second of the timeout
            float fadeStart = fadeTimeout - 1f;
            if (timeSinceLastMessage > fadeStart)
                opacity = passiveOpacity * (1f - (timeSinceLastMessage - fadeStart));
            else
                opacity = passiveOpacity;
        }

        if (opacity <= 0f) return;

        // Draw in top-left
        float width = Screen.width * 0.4f;
        float height = isFocused ? Screen.height * 0.35f : Screen.height * 0.25f;
        Rect consoleRect = new Rect(10, 10, width, height);

        // Background with dynamic opacity
        Color bgColor = new Color(0f, 0f, 0f, opacity);
        GUI.color = bgColor;
        GUI.DrawTexture(consoleRect, bgTexture);
        GUI.color = Color.white;

        GUILayout.BeginArea(consoleRect);

        // Log output
        float logHeight = isFocused ? height - 30 : height;
        scrollPosition = GUILayout.BeginScrollView(scrollPosition, GUIStyle.none, GUIStyle.none, GUILayout.Height(logHeight));

        foreach (var entry in logEntries)
        {
            Color color = entry.type switch
            {
                LogType.Error => Color.red,
                LogType.Warning => Color.yellow,
                LogType.Exception => Color.magenta,
                _ => Color.white
            };
            color.a = opacity;
            logStyle.normal.textColor = color;
            GUILayout.Label(entry.message, logStyle);
        }

        GUILayout.EndScrollView();

        // Input field only in focused mode
        if (isFocused)
        {
            GUILayout.BeginHorizontal();
            GUI.SetNextControlName("DevInput");
            inputText = GUILayout.TextField(inputText, inputStyle, GUILayout.Height(22));

            bool submitted = false;
            if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Return
                && GUI.GetNameOfFocusedControl() == "DevInput")
            {
                submitted = true;
                Event.current.Use();
            }

            if (submitted || GUILayout.Button("Run", GUILayout.Width(40), GUILayout.Height(22)))
            {
                if (!string.IsNullOrWhiteSpace(inputText))
                {
                    ExecuteCommand(inputText.Trim());
                    inputText = "";
                }
                // Exit focus after sending
                isFocused = false;
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }

            GUILayout.EndHorizontal();
            GUI.FocusControl("DevInput");
        }

        GUILayout.EndArea();
    }

    private void ExecuteCommand(string command)
    {
        Debug.Log($"> {command}");

        switch (command.ToLower())
        {
            case "help":
                Debug.Log("Commands: help, clear, fps, state, clearinventory, resetdex");
                break;
            case "clear":
                logEntries.Clear();
                break;
            case "fps":
                Debug.Log($"FPS: {(1f / Time.unscaledDeltaTime):F0}");
                break;
            case "state":
                var fishing = FindFirstObjectByType<FishingController>();
                if (fishing != null)
                    Debug.Log($"Fishing state: {fishing.CurrentState}");
                else
                    Debug.Log("No FishingController found.");
                break;
            case "clearinventory":
                ClearSteamInventory();
                break;
            case "resetdex":
                UnityEngine.PlayerPrefs.DeleteKey("Fishdex_Caught");
                UnityEngine.PlayerPrefs.Save();
                Debug.Log("Fishdex reset — all species marked uncaught.");
                break;
            default:
                Debug.Log($"Unknown command: {command}");
                break;
        }
    }

    private void ClearSteamInventory()
    {
        if (!SteamManager.Initialized)
        {
            Debug.LogError("Steam not initialized.");
            return;
        }

        Debug.Log("Fetching inventory to clear...");
        Steamworks.SteamInventoryResult_t result;
        bool success = Steamworks.SteamInventory.GetAllItems(out result);
        if (!success)
        {
            Debug.LogError("GetAllItems failed.");
            return;
        }

        // Need to wait for the result — use a callback
        Steamworks.Callback<Steamworks.SteamInventoryResultReady_t>.Create(cb =>
        {
            if (cb.m_handle != result) return;
            if (cb.m_result != Steamworks.EResult.k_EResultOK)
            {
                Debug.LogError($"Inventory fetch failed: {cb.m_result}");
                Steamworks.SteamInventory.DestroyResult(result);
                return;
            }

            uint count = 0;
            Steamworks.SteamInventory.GetResultItems(result, null, ref count);
            if (count == 0)
            {
                Debug.Log("Inventory already empty.");
                Steamworks.SteamInventory.DestroyResult(result);
                return;
            }

            var items = new Steamworks.SteamItemDetails_t[count];
            Steamworks.SteamInventory.GetResultItems(result, items, ref count);

            int destroyed = 0;
            for (int i = 0; i < count; i++)
            {
                Steamworks.SteamInventoryResult_t consumeResult;
                if (Steamworks.SteamInventory.ConsumeItem(out consumeResult, items[i].m_itemId, items[i].m_unQuantity))
                {
                    destroyed++;
                    Steamworks.SteamInventory.DestroyResult(consumeResult);
                }
            }

            Debug.Log($"Cleared {destroyed}/{count} items from inventory.");
            Steamworks.SteamInventory.DestroyResult(result);
        });
    }
}

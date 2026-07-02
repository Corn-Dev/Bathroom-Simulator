using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Fishdex panel UI. Shows a grid of all fish species.
/// Uncaught fish show as silhouettes, caught fish show full color icons.
///
/// Toggle with Tab key. Pauses fishing input when open (cursor unlocks).
///
/// Setup: Create a Canvas with a Panel containing a GridLayoutGroup.
/// Each grid cell is an Image + TMP text child. This script spawns cells
/// from a prefab at runtime based on the fish species list.
/// </summary>
public class FishdexUI : MonoBehaviour
{
    [Header("Config")]
    [SerializeField] private KeyCode toggleKey = KeyCode.Tab;

    [Header("References")]
    [SerializeField] private GameObject fishdexPanel;
    [SerializeField] private Transform gridParent;
    [SerializeField] private GameObject fishEntryPrefab;
    [SerializeField] private TextMeshProUGUI counterText;

    private FishingController fishingController;

    private bool isOpen = false;
    public bool IsOpen => isOpen;
    private FishdexEntry[] entries;
    private MockFishingBackend backend;

    private struct FishdexEntry
    {
        public Image icon;
        public TextMeshProUGUI nameText;
        public FishSpeciesData data;
    }

    private void Start()
    {
        if (fishdexPanel != null)
            fishdexPanel.SetActive(false);

        if (fishingController == null)
            fishingController = FindFirstObjectByType<FishingController>();

        BuildGrid();
    }

    private void Update()
    {
        if (Input.GetKeyDown(toggleKey))
        {
            ToggleFishdex();
        }
    }

    private void ToggleFishdex()
    {
        isOpen = !isOpen;

        if (fishdexPanel != null)
            fishdexPanel.SetActive(isOpen);

        if (isOpen)
        {
            RefreshGrid();
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        else
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    private void BuildGrid()
    {
        if (gridParent == null || fishEntryPrefab == null) return;

        var db = FishDatabase.Instance;
        if (db == null || db.AllFish == null) return;

        var allFish = db.AllFish;
        entries = new FishdexEntry[allFish.Length];

        for (int i = 0; i < allFish.Length; i++)
        {
            GameObject cell = Instantiate(fishEntryPrefab, gridParent);
            Image icon = cell.GetComponentInChildren<Image>();
            TextMeshProUGUI text = cell.GetComponentInChildren<TextMeshProUGUI>();

            entries[i] = new FishdexEntry
            {
                icon = icon,
                nameText = text,
                data = allFish[i]
            };
        }

        RefreshGrid();
    }

    private void RefreshGrid()
    {
        if (entries == null) return;

        // Get backend fresh each time (player may have spawned after Start)
        if (fishingController == null)
            fishingController = FindFirstObjectByType<FishingController>();

        backend = (fishingController != null) ? fishingController.GetBackend() as MockFishingBackend : null;

        for (int i = 0; i < entries.Length; i++)
        {
            var entry = entries[i];
            bool caught = backend != null && backend.HasCaughtSpecies(entry.data.fishId);

            if (entry.icon != null)
            {
                if (caught && entry.data.icon != null)
                {
                    entry.icon.sprite = entry.data.icon;
                    entry.icon.color = Color.white;
                }
                else if (entry.data.silhouette != null)
                {
                    entry.icon.sprite = entry.data.silhouette;
                    entry.icon.color = Color.white;
                }
                else
                {
                    // No art — show a dark placeholder
                    entry.icon.sprite = null;
                    entry.icon.color = caught ? entry.data.rarityColor : new Color(0.15f, 0.15f, 0.15f, 1f);
                }
            }

            if (entry.nameText != null)
            {
                entry.nameText.text = caught ? entry.data.displayName : "???";
                entry.nameText.color = caught ? Color.white : Color.gray;
            }
        }

        // Update counter
        if (counterText != null)
        {
            int caughtCount = backend != null ? backend.GetCaughtCount() : 0;
            counterText.text = $"{caughtCount} / {entries.Length}";
        }
    }

    private MockFishingBackend GetBackendFromController()
    {
        if (fishingController == null) return null;
        return fishingController.GetBackend() as MockFishingBackend;
    }
}

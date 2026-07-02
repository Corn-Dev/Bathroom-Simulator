using UnityEngine;
using TMPro;
using UnityEngine.UI;

/// <summary>
/// Displays fishing state UI: bite indicator, reel bar with green zone,
/// catch progress, and fish reveal popup.
///
/// Requires a Canvas in the scene with the following children assigned:
///   - reelBarPanel: parent panel for the reel bar (shown only during Reeling)
///   - reelIndicator: small image that moves vertically inside the bar
///   - greenZoneImage: image representing the safe zone on the bar
///   - catchProgressFill: image with fill amount for the catch meter
///   - bitePrompt: text that flashes "Something's biting! Click!"
///   - resultPanel: panel shown on catch success/fail
///   - resultFishName: TMP text showing fish name
///   - resultRarity: TMP text showing rarity
///   - resultNewSpecies: GameObject that activates when it's a new Fishdex entry
///
/// All references are optional — the script gracefully handles missing refs
/// so you can build the UI incrementally.
/// </summary>
public class FishingUI : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private FishingController fishingController;

    [Header("Cast Power Bar")]
    [SerializeField] private GameObject castPowerPanel;
    [SerializeField] private Image castPowerFill;

    [Header("Reel Bar")]
    [SerializeField] private GameObject reelBarPanel;
    [SerializeField] private RectTransform reelIndicator;
    [SerializeField] private RectTransform reelBarArea;

    [Header("Prompts")]
    [SerializeField] private GameObject bitePrompt;
    [SerializeField] private TextMeshProUGUI stateText;

    [Header("Result Popup")]
    [SerializeField] private GameObject resultPanel;
    [SerializeField] private TextMeshProUGUI resultFishName;
    [SerializeField] private TextMeshProUGUI resultRarity;
    [SerializeField] private GameObject resultNewSpecies;
    [SerializeField] private Image resultFishIcon;

    private void Start()
    {
        // Hide everything initially — controller will be found when player spawns
        SetActive(castPowerPanel, false);
        SetActive(reelBarPanel, false);
        SetActive(bitePrompt, false);
        SetActive(resultPanel, false);
    }

    private void TryFindController()
    {
        if (fishingController != null) return;

        foreach (var fc in FindObjectsByType<FishingController>(FindObjectsSortMode.None))
        {
            if (fc.enabled)
            {
                fishingController = fc;
                fishingController.OnStateChanged += HandleStateChanged;
                fishingController.OnFishCaught += HandleFishCaught;
                fishingController.OnFishEscaped += HandleFishEscaped;
                break;
            }
        }
    }

    private void OnDestroy()
    {
        if (fishingController != null)
        {
            fishingController.OnStateChanged -= HandleStateChanged;
            fishingController.OnFishCaught -= HandleFishCaught;
            fishingController.OnFishEscaped -= HandleFishEscaped;
        }
    }

    private void Update()
    {
        TryFindController();
        if (fishingController == null) return;

        // Update cast power bar during charging
        if (fishingController.CurrentState == FishingState.Charging)
        {
            UpdateCastPowerBar();
        }

        // Update reel bar during reeling
        if (fishingController.CurrentState == FishingState.Reeling)
        {
            UpdateReelBar();
        }

        // Update state text
        if (stateText != null)
        {
            stateText.text = fishingController.CurrentState switch
            {
                FishingState.Idle => "",
                FishingState.Charging => "",
                FishingState.CastFlying => "",
                FishingState.WaitingForBite => "Waiting for a bite...",
                FishingState.Biting => "",
                FishingState.Reeling => "",
                FishingState.CatchSuccess => "",
                FishingState.CatchFailed => "It got away...",
                FishingState.HookedPlayer => "Hooked a player!",
                _ => ""
            };
        }
    }

    private void UpdateCastPowerBar()
    {
        if (castPowerFill != null)
            castPowerFill.fillAmount = fishingController.GetCastPower();
    }

    private void HandleStateChanged(FishingState newState)
    {
        SetActive(castPowerPanel, newState == FishingState.Charging);
        SetActive(reelBarPanel, newState == FishingState.Reeling);
        SetActive(bitePrompt, newState == FishingState.Biting);
        SetActive(resultPanel, newState == FishingState.CatchSuccess);
    }

    private void HandleFishCaught(CatchResult result)
    {
        SetActive(resultPanel, true);

        if (resultFishName != null)
            resultFishName.text = result.fish.displayName;

        if (resultRarity != null)
        {
            resultRarity.text = result.fish.rarity.ToString();
            resultRarity.color = result.fish.rarityColor;
        }

        if (resultNewSpecies != null)
            resultNewSpecies.SetActive(result.isNewSpecies);

        if (resultFishIcon != null && result.fish.icon != null)
        {
            resultFishIcon.sprite = result.fish.icon;
            resultFishIcon.enabled = true;
        }
        else if (resultFishIcon != null)
        {
            resultFishIcon.enabled = false;
        }
    }

    private void HandleFishEscaped()
    {
        SetActive(reelBarPanel, false);
        SetActive(bitePrompt, false);
    }

    private void UpdateReelBar()
    {
        if (reelIndicator == null || reelBarArea == null) return;

        float position = fishingController.GetReelPosition();
        float barHeight = reelBarArea.rect.height;

        // Indicator is anchored to bottom of bar (Y=0), so position 0 = bottom, 1 = top
        Vector2 pos = reelIndicator.anchoredPosition;
        pos.y = position * barHeight;
        reelIndicator.anchoredPosition = pos;
    }

    private void SetActive(Object obj, bool active)
    {
        if (obj == null) return;
        if (obj is GameObject go) go.SetActive(active);
    }
}

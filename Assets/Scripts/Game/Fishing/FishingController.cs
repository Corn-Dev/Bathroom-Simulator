using System;
using UnityEngine;

/// <summary>
/// Controls the fishing state machine for the local player.
/// Transitions: Idle → Casting → WaitingForBite → Biting → Reeling → CatchSuccess/CatchFailed → Idle
///
/// This script handles state transitions and timing. The actual reel-in minigame
/// is delegated to a separate component (IReelMinigame) so it can be swapped/tuned independently.
///
/// Attach to the Player prefab. Only runs on the owner (checks IsOwner equivalent via enabled flag).
/// </summary>
public class FishingController : MonoBehaviour
{
    [Header("Config")]
    [SerializeField] private FishingRodData equippedRod;
    [SerializeField] private FishSpeciesData[] availableFish;    // All fish that can be caught in this area
    [SerializeField] private KeyCode castKey = KeyCode.Mouse0;
    [SerializeField] private KeyCode reelKey = KeyCode.Mouse0;

    [Header("Timing")]
    [SerializeField] private float castDuration = 0.5f;
    [SerializeField] private float biteWindowDuration = 2f;     // How long the player has to react to a bite
    [SerializeField] private float resultDisplayDuration = 2f;  // How long to show catch result before returning to idle

    // Current state
    public FishingState CurrentState { get; private set; } = FishingState.Idle;
    public FishSpeciesData CurrentFish { get; private set; }

    // Events for UI/audio/animation to hook into
    public event Action<FishingState> OnStateChanged;
    public event Action<FishSpeciesData> OnFishCaught;
    public event Action OnFishEscaped;

    private IFishRollService fishRollService;
    private float stateTimer;
    private float biteTime;        // Random time until fish bites (set on cast)
    private float reelProgress;    // 0 to 1 — progress toward catching
    private float reelDrain;       // How fast reel progress drains if not reeling

    private void Awake()
    {
        fishRollService = new LocalFishRollService(availableFish);
    }

    private void Update()
    {
        stateTimer += Time.deltaTime;

        switch (CurrentState)
        {
            case FishingState.Idle:
                HandleIdle();
                break;
            case FishingState.Casting:
                HandleCasting();
                break;
            case FishingState.WaitingForBite:
                HandleWaitingForBite();
                break;
            case FishingState.Biting:
                HandleBiting();
                break;
            case FishingState.Reeling:
                HandleReeling();
                break;
            case FishingState.CatchSuccess:
            case FishingState.CatchFailed:
                HandleResult();
                break;
        }
    }

    private void TransitionTo(FishingState newState)
    {
        CurrentState = newState;
        stateTimer = 0f;
        OnStateChanged?.Invoke(newState);
    }

    // --- State Handlers ---

    private void HandleIdle()
    {
        if (Input.GetKeyDown(castKey))
        {
            TransitionTo(FishingState.Casting);
        }
    }

    private void HandleCasting()
    {
        // Cast animation plays for castDuration, then line enters water
        if (stateTimer >= castDuration)
        {
            // Roll which fish will bite
            var context = new FishingContext { rod = equippedRod };
            CurrentFish = fishRollService.RollFish(context);

            if (CurrentFish == null)
            {
                TransitionTo(FishingState.Idle);
                return;
            }

            // Determine how long until the fish bites
            float minBite = CurrentFish.biteTimeRange.x;
            float maxBite = CurrentFish.biteTimeRange.y;
            if (equippedRod != null)
            {
                minBite *= equippedRod.biteTimeMultiplier;
                maxBite *= equippedRod.biteTimeMultiplier;
            }
            biteTime = UnityEngine.Random.Range(minBite, maxBite);

            TransitionTo(FishingState.WaitingForBite);
        }
    }

    private void HandleWaitingForBite()
    {
        if (stateTimer >= biteTime)
        {
            TransitionTo(FishingState.Biting);
        }

        // Allow canceling the cast
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            TransitionTo(FishingState.Idle);
        }
    }

    private void HandleBiting()
    {
        // Fish is biting! Player must press reel key within the window
        if (Input.GetKeyDown(reelKey))
        {
            // Start reeling
            reelProgress = 0f;
            reelDrain = CurrentFish.reelDifficulty * 0.5f; // Harder fish drain faster
            TransitionTo(FishingState.Reeling);
            return;
        }

        // Missed the window — fish escapes
        if (stateTimer >= biteWindowDuration)
        {
            OnFishEscaped?.Invoke();
            TransitionTo(FishingState.CatchFailed);
        }
    }

    private void HandleReeling()
    {
        // Simple reel mechanic: hold/press reel key to fill progress bar.
        // Progress drains when not pressing. Fill to 1.0 to catch.
        float reelSpeed = equippedRod != null ? equippedRod.reelSpeedMultiplier : 1f;

        if (Input.GetKey(reelKey))
        {
            reelProgress += reelSpeed * Time.deltaTime * 0.8f;
        }
        else
        {
            reelProgress -= reelDrain * Time.deltaTime;
        }

        reelProgress = Mathf.Clamp01(reelProgress);

        // Caught it!
        if (reelProgress >= 1f)
        {
            OnFishCaught?.Invoke(CurrentFish);
            TransitionTo(FishingState.CatchSuccess);
            return;
        }

        // Fish escaped — progress hit zero
        if (reelProgress <= 0f && stateTimer > 0.5f) // small grace period
        {
            OnFishEscaped?.Invoke();
            TransitionTo(FishingState.CatchFailed);
        }
    }

    private void HandleResult()
    {
        // Show result for a moment, then return to idle
        if (stateTimer >= resultDisplayDuration)
        {
            CurrentFish = null;
            TransitionTo(FishingState.Idle);
        }
    }

    /// <summary>
    /// Get current reel progress (0-1) for UI display.
    /// </summary>
    public float GetReelProgress() => reelProgress;
}

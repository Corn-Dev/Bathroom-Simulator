using System;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Controls the fishing state machine for the local player.
/// Flow: Idle → Charging → CastFlying → WaitingForBite → Biting → Reeling → CatchSuccess/CatchFailed
///
/// Handles the power bar minigame, bobber spawning, bite timing, and reel mechanic.
/// Delegates fish determination and granting to IFishingBackend.
/// Landing detection supports water (fishing), players (hooking), and ground (fail).
/// </summary>
public class FishingController : NetworkBehaviour
{
    [Header("Config")]
    [SerializeField] private FishingRodData equippedRod;
    [SerializeField] private KeyCode castKey = KeyCode.Mouse0;
    [SerializeField] private KeyCode reelKey = KeyCode.Mouse0;
    [SerializeField] private KeyCode cancelKey = KeyCode.Escape;

    [Header("Cast")]
    [SerializeField] private Transform rodTip;
    [SerializeField] private GameObject bobberPrefab;
    [SerializeField] private LayerMask waterLayer;
    [SerializeField] private LayerMask playerLayer;
    [SerializeField] private float minCastDistance = 3f;  // Minimum distance even at lowest power

    [Header("Timing")]
    [SerializeField] private float biteWindowDuration = 5f;
    [SerializeField] private float resultDisplayDuration = 2f;
    [SerializeField] private Vector2 biteTimeRange = new Vector2(3f, 8f);

    [Header("Reel Mechanic")]
    [Tooltip("Base upward acceleration after 1 second of holding.")]
    [SerializeField] private float reelBaseAcceleration = 1.5f;
    [Tooltip("How quickly the reel force ramps up the longer you hold (exponential feel).")]
    [SerializeField] private float reelRampRate = 2.0f;
    [Tooltip("Base downward gravity (fish escape pull).")]
    [SerializeField] private float reelGravity = 0.6f;
    [Tooltip("Max velocity in either direction.")]
    [SerializeField] private float reelMaxVelocity = 1.2f;
    [Tooltip("Velocity drag — bleeds velocity over time for smoother feel.")]
    [SerializeField] private float reelDrag = 1.0f;
    [Tooltip("How fast catch progress fills while holding (progress per second).")]
    [SerializeField] private float catchFillRate = 0.15f;
    [Tooltip("How much catch progress is needed to land the fish.")]
    [SerializeField] private float catchTarget = 1f;

    // State
    public FishingState CurrentState { get; private set; } = FishingState.Idle;
    public CatchResult? LastCatchResult { get; private set; }

    // Events
    public event Action<FishingState> OnStateChanged;
    public event Action<CatchResult> OnFishCaught;
    public event Action OnFishEscaped;
    public event Action<string> OnBackendError;
    public event Action<CastHitResult> OnCastLanded;

    // Backend
    private IFishingBackend backend;

    // Internal state
    private float stateTimer;
    private float castPower;
    private bool castBarRising;
    private Vector3 castDirection;
    private float biteTime;
    private float reelPosition;
    private float reelVelocity;
    private float holdDuration;
    private float catchProgress;
    private BobberController activeBobber;

    private void Awake()
    {
        var db = FishDatabase.Instance;
        backend = new MockFishingBackend(db != null ? db.AllFish : new FishSpeciesData[0]);
    }

    public override void OnNetworkSpawn()
    {
        if (!IsOwner)
        {
            enabled = false;
            return;
        }
    }

    private void Update()
    {
        stateTimer += Time.deltaTime;

        switch (CurrentState)
        {
            case FishingState.Idle:
                HandleIdle();
                break;
            case FishingState.Charging:
                HandleCharging();
                break;
            case FishingState.CastFlying:
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
            case FishingState.HookedPlayer:
                break;
        }
    }

    private void TransitionTo(FishingState newState)
    {
        Debug.Log($"[Fishing] {CurrentState} → {newState}");
        CurrentState = newState;
        stateTimer = 0f;
        OnStateChanged?.Invoke(newState);
    }

    // --- State Handlers ---

    private void HandleIdle()
    {
        if (Input.GetKeyDown(castKey))
        {
            // Lock direction at cast start (horizontal only — ignore pitch)
            Transform cam = Camera.main.transform;
            castDirection = new Vector3(cam.forward.x, 0f, cam.forward.z).normalized;

            castPower = 0f;
            castBarRising = true;
            TransitionTo(FishingState.Charging);
        }
    }

    private void HandleCharging()
    {
        float barSpeed = equippedRod != null ? equippedRod.castBarSpeed : 1.2f;

        // Bar bounces up then down
        if (castBarRising)
        {
            castPower += barSpeed * Time.deltaTime;
            if (castPower >= 1f)
            {
                castPower = 1f;
                castBarRising = false;
            }
        }
        else
        {
            castPower -= barSpeed * Time.deltaTime;
            if (castPower <= 0f)
            {
                // Hit bottom — cancel cast
                castPower = 0f;
                TransitionTo(FishingState.Idle);
                return;
            }
        }

        // Release to cast at current power
        if (Input.GetKeyUp(castKey))
        {
            ExecuteCast();
        }

        // Manual cancel
        if (Input.GetKeyDown(cancelKey))
        {
            TransitionTo(FishingState.Idle);
        }
    }

    private void ExecuteCast()
    {
        float maxRange = equippedRod != null ? equippedRod.castRange : 10f;
        float castDistance = Mathf.Lerp(minCastDistance, maxRange, castPower);

        // Calculate target point
        Vector3 origin = rodTip != null ? rodTip.position : transform.position + Vector3.up;
        Vector3 targetPoint = transform.position + castDirection * castDistance;

        // Raycast down from the target to find the actual surface level
        if (Physics.Raycast(targetPoint + Vector3.up * 10f, Vector3.down, out RaycastHit surfaceHit, 30f))
        {
            targetPoint.y = surfaceHit.point.y;
        }
        else
        {
            targetPoint.y = transform.position.y;
        }

        // Spawn bobber as a NetworkObject
        if (bobberPrefab != null)
        {
            SpawnBobberServerRpc(origin, targetPoint);
        }

        TransitionTo(FishingState.CastFlying);
    }

    [ServerRpc]
    private void SpawnBobberServerRpc(Vector3 origin, Vector3 targetPoint)
    {
        GameObject bobberObj = Instantiate(bobberPrefab, origin, Quaternion.identity);
        var netObj = bobberObj.GetComponent<NetworkObject>();
        netObj.SpawnWithOwnership(OwnerClientId);

        // Tell the owner to initialize the bobber
        InitializeBobberClientRpc(bobberObj.GetComponent<NetworkObject>().NetworkObjectId, origin, targetPoint);
    }

    [ClientRpc]
    private void InitializeBobberClientRpc(ulong bobberNetId, Vector3 origin, Vector3 targetPoint)
    {
        // Find the spawned bobber by network ID
        if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(bobberNetId, out var netObj))
        {
            var bobber = netObj.GetComponent<BobberController>();
            if (bobber != null)
            {
                activeBobber = bobber;

                // Only the owner runs the flight logic
                if (IsOwner)
                {
                    activeBobber.OnLanded += HandleBobberLanded;
                    activeBobber.Launch(origin, targetPoint, waterLayer, playerLayer);
                }
            }
        }
    }

    private void HandleBobberLanded(CastHitResult result)
    {
        if (activeBobber != null)
            activeBobber.OnLanded -= HandleBobberLanded;

        OnCastLanded?.Invoke(result);

        switch (result.type)
        {
            case CastHitType.Water:
                // Determine bite time
                float minBite = biteTimeRange.x;
                float maxBite = biteTimeRange.y;
                if (equippedRod != null)
                {
                    minBite *= equippedRod.biteTimeMultiplier;
                    maxBite *= equippedRod.biteTimeMultiplier;
                }
                biteTime = UnityEngine.Random.Range(minBite, maxBite);
                TransitionTo(FishingState.WaitingForBite);
                break;

            case CastHitType.Player:
                TransitionTo(FishingState.HookedPlayer);
                break;

            case CastHitType.Ground:
            case CastHitType.Nothing:
            default:
                // Cast missed water — bobber stays, player must right-click to reel back
                TransitionTo(FishingState.CatchFailed);
                break;
        }
    }

    private void HandleWaitingForBite()
    {
        if (stateTimer >= biteTime)
        {
            if (activeBobber != null)
                activeBobber.TriggerBiteJerk();

            TransitionTo(FishingState.Biting);
        }

        if (Input.GetKeyDown(cancelKey))
        {
            DestroyBobber();
            TransitionTo(FishingState.Idle);
        }
    }

    private void HandleBiting()
    {
        if (Input.GetKeyDown(reelKey))
        {
            reelPosition = 0.5f;
            reelVelocity = -0.3f; // Start with downward momentum — fish is already pulling
            holdDuration = 0f;
            catchProgress = 0f;
            TransitionTo(FishingState.Reeling);
            return;
        }

        if (stateTimer >= biteWindowDuration)
        {
            OnFishEscaped?.Invoke();
            DestroyBobber();
            TransitionTo(FishingState.CatchFailed);
        }
    }

    private void HandleReeling()
    {
        float dt = Time.deltaTime;

        // --- Fish fight: gravity varies over time ---
        float fightIntensity = 0.4f; // TODO: from fish species reelDifficulty
        float wave1 = Mathf.Sin(Time.time * 1.3f) * 0.5f + 0.5f;   // 0-1 slow cycle
        float wave2 = Mathf.Sin(Time.time * 3.7f) * 0.3f + 0.5f;   // 0-1 medium cycle
        float gravityMultiplier = 0.5f + (wave1 * wave2 * fightIntensity * 3f);
        float currentGravity = reelGravity * gravityMultiplier;

        // --- Player input ---
        if (Input.GetKey(reelKey))
        {
            // Hold duration ramps up — longer hold = stronger upward force
            holdDuration += dt;

            // Ramped acceleration: instant kick + builds with hold time
            float instantKick = reelBaseAcceleration * 0.3f; // 30% force immediately on hold
            float rampedPortion = reelBaseAcceleration * 0.7f * (1f - Mathf.Exp(-reelRampRate * holdDuration));
            float rampedForce = instantKick + rampedPortion;
            reelVelocity += rampedForce * dt;

            // Progress increases while holding
            catchProgress += catchFillRate * dt;
        }
        else
        {
            // Reset hold duration — tapping resets the ramp
            holdDuration = 0f;

            // Gravity pulls down (accelerating)
            reelVelocity -= currentGravity * dt;
        }

        // --- Apply drag ---
        reelVelocity -= reelVelocity * reelDrag * dt;

        // --- Clamp velocity ---
        reelVelocity = Mathf.Clamp(reelVelocity, -reelMaxVelocity, reelMaxVelocity);

        // --- Update position ---
        reelPosition += reelVelocity * dt;

        // --- Fail states ---
        if (reelPosition >= 1f)
        {
            reelPosition = 1f;
            reelVelocity = 0f;
            holdDuration = 0f;
            OnFishEscaped?.Invoke();
            DestroyBobber();
            TransitionTo(FishingState.CatchFailed);
            return;
        }

        if (reelPosition <= 0f)
        {
            reelPosition = 0f;
            reelVelocity = 0f;
            holdDuration = 0f;
            OnFishEscaped?.Invoke();
            DestroyBobber();
            TransitionTo(FishingState.CatchFailed);
            return;
        }

        // --- Success ---
        if (catchProgress >= catchTarget)
        {
            holdDuration = 0f;
            DestroyBobber();
            RequestCatchFromBackend();
        }
    }

    private void RequestCatchFromBackend()
    {
        var context = new FishingContext { rod = equippedRod };

        backend.RequestCatch(context,
            onResult: result =>
            {
                LastCatchResult = result;
                Debug.Log($"[Fishing] Caught: {result.fish.displayName} ({result.fish.rarity}) — {result.size}kg{(result.isNewSpecies ? " — NEW SPECIES!" : "")}{(result.isPersonalBest ? " — NEW PB!" : "")}");
                OnFishCaught?.Invoke(result);
                TransitionTo(FishingState.CatchSuccess);
            },
            onError: error =>
            {
                Debug.LogWarning($"[Fishing] Backend error: {error}");
                OnBackendError?.Invoke(error);
                TransitionTo(FishingState.CatchFailed);
            }
        );
    }

    private void HandleResult()
    {
        // On success: show result for a duration then auto-return
        if (CurrentState == FishingState.CatchSuccess)
        {
            if (stateTimer >= resultDisplayDuration)
            {
                LastCatchResult = null;
                DestroyBobber();
                TransitionTo(FishingState.Idle);
            }
            return;
        }

        // On fail: bobber stays out, player must click to reel back
        if (Input.GetKeyDown(castKey) || Input.GetKeyDown(cancelKey))
        {
            DestroyBobber();
            TransitionTo(FishingState.Idle);
        }
    }

    private void DestroyBobber()
    {
        if (activeBobber != null)
        {
            if (IsOwner)
                DespawnBobberServerRpc(activeBobber.GetComponent<NetworkObject>().NetworkObjectId);
            activeBobber = null;
        }
    }

    [ServerRpc]
    private void DespawnBobberServerRpc(ulong bobberNetId)
    {
        if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(bobberNetId, out var netObj))
        {
            netObj.Despawn();
        }
    }

    // --- Public accessors for UI ---

    public float GetCastPower() => castPower;
    public float GetReelPosition() => reelPosition;
    public float GetCatchProgress() => catchProgress;
    public float GetCatchTarget() => catchTarget;

    public void SetBackend(IFishingBackend newBackend) { backend = newBackend; }
    public IFishingBackend GetBackend() => backend;
    public Transform GetRodTip() => rodTip;
}

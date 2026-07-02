using System;
using UnityEngine;

/// <summary>
/// Controls the bobber's arc flight, landing detection, and idle bob animation.
/// Spawned by FishingController when a cast is released. Destroyed on reel-in or cancel.
///
/// The bobber simulates a simple parabolic arc from the rod tip to the target point.
/// On landing, it reports what it hit via the OnLanded callback.
/// While waiting for a bite, it bobs gently on the water surface.
/// On bite, it jerks downward as a visual cue.
/// </summary>
public class BobberController : MonoBehaviour
{
    [Header("Flight")]
    [SerializeField] private float arcHeight = 3f;
    [SerializeField] private float flightDuration = 0.8f;

    [Header("Idle Bob")]
    [SerializeField] private float bobAmplitude = 0.05f;
    [SerializeField] private float bobFrequency = 1.5f;

    [Header("Bite Jerk")]
    [SerializeField] private float biteJerkDepth = 0.3f;
    [SerializeField] private float biteJerkDuration = 0.15f;

    [Header("Detection")]
    private LayerMask waterLayer;
    private LayerMask playerLayer;

    /// <summary>Called when the bobber lands after its arc flight.</summary>
    public event Action<CastHitResult> OnLanded;

    private Vector3 startPoint;
    private Vector3 targetPoint;
    private float flightTimer;
    private bool isFlying;
    private bool isIdleBobbing;
    private float landedY;
    private float biteTimer;
    private bool isBiteJerking;

    /// <summary>
    /// Launch the bobber from the rod tip toward a target point.
    /// </summary>
    public void Launch(Vector3 from, Vector3 to, LayerMask water, LayerMask player)
    {
        startPoint = from;
        targetPoint = to;
        waterLayer = water;
        playerLayer = player;
        transform.position = from;
        flightTimer = 0f;
        isFlying = true;
        isIdleBobbing = false;
        isBiteJerking = false;

        // Disable own collider so raycasts don't hit ourselves
        var col = GetComponent<Collider>();
        if (col != null) col.enabled = false;
    }

    private void Update()
    {
        if (isFlying)
        {
            UpdateFlight();
        }
        else if (isBiteJerking)
        {
            UpdateBiteJerk();
        }
        else if (isIdleBobbing)
        {
            UpdateIdleBob();
        }
    }

    private void UpdateFlight()
    {
        flightTimer += Time.deltaTime;
        float t = Mathf.Clamp01(flightTimer / flightDuration);

        // Parabolic arc: lerp XZ, arc Y
        Vector3 pos = Vector3.Lerp(startPoint, targetPoint, t);
        float arc = arcHeight * 4f * t * (1f - t);
        pos.y += arc;

        // Check if we've crossed below a surface during this frame
        Vector3 previousPos = transform.position;
        transform.position = pos;

        // Raycast from previous position to current to detect surface crossing
        Vector3 moveDir = (pos - previousPos).normalized;
        float moveDist = Vector3.Distance(previousPos, pos);

        if (moveDist > 0.01f && Physics.Raycast(previousPos, moveDir, out RaycastHit hit, moveDist + 0.1f))
        {
            // Hit something during the arc — land here
            transform.position = hit.point;
            OnFlightComplete();
            return;
        }

        // Reached end of arc without hitting anything — do final detection
        if (t >= 1f)
        {
            OnFlightComplete();
        }
    }

    private void OnFlightComplete()
    {
        isFlying = false;

        CastHitResult result = DetectLanding();

        // Always place bobber at the detected surface
        if (result.type != CastHitType.Nothing)
        {
            Vector3 pos = result.hitPoint;
            if (result.type == CastHitType.Water)
                pos.y += 0.03f; // Half-submerged for water
            transform.position = pos;
        }

        if (result.type == CastHitType.Water)
        {
            landedY = transform.position.y;
            isIdleBobbing = true;
        }

        OnLanded?.Invoke(result);
    }

    private CastHitResult DetectLanding()
    {
        // Single raycast downward, then classify by layer
        Vector3 origin = transform.position + Vector3.up * 1f;

        if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 20f))
        {
            int hitLayer = hit.collider.gameObject.layer;

            // Check if it's on the water layer
            if (hitLayer == LayerMask.NameToLayer("Water"))
            {
                return new CastHitResult
                {
                    type = CastHitType.Water,
                    hitPoint = hit.point,
                    hitObject = hit.collider.gameObject
                };
            }

            // Check if it's on the player layer
            if (((1 << hitLayer) & playerLayer) != 0)
            {
                return new CastHitResult
                {
                    type = CastHitType.Player,
                    hitPoint = hit.point,
                    hitObject = hit.collider.gameObject
                };
            }

            // Anything else is ground
            return new CastHitResult
            {
                type = CastHitType.Ground,
                hitPoint = hit.point,
                hitObject = hit.collider.gameObject
            };
        }

        Debug.Log($"[Bobber] DetectLanding hit NOTHING from {origin}");
        return new CastHitResult
        {
            type = CastHitType.Nothing,
            hitPoint = transform.position,
            hitObject = null
        };
    }

    private void UpdateIdleBob()
    {
        Vector3 pos = transform.position;
        pos.y = landedY + Mathf.Sin(Time.time * bobFrequency * Mathf.PI * 2f) * bobAmplitude;
        transform.position = pos;
    }

    /// <summary>
    /// Call this when a fish bites — jerks the bobber down as a visual cue.
    /// </summary>
    public void TriggerBiteJerk()
    {
        isBiteJerking = true;
        isIdleBobbing = false;
        biteTimer = 0f;
    }

    private void UpdateBiteJerk()
    {
        biteTimer += Time.deltaTime;
        float t = biteTimer / biteJerkDuration;

        if (t >= 1f)
        {
            // Return to bob position after jerk
            isBiteJerking = false;
            isIdleBobbing = true;
            return;
        }

        // Quick down then back up
        float jerk = Mathf.Sin(t * Mathf.PI) * biteJerkDepth;
        Vector3 pos = transform.position;
        pos.y = landedY - jerk;
        transform.position = pos;
    }
}

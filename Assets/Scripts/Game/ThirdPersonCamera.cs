using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Third-person orbit camera that follows the local player.
/// Handles mouse-look orbit, distance, and collision so the camera
/// doesn't clip through terrain or walls.
///
/// Attach to the Camera child of the Player prefab.
/// The camera should be a direct child of the player root (not nested further).
///
/// Prefab setup:
///   - Camera child at any position (script overrides it each frame)
///   - Assign 'followTarget' to the player root (or a point slightly above center)
///   - Or leave null and it will auto-use the parent transform
/// </summary>
public class ThirdPersonCamera : MonoBehaviour
{
    [Header("Target")]
    [Tooltip("Point the camera orbits around. If null, uses parent transform.")]
    [SerializeField] private Transform followTarget;
    [Tooltip("Vertical offset from the follow target (eye height).")]
    [SerializeField] private float targetHeightOffset = 1.2f;

    [Header("Orbit")]
    [SerializeField] private float defaultDistance = 5f;
    [SerializeField] private float minDistance = 1.5f;
    [SerializeField] private float maxDistance = 10f;
    [SerializeField] private float mouseSensitivity = 2f;
    [SerializeField] private float scrollSensitivity = 2f;
    [SerializeField] private float minPitch = -20f;
    [SerializeField] private float maxPitch = 70f;

    [Header("Collision")]
    [Tooltip("Layer mask for camera collision (terrain, walls, etc).")]
    [SerializeField] private LayerMask collisionLayers = ~0;
    [SerializeField] private float collisionRadius = 0.3f;
    [SerializeField] private float collisionSnapSpeed = 10f;

    [Header("Smoothing")]
    [SerializeField] private float orbitSmoothTime = 0.05f;

    // State
    private float yaw;
    private float pitch = 20f;
    private float currentDistance;
    private float desiredDistance;
    private Vector3 smoothVelocity;

    /// <summary>Current yaw angle — PlayerMovement reads this for movement-relative input.</summary>
    public float Yaw => yaw;

    /// <summary>Current pitch angle.</summary>
    public float Pitch => pitch;

    private void Start()
    {
        if (followTarget == null)
            followTarget = transform.parent;

        currentDistance = defaultDistance;
        desiredDistance = defaultDistance;

        // Seed yaw from player's current facing
        if (followTarget != null)
            yaw = followTarget.eulerAngles.y;
    }

    private void LateUpdate()
    {
        if (followTarget == null) return;

        HandleInput();
        UpdatePosition();
    }

    private void HandleInput()
    {
        // Only process input when cursor is locked (gameplay mode)
        if (Cursor.lockState != CursorLockMode.Locked) return;

        float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity;
        float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity;
        float scroll = Input.GetAxis("Mouse ScrollWheel") * scrollSensitivity;

        yaw += mouseX;
        pitch -= mouseY;
        pitch = Mathf.Clamp(pitch, minPitch, maxPitch);

        desiredDistance = Mathf.Clamp(desiredDistance - scroll, minDistance, maxDistance);
    }

    private void UpdatePosition()
    {
        Vector3 targetPoint = followTarget.position + Vector3.up * targetHeightOffset;

        // Calculate desired camera position from orbit angles
        Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 desiredPosition = targetPoint - rotation * Vector3.forward * desiredDistance;

        // Collision check: spherecast from target toward desired position
        float actualDistance = desiredDistance;
        Vector3 direction = (desiredPosition - targetPoint).normalized;

        if (Physics.SphereCast(targetPoint, collisionRadius, direction, out RaycastHit hit,
            desiredDistance, collisionLayers, QueryTriggerInteraction.Ignore))
        {
            actualDistance = Mathf.Max(hit.distance - collisionRadius, minDistance * 0.5f);
        }

        // Smoothly adjust distance when collision pulls camera closer
        currentDistance = Mathf.Lerp(currentDistance, actualDistance, collisionSnapSpeed * Time.deltaTime);

        // Final position
        Vector3 finalPosition = targetPoint - rotation * Vector3.forward * currentDistance;

        transform.position = finalPosition;
        transform.rotation = rotation;
    }

    /// <summary>
    /// Set the yaw externally (e.g., when respawning or teleporting).
    /// </summary>
    public void SetYaw(float newYaw)
    {
        yaw = newYaw;
    }
}

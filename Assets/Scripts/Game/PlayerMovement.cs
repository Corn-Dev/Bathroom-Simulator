using Unity.Netcode;
using UnityEngine;

/// <summary>
/// First-person player movement for the local owner.
/// Drives the Rigidbody directly via NetworkMovementSync for responsive,
/// owner-authoritative feel. Non-owners are disabled — their position and
/// rotation come from NetworkMovementSync's network broadcasts.
///
/// Prefab requirements:
///   - Rigidbody: useGravity=true, isKinematic=false, interpolate=Interpolate
///   - Freeze Rotation X and Z (Y left free for yaw)
///   - CapsuleCollider on root
///   - Camera child GameObject assigned to cameraTransform
///   - NetworkMovementSync on same GameObject
/// </summary>
[RequireComponent(typeof(NetworkMovementSync))]
public class PlayerMovement : NetworkBehaviour
{
    [Header("Movement")]
    [SerializeField] private float moveSpeed = 5f;
    [SerializeField] private float jumpForce = 5f;
    [Tooltip("Deceleration applied in-air to horizontal movement. Higher = snappier air control.")]
    [SerializeField] private float airDrag = 1f;

    [Header("Look")]
    [SerializeField] private float mouseSensitivity = 2f;
    [SerializeField] private float maxPitchAngle = 80f;

    [Header("Ground Check")]
    [SerializeField] private float groundCheckDistance = 0.1f;
    [SerializeField] private LayerMask groundLayer;

    [Header("References")]
    [SerializeField] private Transform cameraTransform;

    private NetworkMovementSync networkSync;
    private INetworkSpawnPolicy spawnPolicy;

    private float yawAngle;
    private float pitchAngle;
    private bool isGrounded;

    private void Awake()
    {
        networkSync = GetComponent<NetworkMovementSync>();
        spawnPolicy = GetComponent<INetworkSpawnPolicy>();
    }

    public override void OnNetworkSpawn()
    {
        if (!IsOwner)
        {
            enabled = false;
            return;
        }

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        // Seed yaw from spawn rotation so the first mouse input doesn't snap
        yawAngle = transform.eulerAngles.y;
    }

    public override void OnNetworkDespawn()
    {
        if (IsOwner)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }

    private void Update()
    {
        if (!IsOwner) return;
        if (spawnPolicy != null && !spawnPolicy.CanEnableGameplayForLocalPlayer()) return;

        CheckGrounded();
        HandleLook();
        HandleJump();
        ApplyDrag();
    }

    private void FixedUpdate()
    {
        if (!IsOwner) return;
        if (spawnPolicy != null && !spawnPolicy.CanEnableGameplayForLocalPlayer()) return;

        HandleMovement();
    }

    private void HandleMovement()
    {
        float h = Input.GetAxisRaw("Horizontal");
        float v = Input.GetAxisRaw("Vertical");
        Vector3 input = new Vector3(h, 0f, v).normalized;

        if (input.sqrMagnitude > 0.01f)
        {
            // Accelerate toward target velocity
            Vector3 moveDir = transform.TransformDirection(input);
            Vector3 currentVel = networkSync.GetVelocity();
            Vector3 targetHorizontal = moveDir * moveSpeed;
            Vector3 velocityDelta = new Vector3(
                targetHorizontal.x - currentVel.x,
                0f,
                targetHorizontal.z - currentVel.z);
            networkSync.AddMovementForce(velocityDelta, ForceMode.VelocityChange);
        }
        else if (isGrounded && !networkSync.IsUnderExternalForce)
        {
            // No input and on the ground — kill horizontal velocity immediately.
            // Skip if an external force (knockback, taser, etc.) is active so
            // physics effects play out before movement resumes normal control.
            Vector3 vel = networkSync.GetVelocity();
            networkSync.SetHorizontalVelocity(Vector3.zero, vel.y);
        }
    }

    private void HandleLook()
    {
        float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity;
        float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity;

        // Yaw: rotate the whole body
        yawAngle += mouseX;
        networkSync.SetRotation(Quaternion.Euler(0f, yawAngle, 0f));

        // Pitch: tilt the camera only (local, not synced)
        pitchAngle -= mouseY;
        pitchAngle = Mathf.Clamp(pitchAngle, -maxPitchAngle, maxPitchAngle);
        if (cameraTransform != null)
            cameraTransform.localRotation = Quaternion.Euler(pitchAngle, 0f, 0f);
    }

    private void HandleJump()
    {
        if (isGrounded && Input.GetButtonDown("Jump"))
            networkSync.AddJumpForce(jumpForce);
    }

    private void CheckGrounded()
    {
        // Cast from slightly above the capsule bottom to catch slight floor gaps
        CapsuleCollider col = GetComponent<CapsuleCollider>();
        Vector3 origin = transform.position + Vector3.down * (col.height / 2f - col.radius);
        isGrounded = Physics.SphereCast(origin, col.radius * 0.9f, Vector3.down,
            out _, groundCheckDistance, groundLayer);
    }

    private void ApplyDrag()
    {
        // In-air drag only — grounded stopping is handled in HandleMovement
        if (isGrounded) return;
        Vector3 vel = networkSync.GetVelocity();
        Vector3 horizontalVel = new Vector3(vel.x, 0f, vel.z);
        if (horizontalVel.magnitude > 0.1f)
            networkSync.AddMovementForce(-horizontalVel * airDrag, ForceMode.Acceleration);
    }
}

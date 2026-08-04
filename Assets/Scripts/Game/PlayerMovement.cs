using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Third-person player movement for the local owner.
/// Movement direction is relative to the orbit camera's yaw.
/// The character rotates to face the direction it's moving.
///
/// Drives the Rigidbody directly via NetworkMovementSync for responsive,
/// owner-authoritative feel. Non-owners are disabled — their position and
/// rotation come from NetworkMovementSync's network broadcasts.
///
/// Prefab requirements:
///   - Rigidbody: useGravity=true, isKinematic=false, interpolate=Interpolate
///   - Freeze Rotation X and Z (Y left free for yaw)
///   - CapsuleCollider on root
///   - ThirdPersonCamera on Camera child GameObject
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
    [Tooltip("How fast the character rotates to face movement direction (degrees/sec).")]
    [SerializeField] private float rotationSpeed = 720f;

    [Header("Ground Check")]
    [SerializeField] private float groundCheckDistance = 0.1f;
    [SerializeField] private LayerMask groundLayer;

    [Header("References")]
    [SerializeField] private Transform cameraTransform;

    private NetworkMovementSync networkSync;
    private INetworkSpawnPolicy spawnPolicy;
    private ThirdPersonCamera orbitCamera;

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

        // Find the orbit camera on the camera child
        if (cameraTransform != null)
            orbitCamera = cameraTransform.GetComponent<ThirdPersonCamera>();
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
            // Get camera-relative direction
            float cameraYaw = orbitCamera != null ? orbitCamera.Yaw : transform.eulerAngles.y;
            Quaternion cameraRotation = Quaternion.Euler(0f, cameraYaw, 0f);
            Vector3 moveDir = cameraRotation * input;

            // Rotate character to face movement direction
            Quaternion targetRotation = Quaternion.LookRotation(moveDir, Vector3.up);
            Quaternion smoothedRotation = Quaternion.RotateTowards(
                transform.rotation, targetRotation, rotationSpeed * Time.fixedDeltaTime);
            networkSync.SetRotation(smoothedRotation);

            // Accelerate toward target velocity
            Vector3 currentVel = networkSync.GetVelocity();
            Vector3 targetHorizontal = moveDir * moveSpeed;
            Vector3 velocityDelta = new Vector3(
                targetHorizontal.x - currentVel.x,
                0f,
                targetHorizontal.z - currentVel.z);
            networkSync.AddMovementForce(velocityDelta, ForceMode.VelocityChange);
        }
        else
        {
            // No movement input — lock rotation in place to prevent drift/spin
            networkSync.SetRotation(transform.rotation);

            if (isGrounded && !networkSync.IsUnderExternalForce)
            {
                // Kill horizontal velocity immediately.
                Vector3 vel = networkSync.GetVelocity();
                networkSync.SetHorizontalVelocity(Vector3.zero, vel.y);
            }
        }
    }

    private void HandleJump()
    {
        if (isGrounded && Input.GetButtonDown("Jump"))
            networkSync.AddJumpForce(jumpForce);
    }

    private void CheckGrounded()
    {
        CapsuleCollider col = GetComponent<CapsuleCollider>();
        Vector3 origin = transform.position + Vector3.down * (col.height / 2f - col.radius);
        isGrounded = Physics.SphereCast(origin, col.radius * 0.9f, Vector3.down,
            out _, groundCheckDistance, groundLayer);
    }

    private void ApplyDrag()
    {
        if (isGrounded) return;
        Vector3 vel = networkSync.GetVelocity();
        Vector3 horizontalVel = new Vector3(vel.x, 0f, vel.z);
        if (horizontalVel.magnitude > 0.1f)
            networkSync.AddMovementForce(-horizontalVel * airDrag, ForceMode.Acceleration);
    }
}

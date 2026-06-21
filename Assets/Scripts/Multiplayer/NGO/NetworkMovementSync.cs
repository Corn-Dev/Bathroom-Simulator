using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Owner-authoritative movement sync component.
///
/// The owning client drives their own Rigidbody directly — movement feels
/// perfectly responsive with zero latency. Position and rotation are broadcast
/// to all other clients each tick so remote players see smooth movement.
///
/// Physics effects (knockback, taser, launch, etc.) are applied server-side
/// via ApplyImpulse / ApplyImpulseToPlayer so the server controls when they
/// land and all clients see the result.
///
/// Prefab requirements:
///   - Rigidbody: useGravity=true, isKinematic=false, interpolate=Interpolate
///   - Freeze Rotation X and Z (leave Y free for yaw)
///   - This component on the same GameObject as the Rigidbody
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class NetworkMovementSync : NetworkBehaviour
{
    [Header("Sync")]
    [Tooltip("How often (seconds) the owner broadcasts position/rotation to other clients.")]
    [SerializeField] private float syncInterval = 0.05f; // 20hz

    private Rigidbody rb;
    private float syncTimer;
    private float externalForceTimer;

    /// <summary>
    /// True for a short window after an external impulse lands.
    /// PlayerMovement checks this to avoid immediately killing knockback momentum.
    /// </summary>
    public bool IsUnderExternalForce => externalForceTimer > 0f;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
    }

    public override void OnNetworkSpawn()
    {
        if (!IsOwner)
        {
            // Non-owners: make Rigidbody kinematic so it doesn't simulate locally.
            // Position/rotation will be set directly from network updates.
            rb.isKinematic = true;
        }
    }

    private void FixedUpdate()
    {
        if (!IsOwner) return;

        if (externalForceTimer > 0f)
            externalForceTimer -= Time.fixedDeltaTime;

        // Broadcast position + rotation to all other clients at the sync rate
        syncTimer += Time.fixedDeltaTime;
        if (syncTimer >= syncInterval)
        {
            syncTimer = 0f;
            BroadcastStateServerRpc(transform.position, transform.rotation, rb.velocity);
        }
    }

    // --- Called by PlayerMovement on the owner to directly drive the Rigidbody ---

    public void AddMovementForce(Vector3 force, ForceMode mode)
    {
        if (!IsOwner) return;
        rb.AddForce(force, mode);
    }

    public void SetRotation(Quaternion rotation)
    {
        if (!IsOwner) return;
        rb.MoveRotation(rotation);
    }

    public void AddJumpForce(float jumpForce)
    {
        if (!IsOwner) return;
        // Zero Y before jumping for consistent height regardless of fall speed
        rb.velocity = new Vector3(rb.velocity.x, 0f, rb.velocity.z);
        rb.AddForce(Vector3.up * jumpForce, ForceMode.Impulse);
    }

    public Vector3 GetVelocity() => rb.velocity;

    public void SetHorizontalVelocity(Vector3 horizontal, float yVelocity)
    {
        if (!IsOwner) return;
        rb.velocity = new Vector3(horizontal.x, yVelocity, horizontal.z);
    }

    // --- Physics effects: called from game code (upgrades, abilities, etc.) ---

    /// <summary>
    /// Apply an impulse to this player. Call this from any server-side game mechanic
    /// (taser, knockback, launch pad, etc.). Safe to call from any client — it routes
    /// through a ServerRpc so the server owns when the effect lands.
    /// </summary>
    public void ApplyImpulse(Vector3 force)
    {
        ApplyImpulseServerRpc(force);
    }

    [ServerRpc(RequireOwnership = false)]
    private void ApplyImpulseServerRpc(Vector3 force)
    {
        ApplyImpulseClientRpc(force);
    }

    [ClientRpc]
    private void ApplyImpulseClientRpc(Vector3 force)
    {
        if (!IsOwner) return;
        rb.isKinematic = false;
        rb.AddForce(force, ForceMode.Impulse);
        externalForceTimer = 1.5f; // hold off normal stopping for 1.5s after impact
    }

    // --- State broadcast: owner → server → all clients ---

    [ServerRpc]
    private void BroadcastStateServerRpc(Vector3 position, Quaternion rotation, Vector3 velocity)
    {
        // Relay to all non-owner clients
        UpdateStateClientRpc(position, rotation, velocity);
    }

    [ClientRpc]
    private void UpdateStateClientRpc(Vector3 position, Quaternion rotation, Vector3 velocity)
    {
        if (IsOwner) return;

        // Snap if too far off (teleport, spawn, etc.), otherwise interpolate
        // using the broadcast velocity for dead-reckoning between updates.
        float distance = Vector3.Distance(transform.position, position);
        if (distance > 2f)
        {
            transform.position = position;
            transform.rotation = rotation;
        }
        else
        {
            transform.position = Vector3.MoveTowards(transform.position, position, velocity.magnitude * Time.fixedDeltaTime + 0.1f);
            transform.rotation = Quaternion.Slerp(transform.rotation, rotation, 0.3f);
        }
    }
}

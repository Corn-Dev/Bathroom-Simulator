using UnityEngine;

[RequireComponent(typeof(NetworkMovementSync))]
public class PlayerMovement : MonoBehaviour
{
    [Header("Movement")]
    public float speed = 5f;
    public float rotationSpeed = 200f;

    private NetworkMovementSync networkSync;
    private INetworkSpawnPolicy spawnPolicy;

    private void Awake()
    {
        networkSync = GetComponent<NetworkMovementSync>();
        spawnPolicy = GetComponent<INetworkSpawnPolicy>();
    }

    private void Update()
    {
        if (networkSync == null || !networkSync.IsOwner)
        {
            return;
        }

        if (spawnPolicy != null && !spawnPolicy.CanEnableGameplayForLocalPlayer())
        {
            return;
        }

        HandleMovement();
        HandleRotation();
    }

    private void HandleMovement()
    {
        float h = Input.GetAxisRaw("Horizontal");
        float v = Input.GetAxisRaw("Vertical");
        Vector3 input = new Vector3(h, 0f, v);

        // Client prediction: move immediately
        if (input.sqrMagnitude > 0.01f)
        {
            Vector3 move = transform.TransformDirection(input.normalized) * speed * Time.deltaTime;
            transform.position += move;
        }

        networkSync.SubmitMovementInput(input, Time.deltaTime, transform.position);
    }

    private void HandleRotation()
    {
        float mouseX = Input.GetAxis("Mouse X");

        if (Mathf.Abs(mouseX) > 0.01f)
        {
            // Apply rotation locally
            float clientSensitivity = 200f; // tweak in inspector
            float deltaYaw = mouseX * clientSensitivity * Time.deltaTime;
            transform.Rotate(Vector3.up * deltaYaw);

            networkSync.SubmitRotationDelta(deltaYaw);
        }
    }
}

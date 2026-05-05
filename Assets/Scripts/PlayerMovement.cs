using Unity.Netcode;
using UnityEngine;

public class PlayerMovement : NetworkBehaviour
{
    [Header("Movement")]
    public float speed = 5f;
    public float rotationSpeed = 200f;

    private void Update()
    {
        if (!IsOwner) return;

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

        // Always tell server the input (even zero)
        MoveServerRpc(input, Time.deltaTime, transform.position);
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

            // Send only the delta to the server
            RotateServerRpc(deltaYaw);
        }
    }

    // --------- Server Side ---------

    [ServerRpc]
    private void MoveServerRpc(Vector3 input, float deltaTime, Vector3 clientPos)
    {
        // Apply movement on server
        if (input.sqrMagnitude > 0.01f)
        {
            Vector3 move = transform.TransformDirection(input.normalized) * speed * deltaTime;
            transform.position += move;
        }

        // Sync authoritative position back
        UpdatePositionClientRpc(transform.position, clientPos);
    }

    [ServerRpc]
    private void RotateServerRpc(float deltaYaw)
    {
        // Server just applies the same rotation delta
        transform.Rotate(Vector3.up * deltaYaw);
    }


    // --------- Reconciliation ---------

    [ClientRpc]
    private void UpdatePositionClientRpc(Vector3 serverPos, Vector3 clientPos)
    {
        if (!IsOwner) return;

        float distance = Vector3.Distance(serverPos, clientPos);

        // Only snap if we’re really off — prevents "sliding"
        if (distance > 0.15f)
        {
            transform.position = serverPos;
        }
    }
}

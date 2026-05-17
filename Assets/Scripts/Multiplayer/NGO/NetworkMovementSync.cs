using Unity.Netcode;
using UnityEngine;

public class NetworkMovementSync : NetworkBehaviour
{
    [SerializeField] private float reconciliationThreshold = 0.15f;
    [SerializeField] private float speed = 5f;

    public void SubmitMovementInput(Vector3 input, float deltaTime, Vector3 predictedPosition)
    {
        if (!IsOwner)
        {
            return;
        }

        MoveServerRpc(input, deltaTime, predictedPosition);
    }

    public void SubmitRotationDelta(float deltaYaw)
    {
        if (!IsOwner)
        {
            return;
        }

        RotateServerRpc(deltaYaw);
    }

    [ServerRpc]
    private void MoveServerRpc(Vector3 input, float deltaTime, Vector3 clientPos)
    {
        if (input.sqrMagnitude > 0.01f)
        {
            Vector3 move = transform.TransformDirection(input.normalized) * speed * deltaTime;
            transform.position += move;
        }

        UpdatePositionClientRpc(transform.position, clientPos);
    }

    // TODO: Rotation is applied server-side only. Other clients do not receive an
    // authoritative rotation broadcast via ClientRpc, so remote players will not
    // see this player rotate. Add a RotateClientRpc to sync rotation to all clients.
    [ServerRpc]
    private void RotateServerRpc(float deltaYaw)
    {
        transform.Rotate(Vector3.up * deltaYaw);
    }

    [ClientRpc]
    private void UpdatePositionClientRpc(Vector3 serverPos, Vector3 clientPos)
    {
        if (!IsOwner)
        {
            return;
        }

        if (Vector3.Distance(serverPos, clientPos) > reconciliationThreshold)
        {
            transform.position = serverPos;
        }
    }
}

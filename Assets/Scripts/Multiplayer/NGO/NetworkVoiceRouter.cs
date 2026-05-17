using Unity.Collections;
using Unity.Netcode;

public class NetworkVoiceRouter : NetworkBehaviour
{
    [ServerRpc(Delivery = RpcDelivery.Unreliable)]
    public void SendVoiceServerRpc(byte[] compressed, int size, ServerRpcParams serverRpcParams = default)
    {
        if (compressed == null || size <= 0)
        {
            return;
        }

        if (size > compressed.Length)
        {
            size = compressed.Length;
        }

        byte[] payload = new byte[size];
        System.Buffer.BlockCopy(compressed, 0, payload, 0, size);
        ReceiveVoiceClientRpc(payload, size);
    }

    [ClientRpc(Delivery = RpcDelivery.Unreliable)]
    private void ReceiveVoiceClientRpc(byte[] compressed, int size, ClientRpcParams clientRpcParams = default)
    {
        VoicePacketBus.Publish(NetworkObjectId, compressed, size, OwnerClientId);
    }
}

public static class VoicePacketBus
{
    public delegate void VoicePacketHandler(ulong sourceNetworkObjectId, byte[] compressed, int size, ulong ownerClientId);
    public static event VoicePacketHandler OnVoicePacket;

    public static void Publish(ulong sourceNetworkObjectId, byte[] compressed, int size, ulong ownerClientId)
    {
        OnVoicePacket?.Invoke(sourceNetworkObjectId, compressed, size, ownerClientId);
    }
}

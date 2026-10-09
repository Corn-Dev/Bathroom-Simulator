using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Gates gameplay for the local player until their persistent progress has loaded.
/// Place this on the networked player prefab. Implements <see cref="INetworkSpawnPolicy"/>
/// so other systems can check whether gameplay should be enabled yet.
/// </summary>
public class PlayerReadyHandshake : NetworkBehaviour, INetworkSpawnPolicy
{
    private readonly NetworkVariable<bool> isProgressReady = new NetworkVariable<bool>(false);

    private IPlayerIdentityProvider identityProvider;
    private IPlayerProgressService progressService;

    private void Awake()
    {
        identityProvider = new SteamPlayerIdentityProvider();
        progressService = new PlayerProgressService();
    }

    public override void OnNetworkSpawn()
    {
        if (!IsOwner)
        {
            return;
        }

        string identityKey = identityProvider.GetLocalIdentityKey();
        progressService.BeginLoadProgress(identityKey, loaded =>
        {
            if (loaded)
            {
                ConfirmProgressLoadedServerRpc();
            }
        });
    }

    [ServerRpc]
    private void ConfirmProgressLoadedServerRpc()
    {
        isProgressReady.Value = true;
    }

    public bool CanEnableGameplayForLocalPlayer()
    {
        return isProgressReady.Value;
    }
}

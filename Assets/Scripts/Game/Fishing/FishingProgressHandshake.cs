using Unity.Netcode;
using UnityEngine;

public class FishingProgressHandshake : NetworkBehaviour, INetworkSpawnPolicy
{
    private readonly NetworkVariable<bool> isProgressReady = new NetworkVariable<bool>(false);

    private IPlayerIdentityProvider identityProvider;
    private IPlayerProgressService progressService;

    private void Awake()
    {
        identityProvider = new SteamPlayerIdentityProvider();
        progressService = new FishingPlayerProgressService();
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

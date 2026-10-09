using UnityEngine;

public class GameSessionBootstrap : MonoBehaviour
{
    [SerializeField] private SteamLobby steamLobby;

    private void OnEnable()
    {
        steamLobby.OnSessionReady += HandleSessionReady;
    }

    private void OnDisable()
    {
        steamLobby.OnSessionReady -= HandleSessionReady;
    }

    private void HandleSessionReady()
    {
        Debug.Log("Session ready — load player progress here.");
        // PlayerProgressService load goes here when ready
    }
}
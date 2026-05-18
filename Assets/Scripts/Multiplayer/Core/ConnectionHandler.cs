using UnityEngine;
using Unity.Netcode;
using UnityEngine.SceneManagement;

public class ConnectionHandler : MonoBehaviour
{
    [SerializeField] private string mainMenuSceneName = "Main Menu";

    private void OnEnable()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback += HandleClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback += HandleClientDisconnected;
            NetworkManager.Singleton.OnServerStarted += HandleServerStarted;
        }
    }

    private void OnDisable()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= HandleClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= HandleClientDisconnected;
            NetworkManager.Singleton.OnServerStarted -= HandleServerStarted;
        }
    }

    private void HandleServerStarted()
    {
        Debug.Log("Server started (host or dedicated).");
    }

    private void HandleClientConnected(ulong clientId)
    {
        Debug.Log($"Client {clientId} connected.");

        if (NetworkManager.Singleton.IsServer)
        {
            // Example: spawn NPCs or sync initial game state for this client 
        }
    }

    private void HandleClientDisconnected(ulong clientId)
    {
        Debug.Log($"Client {clientId} disconnected.");

        if (NetworkManager.Singleton == null)
        {
            return;
        }

        // Player object despawn is handled by SpawnManager.

        // If the host disconnects, everyone gets kicked —
        // detect this on clients and bounce them to main menu.
        if (!NetworkManager.Singleton.IsServer && clientId == NetworkManager.ServerClientId)
        {
            Debug.Log("Host disconnected! Returning to main menu...");
            SceneManager.LoadScene(mainMenuSceneName);
        }
    }
}

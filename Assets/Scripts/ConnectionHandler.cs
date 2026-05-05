using UnityEngine;
using Unity.Netcode;
using UnityEngine.SceneManagement;

public class ConnectionHandler : MonoBehaviour
{
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

        if (NetworkManager.Singleton.IsServer)
        {
            // Handle cleanup — despawn objects owned by client, etc.
            var player = NetworkManager.Singleton.ConnectedClients[clientId].PlayerObject;
            if (player != null)
            {
                Destroy(player.gameObject);
            }

            // TODO: Optionally broadcast to remaining players (chat/log UI)
        }

        // If the host disconnects, everyone gets kicked — 
        // you can detect this on clients and bounce them to main menu:
        if (!NetworkManager.Singleton.IsServer && clientId == NetworkManager.ServerClientId)
        {
            Debug.Log("Host disconnected! Returning to main menu...");
            SceneManager.LoadScene("Main Menu");
        }
    }
}

using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Server-side player spawner. Place this in the Game Scene (not DontDestroyOnLoad).
/// Listens for client connections and spawns the player prefab at the next available
/// spawn point, wrapping around if there are fewer points than players.
/// </summary>
public class SpawnManager : NetworkBehaviour
{
    [SerializeField] private Transform[] spawnPoints;
    [SerializeField] private NetworkObject playerPrefab;

    private int spawnCount = 0;

    public override void OnNetworkSpawn()
    {
        if (!IsServer)
        {
            return;
        }

        NetworkManager.Singleton.OnClientConnectedCallback += HandleClientConnected;

        // Spawn players for any clients that connected before this scene loaded
        // (e.g. the host, who connected before the Game Scene was active).
        foreach (ulong clientId in NetworkManager.Singleton.ConnectedClientsIds)
        {
            HandleClientConnected(clientId);
        }
    }

    public override void OnNetworkDespawn()
    {
        if (!IsServer)
        {
            return;
        }

        NetworkManager.Singleton.OnClientConnectedCallback -= HandleClientConnected;
    }

    private void HandleClientConnected(ulong clientId)
    {
        if (playerPrefab == null)
        {
            Debug.LogError("[SpawnManager] playerPrefab is not assigned.");
            return;
        }

        if (spawnPoints == null || spawnPoints.Length == 0)
        {
            Debug.LogError("[SpawnManager] No spawn points assigned.");
            return;
        }

        Transform spawnPoint = spawnPoints[spawnCount % spawnPoints.Length];
        spawnCount++;

        NetworkObject instance = Instantiate(
            playerPrefab,
            spawnPoint.position,
            spawnPoint.rotation
        );

        instance.SpawnAsPlayerObject(clientId, destroyWithScene: true);

        Debug.Log($"[SpawnManager] Spawned player for client {clientId} at {spawnPoint.name}.");
    }
}

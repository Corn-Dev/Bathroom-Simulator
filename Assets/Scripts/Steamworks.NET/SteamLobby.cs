using System;
using System.Collections.Generic;
using Steamworks;
using Unity.Netcode;
using UnityEngine;

public class SteamLobby : MonoBehaviour
{
    public Action OnLobbyEnteredEvent;
    public event Action<List<(string playerName, Sprite avatar)>> OnLobbyPlayerListChanged;
    public event Action<MultiplayerSessionState> OnSessionStateChanged;

    [Header("Session Config")]
    [SerializeField] private int maxPlayers = 8;
    [SerializeField] private string gameplaySceneName = "Game Scene";

    private MultiplayerSessionStateMachine stateMachine;
    private SteamLobbyDiscoveryService discoveryService;
    private SteamInviteService inviteService;
    private NgoSessionCoordinator sessionCoordinator;
    private CSteamID currentLobbyID = CSteamID.Nil;
    public event Action OnSessionReady;


    private void Awake()
    {
        DontDestroyOnLoad(gameObject);
        stateMachine = new MultiplayerSessionStateMachine();
    }

    private void Start()
    {
        if (!SteamManager.Initialized)
        {
            Debug.LogError("Steam is not initialized!");
            return;
        }

        inviteService = new SteamInviteService();
        sessionCoordinator = new NgoSessionCoordinator(NetworkManager.Singleton, stateMachine);
        sessionCoordinator.SessionStateChanged += state => OnSessionStateChanged?.Invoke(state);

        discoveryService = new SteamLobbyDiscoveryService(IsHost, GetSteamAvatar);
        discoveryService.Initialize();
        discoveryService.LobbyEntered += HandleLobbyEntered;
        discoveryService.LobbyPlayerListChanged += players => OnLobbyPlayerListChanged?.Invoke(players);

        stateMachine.ForceState(MultiplayerSessionState.Idle);

    }

    // Host lobby
    public void HostLobby()
    {
        stateMachine.TryTransition(MultiplayerSessionState.Discovering);
        discoveryService.CreateFriendsLobby(maxPlayers);
    }

    public bool IsHost(CSteamID playerID)
    {
        if (currentLobbyID == CSteamID.Nil) return false; // Lobby not set
        CSteamID hostID = SteamMatchmaking.GetLobbyOwner(currentLobbyID);
        return playerID == hostID;
    }

    public bool IsHost()
    {
        // Make sure we have a valid lobby
        if (currentLobbyID == CSteamID.Nil) return false;

        // Get the local player's SteamID
        CSteamID localID = SteamUser.GetSteamID();

        // Get the current lobby owner
        CSteamID hostID = SteamMatchmaking.GetLobbyOwner(currentLobbyID);

        // Compare
        return localID == hostID;
    }

    // Join via Steam overlay
    public void JoinFriendLobby()
    {
        inviteService.OpenFriendsOverlay();
    }

    public void JoinRandomOpenLobby()
    {
        stateMachine.TryTransition(MultiplayerSessionState.Discovering);
        discoveryService.JoinRandomOpenLobby();
    }

    private Sprite GetSteamAvatar(CSteamID steamID)
    {
        int imageID = SteamFriends.GetMediumFriendAvatar(steamID); // -1 if none
        if (imageID == -1) return null;

        if (!SteamUtils.GetImageSize(imageID, out uint width, out uint height)) return null;
        if (width == 0 || height == 0) return null;

        int dataSize = (int)(width * height * 4);
        byte[] imageData = new byte[dataSize];
        if (!SteamUtils.GetImageRGBA(imageID, imageData, dataSize)) return null;

        // --- Flip the rows manually ---
        byte[] flipped = new byte[dataSize];
        int rowBytes = (int)width * 4;
        for (int y = 0; y < (int)height; y++)
        {
            int src = y * rowBytes;
            int dst = ((int)height - 1 - y) * rowBytes;
            System.Buffer.BlockCopy(imageData, src, flipped, dst, rowBytes);
        }

        Texture2D tex = new Texture2D((int)width, (int)height, TextureFormat.RGBA32, false);
        tex.LoadRawTextureData(flipped);
        tex.Apply();

        return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
    }

    private void HandleLobbyEntered(CSteamID lobbyId)
    {
        currentLobbyID = lobbyId;
        Debug.Log("Entered Lobby: " + currentLobbyID);

        stateMachine.TryTransition(MultiplayerSessionState.Joining);
        OnLobbyEnteredEvent?.Invoke();

        string hostIDStr = SteamMatchmaking.GetLobbyData(currentLobbyID, "HostID");
        if (!ulong.TryParse(hostIDStr, out ulong hostSteamId))
        {
            Debug.LogError("Invalid host steam ID in lobby data.");
            stateMachine.ForceState(MultiplayerSessionState.Idle);
            return;
        }

        bool started = IsHost()
            ? sessionCoordinator.StartHost()
            : sessionCoordinator.StartClient(hostSteamId);
        if (!started)
        {
            Debug.LogError("Failed to bootstrap NGO session.");
            stateMachine.ForceState(MultiplayerSessionState.Idle);
        }else 
        {
                OnSessionReady?.Invoke();
        }
    }

    public void LeaveLobby()
    {
        if (currentLobbyID == CSteamID.Nil)
        {
            Debug.LogError("No active lobby to leave!");
            return;
        }

        stateMachine.TryTransition(MultiplayerSessionState.Leaving);
        CSteamID leavingLobbyId = currentLobbyID;
        sessionCoordinator.ShutdownSession();
        discoveryService.LeaveLobby(leavingLobbyId);
        currentLobbyID = CSteamID.Nil;
        stateMachine.ForceState(MultiplayerSessionState.Idle);
    }

    // Host starts the game once enough players have joined
    public void StartGame()
    {
        if (currentLobbyID == CSteamID.Nil)
        {
            Debug.LogError("No active lobby to start!");
            return;
        }

        // Only the host can start the game
        if (!IsHost())
        {
            Debug.LogWarning("Only the host can start the game!");
            return;
        }

        int memberCount = SteamMatchmaking.GetNumLobbyMembers(currentLobbyID);
        if (memberCount < 1)
        {
            Debug.Log("Not enough players to start!");
            return;
        }

        Debug.Log("Starting game... Loading GameScene for all players.");

        // Host loads the GameScene; NGO handles scene sync
        sessionCoordinator.LoadGameplayScene(gameplaySceneName);
    }

    public CSteamID GetLobbyId()
    {
        return currentLobbyID;
    }
}

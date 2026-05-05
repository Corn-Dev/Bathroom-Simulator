using System;
using UnityEngine;
using Steamworks;
using Unity.Netcode;
using UnityEngine.SceneManagement;
using System.Collections.Generic;

public class SteamLobby : MonoBehaviour
{
    // Callbacks
    protected Callback<LobbyCreated_t> lobbyCreated;
    protected Callback<GameLobbyJoinRequested_t> lobbyJoinRequested;
    protected Callback<LobbyEnter_t> lobbyEntered;
    protected Callback<LobbyChatUpdate_t> lobbyChatUpdate;

    public Action OnLobbyEnteredEvent;
    public event Action<List<(string playerName, Sprite avatar)>> OnLobbyPlayerListChanged;


    private const int maxPlayers = 8;
    private CSteamID currentLobbyID;

    private void Start()
    {
        if (!SteamManager.Initialized)
        {
            Debug.LogError("Steam is not initialized!");
            return;
        }

        lobbyCreated = Callback<LobbyCreated_t>.Create(OnLobbyCreated);
        lobbyJoinRequested = Callback<GameLobbyJoinRequested_t>.Create(OnGameLobbyJoinRequested);
        lobbyEntered = Callback<LobbyEnter_t>.Create(OnLobbyEntered);
        lobbyChatUpdate = Callback<LobbyChatUpdate_t>.Create(OnLobbyChatUpdate);

    }

    private void Awake()
    {
        DontDestroyOnLoad(gameObject);
    }

    // Host lobby
    public void HostLobby()
    {
        SteamMatchmaking.CreateLobby(ELobbyType.k_ELobbyTypeFriendsOnly, maxPlayers);
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
        if (!SteamManager.Initialized) return;
        SteamFriends.ActivateGameOverlay("Friends");
    }

    private void OnLobbyChatUpdate(LobbyChatUpdate_t callback)
    {
        if (callback.m_ulSteamIDLobby != currentLobbyID.m_SteamID) return;

        // Update the UI
        UpdateLobbyPlayerList();
    }

    //Hopefully remove logic from here
    private void UpdateLobbyPlayerList()
    {
        int memberCount = SteamMatchmaking.GetNumLobbyMembers(currentLobbyID);
        var players = new List<(string, Sprite)>();

        for (int i = 0; i < memberCount; i++)
        {
            CSteamID memberID = SteamMatchmaking.GetLobbyMemberByIndex(currentLobbyID, i);
            string playerName = SteamFriends.GetFriendPersonaName(memberID);
            Sprite avatar = GetSteamAvatar(memberID);

            if (IsHost(memberID))
            {
                playerName += " (host)";
            }

            players.Add((playerName, avatar));
        }

        OnLobbyPlayerListChanged?.Invoke(players);
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

    // Lobby created
    private void OnLobbyCreated(LobbyCreated_t callback)
    {
        if (callback.m_eResult != EResult.k_EResultOK)
        {
            Debug.LogError("Lobby creation failed: " + callback.m_eResult);
            return;
        }

        currentLobbyID = new CSteamID(callback.m_ulSteamIDLobby);
        Debug.Log("Lobby created! ID: " + currentLobbyID);

        // Store host info in lobby data
        SteamMatchmaking.SetLobbyData(currentLobbyID, "HostID", SteamUser.GetSteamID().ToString());
        SteamMatchmaking.SetLobbyData(currentLobbyID, "name", SteamFriends.GetPersonaName() + "'s Lobby");
    }

    // Friend invited / request to join
    private void OnGameLobbyJoinRequested(GameLobbyJoinRequested_t callback)
    {
        Debug.Log("Join request from lobby: " + callback.m_steamIDLobby);
        SteamMatchmaking.JoinLobby(callback.m_steamIDLobby);
    }

    // Lobby entered
    private void OnLobbyEntered(LobbyEnter_t callback)
    {

        currentLobbyID = new CSteamID(callback.m_ulSteamIDLobby);
        Debug.Log("Entered Lobby: " + currentLobbyID);

        

        string hostIDStr = SteamMatchmaking.GetLobbyData(currentLobbyID, "HostID");
        CSteamID hostID = new CSteamID(ulong.Parse(hostIDStr));

        var transport = NetworkManager.Singleton.GetComponent<Netcode.Transports.SteamNetworkingSocketsTransport>();

        OnLobbyEnteredEvent?.Invoke();

        UpdateLobbyPlayerList();

        if (IsHost())
        {
            Debug.Log("You are the HOST. NGO Host will start later when you press StartGame()");
            NetworkManager.Singleton.StartHost();

            // Host doesn’t need to set ConnectToSteamID
            // TODO: Show "Start Game" button in UI
        }
        else
        {
            Debug.Log("You are a CLIENT. Starting NGO Client...");

            // Assign host SteamID so client knows where to connect
            transport.ConnectToSteamID = hostID.m_SteamID;

            NetworkManager.Singleton.StartClient();

            // TODO: Hide/disable "Start Game" button in UI
        }
    }

    public void LeaveLobby()
    {
        if (currentLobbyID == CSteamID.Nil)
        {
            Debug.LogError("No active lobby to leave!");
            return;
        }

        NetworkManager.Singleton.Shutdown();
        currentLobbyID = CSteamID.Nil;
        SteamMatchmaking.LeaveLobby(currentLobbyID);
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
        string hostIDStr = SteamMatchmaking.GetLobbyData(currentLobbyID, "HostID");
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
        NetworkManager.Singleton.SceneManager.LoadScene("Game Scene", LoadSceneMode.Single);
    }

    public CSteamID GetLobbyId()
    {

        return currentLobbyID;
    }
}

using System;
using System.Collections.Generic;
using Steamworks;
using UnityEngine;

public sealed class SteamLobbyDiscoveryService : IMatchDiscovery
{
    private readonly Func<CSteamID, bool> isHostPredicate;
    private readonly Func<CSteamID, Sprite> avatarResolver;

    private Callback<LobbyCreated_t> lobbyCreated;
    private Callback<GameLobbyJoinRequested_t> lobbyJoinRequested;
    private Callback<LobbyEnter_t> lobbyEntered;
    private Callback<LobbyChatUpdate_t> lobbyChatUpdate;
    private Callback<LobbyMatchList_t> lobbyMatchList;

    private CSteamID currentLobbyId;

    public event Action<CSteamID> LobbyEntered;
    public event Action<List<(string playerName, Sprite avatar)>> LobbyPlayerListChanged;
    public event Action<CSteamID> JoinRequested;

    public SteamLobbyDiscoveryService(Func<CSteamID, bool> isHostPredicate, Func<CSteamID, Sprite> avatarResolver)
    {
        this.isHostPredicate = isHostPredicate;
        this.avatarResolver = avatarResolver;
        currentLobbyId = CSteamID.Nil;
    }

    public CSteamID CurrentLobbyId => currentLobbyId;

    public void Initialize()
    {
        lobbyCreated = Callback<LobbyCreated_t>.Create(OnLobbyCreated);
        lobbyJoinRequested = Callback<GameLobbyJoinRequested_t>.Create(OnGameLobbyJoinRequested);
        lobbyEntered = Callback<LobbyEnter_t>.Create(OnLobbyEntered);
        lobbyChatUpdate = Callback<LobbyChatUpdate_t>.Create(OnLobbyChatUpdate);
        lobbyMatchList = Callback<LobbyMatchList_t>.Create(OnLobbyMatchList);
    }

    public void CreateFriendsLobby(int maxPlayers)
    {
        SteamMatchmaking.CreateLobby(ELobbyType.k_ELobbyTypeFriendsOnly, maxPlayers);
    }

    public void JoinLobby(CSteamID lobbyId)
    {
        SteamMatchmaking.JoinLobby(lobbyId);
    }

    public void JoinRandomOpenLobby()
    {
        SteamMatchmaking.AddRequestLobbyListDistanceFilter(ELobbyDistanceFilter.k_ELobbyDistanceFilterWorldwide);
        SteamMatchmaking.AddRequestLobbyListResultCountFilter(20);
        SteamMatchmaking.AddRequestLobbyListFilterSlotsAvailable(1);
        SteamMatchmaking.RequestLobbyList();
    }

    public void LeaveLobby(CSteamID lobbyId)
    {
        if (lobbyId != CSteamID.Nil)
        {
            SteamMatchmaking.LeaveLobby(lobbyId);
        }

        currentLobbyId = CSteamID.Nil;
        LobbyPlayerListChanged?.Invoke(new List<(string playerName, Sprite avatar)>());
    }

    private void OnLobbyCreated(LobbyCreated_t callback)
    {
        if (callback.m_eResult != EResult.k_EResultOK)
        {
            Debug.LogError("Lobby creation failed: " + callback.m_eResult);
            return;
        }

        currentLobbyId = new CSteamID(callback.m_ulSteamIDLobby);
        SteamMatchmaking.SetLobbyData(currentLobbyId, "HostID", SteamUser.GetSteamID().ToString());
        SteamMatchmaking.SetLobbyData(currentLobbyId, "name", SteamFriends.GetPersonaName() + "'s Lobby");
    }

    private void OnGameLobbyJoinRequested(GameLobbyJoinRequested_t callback)
    {
        JoinRequested?.Invoke(callback.m_steamIDLobby);
        SteamMatchmaking.JoinLobby(callback.m_steamIDLobby);
    }

    private void OnLobbyEntered(LobbyEnter_t callback)
    {
        currentLobbyId = new CSteamID(callback.m_ulSteamIDLobby);
        LobbyEntered?.Invoke(currentLobbyId);
        PublishPlayerList();
    }

    private void OnLobbyMatchList(LobbyMatchList_t callback)
    {
        if (callback.m_nLobbiesMatching == 0)
        {
            return;
        }

        CSteamID lobbyId = SteamMatchmaking.GetLobbyByIndex(0);
        SteamMatchmaking.JoinLobby(lobbyId);
    }

    private void OnLobbyChatUpdate(LobbyChatUpdate_t callback)
    {
        if (currentLobbyId == CSteamID.Nil || callback.m_ulSteamIDLobby != currentLobbyId.m_SteamID)
        {
            return;
        }

        PublishPlayerList();
    }

    private void PublishPlayerList()
    {
        if (currentLobbyId == CSteamID.Nil)
        {
            return;
        }

        int memberCount = SteamMatchmaking.GetNumLobbyMembers(currentLobbyId);
        var players = new List<(string, Sprite)>(memberCount);

        for (int i = 0; i < memberCount; i++)
        {
            CSteamID memberId = SteamMatchmaking.GetLobbyMemberByIndex(currentLobbyId, i);
            string playerName = SteamFriends.GetFriendPersonaName(memberId);
            if (isHostPredicate(memberId))
            {
                playerName += " (host)";
            }

            players.Add((playerName, avatarResolver(memberId)));
        }

        LobbyPlayerListChanged?.Invoke(players);
    }
}

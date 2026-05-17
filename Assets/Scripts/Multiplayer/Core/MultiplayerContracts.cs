using System;
using System.Collections.Generic;
using Steamworks;
using UnityEngine;

public interface IMatchDiscovery
{
    event Action<CSteamID> LobbyEntered;
    event Action<List<(string playerName, Sprite avatar)>> LobbyPlayerListChanged;
    event Action<CSteamID> JoinRequested;

    void CreateFriendsLobby(int maxPlayers);
    void JoinLobby(CSteamID lobbyId);
    void JoinRandomOpenLobby();
    void LeaveLobby(CSteamID lobbyId);
}

public interface ISessionBootstrap
{
    event Action<MultiplayerSessionState> SessionStateChanged;

    bool StartHost();
    bool StartClient(ulong hostSteamId);
    void ShutdownSession();
    void LoadGameplayScene(string sceneName);
}

public interface IPlayerIdentityProvider
{
    string GetLocalIdentityKey();
}

public interface IPlayerProgressService
{
    void BeginLoadProgress(string identityKey, Action<bool> onCompleted);
}

public interface INetworkSpawnPolicy
{
    bool CanEnableGameplayForLocalPlayer();
}

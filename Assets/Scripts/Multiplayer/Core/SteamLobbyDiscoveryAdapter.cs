using System;
using System.Collections.Generic;
using Steamworks;
using UnityEngine;

public sealed class SteamLobbyDiscoveryAdapter : IMatchDiscovery
{
    private readonly SteamLobby steamLobby;

    public event Action<CSteamID> LobbyEntered;
    public event Action<List<(string playerName, Sprite avatar)>> LobbyPlayerListChanged;
    public event Action<CSteamID> JoinRequested;

    public SteamLobbyDiscoveryAdapter(SteamLobby steamLobby)
    {
        this.steamLobby = steamLobby;
        if (steamLobby != null)
        {
            steamLobby.OnLobbyEnteredEvent += HandleLobbyEntered;
            steamLobby.OnLobbyPlayerListChanged += players => LobbyPlayerListChanged?.Invoke(players);
        }
    }

    public void CreateFriendsLobby(int maxPlayers)
    {
        steamLobby?.HostLobby();
    }

    // TODO: This only fires the JoinRequested event and does not actually call
    // SteamMatchmaking.JoinLobby. If direct lobby joining is needed, wire through
    // SteamLobby or call SteamMatchmaking directly here.
    public void JoinLobby(CSteamID lobbyId)
    {
        JoinRequested?.Invoke(lobbyId);
    }

    public void JoinRandomOpenLobby()
    {
        steamLobby?.JoinRandomOpenLobby();
    }

    public void LeaveLobby(CSteamID lobbyId)
    {
        steamLobby?.LeaveLobby();
    }

    private void HandleLobbyEntered()
    {
        if (steamLobby == null)
        {
            return;
        }

        LobbyEntered?.Invoke(steamLobby.GetLobbyId());
    }
}

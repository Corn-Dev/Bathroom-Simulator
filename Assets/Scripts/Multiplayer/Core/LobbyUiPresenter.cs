using System.Collections.Generic;
using UnityEngine;

public sealed class LobbyUiPresenter
{
    private readonly LobbyPlayerList playerList;

    public LobbyUiPresenter(LobbyPlayerList playerList)
    {
        this.playerList = playerList;
    }

    public void Bind(IMatchDiscovery discovery)
    {
        if (discovery == null)
        {
            return;
        }

        discovery.LobbyPlayerListChanged += HandleLobbyPlayerListChanged;
    }

    public void Unbind(IMatchDiscovery discovery)
    {
        if (discovery == null)
        {
            return;
        }

        discovery.LobbyPlayerListChanged -= HandleLobbyPlayerListChanged;
    }

    private void HandleLobbyPlayerListChanged(List<(string playerName, Sprite avatar)> players)
    {
        if (playerList == null)
        {
            return;
        }

        playerList.RefreshUI(players);
    }
}

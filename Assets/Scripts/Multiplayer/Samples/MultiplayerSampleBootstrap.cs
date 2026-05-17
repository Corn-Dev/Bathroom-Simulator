using UnityEngine;

public class MultiplayerSampleBootstrap : MonoBehaviour
{
    [SerializeField] private SteamLobby steamLobby;

    // This script is intended for a minimal sample scene where a game can
    // reuse the multiplayer stack without lobby UI.
    public void Host()
    {
        if (steamLobby != null)
        {
            steamLobby.HostLobby();
        }
    }

    public void QuickJoinOpenLobby()
    {
        if (steamLobby != null)
        {
            steamLobby.JoinRandomOpenLobby();
        }
    }

    public void OpenFriendInviteJoin()
    {
        if (steamLobby != null)
        {
            steamLobby.JoinFriendLobby();
        }
    }

    public void Leave()
    {
        if (steamLobby != null)
        {
            steamLobby.LeaveLobby();
        }
    }
}

using Steamworks;
using UnityEngine;

public sealed class SteamInviteService
{
    public void OpenFriendsOverlay()
    {
        if (!SteamManager.Initialized)
        {
            Debug.LogError("[SteamInviteService] Steam is not initialized — cannot open overlay.");
            return;
        }

        // Opens the Steam friends list. The joining player should click "Join Game"
        // on the host's profile from here. Requires Steam overlay to be enabled
        // (Steam → Settings → In-Game → Enable Steam Overlay).
        Debug.Log("[SteamInviteService] Opening Steam friends overlay.");
        SteamFriends.ActivateGameOverlay("Friends");
    }
}

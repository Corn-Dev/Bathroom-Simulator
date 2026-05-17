using Steamworks;

public sealed class SteamInviteService
{
    public void OpenFriendsOverlay()
    {
        if (!SteamManager.Initialized)
        {
            return;
        }

        SteamFriends.ActivateGameOverlay("Friends");
    }
}

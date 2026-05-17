using Steamworks;

public sealed class SteamPlayerIdentityProvider : IPlayerIdentityProvider
{
    public string GetLocalIdentityKey()
    {
        if (!SteamManager.Initialized)
        {
            return string.Empty;
        }

        return SteamUser.GetSteamID().m_SteamID.ToString();
    }
}

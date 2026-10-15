using Steamworks;

/// <summary>
/// Game-side identity source for the player-character layer: the local Steam id as a
/// string, used to key per-player progress/saves. Kept distinct from the lobby kit's
/// MultiplayerKit.Steam.SteamPlayerIdentity (which serves the lobby roster/avatars).
/// </summary>
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

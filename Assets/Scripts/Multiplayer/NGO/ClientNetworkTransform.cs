using Unity.Netcode.Components;

/// <summary>
/// Owner-authoritative NetworkTransform. The owning client writes position and
/// rotation; the server and all other clients receive and interpolate it.
/// Use this on player-controlled objects instead of the default NetworkTransform
/// (which is server-authoritative and will override client movement).
/// </summary>
public class ClientNetworkTransform : NetworkTransform
{
    protected override bool OnIsServerAuthoritative() => false;
}

using UnityEngine;

/// <summary>
/// What the bobber hit when it landed after a cast.
/// The FishingController uses this to determine what happens next.
/// </summary>
public struct CastHitResult
{
    public CastHitType type;
    public Vector3 hitPoint;
    public GameObject hitObject;  // The specific object that was hit (water collider, player, item, etc.)
}

public enum CastHitType
{
    /// <summary>Landed on water — proceed with fishing.</summary>
    Water,

    /// <summary>Hit another player — can hook/pull them.</summary>
    Player,

    /// <summary>Hit the ground or something non-fishable — cast fails.</summary>
    Ground,

    /// <summary>Hit a world item that can be reeled in (future).</summary>
    Item,

    /// <summary>Bobber went out of range or off the map.</summary>
    Nothing
}

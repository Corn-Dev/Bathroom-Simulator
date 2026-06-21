/// <summary>
/// States of the fishing flow.
/// The fishing controller transitions through these sequentially.
/// </summary>
public enum FishingState
{
    /// <summary>Player is not fishing — can move freely, open menus, etc.</summary>
    Idle,

    /// <summary>Player is performing the cast animation/action.</summary>
    Casting,

    /// <summary>Line is in the water, waiting for a fish to bite.</summary>
    WaitingForBite,

    /// <summary>A fish is on the line — player must react within a window to start reeling.</summary>
    Biting,

    /// <summary>Player is actively reeling — minigame in progress.</summary>
    Reeling,

    /// <summary>Fish was successfully caught — showing result.</summary>
    CatchSuccess,

    /// <summary>Fish got away — line snapped or player failed the minigame.</summary>
    CatchFailed
}

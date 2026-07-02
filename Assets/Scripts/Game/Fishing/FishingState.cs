/// <summary>
/// States of the fishing flow.
/// </summary>
public enum FishingState
{
    /// <summary>Player is not fishing — can move freely.</summary>
    Idle,

    /// <summary>Player is holding to charge cast power. Bar bounces up then down.</summary>
    Charging,

    /// <summary>Bobber is flying through the air in an arc.</summary>
    CastFlying,

    /// <summary>Bobber landed on water, waiting for a fish to bite.</summary>
    WaitingForBite,

    /// <summary>A fish is on the line — player must react within a window.</summary>
    Biting,

    /// <summary>Player is actively reeling — minigame in progress.</summary>
    Reeling,

    /// <summary>Fish was successfully caught.</summary>
    CatchSuccess,

    /// <summary>Fish got away or cast failed.</summary>
    CatchFailed,

    /// <summary>Bobber hooked another player — reeling them in.</summary>
    HookedPlayer
}

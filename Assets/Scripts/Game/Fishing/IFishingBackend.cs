using System;

/// <summary>
/// Abstraction over the fishing backend. The fishing controller calls this
/// when a player successfully reels in a fish. The backend determines what
/// fish was caught and grants it.
///
/// Implementations:
///   - MockFishingBackend: rolls locally, grants via Steam Inventory client API (dev/test)
///   - RemoteFishingBackend: calls VPS server which rolls + grants securely (production)
/// </summary>
public interface IFishingBackend
{
    /// <summary>
    /// Called when the player successfully completes the reel-in.
    /// The backend rolls which fish was caught and grants it.
    /// </summary>
    /// <param name="context">Current fishing context (rod, location, etc.)</param>
    /// <param name="onResult">Called on success with the caught fish details.</param>
    /// <param name="onError">Called if the backend is unavailable or request fails.</param>
    void RequestCatch(FishingContext context, Action<CatchResult> onResult, Action<string> onError);
}

/// <summary>
/// Result returned by the backend after a successful catch.
/// </summary>
public struct CatchResult
{
    public FishSpeciesData fish;
    public bool isNewSpecies;
    public float size;
    public bool isPersonalBest;
}

/// <summary>
/// Service that determines which fish species bites.
/// Local implementation rolls randomly from a weighted table.
/// Production implementation will call the backend server for the roll.
/// </summary>
public interface IFishRollService
{
    /// <summary>
    /// Roll for a fish species based on the current context (rod, location, etc.).
    /// Returns the fish that will bite.
    /// </summary>
    FishSpeciesData RollFish(FishingContext context);
}

/// <summary>
/// Context passed to the roll service — everything that can influence what fish bites.
/// </summary>
public struct FishingContext
{
    public FishingRodData rod;
    // Future: location, time of day, bait type, weather, etc.
}

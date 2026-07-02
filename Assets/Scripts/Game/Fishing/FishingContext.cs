/// <summary>
/// Context passed to the fishing backend — everything that can influence what fish is caught.
/// Expand this as you add features (bait, location, time of day, weather, etc.)
/// </summary>
public struct FishingContext
{
    public FishingRodData rod;
    // Future additions:
    // public BaitData bait;
    // public FishingZone zone;
    // public TimeOfDay timeOfDay;
    // public WeatherType weather;
}

using UnityEngine;

/// <summary>
/// Single source of truth for all fish species in the game.
/// Create one asset at: Assets/Resources/FishDatabase.asset
/// (Assets → Create → Phish → Fish Database, then move to Resources folder)
/// All scripts load it automatically via FishDatabase.Instance — no Inspector assignment needed.
/// </summary>
[CreateAssetMenu(fileName = "FishDatabase", menuName = "Phish/Fish Database")]
public class FishDatabase : ScriptableObject
{
    [SerializeField] private FishSpeciesData[] allFish;

    public FishSpeciesData[] AllFish => allFish;

    private static FishDatabase instance;
    public static FishDatabase Instance
    {
        get
        {
            if (instance == null)
                instance = Resources.Load<FishDatabase>("FishDatabase");
            return instance;
        }
    }

    public FishSpeciesData GetById(string fishId)
    {
        for (int i = 0; i < allFish.Length; i++)
        {
            if (allFish[i].fishId == fishId)
                return allFish[i];
        }
        return null;
    }

    public FishSpeciesData GetBySteamDefId(int steamItemDefId)
    {
        for (int i = 0; i < allFish.Length; i++)
        {
            if (allFish[i].steamItemDefId == steamItemDefId)
                return allFish[i];
        }
        return null;
    }
}

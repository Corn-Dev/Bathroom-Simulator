using UnityEngine;

/// <summary>
/// ScriptableObject defining a single fish species.
/// Create new fish via: Assets → Create → Phish → Fish Species.
/// Adding new fish requires zero code changes — just create a new asset.
/// </summary>
[CreateAssetMenu(fileName = "New Fish", menuName = "Phish/Fish Species")]
public class FishSpeciesData : ScriptableObject
{
    [Header("Identity")]
    public string fishId;          // Unique ID — must match Steam Inventory itemdefid mapping
    public string displayName;
    [TextArea] public string description;

    [Header("Rarity")]
    public FishRarity rarity;
    [Tooltip("Relative weight in the drop table. Higher = more common within its rarity tier.")]
    public float dropWeight = 1f;

    [Header("Fishing Behavior")]
    [Tooltip("Seconds range before the fish bites after casting.")]
    public Vector2 biteTimeRange = new Vector2(3f, 8f);
    [Tooltip("How difficult the reel-in minigame is. 0 = trivial, 1 = very hard.")]
    [Range(0f, 1f)] public float reelDifficulty = 0.3f;

    [Header("Battle Stats (Future)")]
    public int baseHealth = 10;
    public int baseAttack = 3;
    public int baseSpeed = 5;

    [Header("Visuals")]
    public Sprite icon;             // Full color icon for Fishdex (once caught)
    public Sprite silhouette;       // Blacked-out shape for Fishdex (uncaught)
    public Color rarityColor = Color.white;

    [Header("Steam Inventory")]
    [Tooltip("The itemdefid in Steam Inventory. Must match items.json.")]
    public int steamItemDefId;
}

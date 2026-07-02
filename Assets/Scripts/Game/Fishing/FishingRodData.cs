using UnityEngine;

/// <summary>
/// ScriptableObject defining a fishing rod type.
/// Rods modify the fishing experience — cast distance, reel speed, rarity bonuses.
/// Create new rods via: Assets → Create → Phish → Fishing Rod.
/// </summary>
[CreateAssetMenu(fileName = "New Rod", menuName = "Phish/Fishing Rod")]
public class FishingRodData : ScriptableObject
{
    [Header("Identity")]
    public string rodId;
    public string displayName;
    [TextArea] public string description;

    [Header("Stats")]
    [Tooltip("Maximum cast distance in world units at full power.")]
    public float castRange = 10f;

    [Tooltip("Multiplier on reel-in speed. Higher = easier reeling.")]
    public float reelSpeedMultiplier = 1f;

    [Tooltip("Additive bonus to rare fish chance (0.0 = no bonus, 0.05 = +5% rare chance).")]
    [Range(0f, 0.2f)] public float rarityBonus = 0f;

    [Tooltip("Multiplier on bite wait time. Lower = fish bite sooner.")]
    [Range(0.5f, 2f)] public float biteTimeMultiplier = 1f;

    [Tooltip("How fast the cast power bar fills/drains per second. Higher = harder timing.")]
    public float castBarSpeed = 1.2f;

    [Header("Visuals")]
    public Sprite icon;
}

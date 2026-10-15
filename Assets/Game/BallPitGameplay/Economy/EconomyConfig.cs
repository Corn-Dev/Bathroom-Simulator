// EconomyConfig: how much a correctly-sorted ball sells for, per colour (brief §4.3; game-design §4). Default 1 coin;
// per-colour multipliers (all 1 by default) and a combined cap (×2) so no colour ever sells for more than twice the
// base. A ScriptableObject so the owner tunes it without code; GameplayCore reads Value(color) on every sale.
using UnityEngine;

namespace BallPitGameplay
{
    [CreateAssetMenu(menuName = "Ball Pit/Economy Config", fileName = "EconomyConfig")]
    public sealed class EconomyConfig : ScriptableObject
    {
        [Tooltip("Base coins per correctly-sorted ball.")]
        [Min(0)] public long baseValue = 1;

        [Tooltip("Per-colour multiplier (index = colour). Missing entries use 1. All 1 by default (game-design §4).")]
        public float[] colorMultipliers = new float[0];

        [Tooltip("The combined per-colour value never exceeds baseValue × this cap (game-design §4).")]
        [Min(1f)] public float combinedCap = 2f;

        /// <summary>Coins for selling one ball of <paramref name="color"/>: baseValue × clamp(multiplier) within the cap,
        /// never below 0. A missing or non-finite multiplier reads as 1.</summary>
        public long Value(int color)
        {
            float mul = 1f;
            if (colorMultipliers != null && color >= 0 && color < colorMultipliers.Length)
            {
                float m = colorMultipliers[color];
                if (!float.IsNaN(m) && !float.IsInfinity(m) && m > 0f) mul = m;
            }
            float cap = float.IsNaN(combinedCap) || combinedCap < 1f ? 1f : combinedCap;
            mul = Mathf.Min(mul, cap);
            long v = (long)Mathf.Round(Mathf.Max(0, baseValue) * mul);
            return v < 0 ? 0 : v;
        }
    }
}

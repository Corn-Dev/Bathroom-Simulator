using UnityEngine;

namespace BallPitFinal
{
    public enum FillType : byte { Fcc = 0, Random = 1 }

    [System.Serializable]
    public sealed class PitSettings
    {
        [Header("Pit (ball count = pitX * pitY * pitZ for both fill types)")]
        [Tooltip("Fcc: instant crystal lattice (mechanically locked: crater walls terrace at ~55-65 deg). " +
                 "Random: poured random loose packing (~0.58, walls slump like a real pit). Cached to StreamingAssets/BallPitFills.")]
        public FillType fillType = FillType.Fcc;
        public int pitX = 100, pitY = 100, pitZ = 100;
        public float ballDiameter = 0.5f;
        [Range(1, 16)] public int colorCount = 4;
        public int seed = 12345;
        [Range(0.9f, 1f)] public float colliderRadiusScale = 0.97f;
        [Range(0f, 1f)] public float jitter = 1f;
        public bool pitWalls = true;
        public bool renderWalls = true;
        [Tooltip("An unused layer for ball colliders. Environment support queries exclude it.")]
        public int ballLayer = 8;

        [Header("Active budget (the rigidbody pool grows to maxActive; there is no separate pool size)")]
        [Tooltip("Hard cap on concurrent rigidbodies.")]
        public int maxActive = 4000;
        [Tooltip("Bodies created at build (the rest are created on demand in batches of 256).")]
        public int activePrewarm = 2048;
        [Tooltip("Adaptive cap: collapse thaws pause while the smoothed Physics.Simulate time is above this (ms). 0 = off.")]
        public float targetSimulateMs = 8f;
        [Tooltip("Collapse thaws are always allowed below this many active balls, regardless of targetSimulateMs.")]
        public int minActive = 500;
        [Tooltip("Active slots that collapse thaws may not use, kept free for player actions (grab, dump).")]
        public int collapseReserve = 100;
        [Tooltip("PhysX solver iterations for ball bodies (0 = project default). Higher = steadier stacks, more cost.")]
        public int solverIterations = 0;
        [Range(20, 120)] public int physicsHz = 50;

        [Header("Ball physics")]
        public float linearDrag = 0.05f, angularDrag = 1.2f, maxDepenetrationVelocity = 1f;
        public float bounciness = 0.2f, friction = 0.5f;
        [Tooltip("PhysX mass-normalized sleep energy (Unity default 0.005).")]
        public float sleepThreshold = 0.02f;
        [Tooltip("Below this speed (m/s) a ball gets settle drag (rolling resistance). PhysX spheres have none, so piles creep forever without it.")]
        public float settleSpeed = 0.25f;
        public float settleDrag = 1f, settleAngularDrag = 6f;

        [Header("Freeze (active -> resting at the exact pose)")]
        [Tooltip("Freeze when slower than this (m/s) for freezeSeconds (or when the PhysX island sleeps).")]
        public float freezeSpeed = 0.06f;
        public float freezeSeconds = 0.3f;
        [Tooltip("...and drifted less than this fraction of a diameter during that time.")]
        public float freezeMaxDriftFraction = 0.05f;

        [Header("Collapse scheduling")]
        [Tooltip("Collapse thaws per step (frame-cost smoothing only; the real limit is the active budget).")]
        public int supportThawsPerStep = 600;
        [Tooltip("Upper bound on support tests per step.")]
        public int maxSupportChecksPerStep = 6000;
        [Tooltip("Wall-clock budget for the collapse drain per step (ms).")]
        public float collapseRulesBudgetMs = 3f;
        [Tooltip("Collapse regions are vertical columns this many diameters wide; regions drain round-robin in parallel.")]
        public float regionSizeDiameters = 8f;
        [Tooltip("Minimum thaws a region gets per visit.")]
        public int minRegionShare = 8;
        [Tooltip("A ball that loses support with an open drop deeper than this (diameters) is free-falling: everything resting " +
                 "on it is re-tested in the same step with it excluded, so a whole roof over a cavity drops together.")]
        public float freeFallDiameters = 0.5f;
        [Tooltip("A live ball moving down faster than this (m/s) cannot support anything.")]
        public float fallingSpeed = 0.5f;
        [Tooltip("How long a free-falling ball stays flagged as non-supporting after it was thawed (s).")]
        public float fallingMarkSeconds = 0.2f;

        [Header("Collapse rules")]
        public int impactThawsPerStep = 20;
        [Tooltip("Speed (m/s) above which an ISOLATED active ball thaws resting balls in its path before the step.")]
        public float fastSpeed = 4f;
        [Tooltip("A fast ball with more active neighbors than this is part of a mass flow, not an impact.")]
        public int impactMaxNeighbors = 2;
        public float thawMoveFraction = 0.1f;
        [Range(120f, 180f)] public float maxSupportGapDegrees = 170f;
        public float killY = -20f;

        [Header("Interaction (final-game rule: moving balls are interactable)")]
        public bool grabActiveBalls = true;
        public bool vacuumActiveBalls = true;
        [Tooltip("Allow grabbing a ball another player is holding.")]
        public bool stealHeldBalls = false;

        [Header("Collider shell")]
        public int maxColliders = 16000;
        public float colliderReachDiameters = 1.3f;
        public float colliderKeepSeconds = 1f;
        public int colliderReleasePerStep = 256;

        [Header("Test tools")]
        public float vacuumRadius = 1.5f;
        public int vacuumPerStep = 20;
        public float undermineDepth = 8f, undermineRadius = 4f;
        public int undermineMax = 20000;
        public int dumpPerStep = 6;
        public float dumpHeight = 3f, dumpSpread = 0.6f;

        public PitSettings Clone() => (PitSettings)MemberwiseClone();
    }
}

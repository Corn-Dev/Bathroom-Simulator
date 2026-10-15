// PlayerConfig: every player dimension and tunable in one asset (deliverable A, file plan P1; design c 14, c 12, d.15).
// The owner retunes the player's size relative to the balls here (capsule, eye height, hold offset, step and mantle
// heights, reach), and every value is an absolute metre value that works on piles of 0.15-0.5 m balls.
// PlayerAvatar resolves one runtime instance in Awake and Sanitize()s it; OnValidate clamps in the editor.
// Also PlayerPitPhysicsConfig, section d's block (serialized on PlayerSpawner.physics).
using System.Collections.Generic;
using UnityEngine;

namespace BallPitGameplay
{
    [CreateAssetMenu(menuName = "Ball Pit/Player Config", fileName = "PlayerConfig")]
    public sealed class PlayerConfig : ScriptableObject
    {
        [Header("Dimensions (m)")]
        [Range(1f, 2.5f)] public float capsuleHeight = 1.8f;
        [Range(0.2f, 0.6f)] public float capsuleRadius = 0.35f;
        [Tooltip("Head height above the feet. Kept within [capsuleRadius + 0.1, capsuleHeight - 0.05].")]
        public float eyeHeight = 1.62f;
        [Tooltip("HoldPoint local position under Head (right, up, forward). Keep it outside the capsule at level aim.")]
        public Vector3 holdOffset = new Vector3(0.25f, -0.28f, 0.55f);
        [Range(0f, 0.6f)] public float stepHeight = 0.35f;
        [Range(0f, 0.1f)] public float stepMinHeight = 0.02f;
        [Range(0f, 1.5f)] public float mantleMinHeight = 0.3f;
        [Range(0.4f, 2.5f)] public float mantleMaxHeight = 1.3f;
        [Tooltip("Base interaction reach (m), times PlayerCapabilities.Reach.")]
        [Range(1f, 10f)] public float reach = 4f;

        [Header("Body")]
        [Range(20f, 200f)] public float mass = 60f;
        [Range(4, 32)] public int solverIterations = 8;
        [Range(0.5f, 20f)] public float maxDepenetrationVelocity = 4f;
        public LayerMask excludedLayers = 0;

        [Header("Locomotion")]
        [Range(0.5f, 20f)] public float walkSpeed = 4.5f;
        [Range(1f, 3f)] public float sprintMultiplier = 1.6f;
        public bool allowSprint = true;
        [Range(1f, 500f)] public float groundAcceleration = 60f;
        [Range(1f, 500f)] public float groundDeceleration = 70f;
        [Range(0f, 200f)] public float airAcceleration = 12f;
        [Range(0f, 200f)] public float airDeceleration = 1f;
        [Range(0.1f, 5f)] public float gravityScale = 2f;
        [Range(5f, 100f)] public float maxFallSpeed = 35f;
        [Range(0f, 5f)] public float jumpHeight = 1.1f;
        [Range(0f, 0.5f)] public float coyoteTime = 0.12f;
        [Range(0f, 0.5f)] public float jumpBuffer = 0.12f;

        [Header("Ground")]
        [Range(0f, 89f)] public float slopeLimit = 50f;
        [Range(0.5f, 1f)] public float groundProbeRadiusScale = 0.9f;
        [Range(0f, 1.5f)] public float groundSnapDistance = 0.45f;
        [Range(0f, 50f)] public float maxSnapSpeed = 12f;

        [Header("Climb")]
        [Range(0.1f, 20f)] public float climbSpeed = 3f;
        [Range(0f, 1f)] public float climbStrafeFactor = 0.8f;
        [Range(0f, 1f)] public float climbAttachInput = 0.5f;
        [Range(0f, 90f)] public float climbMaxFacingAngle = 60f;
        [Range(0f, 0.9f)] public float climbMaxNormalY = 0.35f;
        [Range(0f, 5f)] public float climbStickSpeed = 0.6f;
        [Range(0f, 5f)] public float climbReleaseDelay = 0.35f;
        [Range(0f, 20f)] public float wallJumpOffSpeed = 4.5f;
        [Range(0f, 20f)] public float wallJumpUpSpeed = 5f;
        [Range(0f, 2f)] public float wallReattachLockout = 0.35f;
        [Tooltip("Climb probe height as a fraction of capsuleHeight.")]
        [Range(0.2f, 0.9f)] public float climbProbeHeightFraction = 0.55f;
        [Tooltip("Climb probe radius as a fraction of capsuleRadius.")]
        [Range(0.3f, 1f)] public float climbProbeRadiusFraction = 0.85f;
        [Range(0f, 0.5f)] public float climbProbeReach = 0.15f;
        [Range(0f, 1f)] public float climbLostSeconds = 0.1f;

        [Header("Mantle")]
        [Range(0.1f, 1.5f)] public float mantleForwardReach = 0.6f;
        [Range(0.05f, 0.4f)] public float ledgeProbeRadius = 0.15f;
        [Range(0.05f, 1f)] public float mantleDurationBase = 0.22f;
        [Range(0f, 1f)] public float mantleDurationPerMeter = 0.18f;
        [Range(0f, 10f)] public float mantleExitSpeed = 1.5f;
        [Range(0f, 2f)] public float mantleLockoutSeconds = 0.2f;
        [Range(0.2f, 0.9f)] public float mantleRiseFraction = 0.6f;

        [Header("Contacts")]
        [Tooltip("Watchdog wedge test: two contacts whose normals' dot is below this oppose each other.")]
        [Range(-1f, 0f)] public float wedgeOpposingDot = -0.3f;
        [Tooltip("Contacts with normal.y below this count as ceiling.")]
        [Range(-1f, 0f)] public float ceilingNormalY = -0.3f;

        [Header("Teleport")]
        [Range(0.05f, 1f)] public float teleportLiftStep = 0.25f;
        [Range(0f, 10f)] public float teleportLiftMax = 2f;

        [Header("Pile scramble")]
        [Range(0f, 20f)] public float scrambleUpSpeed = 6f;
        [Range(0f, 20f)] public float scrambleForwardSpeed = 2.5f;
        [Range(0.05f, 2f)] public float scrambleCooldown = 0.3f;
        [Range(0f, 0.5f)] public float scrambleGrace = 0.15f;
        public bool scrambleOnHold = true;

        [Header("Anti-stuck watchdog")]
        [Range(0.01f, 1f)] public float stuckProgressDistance = 0.1f;
        [Range(0.2f, 10f)] public float stuckSeconds = 1.5f;
        [Range(0f, 20f)] public float unstickUpSpeed = 4f;
        [Range(0f, 20f)] public float unstickForwardSpeed = 2f;
        [Range(0f, 20f)] public float unstickNormalSpeed = 1.5f;
        [Range(0f, 2f)] public float maxDepenetrationPerAttempt = 0.6f;
        [Range(1, 20)] public int unstickAttemptsBeforeOffer = 3;
        [Range(1f, 120f)] public float attemptWindowSeconds = 12f;
        [Range(0.1f, 20f)] public float escapeDistance = 1.5f;

        [Header("Respawn")]
        [Range(0f, 60f)] public float respawnCooldown = 3f;
        [Tooltip("Off: R always respawns (after the cooldown). On: only after the watchdog offered it.")]
        public bool respawnRequiresOffer = false;

        [Header("Out of bounds")]
        [Tooltip("Falling this far below the pit floor teleports the player to its spawn point.")]
        [Range(1f, 50f)] public float fallBelowFloor = 5f;
        public bool respawnOutsidePlayBounds = true;

        [Header("Interaction")]
        [Range(0f, 50f)] public float throwSpeed = 12f;
        [Range(0f, 10f)] public float throwUpBias = 2.5f;
        public bool throwInheritsVelocity = true;

        [Header("Collider-shell sources")]
        [Range(0f, 0.5f)] public float shellSlack = 0.05f;
        [Range(0f, 1f)] public float groundCover = 0.1f;

        [Header("View feel")]
        [Range(60f, 89f)] public float pitchLimit = 85f;
        [Range(0f, 0.5f)] public float eyeSmoothTime = 0.08f;
        [Range(0f, 1f)] public float groundNormalSmoothTime = 0.15f;
        [Range(0f, 0.2f)] public float holdClearance = 0.03f;

        [Header("Pile hook (not implemented in A)")]
        [Tooltip("Off: walking never disturbs resting balls (the pile is solid underfoot).")]
        public bool playersDisturbPile = false;
        [Range(0f, 20f)] public float pileDisturbSpeed = 1.5f;

#if UNITY_EDITOR
        [System.NonSerialized] private string lastValidation;
#endif

        /// <summary>
        /// Clamps every field to its range and applies the cross-field rules (non-finite values get the default).
        /// Idempotent. Returns true when anything changed.
        /// </summary>
        public bool Sanitize()
        {
            bool c = false;
            c |= Fix(ref capsuleHeight, 1f, 2.5f, 1.8f);
            c |= Fix(ref capsuleRadius, 0.2f, 0.6f, 0.35f);
            c |= Fix(ref eyeHeight, capsuleRadius + 0.1f, capsuleHeight - 0.05f, 1.62f);
            if (!Finite(holdOffset.x) || !Finite(holdOffset.y) || !Finite(holdOffset.z)) { holdOffset = new Vector3(0.25f, -0.28f, 0.55f); c = true; }
            c |= Fix(ref stepHeight, 0f, 0.6f, 0.35f);
            c |= Fix(ref stepMinHeight, 0f, 0.1f, 0.02f);
            if (stepHeight > 0f && stepMinHeight >= stepHeight) { stepMinHeight = stepHeight * 0.5f; c = true; }
            c |= Fix(ref mantleMaxHeight, 0.4f, Mathf.Min(2.5f, capsuleHeight), 1.3f);
            c |= Fix(ref mantleMinHeight, 0f, Mathf.Min(1.5f, mantleMaxHeight), 0.3f);
            c |= Fix(ref reach, 1f, 10f, 4f);

            c |= Fix(ref mass, 20f, 200f, 60f);
            c |= Fix(ref solverIterations, 4, 32);
            c |= Fix(ref maxDepenetrationVelocity, 0.5f, 20f, 4f);

            c |= Fix(ref walkSpeed, 0.5f, 20f, 4.5f);
            c |= Fix(ref sprintMultiplier, 1f, 3f, 1.6f);
            c |= Fix(ref groundAcceleration, 1f, 500f, 60f);
            c |= Fix(ref groundDeceleration, 1f, 500f, 70f);
            c |= Fix(ref airAcceleration, 0f, 200f, 12f);
            c |= Fix(ref airDeceleration, 0f, 200f, 1f);
            c |= Fix(ref gravityScale, 0.1f, 5f, 2f);
            c |= Fix(ref maxFallSpeed, 5f, 100f, 35f);
            c |= Fix(ref jumpHeight, 0f, 5f, 1.1f);
            c |= Fix(ref coyoteTime, 0f, 0.5f, 0.12f);
            c |= Fix(ref jumpBuffer, 0f, 0.5f, 0.12f);

            c |= Fix(ref slopeLimit, 0f, 89f, 50f);
            c |= Fix(ref groundProbeRadiusScale, 0.5f, 1f, 0.9f);
            c |= Fix(ref groundSnapDistance, 0f, 1.5f, 0.45f);
            c |= Fix(ref maxSnapSpeed, 0f, 50f, 12f);

            c |= Fix(ref climbSpeed, 0.1f, 20f, 3f);
            c |= Fix(ref climbStrafeFactor, 0f, 1f, 0.8f);
            c |= Fix(ref climbAttachInput, 0f, 1f, 0.5f);
            c |= Fix(ref climbMaxFacingAngle, 0f, 90f, 60f);
            c |= Fix(ref climbMaxNormalY, 0f, 0.9f, 0.35f);
            c |= Fix(ref climbStickSpeed, 0f, 5f, 0.6f);
            c |= Fix(ref climbReleaseDelay, 0f, 5f, 0.35f);
            c |= Fix(ref wallJumpOffSpeed, 0f, 20f, 4.5f);
            c |= Fix(ref wallJumpUpSpeed, 0f, 20f, 5f);
            c |= Fix(ref wallReattachLockout, 0f, 2f, 0.35f);
            c |= Fix(ref climbProbeHeightFraction, 0.2f, 0.9f, 0.55f);
            c |= Fix(ref climbProbeRadiusFraction, 0.3f, 1f, 0.85f);
            c |= Fix(ref climbProbeReach, 0f, 0.5f, 0.15f);
            c |= Fix(ref climbLostSeconds, 0f, 1f, 0.1f);

            c |= Fix(ref mantleForwardReach, 0.1f, 1.5f, 0.6f);
            c |= Fix(ref ledgeProbeRadius, 0.05f, 0.4f, 0.15f);
            c |= Fix(ref mantleDurationBase, 0.05f, 1f, 0.22f);
            c |= Fix(ref mantleDurationPerMeter, 0f, 1f, 0.18f);
            c |= Fix(ref mantleExitSpeed, 0f, 10f, 1.5f);
            c |= Fix(ref mantleLockoutSeconds, 0f, 2f, 0.2f);
            c |= Fix(ref mantleRiseFraction, 0.2f, 0.9f, 0.6f);

            c |= Fix(ref wedgeOpposingDot, -1f, 0f, -0.3f);
            c |= Fix(ref ceilingNormalY, -1f, 0f, -0.3f);
            c |= Fix(ref teleportLiftStep, 0.05f, 1f, 0.25f);
            c |= Fix(ref teleportLiftMax, 0f, 10f, 2f);

            c |= Fix(ref scrambleUpSpeed, 0f, 20f, 6f);
            c |= Fix(ref scrambleForwardSpeed, 0f, 20f, 2.5f);
            c |= Fix(ref scrambleCooldown, 0.05f, 2f, 0.3f);
            c |= Fix(ref scrambleGrace, 0f, 0.5f, 0.15f);

            c |= Fix(ref stuckProgressDistance, 0.01f, 1f, 0.1f);
            c |= Fix(ref stuckSeconds, 0.2f, 10f, 1.5f);
            c |= Fix(ref unstickUpSpeed, 0f, 20f, 4f);
            c |= Fix(ref unstickForwardSpeed, 0f, 20f, 2f);
            c |= Fix(ref unstickNormalSpeed, 0f, 20f, 1.5f);
            c |= Fix(ref maxDepenetrationPerAttempt, 0f, 2f, 0.6f);
            c |= Fix(ref unstickAttemptsBeforeOffer, 1, 20);
            c |= Fix(ref attemptWindowSeconds, 1f, 120f, 12f);
            c |= Fix(ref escapeDistance, 0.1f, 20f, 1.5f);

            c |= Fix(ref respawnCooldown, 0f, 60f, 3f);
            c |= Fix(ref fallBelowFloor, 1f, 50f, 5f);
            c |= Fix(ref throwSpeed, 0f, 50f, 12f);
            c |= Fix(ref throwUpBias, 0f, 10f, 2.5f);
            c |= Fix(ref shellSlack, 0f, 0.5f, 0.05f);
            c |= Fix(ref groundCover, 0f, 1f, 0.1f);
            c |= Fix(ref pitchLimit, 60f, 89f, 85f);
            c |= Fix(ref eyeSmoothTime, 0f, 0.5f, 0.08f);
            c |= Fix(ref groundNormalSmoothTime, 0f, 1f, 0.15f);
            c |= Fix(ref holdClearance, 0f, 0.2f, 0.03f);
            c |= Fix(ref pileDisturbSpeed, 0f, 20f, 1.5f);
            return c;
        }

        /// <summary>Advisory checks that Sanitize does not fix (the hold point should sit outside the capsule at level aim).</summary>
        public void CollectWarnings(List<string> warnings)
        {
            float horizontal = new Vector2(holdOffset.x, holdOffset.z).magnitude;
            if (horizontal < capsuleRadius + 0.25f)
                warnings.Add($"holdOffset is {horizontal:0.###} m from the head axis; keep it at least capsuleRadius + 0.25 = " +
                             $"{capsuleRadius + 0.25f:0.###} m so a held ball stays outside the capsule at level aim");
        }

        private static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);

        private static bool Fix(ref float v, float min, float max, float def)
        {
            float old = v;
            if (!Finite(v)) v = def;
            v = Mathf.Clamp(v, min, max);
            return float.IsNaN(old) || old != v;
        }

        private static bool Fix(ref int v, int min, int max)
        {
            int old = v;
            v = Mathf.Clamp(v, min, max);
            return old != v;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            bool changed = Sanitize();
            var warnings = new List<string>();
            CollectWarnings(warnings);
            if (changed) warnings.Insert(0, "out-of-range values were clamped");
            string text = warnings.Count == 0 ? null : string.Join("\n", warnings);
            if (text == lastValidation) return;
            lastValidation = text;
            if (text != null) Debug.LogWarning($"[PlayerConfig] '{name}':\n{text}", this);
        }
#endif
    }

    /// <summary>Player and pit physics tunables (design d.15), serialized on PlayerSpawner.physics.</summary>
    [System.Serializable]
    public sealed class PlayerPitPhysicsConfig
    {
        [Header("Freeze blocker")]
        [Tooltip("Gap (m) that still counts as touching a player capsule.")]
        [Min(0f)] public float freezeTouchMargin = 0.02f;
        [Tooltip("A touching ball whose nearest capsule point is within this angle of straight above it may still freeze " +
                 "(it is under the feet). 0 = the brief's literal rule: no touching ball freezes.")]
        [Range(0f, 90f)] public float underFootAngleDeg = 40f;

        [Header("Held balls")]
        [Tooltip("Clearance (m) before a released ball collides with a capsule that ignored it again.")]
        [Min(0f)] public float releaseClearMargin = 0.01f;
        [Tooltip("Host grab check: allowed distance beyond the player's reach (latency and interpolation).")]
        [Min(0f)] public float grabReachSlack = 1f;

        [Header("Host shell sources and remote pushers")]
        [Min(0f)] public float sourceMaxSpeed = 30f;
        [Tooltip("A remote player jump larger than this (m) teleports its pusher instead of sweeping it.")]
        [Min(0f)] public float pusherTeleportDistance = 1.5f;

        [Header("Client collider shell")]
        [Min(256)] public int clientShellMaxColliders = 4096;
        [Min(0f)] public float clientShellKeepSeconds = 0.5f;

        [Header("Client live-ball proxies")]
        public bool clientLiveBallColliders = true;
        [Range(1, 1024)] public int liveProxyBudget = 256;
        [Min(0f)] public float liveProxyRange = 4f;
        [Min(0.01f)] public float liveProxyRadiusScale = 1f;
        [Min(0f)] public float liveProxyTeleportDistance = 1f;

        [Header("Spawning")]
        [Min(0f)] public float spawnClearance = 0.1f;
        [Min(0f)] public float spawnRingMargin = 2f;
        [Min(0f)] public float spawnProbeUp = 2f;
        [Min(0f)] public float spawnProbeDown = 4f;

        /// <summary>Clamps the proxy budget to 1..1024 and the client pool to at least 256; non-finite values get defaults.</summary>
        public void Sanitize()
        {
            liveProxyBudget = Mathf.Clamp(liveProxyBudget, 1, 1024);
            clientShellMaxColliders = Mathf.Max(256, clientShellMaxColliders);
            Fix(ref freezeTouchMargin, 0.02f); Fix(ref releaseClearMargin, 0.01f); Fix(ref grabReachSlack, 1f);
            Fix(ref sourceMaxSpeed, 30f); Fix(ref pusherTeleportDistance, 1.5f); Fix(ref clientShellKeepSeconds, 0.5f);
            Fix(ref liveProxyRange, 4f); Fix(ref liveProxyTeleportDistance, 1f); Fix(ref spawnClearance, 0.1f);
            Fix(ref spawnRingMargin, 2f); Fix(ref spawnProbeUp, 2f); Fix(ref spawnProbeDown, 4f);
            if (float.IsNaN(underFootAngleDeg) || float.IsInfinity(underFootAngleDeg)) underFootAngleDeg = 40f;
            underFootAngleDeg = Mathf.Clamp(underFootAngleDeg, 0f, 90f);
            if (float.IsNaN(liveProxyRadiusScale) || float.IsInfinity(liveProxyRadiusScale) || liveProxyRadiusScale < 0.01f) liveProxyRadiusScale = 1f;
        }

        private static void Fix(ref float v, float def)
        {
            if (float.IsNaN(v) || float.IsInfinity(v)) v = def;
            if (v < 0f) v = 0f;
        }
    }
}

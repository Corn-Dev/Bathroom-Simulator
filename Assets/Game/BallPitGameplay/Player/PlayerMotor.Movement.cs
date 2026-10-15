// PlayerMotor.Movement: the ground and locomotion parts of the motor (deliverable A, file plan P7; design c 7):
// contact buckets from the last Simulate, the snap probe, the crevice rule, step-up assist, velocity control, jump with
// coyote time and buffer, manual gravity, and the collider-shell sources section d turns into colliders. Every value is
// an absolute metre or second from PlayerConfig, valid for 0.15-0.5 m balls. Also MotorMath, the pure math (public so
// EditMode tests in Assembly-CSharp-Editor can reach it).
using System.Collections.Generic;
using BallPitFinal;
using UnityEngine;

namespace BallPitGameplay
{
    public sealed partial class PlayerMotor
    {
        private const int MaxContacts = 16, MaxSteep = 16, StepsLarge = 1 << 20;

        private readonly ContactPoint[] contactBuffer = new ContactPoint[MaxContacts];
        private int groundCount, steepCount, climbCount, steepStored;
        private Vector3 groundNormalSum, steepNormalSum, climbNormalSum;
        private bool steepPile;
        private readonly Vector3[] steepNormals = new Vector3[MaxSteep];
        private readonly float[] steepHeights = new float[MaxSteep];
        private readonly bool[] steepIsPlayer = new bool[MaxSteep];

        private bool grounded, groundedPrevious, coyoteAvailable;
        private Vector3 groundNormal = Vector3.up;
        private int stepsSinceGrounded, stepsSinceJump = StepsLarge, stepUpCooldown, lastJumpPresses;
        private float timeSinceGrounded, jumpBufferTimer;
        private float minGroundDot = 0.6428f; // cos(slopeLimit), refreshed in BuildMasks
        private readonly Collider[] overlap = new Collider[32];
        private readonly List<int> gather = new List<int>(16);

        // ------------------------------------------------------------------ Contacts (gathered inside Physics.Simulate)

        private void OnCollisionEnter(Collision c) => GatherContacts(c, true);
        private void OnCollisionStay(Collision c) => GatherContacts(c, false);

        private void GatherContacts(Collision c, bool entered)
        {
            if ((mode != MotorMode.Driven && mode != MotorMode.Climbing) || config == null) return;
            Collider other = c.collider;
            if (other == null) return;
            int layer = other.gameObject.layer;
            bool isPlayer = playerLayer >= 0 && layer == playerLayer;
            bool isBall = layer == ballLayer;
            if (entered && isBall && config.playersDisturbPile && PileContact != null)
            {
                float speed = c.relativeVelocity.magnitude;
                if (speed > config.pileDisturbSpeed && c.contactCount > 0)
                {
                    ContactPoint first = c.GetContact(0);
                    try { PileContact(new PileContactInfo(other, first.point, first.normal, speed)); }
                    catch (System.Exception e) { Debug.LogException(e, this); }
                }
            }
            int n = c.GetContacts(contactBuffer);
            float feetY = rb.position.y;
            for (int i = 0; i < n; i++)
            {
                Vector3 normal = contactBuffer[i].normal; // from the other collider toward the player
                ContactBucket bucket = MotorMath.SortContact(normal.y, minGroundDot, config.ceilingNormalY, isPlayer);
                if (bucket == ContactBucket.Ground) { groundCount++; groundNormalSum += normal; continue; }
                if (bucket != ContactBucket.Steep) continue; // ceiling: PhysX resolves head bumps
                steepCount++;
                steepNormalSum += normal;
                if (isBall) steepPile = true;
                if (steepStored < MaxSteep)
                {
                    steepNormals[steepStored] = normal;
                    steepHeights[steepStored] = contactBuffer[i].point.y - feetY;
                    steepIsPlayer[steepStored] = isPlayer;
                    steepStored++;
                }
                if (!isPlayer && Mathf.Abs(normal.y) <= config.climbMaxNormalY &&
                    ClimbableSurface.TryGet(other, out ClimbableSurface surface) && surface.AppliesTo(activeSite))
                { climbCount++; climbNormalSum += normal; }
            }
        }

        private void ClearContacts()
        {
            groundCount = steepCount = climbCount = steepStored = 0;
            groundNormalSum = steepNormalSum = climbNormalSum = Vector3.zero;
            steepPile = false;
        }

        // Freeze and teleport: contacts, ground state and timers.
        private void ResetMotionState()
        {
            ClearContacts();
            grounded = groundedPrevious = coyoteAvailable = false;
            groundNormal = Vector3.up;
            stepsSinceJump = StepsLarge;
            timeSinceGrounded = 0f;
            jumpBufferTimer = 0f;
            stepUpCooldown = 0;
            mantleVelocity = Vector3.zero;
        }

        // ------------------------------------------------------------------ Ground state (start of each tick)

        private void EvaluateGround(float dt, Vector3 feet)
        {
            if (stepsSinceJump < StepsLarge) stepsSinceJump++;
            bool onGround = groundCount > 0;
            Vector3 n = onGround ? groundNormalSum.normalized : Vector3.up;

            // Snap probe (stick-to-ground): a big sphere from inside the capsule, so it reports the macro slope.
            if (!onGround && mode == MotorMode.Driven && stepsSinceGrounded <= 1 && stepsSinceJump > 2 &&
                rb.velocity.magnitude <= config.maxSnapSpeed && capsule != null)
            {
                float r = capsule.radius, probe = r * config.groundProbeRadiusScale;
                if (Physics.SphereCast(feet + Vector3.up * r, probe, Vector3.down, out RaycastHit hit, (r - probe) + config.groundSnapDistance,
                                       collisionMask, QueryTriggerInteraction.Ignore) && hit.normal.y >= minGroundDot)
                {
                    onGround = true;
                    n = hit.normal;
                    rb.velocity = MotorMath.ReaimAlongSurface(rb.velocity, n);
                }
            }
            // Crevice rule: wedged between two balls (or a ball and a wall) is standing.
            if (!onGround && MotorMath.CreviceGround(steepCount, steepNormalSum, minGroundDot))
            {
                onGround = true;
                n = steepNormalSum.normalized;
            }

            groundedPrevious = grounded;
            grounded = onGround;
            groundNormal = onGround ? n : Vector3.up;
            if (onGround)
            {
                stepsSinceGrounded = 0;
                timeSinceGrounded = 0f;
                if (stepsSinceJump > 2) coyoteAvailable = true; // a real ground step, not the jump's own take-off
            }
            else
            {
                if (stepsSinceGrounded < StepsLarge) stepsSinceGrounded++;
                timeSinceGrounded += dt;
            }
        }

        private void BuildFrame(float dt, Vector3 feet, float yaw, bool inputActive, Vector2 move, bool jumpHeld, bool sprintHeld)
        {
            Quaternion q = Quaternion.Euler(0f, yaw, 0f);
            Vector3 fwd = q * Vector3.forward, right = q * Vector3.right;
            Vector3 desired = fwd * move.y + right * move.x;
            frame = new MotorFrame
            {
                Dt = dt, Mode = mode, Feet = feet, Velocity = Velocity, Yaw = yaw, LookForwardH = fwd,
                InputActive = inputActive, Move = move, DesiredDirH = desired.sqrMagnitude > 1e-6f ? desired.normalized : Vector3.zero,
                JumpBuffered = jumpBufferTimer > 0f, JumpHeld = jumpHeld, SprintHeld = sprintHeld,
                Grounded = grounded, GroundNormal = groundNormal,
                SteepCount = steepCount, SteepNormalSum = steepNormalSum, SteepPile = steepPile, Wedged = ComputeWedged(),
                ClimbCount = climbCount, ClimbNormalSum = climbNormalSum,
                CapsuleRadius = CapsuleRadius, CapsuleHeight = CapsuleHeight,
            };
        }

        // Two steep contacts whose horizontal normals oppose each other (lowest pairwise dot below wedgeOpposingDot).
        private bool ComputeWedged()
        {
            if (steepStored < 2) return false;
            float lowest = 1f;
            for (int i = 0; i < steepStored; i++)
            {
                Vector3 a = new Vector3(steepNormals[i].x, 0f, steepNormals[i].z);
                if (a.sqrMagnitude < 1e-6f) continue;
                a.Normalize();
                for (int j = i + 1; j < steepStored; j++)
                {
                    Vector3 b = new Vector3(steepNormals[j].x, 0f, steepNormals[j].z);
                    if (b.sqrMagnitude < 1e-6f) continue;
                    lowest = Mathf.Min(lowest, Vector3.Dot(a, b.normalized));
                }
            }
            return lowest < config.wedgeOpposingDot;
        }

        // ------------------------------------------------------------------ Locomotion (Driven)

        private void Locomotion(float dt, Vector2 move, bool sprintHeld)
        {
            Vector3 v = rb.velocity;
            Vector3 g = Physics.gravity * config.gravityScale;
            Quaternion q = Quaternion.Euler(0f, frame.Yaw, 0f);
            Vector3 fwd = q * Vector3.forward, right = q * Vector3.right;
            bool hasInput = move.sqrMagnitude > 1e-4f;
            float maxSpeed = MotorMath.MaxSpeed(config.walkSpeed, stats != null ? stats.MoveSpeed : 1f,
                                                sprintHeld && config.allowSprint && move.y > 0f, config.sprintMultiplier);
            if (grounded)
            {
                Vector3 n = groundNormal;
                Vector3 f = Vector3.ProjectOnPlane(fwd, n).normalized, r = Vector3.ProjectOnPlane(right, n).normalized;
                Vector3 desired = (f * move.y + r * move.x) * maxSpeed;
                float vn = Vector3.Dot(v, n);
                Vector3 planar = MotorMath.ControlPlanar(v - n * vn, desired, config.groundAcceleration, config.groundDeceleration, hasInput, dt);
                v = planar + n * vn;
                if (stepsSinceJump > 2) v = MotorMath.RemoveSeparation(v, n); // pile bumps never hop the player
                v += MotorMath.GroundGravity(g, n, dt);                       // into the surface only: no idle sliding
            }
            else
            {
                Vector3 desired = (fwd * move.y + right * move.x) * maxSpeed;
                Vector3 planar = MotorMath.ControlPlanar(new Vector3(v.x, 0f, v.z), desired, config.airAcceleration,
                                                         config.airDeceleration, hasInput, dt);
                v = new Vector3(planar.x, v.y, planar.z) + g * dt;
                v.y = Mathf.Max(v.y, -config.maxFallSpeed);
            }
            if (jumpBufferTimer > 0f && coyoteAvailable && (grounded || timeSinceGrounded <= config.coyoteTime))
            {
                v.y = Mathf.Max(v.y, MotorMath.JumpSpeed(g.magnitude, config.jumpHeight)); // straight up, never along a pile normal
                OnJumped();
            }
            rb.velocity = v;
            if (grounded && move.magnitude > 0.1f && stepsSinceJump > 2 && stepUpCooldown <= 0) TryStepUp(frame.DesiredDirH);
        }

        private void OnJumped()
        {
            stepsSinceJump = 0;
            jumpBufferTimer = 0f;
            coyoteAvailable = false;
        }

        // Step-up assist over bumps up to stepHeight (c 7): a qualifying steep contact, a walkable top found by the big
        // probe sphere ahead of the axis, and a clear capsule there. Off while overlap checks are disabled.
        private void TryStepUp(Vector3 dirH)
        {
            if (!OverlapChecksEnabled || dirH.sqrMagnitude < 0.5f) return;
            float stepHeight = config.stepHeight, stepMin = config.stepMinHeight;
            bool candidate = false;
            for (int i = 0; i < steepStored && !candidate; i++)
            {
                if (steepIsPlayer[i] || steepHeights[i] <= stepMin || steepHeights[i] > stepHeight) continue;
                Vector3 nH = new Vector3(steepNormals[i].x, 0f, steepNormals[i].z);
                candidate = nH.sqrMagnitude > 1e-6f && Vector3.Dot(dirH, -nH.normalized) >= 0.5f;
            }
            if (!candidate) return;

            Vector3 feet = rb.position;
            float r = capsule.radius, h = capsule.height, probe = r * config.groundProbeRadiusScale;
            Vector3 start = feet + Vector3.up * (stepHeight + probe + MotorMath.Skin) + dirH * (r + MotorMath.ProbeLead);
            if (Physics.CheckSphere(start, probe, collisionMask, QueryTriggerInteraction.Ignore)) return; // taller than a step
            if (!Physics.SphereCast(start, probe, Vector3.down, out RaycastHit hit, stepHeight + MotorMath.Skin, collisionMask,
                                    QueryTriggerInteraction.Ignore) || hit.normal.y < minGroundDot) return;
            float top = hit.point.y - feet.y;
            if (top <= stepMin || top > stepHeight) return;
            Vector3 target = feet + Vector3.up * (top + MotorMath.Skin) + dirH * MotorMath.ProbeLead;
            float rc = r - MotorMath.SkinTight;
            if (Physics.CheckCapsule(target + Vector3.up * r, target + Vector3.up * Mathf.Max(r, h - r), rc, collisionMask,
                                     QueryTriggerInteraction.Ignore)) return;

            rb.position = target;
            Vector3 v = rb.velocity;
            if (v.y < 0f) { v.y = 0f; rb.velocity = v; }
            stepUpCooldown = MotorMath.StepCooldownSteps;
            if (SteppedUp == null) return;
            try { SteppedUp(top + MotorMath.Skin); }
            catch (System.Exception e) { Debug.LogException(e, this); }
        }

        // ------------------------------------------------------------------ Capsule probes (teleport, mantle, unstick)

        /// <summary>True when the capsule (radius shrunk by SkinTight, lifted by Skin) overlaps a non-trigger collider in
        /// the collision mask, plus other players when includePlayers; this body is skipped.</summary>
        internal bool IsCapsuleBlocked(Vector3 feet, float radius, bool includePlayers)
        {
            if (capsule == null) return false;
            float r = Mathf.Max(0.01f, radius - MotorMath.SkinTight), h = capsule.height;
            int mask = collisionMask | (includePlayers && playerLayer >= 0 ? 1 << playerLayer : 0);
            Vector3 a = feet + Vector3.up * (radius + MotorMath.Skin), b = feet + Vector3.up * Mathf.Max(radius + MotorMath.Skin, h - radius);
            int n = Physics.OverlapCapsuleNonAlloc(a, b, r, overlap, mask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
                if (overlap[i] != null && overlap[i].attachedRigidbody != rb) return true;
            return false;
        }

        /// <summary>Three Resting.Gather probes along the capsule (radius r + ball radius): the pile's own grid, which works
        /// without collider-shell coverage on host and client alike.</summary>
        internal bool RestingBallsInCapsule(PitWorld w, Vector3 feet)
        {
            if (w == null || capsule == null) return false;
            float r = capsule.radius, h = capsule.height, probe = r + w.Diameter * 0.5f;
            w.Resting.Gather(feet + Vector3.up * r, probe, w.Store.pos, gather);
            if (gather.Count > 0) return true;
            w.Resting.Gather(feet + Vector3.up * (h * 0.5f), probe, w.Store.pos, gather);
            if (gather.Count > 0) return true;
            w.Resting.Gather(feet + Vector3.up * (h - r), probe, w.Store.pos, gather);
            return gather.Count > 0;
        }

        /// <summary>Unstick step 1: the summed separation from every overlapping collider at the current pose (this body
        /// skipped), clamped to maxDistance, plus SkinTight along it.</summary>
        internal Vector3 ComputeDepenetration(float maxDistance)
        {
            if (!OverlapChecksEnabled) return Vector3.zero;
            Vector3 feet = rb.position;
            float r = capsule.radius, h = capsule.height;
            int n = Physics.OverlapCapsuleNonAlloc(feet + Vector3.up * r, feet + Vector3.up * Mathf.Max(r, h - r), r, overlap,
                                                   collisionMask, QueryTriggerInteraction.Ignore);
            Vector3 sum = Vector3.zero;
            for (int i = 0; i < n; i++)
            {
                Collider other = overlap[i];
                if (other == null || other.attachedRigidbody == rb) continue;
                if (Physics.ComputePenetration(capsule, feet, rb.rotation, other, other.transform.position, other.transform.rotation,
                                               out Vector3 dir, out float distance))
                    sum += dir * distance;
            }
            if (sum.sqrMagnitude < 1e-10f) return Vector3.zero;
            sum = Vector3.ClampMagnitude(sum, maxDistance);
            return sum + sum.normalized * MotorMath.SkinTight;
        }

        // ------------------------------------------------------------------ Collider-shell sources (section d)

        /// <summary>This player's three shell spheres (center, radius) at the physics pose, leading along the velocity.
        /// results is cleared first.</summary>
        public void GetShellSources(List<Vector4> results)
        {
            if (results == null) return;
            if (rb == null || capsule == null) { results.Clear(); return; }
            ComputeShellSources(rb.position, Velocity, Time.fixedDeltaTime, capsule.radius, capsule.height, config, ballDiameter, results);
        }

        /// <summary>
        /// Three spheres hugging the capsule, from the capsule plus the ball diameter d (brief 11.9): feet sphere at
        /// up * r with radius r + inflate + groundCover; mid at h / 2 and head at h - r with r + inflate, where
        /// inflate = d + shellSlack + |v| dt, all around feet + v dt. results is cleared first.
        /// </summary>
        public static void ComputeShellSources(Vector3 feet, Vector3 velocity, float dt, float capsuleRadius, float capsuleHeight,
                                               PlayerConfig cfg, float ballDiameter, List<Vector4> results)
        {
            if (results == null) return;
            results.Clear();
            if (!MotorMath.Finite(velocity) || float.IsNaN(dt) || float.IsInfinity(dt)) { velocity = Vector3.zero; dt = 0f; }
            float slack = cfg != null ? cfg.shellSlack : 0.05f, cover = cfg != null ? cfg.groundCover : 0.1f;
            Vector3 p = feet + velocity * dt;
            float r = capsuleRadius, h = capsuleHeight;
            float inflate = ballDiameter + slack + velocity.magnitude * dt;
            results.Add(new Vector4(p.x, p.y + r, p.z, r + inflate + cover));
            results.Add(new Vector4(p.x, p.y + h * 0.5f, p.z, r + inflate));
            results.Add(new Vector4(p.x, p.y + h - r, p.z, r + inflate));
        }
    }

    public enum ContactBucket { Ground, Steep, Ceiling }
    public enum OutOfBoundsTarget { None, Spawn, Fallback }

    /// <summary>The motor's out-of-bounds test (c 7 step 1): below floor - fallBelowFloor, or outside PlayBounds at
    /// feet + BoundsProbeLift while the rule is on. False with no world context.</summary>
    public readonly struct OutOfBoundsRule
    {
        public OutOfBoundsRule(bool hasWorld, float floorY, float fallBelowFloor, bool requirePlayBounds, Bounds playBounds)
        {
            HasWorld = hasWorld; FloorY = floorY; FallBelowFloor = fallBelowFloor; RequirePlayBounds = requirePlayBounds; PlayBounds = playBounds;
        }

        public bool HasWorld { get; }
        public float FloorY { get; }
        public float FallBelowFloor { get; }
        public bool RequirePlayBounds { get; }
        public Bounds PlayBounds { get; }

        public bool IsOut(Vector3 feet)
        {
            if (!HasWorld) return false;
            if (feet.y < FloorY - FallBelowFloor) return true;
            return RequirePlayBounds && !PlayBounds.Contains(feet + Vector3.up * MotorMath.BoundsProbeLift);
        }
    }

    /// <summary>The motor's pure math and geometric skins (c 7, c 13). Feel and size values live in PlayerConfig.</summary>
    public static class MotorMath
    {
        public const float SkinTight = 0.01f, Skin = 0.02f, ProbeLead = 0.05f;
        /// <summary>The lift of the bounds test; section d's spawn resolver uses the same value.</summary>
        public const float BoundsProbeLift = 0.1f;
        public const int StepCooldownSteps = 2;

        /// <summary>Ground: walkable and not a player. Steep: otherwise, above ceilingNormalY. Ceiling: the rest.</summary>
        public static ContactBucket SortContact(float normalY, float minGroundDot, float ceilingNormalY, bool otherIsPlayer)
        {
            if (normalY >= minGroundDot && !otherIsPlayer) return ContactBucket.Ground;
            return normalY > ceilingNormalY ? ContactBucket.Steep : ContactBucket.Ceiling;
        }

        /// <summary>Two or more steep contacts whose summed normal is walkable count as ground.</summary>
        public static bool CreviceGround(int steepCount, Vector3 steepNormalSum, float minGroundDot) =>
            steepCount >= 2 && steepNormalSum.sqrMagnitude > 1e-8f && steepNormalSum.normalized.y >= minGroundDot;

        public static float JumpSpeed(float gravityMagnitude, float jumpHeight) =>
            Mathf.Sqrt(2f * Mathf.Max(0f, gravityMagnitude) * Mathf.Max(0f, jumpHeight));

        public static float MaxSpeed(float walkSpeed, float moveMultiplier, bool sprinting, float sprintMultiplier) =>
            walkSpeed * moveMultiplier * (sprinting ? sprintMultiplier : 1f);

        /// <summary>Moves the planar velocity toward desired by (input ? accel : decel) * dt.</summary>
        public static Vector3 ControlPlanar(Vector3 planar, Vector3 desired, float accel, float decel, bool hasInput, float dt) =>
            Vector3.MoveTowards(planar, desired, (hasInput ? accel : decel) * dt);

        /// <summary>Removes a positive (separating) component along n.</summary>
        public static Vector3 RemoveSeparation(Vector3 v, Vector3 n)
        {
            float d = Vector3.Dot(v, n);
            return d > 0f ? v - n * d : v;
        }

        /// <summary>The into-surface part of gravity for one step: nothing pulls along the slope.</summary>
        public static Vector3 GroundGravity(Vector3 g, Vector3 n, float dt) => n * (Vector3.Dot(g, n) * dt);

        /// <summary>A velocity pointing away from the surface is re-aimed along it at the same speed.</summary>
        public static Vector3 ReaimAlongSurface(Vector3 v, Vector3 n)
        {
            float d = Vector3.Dot(v, n);
            if (d <= 0f) return v;
            Vector3 along = v - n * d;
            return along.sqrMagnitude > 1e-10f ? along.normalized * v.magnitude : Vector3.zero;
        }

        /// <summary>The loop guard (c 7 step 1): the spawn pose if it is in bounds, else the fallback if it is, else None.
        /// Never returns a pose that fails rule.IsOut.</summary>
        public static OutOfBoundsTarget ChooseOutOfBoundsTarget(in OutOfBoundsRule rule, bool hasSpawn, Vector3 spawn, bool hasFallback,
                                                                Vector3 fallback, out Vector3 target)
        {
            if (hasSpawn && Finite(spawn) && !rule.IsOut(spawn)) { target = spawn; return OutOfBoundsTarget.Spawn; }
            if (hasFallback && Finite(fallback) && !rule.IsOut(fallback)) { target = fallback; return OutOfBoundsTarget.Fallback; }
            target = Vector3.zero;
            return OutOfBoundsTarget.None;
        }

        public static float WrapYaw(float yaw)
        {
            float w = Mathf.Repeat(yaw, 360f);
            return w >= 360f ? 0f : w;
        }

        public static bool Finite(Vector3 v) =>
            !float.IsNaN(v.x) && !float.IsInfinity(v.x) && !float.IsNaN(v.y) && !float.IsInfinity(v.y) &&
            !float.IsNaN(v.z) && !float.IsInfinity(v.z);
    }
}

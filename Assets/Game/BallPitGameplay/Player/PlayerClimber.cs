// PlayerClimber: wall climb, mantle, pile scramble, the anti-stuck watchdog and respawn (deliverable A, file plan P8;
// design c 8). It never touches the Rigidbody: each step it reads the motor's MotorFrame and returns a ClimbCommand
// that the motor applies. Order inside Decide: respawn, mantling, unstick, climbing, a mantle trigger, climb attach,
// scramble, then nothing (locomotion with the normal jump). An action in progress always wins, and a mantle press
// beats a plain jump. Also ClimbState, the pure StuckWatchdog and ClimbMath.
using BallPitFinal;
using UnityEngine;

namespace BallPitGameplay
{
    public enum ClimbState { None, Climbing, Mantling }

    [DisallowMultipleComponent]
    public sealed class PlayerClimber : MonoBehaviour
    {
        private PlayerAvatar avatar;
        private PlayerMotor motor;
        private IPlayerInput input;
        private IPlayerStats stats;
        private PlayerConfig config;
        private PitWorld world;
        private PitSite activeSite;
        private float minGroundDot = 0.6428f;

        private ClimbState state;
        private float clock; // fixed-step time, advanced by Decide
        private Vector3 wallNormal;
        private float lostTime, idleTime, reattachLockoutUntil, mantleLockoutUntil, lastSteepPileTime = float.NegativeInfinity;
        private float scrambleCooldownUntil;
        private Vector3 mantleStart, mantleTarget, mantleForward;
        private float mantleDuration, mantleElapsed;
        private bool mantleArrived;
        private readonly StuckWatchdog watchdog = new StuckWatchdog();
        private bool unstickPending, offered;
        private int lastRespawnPresses;
        private float lastRespawnTime = float.NegativeInfinity;

        public ClimbState State => state;
        public event System.Action<ClimbState> StateChanged;
        public bool RespawnOffered => offered;
        /// <summary>The watchdog offered (true) or withdrew (false) the respawn; the HUD hint follows it.</summary>
        public event System.Action<bool> RespawnOfferChanged;
        /// <summary>A respawn press found no spawn pose (the HUD shows a hint).</summary>
        public event System.Action RespawnUnavailable;
        public float RespawnCooldownRemaining =>
            config == null ? 0f : Mathf.Max(0f, lastRespawnTime + config.respawnCooldown - Time.time);

        /// <summary>PlayerAvatar.Awake, every instance. Null is ignored.</summary>
        public void SetConfig(PlayerConfig value) { if (value != null) config = value; }

        /// <summary>Owner only (design c 3 step 3).</summary>
        public void InitLocal(PlayerAvatar owner, PlayerMotor playerMotor, IPlayerInput playerInput, IPlayerStats playerStats)
        {
            avatar = owner; motor = playerMotor; input = playerInput; stats = playerStats;
            if (config != null)
            {
                minGroundDot = Mathf.Cos(config.slopeLimit * Mathf.Deg2Rad);
                watchdog.Configure(config.stuckProgressDistance, config.stuckSeconds, config.unstickAttemptsBeforeOffer,
                                   config.attemptWindowSeconds, config.escapeDistance);
            }
            if (input != null) lastRespawnPresses = input.PressCount(PlayerButton.Respawn);
        }

        /// <summary>Every PitWorldBuilt (local avatar): the world for the mantle's standing-room test and the active site.</summary>
        public void SetWorldContext(PitWorld pitWorld, PitSite site, float ballDiameter)
        {
            world = pitWorld;
            activeSite = site;
        }

        /// <summary>Teleport and freeze: drops any climb or mantle, the pending unstick, the watchdog and the respawn offer.</summary>
        internal void Cancel()
        {
            mantleArrived = false;
            lostTime = idleTime = 0f;
            unstickPending = false;
            watchdog.Reset();
            SetOffered(false);
            SetState(ClimbState.None);
        }

        internal void Decide(in MotorFrame f, out ClimbCommand command)
        {
            command = default;
            if (config == null || motor == null) return;
            clock += f.Dt;
            if (f.SteepPile) lastSteepPileTime = clock;

            if (TryRespawn(in f, ref command)) return;
            if (state == ClimbState.Mantling) { ContinueMantle(in f, out command); return; }
            if (unstickPending)
            {
                unstickPending = false;
                Unstick(in f, out command);
                SetState(ClimbState.None);
                return;
            }
            if (state == ClimbState.Climbing && ContinueClimb(in f, out command)) return;
            if (f.JumpBuffered && f.Move.y >= 0.5f && clock >= mantleLockoutUntil && TryStartMantle(in f, f.LookForwardH, out command)) return;
            if (TryAttach(in f, out command)) return;
            TryScramble(in f, out command);
        }

        /// <summary>Watchdog sampling with the post-step position (never during a mantle).</summary>
        internal void AfterStep(in MotorFrame f)
        {
            if (config == null || f.Mode == MotorMode.Mantling) return;
            bool intent = f.Move.magnitude > 0.3f || f.JumpHeld;
            bool condition = f.Wedged || !f.Grounded;
            if (watchdog.Sample(f.Feet, f.Dt, intent, condition, f.Grounded) != WatchdogVerdict.None) unstickPending = true;
            SetOffered(watchdog.Offered);
        }

        // ------------------------------------------------------------------ Respawn (R)

        private bool TryRespawn(in MotorFrame f, ref ClimbCommand command)
        {
            if (input == null) return false;
            int presses = input.PressCount(PlayerButton.Respawn);
            if (presses == lastRespawnPresses) return false;
            lastRespawnPresses = presses;
            if (RespawnCooldownRemaining > 0f || (config.respawnRequiresOffer && !offered)) return false;
            if (avatar == null || !avatar.TryGetOwnSpawnPose(out Vector3 feet, out float yaw))
            {
                Debug.LogWarning("[PlayerClimber] respawn unavailable: no spawn pose for this player's slot", this);
                Raise(RespawnUnavailable);
                return false;
            }
            lastRespawnTime = Time.time;
            command = new ClimbCommand { Kind = ClimbCommandKind.Respawn, Target = feet, Yaw = yaw };
            return true;
        }

        // ------------------------------------------------------------------ Wall climb

        private bool TryAttach(in MotorFrame f, out ClimbCommand command)
        {
            command = default;
            if (f.Mode != MotorMode.Driven || f.ClimbCount == 0 || clock < reattachLockoutUntil || f.Move.y < config.climbAttachInput) return false;
            Vector3 w = f.ClimbNormalSum.normalized, wH = Flat(w);
            if (wH.sqrMagnitude < 1e-6f) return false;
            if (Vector3.Dot(f.LookForwardH, -wH.normalized) < Mathf.Cos(config.climbMaxFacingAngle * Mathf.Deg2Rad)) return false;
            wallNormal = w;
            lostTime = idleTime = 0f;
            SetState(ClimbState.Climbing);
            command = ClimbCommandFor(in f);
            return true;
        }

        // False after a detach (the step falls through to the other rules and locomotion).
        private bool ContinueClimb(in MotorFrame f, out ClimbCommand command)
        {
            command = default;
            if (f.ClimbCount > 0) { wallNormal = f.ClimbNormalSum.normalized; lostTime = 0f; }
            else if (ProbeWall(in f, out Vector3 n)) { wallNormal = n; lostTime = 0f; }
            else lostTime += f.Dt;

            if (f.JumpBuffered) // wall jump, then a short re-attach lockout
            {
                SetState(ClimbState.None);
                reattachLockoutUntil = clock + config.wallReattachLockout;
                command = new ClimbCommand
                {
                    Kind = ClimbCommandKind.WallJump,
                    Velocity = wallNormal * config.wallJumpOffSpeed + Vector3.up * config.wallJumpUpSpeed,
                };
                return true;
            }
            if (f.Grounded && f.Move.y <= 0f) { SetState(ClimbState.None); return false; } // stepped off at the bottom
            idleTime = f.Move.sqrMagnitude < 0.01f ? idleTime + f.Dt : 0f;
            if (idleTime >= config.climbReleaseDelay) { SetState(ClimbState.None); return false; } // let go
            if (lostTime > config.climbLostSeconds) // the wall ended: the automatic mantle, else detach
            {
                SetState(ClimbState.None);
                return clock >= mantleLockoutUntil && TryStartMantle(in f, -Flat(wallNormal), out command);
            }
            command = ClimbCommandFor(in f);
            return true;
        }

        private ClimbCommand ClimbCommandFor(in MotorFrame f)
        {
            float speed = config.climbSpeed * (stats != null ? stats.ClimbSpeed : 1f);
            Vector3 v = ClimbMath.ClimbVelocity(wallNormal, f.Move, speed, config.climbStrafeFactor, config.climbStickSpeed);
            return new ClimbCommand { Kind = ClimbCommandKind.Climb, Velocity = v };
        }

        // Chest-height SphereCast toward the wall: refreshes the normal on a climbable collider of the active site.
        private bool ProbeWall(in MotorFrame f, out Vector3 normal)
        {
            normal = wallNormal;
            Vector3 dir = -Flat(wallNormal);
            if (dir.sqrMagnitude < 1e-6f) return false;
            float r = f.CapsuleRadius, probe = r * config.climbProbeRadiusFraction;
            Vector3 origin = f.Feet + Vector3.up * (f.CapsuleHeight * config.climbProbeHeightFraction);
            if (!Physics.SphereCast(origin, probe, dir.normalized, out RaycastHit hit, (r - probe) + config.climbProbeReach,
                                    motor.CollisionMask, QueryTriggerInteraction.Ignore)) return false;
            if (Mathf.Abs(hit.normal.y) > config.climbMaxNormalY) return false;
            if (!ClimbableSurface.TryGet(hit.collider, out ClimbableSurface surface) || !surface.AppliesTo(activeSite)) return false;
            normal = hit.normal;
            return true;
        }

        // ------------------------------------------------------------------ Mantle

        // Ledge probe (environment only: walls, the rim and authored art; pile edges are the scramble's job), path
        // clearance, and standing room (colliders plus the active world's resting balls). Off without the Player layer.
        private bool TryStartMantle(in MotorFrame f, Vector3 forward, out ClimbCommand command)
        {
            command = default;
            if (!motor.OverlapChecksEnabled) return false;
            Vector3 fwd = Flat(forward);
            if (fwd.sqrMagnitude < 1e-6f) return false;
            fwd.Normalize();
            float r = f.CapsuleRadius, h = f.CapsuleHeight, lp = config.ledgeProbeRadius;
            Vector3 q = f.Feet + fwd * (r + config.mantleForwardReach);
            Vector3 probeTop = q + Vector3.up * (config.mantleMaxHeight + lp + MotorMath.ProbeLead);
            int env = motor.EnvironmentMask, all = motor.CollisionMask;
            if (Physics.CheckSphere(probeTop, lp, env, QueryTriggerInteraction.Ignore)) return false; // taller than a mantle
            if (!Physics.SphereCast(probeTop, lp, Vector3.down, out RaycastHit hit, config.mantleMaxHeight - config.mantleMinHeight + MotorMath.ProbeLead,
                                    env, QueryTriggerInteraction.Ignore) || hit.normal.y < minGroundDot) return false;
            float top = hit.point.y - f.Feet.y;
            if (top < config.mantleMinHeight || top > config.mantleMaxHeight) return false;

            float rc = r - MotorMath.Skin;
            Vector3 a = f.Feet + Vector3.up * r, b = f.Feet + Vector3.up * Mathf.Max(r, h - r);
            if (Physics.CapsuleCast(a, b, rc, Vector3.up, top + MotorMath.ProbeLead, all, QueryTriggerInteraction.Ignore)) return false;
            Vector3 lift = Vector3.up * (top + MotorMath.ProbeLead), toQ = Flat(q - f.Feet);
            float dq = toQ.magnitude;
            if (dq > 1e-4f && Physics.CapsuleCast(a + lift, b + lift, rc, toQ / dq, dq, all, QueryTriggerInteraction.Ignore)) return false;
            Vector3 target = new Vector3(q.x, f.Feet.y + top + MotorMath.Skin, q.z);
            if (Physics.CheckCapsule(target + Vector3.up * r, target + Vector3.up * Mathf.Max(r, h - r), rc, all, QueryTriggerInteraction.Ignore)) return false;
            if (motor.RestingBallsInCapsule(world, target)) return false;

            mantleStart = f.Feet;
            mantleTarget = target;
            mantleForward = fwd;
            mantleDuration = config.mantleDurationBase + top * config.mantleDurationPerMeter;
            mantleArrived = false;
            SetState(ClimbState.Mantling);
            mantleElapsed = Mathf.Min(f.Dt, mantleDuration);
            command = new ClimbCommand
            {
                Kind = ClimbCommandKind.MantleStart,
                Target = ClimbMath.MantlePath(mantleStart, mantleTarget, mantleElapsed / mantleDuration, config.mantleRiseFraction),
            };
            return true;
        }

        // Kinematic, owner-side path: rise first, then move over the ledge (each phase smoothstepped). Aborts at the current
        // position with zero velocity when something moves into the next target; ends with forward * mantleExitSpeed.
        private void ContinueMantle(in MotorFrame f, out ClimbCommand command)
        {
            if (mantleArrived) { EndMantle(mantleForward * config.mantleExitSpeed, out command); return; }
            mantleElapsed = Mathf.Min(mantleElapsed + f.Dt, mantleDuration);
            float t = mantleDuration > 0f ? mantleElapsed / mantleDuration : 1f;
            Vector3 next = ClimbMath.MantlePath(mantleStart, mantleTarget, t, config.mantleRiseFraction);
            if (motor.IsCapsuleBlocked(next, f.CapsuleRadius - MotorMath.SkinTight, true)) { EndMantle(Vector3.zero, out command); return; }
            mantleArrived = t >= 1f;
            command = new ClimbCommand { Kind = ClimbCommandKind.MantleStep, Target = next };
        }

        private void EndMantle(Vector3 exitVelocity, out ClimbCommand command)
        {
            mantleArrived = false;
            mantleLockoutUntil = clock + config.mantleLockoutSeconds;
            SetState(ClimbState.None);
            command = new ClimbCommand { Kind = ClimbCommandKind.MantleEnd, Velocity = exitVelocity };
        }

        // ------------------------------------------------------------------ Pile scramble and unstick

        private bool TryScramble(in MotorFrame f, out ClimbCommand command)
        {
            command = default;
            if (f.Mode != MotorMode.Driven || f.Grounded) return false;
            if (!f.SteepPile && clock - lastSteepPileTime > config.scrambleGrace) return false;
            bool press = f.JumpBuffered || (config.scrambleOnHold && f.JumpHeld);
            if (!press || f.Move.y <= 0.1f || clock < scrambleCooldownUntil) return false;
            scrambleCooldownUntil = clock + config.scrambleCooldown;
            command = new ClimbCommand
            {
                Kind = ClimbCommandKind.Scramble,
                Velocity = Vector3.up * config.scrambleUpSpeed + f.DesiredDirH * config.scrambleForwardSpeed,
            };
            return true;
        }

        // Depenetrate (clamped), then kick up, forward and away from the steep contacts.
        private void Unstick(in MotorFrame f, out ClimbCommand command)
        {
            Vector3 away = Flat(f.SteepNormalSum);
            away = away.sqrMagnitude > 1e-6f ? away.normalized : Vector3.zero;
            command = new ClimbCommand
            {
                Kind = ClimbCommandKind.Unstick,
                Offset = motor.ComputeDepenetration(config.maxDepenetrationPerAttempt),
                Velocity = Vector3.up * config.unstickUpSpeed + f.DesiredDirH * config.unstickForwardSpeed + away * config.unstickNormalSpeed,
            };
        }

        // ------------------------------------------------------------------ Helpers

        private void SetState(ClimbState next)
        {
            if (next == state) return;
            state = next;
            if (StateChanged == null) return;
            try { StateChanged(next); } catch (System.Exception e) { Debug.LogException(e, this); }
        }

        private void SetOffered(bool value)
        {
            if (value == offered) return;
            offered = value;
            if (RespawnOfferChanged == null) return;
            try { RespawnOfferChanged(value); } catch (System.Exception e) { Debug.LogException(e, this); }
        }

        private void Raise(System.Action handlers)
        {
            if (handlers == null) return;
            try { handlers(); } catch (System.Exception e) { Debug.LogException(e, this); }
        }

        private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
    }

    public enum WatchdogVerdict { None, Unstick, Offer }

    /// <summary>
    /// The anti-stuck watchdog (design c 8), pure. With intent and condition (wedged or ungrounded) and less than
    /// progressDistance of progress for stuckSeconds: Unstick. attemptsBeforeOffer unsticks within attemptWindow, without
    /// getting escapeDistance from the first stuck point: Offer (Offered stays set until an escape or Reset).
    /// </summary>
    public sealed class StuckWatchdog
    {
        private float progressDistance = 0.1f, stuckSeconds = 1.5f, attemptWindow = 12f, escapeDistance = 1.5f;
        private int attemptsBeforeOffer = 3;
        private bool hasAnchor, hasFirst;
        private Vector3 anchor, firstStuckPoint;
        private float stuckTime, clock, firstAttemptTime;
        private int attempts;

        public bool Offered { get; private set; }

        public void Configure(float progress, float seconds, int attemptsToOffer, float windowSeconds, float escape)
        {
            progressDistance = progress; stuckSeconds = seconds; attemptsBeforeOffer = Mathf.Max(1, attemptsToOffer);
            attemptWindow = windowSeconds; escapeDistance = escape;
        }

        public void Reset()
        {
            hasAnchor = hasFirst = false;
            stuckTime = 0f;
            attempts = 0;
            Offered = false;
        }

        public WatchdogVerdict Sample(Vector3 pos, float dt, bool intent, bool condition, bool grounded)
        {
            clock += dt;
            if (hasFirst && grounded && (pos - firstStuckPoint).sqrMagnitude > escapeDistance * escapeDistance)
            { hasFirst = false; attempts = 0; Offered = false; } // escaped
            if (hasFirst && !Offered && clock - firstAttemptTime > attemptWindow) { hasFirst = false; attempts = 0; }
            if (!intent || !condition) { hasAnchor = false; stuckTime = 0f; return WatchdogVerdict.None; }
            if (!hasAnchor || (pos - anchor).sqrMagnitude >= progressDistance * progressDistance)
            { anchor = pos; hasAnchor = true; stuckTime = 0f; return WatchdogVerdict.None; }
            stuckTime += dt;
            if (stuckTime < stuckSeconds) return WatchdogVerdict.None;

            stuckTime = 0f;
            hasAnchor = false;
            if (!hasFirst) { hasFirst = true; firstStuckPoint = pos; firstAttemptTime = clock; attempts = 0; }
            attempts++;
            if (!Offered && attempts >= attemptsBeforeOffer) { Offered = true; return WatchdogVerdict.Offer; }
            return WatchdogVerdict.Unstick;
        }
    }

    /// <summary>Pure climb and mantle math (design c 8, c 13).</summary>
    public static class ClimbMath
    {
        /// <summary>wallUp * Move.y * speed + wallRight * Move.x * speed * strafe - w * stick, with wallUp = up projected on
        /// the wall plane and wallRight = w x wallUp (the player's right while facing the wall).</summary>
        public static Vector3 ClimbVelocity(Vector3 wallNormal, Vector2 move, float speed, float strafeFactor, float stickSpeed)
        {
            Vector3 w = wallNormal.sqrMagnitude > 1e-8f ? wallNormal.normalized : Vector3.forward;
            Vector3 wallUp = Vector3.ProjectOnPlane(Vector3.up, w);
            wallUp = wallUp.sqrMagnitude > 1e-8f ? wallUp.normalized : Vector3.up;
            Vector3 wallRight = Vector3.Cross(w, wallUp).normalized;
            return wallUp * (move.y * speed) + wallRight * (move.x * speed * strafeFactor) - w * stickSpeed;
        }

        /// <summary>Two-phase path at t in [0, 1]: the first riseFraction lifts to target.y, the rest slides to target's XZ;
        /// each phase smoothstepped.</summary>
        public static Vector3 MantlePath(Vector3 start, Vector3 target, float t, float riseFraction)
        {
            t = Mathf.Clamp01(t);
            float rf = Mathf.Clamp(riseFraction, 0.01f, 0.99f);
            float rise = Smooth(Mathf.Clamp01(t / rf)), slide = Smooth(Mathf.Clamp01((t - rf) / (1f - rf)));
            return new Vector3(Mathf.Lerp(start.x, target.x, slide), Mathf.Lerp(start.y, target.y, rise), Mathf.Lerp(start.z, target.z, slide));
        }

        private static float Smooth(float x) => x * x * (3f - 2f * x);
    }
}

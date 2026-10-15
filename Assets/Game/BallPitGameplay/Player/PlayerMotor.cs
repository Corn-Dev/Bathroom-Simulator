// PlayerMotor: the owner's Rigidbody motor (deliverable A, file plan P6; design c 3, c 7, review 4 finding 2). The only
// writer of the owner's Rigidbody state. It runs as a step listener inside the controller's FixedUpdate, before the
// pit's physics step on the host and before the client's own step. This part: lifecycle, modes, the teleport
// protocol, the tick (sanity and out-of-bounds rule, climber decision, applying commands). PlayerMotor.Movement.cs
// holds contacts, ground, locomotion, step-up, capsule probes, collider-shell sources and MotorMath. Also the shared
// motor types.
using BallPitFinal;
using Unity.Netcode.Components;
using UnityEngine;

namespace BallPitGameplay
{
    public enum MotorMode { None, Remote, Frozen, Driven, Climbing, Mantling }

    /// <summary>One physics step as the motor sees it (pose, input, ground, contact buckets). The climber reads it by in.</summary>
    public struct MotorFrame
    {
        public float Dt;
        public MotorMode Mode;
        public Vector3 Feet, Velocity;
        public float Yaw;
        public Vector3 LookForwardH;   // unit horizontal forward of the yaw
        public bool InputActive;
        public Vector2 Move;           // zero unless the input is active
        public Vector3 DesiredDirH;    // unit horizontal direction of Move under the yaw, or zero
        public bool JumpBuffered, JumpHeld, SprintHeld;
        public bool Grounded;          // walkable ground: contacts, snap probe or crevice rule
        public Vector3 GroundNormal;
        public int SteepCount;
        public Vector3 SteepNormalSum;
        public bool SteepPile;         // a steep ball-layer contact this step
        public bool Wedged;            // two steep contacts with opposing horizontal normals
        public int ClimbCount;
        public Vector3 ClimbNormalSum; // climbable contacts for the active site
        public float CapsuleRadius, CapsuleHeight;
    }

    public enum ClimbCommandKind { None, Climb, WallJump, Scramble, Unstick, MantleStart, MantleStep, MantleEnd, Respawn }

    /// <summary>The climber's decision for one step; the motor applies it (design c 7 step 4).</summary>
    public struct ClimbCommand
    {
        public ClimbCommandKind Kind;
        public Vector3 Velocity;  // Climb, WallJump, Scramble, Unstick, MantleEnd
        public Vector3 Target;    // MantleStart, MantleStep: the feet target; Respawn: the feet
        public Vector3 Offset;    // Unstick: the depenetration added to the position
        public float Yaw;         // Respawn
    }

    /// <summary>A ball-layer contact above pileDisturbSpeed (only with PlayerConfig.playersDisturbPile; no subscriber in A).</summary>
    public readonly struct PileContactInfo
    {
        public PileContactInfo(Collider ball, Vector3 point, Vector3 normal, float speed) { Ball = ball; Point = point; Normal = normal; Speed = speed; }
        public Collider Ball { get; } public Vector3 Point { get; } public Vector3 Normal { get; } public float Speed { get; }
    }

    [DisallowMultipleComponent]
    public sealed partial class PlayerMotor : MonoBehaviour, IPitStepListener
    {
        private Rigidbody rb;
        private CapsuleCollider capsule;
        private NetworkTransform netTransform;
        private PlayerAvatar avatar;
        private IPitStepRegistry steps;
        private IPlayerInput input;
        private PlayerView view;
        private PlayerClimber climber;
        private IPlayerStats stats;
        private PlayerConfig config;
        private bool registered;
        private MotorMode mode = MotorMode.None;

        // World context (SetWorldContext, every PitWorldBuilt).
        private bool hasWorld;
        private int ballLayer = 8;
        private float ballDiameter = 0.5f;
        private Bounds playBounds;
        private float floorY;
        private PitSite activeSite;

        private int collisionMask = Physics.DefaultRaycastLayers, environmentMask = Physics.DefaultRaycastLayers;
        private int playerLayer = -1;
        private bool ownLayerOk, warnedLayer, warnedBodyLayer;

        private bool oobRequested, hasLastGood;
        private Vector3 lastGoodFeet;
        private bool warnedNoPose, warnedOobPose, warnedNonFinite;
        private Vector3 mantleVelocity, lastMantleTarget;
        private MotorFrame frame;

        public PlayerConfig Config => config;
        public MotorMode Mode => mode;
        public bool Grounded => grounded;
        public Vector3 GroundNormal => groundNormal;
        /// <summary>Driven/Climbing: the body velocity; Mantling: the path velocity; otherwise zero.</summary>
        public Vector3 Velocity =>
            rb == null ? Vector3.zero
            : mode == MotorMode.Driven || mode == MotorMode.Climbing ? rb.velocity
            : mode == MotorMode.Mantling ? mantleVelocity : Vector3.zero;

        public event System.Action<TeleportReason> BeforeTeleport, Teleported;
        /// <summary>Step-up height in metres (audio and animation hook).</summary>
        public event System.Action<float> SteppedUp;
        /// <summary>Only when PlayerConfig.playersDisturbPile is on; no subscriber in A.</summary>
        public event System.Action<PileContactInfo> PileContact;

        internal int CollisionMask => collisionMask;
        internal int EnvironmentMask => environmentMask;
        /// <summary>False when the Player layer is missing or the root is not on it: overlap checks would hit the own capsule.</summary>
        internal bool OverlapChecksEnabled => ownLayerOk && capsule != null;
        internal float CapsuleRadius => capsule != null ? capsule.radius : 0.35f;
        internal float CapsuleHeight => capsule != null ? capsule.height : 1.8f;

        /// <summary>PlayerAvatar.Awake, every instance. Null is ignored.</summary>
        public void SetConfig(PlayerConfig value) { if (value != null) config = value; }

        /// <summary>Owner only (design c 3 step 3, review 4 finding 2). Registers the step listener once; starts Frozen.</summary>
        public void InitLocal(PlayerAvatar owner, IPitStepRegistry stepRegistry, IPlayerInput playerInput, PlayerView playerView,
                              PlayerClimber playerClimber, IPlayerStats playerStats)
        {
            avatar = owner; steps = stepRegistry; input = playerInput; view = playerView; climber = playerClimber; stats = playerStats;
            ResolveComponents();
            if (rb == null || capsule == null || config == null)
            { Debug.LogError("[PlayerMotor] no Rigidbody, CapsuleCollider or PlayerConfig; the motor stays inert", this); return; }
            rb.isKinematic = true;
            rb.useGravity = false;                       // gravity is manual (c 7)
            rb.constraints = RigidbodyConstraints.FreezeRotation;
            rb.sleepThreshold = 0f;                      // contact callbacks must never stop
            rb.mass = config.mass;
            rb.maxDepenetrationVelocity = config.maxDepenetrationVelocity;
            rb.solverIterations = config.solverIterations;
            BuildMasks();
            mode = MotorMode.Frozen;
            if (input != null) lastJumpPresses = input.PressCount(PlayerButton.Jump);
            if (steps != null && !registered) { steps.AddStepListener(this, PitStepOrder.PlayerMotor); registered = true; }
        }

        /// <summary>Non-owner: kinematic for its whole life, no listener.</summary>
        public void InitRemote()
        {
            ResolveComponents();
            if (rb != null) { rb.isKinematic = true; rb.useGravity = false; }
            mode = MotorMode.Remote;
        }

        /// <summary>Despawn (PlayerAvatar): removes the step listener. Idempotent.</summary>
        internal void Shutdown()
        {
            if (registered && steps != null) steps.RemoveStepListener(this);
            registered = false;
            if (mode != MotorMode.Remote) mode = MotorMode.None;
        }

        private void OnDestroy() => Shutdown();

        /// <summary>Every PitWorldBuilt (local avatar): validates and stores the world context, rebuilds the masks.</summary>
        public void SetWorldContext(int worldBallLayer, float worldBallDiameter, Bounds worldPlayBounds, float worldFloorY, PitSite site)
        {
            if (worldBallLayer < 0 || worldBallLayer > 31)
            { Debug.LogError($"[PlayerMotor] ball layer {worldBallLayer} is outside 0..31; using 8", this); worldBallLayer = 8; }
            if (float.IsNaN(worldBallDiameter) || float.IsInfinity(worldBallDiameter) || worldBallDiameter < 0.05f || worldBallDiameter > 2f)
            { Debug.LogWarning($"[PlayerMotor] ball diameter {worldBallDiameter} is outside 0.05..2 m; using 0.5", this); worldBallDiameter = 0.5f; }
            ballLayer = worldBallLayer; ballDiameter = worldBallDiameter;
            playBounds = worldPlayBounds; floorY = worldFloorY; activeSite = site;
            hasWorld = true;
            hasLastGood = false; // positions of the previous world mean nothing in this one
            if (config != null) BuildMasks();
        }

        /// <summary>OutOfBoundsVolume: teleport to the spawn pose on the next tick. Idempotent.</summary>
        public void RequestOutOfBounds()
        {
            if (mode == MotorMode.Driven || mode == MotorMode.Climbing || mode == MotorMode.Mantling) oobRequested = true;
        }

        /// <summary>PlayerAvatar only. Kinematic, no input or gravity; clears contacts, timers and the mantle. Idempotent.</summary>
        public void Freeze()
        {
            if (rb == null || mode == MotorMode.Remote || mode == MotorMode.None) return;
            ResetMotionState();
            oobRequested = false;
            rb.isKinematic = true;
            mode = MotorMode.Frozen;
        }

        /// <summary>PlayerAvatar only. Dynamic (Driven) with zero velocity; the snap probe may find the floor at once.</summary>
        public void Unfreeze()
        {
            if (rb == null || mode != MotorMode.Frozen) return;
            mode = MotorMode.Driven;
            rb.isKinematic = false; // velocity only after the body is dynamic
            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            stepsSinceGrounded = 0;
            if (input != null) lastJumpPresses = input.PressCount(PlayerButton.Jump);
        }

        /// <summary>The teleport protocol (design c 3 steps 1-8; HANDOFF-A lists the steps). Local only; false on a
        /// non-finite pose.</summary>
        internal bool TeleportTo(Vector3 feet, float yaw, TeleportReason reason)
        {
            if (rb == null || mode == MotorMode.Remote || mode == MotorMode.None)
            { Debug.LogWarning($"[PlayerMotor] {reason} teleport ignored: not the local, initialized avatar", this); return false; }
            if (!MotorMath.Finite(feet) || float.IsNaN(yaw) || float.IsInfinity(yaw))
            { Debug.LogWarning($"[PlayerMotor] {reason} teleport ignored: non-finite pose {feet} / {yaw}", this); return false; }
            yaw = MotorMath.WrapYaw(yaw);

            RaiseTeleport(BeforeTeleport, reason);
            if (mode == MotorMode.Mantling) rb.isKinematic = false;
            if (mode == MotorMode.Mantling || mode == MotorMode.Climbing) mode = MotorMode.Driven;

            Vector3 target = FindFreeSpot(feet, out bool blocked);
            if (blocked)
                Debug.LogWarning($"[PlayerMotor] {reason} target {feet} is still blocked after lifting {config.teleportLiftMax} m; " +
                                 "teleporting anyway (depenetration resolves it)", this);
            Quaternion rot = Quaternion.Euler(0f, yaw, 0f);
            rb.position = target;
            rb.rotation = rot;
            transform.SetPositionAndRotation(target, rot);
            if (!rb.isKinematic) { rb.velocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }

            if (avatar != null && avatar.IsSpawned && netTransform != null && netTransform.CanCommitToTransform)
            {
                try { netTransform.Teleport(target, rot, transform.localScale); }
                catch (System.Exception e)
                {
                    Debug.LogError($"[PlayerMotor] NetworkTransform.Teleport failed; remote peers interpolate across the {reason} jump", this);
                    Debug.LogException(e, this);
                }
            }

            ResetMotionState();
            oobRequested = false;
            stepsSinceGrounded = 0;                // the snap probe may find the floor on the next step
            if (climber != null) climber.Cancel(); // climb, mantle, watchdog and the respawn offer
            if (view != null) view.SnapTo(target, yaw);
            RaiseTeleport(Teleported, reason);
            return true;
        }

        // ------------------------------------------------------------------ The tick (IPitStepListener)

        public void BeforePhysicsStep(float dt)
        {
            if (mode != MotorMode.Driven && mode != MotorMode.Climbing && mode != MotorMode.Mantling) { ClearContacts(); return; }
            Vector3 feet = rb.position;

            // 1. Sanity, then the out-of-bounds rule.
            if (!MotorMath.Finite(feet) || (!rb.isKinematic && !MotorMath.Finite(rb.velocity))) { Recover(); ClearContacts(); return; }
            if ((oobRequested || CurrentBoundsRule.IsOut(feet)) && HandleOutOfBounds()) { ClearContacts(); return; }

            // 2. Input snapshot.
            bool inputActive = input != null && input.Active;
            Vector2 move = inputActive ? input.Move : Vector2.zero;
            bool sprintHeld = inputActive && input.IsHeld(PlayerButton.Sprint);
            bool jumpHeld = inputActive && input.IsHeld(PlayerButton.Jump);
            if (input != null)
            {
                int presses = input.PressCount(PlayerButton.Jump);
                if (presses != lastJumpPresses) { lastJumpPresses = presses; jumpBufferTimer = Mathf.Max(config.jumpBuffer, 1e-4f); }
            }

            // 3. Yaw to the root, then the ground state from the last Simulate's contacts.
            float yaw = view != null ? view.Yaw : transform.eulerAngles.y;
            Quaternion rot = Quaternion.Euler(0f, yaw, 0f);
            if (mode == MotorMode.Mantling) rb.MoveRotation(rot); else rb.rotation = rot;
            EvaluateGround(dt, feet);

            // 4. The climber's decision.
            BuildFrame(dt, feet, yaw, inputActive, move, jumpHeld, sprintHeld);
            ClimbCommand command = default;
            if (climber != null) climber.Decide(in frame, out command);
            bool locomotion = ApplyCommand(in command, dt);

            // 5. Locomotion, 6. contacts and timers.
            if (locomotion && mode == MotorMode.Driven) Locomotion(dt, move, sprintHeld);
            ClearContacts();
            jumpBufferTimer -= dt;
            if (stepUpCooldown > 0) stepUpCooldown--;
        }

        public void AfterPhysicsStep(float dt)
        {
            if (mode != MotorMode.Driven && mode != MotorMode.Climbing && mode != MotorMode.Mantling) return;
            Vector3 p = rb.position;
            if (mode == MotorMode.Driven && grounded && MotorMath.Finite(p)) { lastGoodFeet = p; hasLastGood = true; }
            if (view != null) view.OnPhysicsStepped(p, mode == MotorMode.Driven && grounded && groundedPrevious, groundNormal);
            MotorFrame after = frame;
            after.Feet = p;
            after.Velocity = Velocity;
            after.Mode = mode;
            if (climber != null) climber.AfterStep(in after);
        }

        // Applies the climber's command; true when locomotion runs this step (None and MantleEnd).
        private bool ApplyCommand(in ClimbCommand c, float dt)
        {
            switch (c.Kind)
            {
                case ClimbCommandKind.Climb:
                    mode = MotorMode.Climbing;
                    rb.velocity = c.Velocity;
                    return false;
                case ClimbCommandKind.WallJump:
                case ClimbCommandKind.Scramble:
                    mode = MotorMode.Driven;
                    rb.velocity = c.Velocity;
                    OnJumped();
                    return false;
                case ClimbCommandKind.Unstick:
                    if (mode == MotorMode.Climbing) mode = MotorMode.Driven;
                    rb.position += c.Offset;
                    rb.velocity = c.Velocity;
                    return false;
                case ClimbCommandKind.MantleStart:
                    rb.velocity = Vector3.zero;  // while still dynamic
                    rb.isKinematic = true;
                    mode = MotorMode.Mantling;
                    jumpBufferTimer = 0f;
                    lastMantleTarget = rb.position;
                    MantleTo(c.Target, dt);
                    return false;
                case ClimbCommandKind.MantleStep:
                    if (mode == MotorMode.Mantling) MantleTo(c.Target, dt);
                    return false;
                case ClimbCommandKind.MantleEnd:
                    if (mode == MotorMode.Mantling) rb.isKinematic = false;
                    mode = MotorMode.Driven;
                    rb.velocity = c.Velocity;
                    mantleVelocity = Vector3.zero;
                    stepsSinceGrounded = 0; // the snap probe settles it on the ledge
                    return true;
                case ClimbCommandKind.Respawn:
                    TeleportTo(c.Target, c.Yaw, TeleportReason.Respawn);
                    return false;
                default:
                    if (mode == MotorMode.Climbing) mode = MotorMode.Driven;
                    return true;
            }
        }

        private void MantleTo(Vector3 target, float dt)
        {
            rb.MovePosition(target);
            mantleVelocity = dt > 0f ? (target - lastMantleTarget) / dt : Vector3.zero;
            lastMantleTarget = target;
        }

        // Out of bounds (c 7 step 1, c 8): the spawn pose, else the last grounded position, never a pose that is itself
        // out of bounds (loop guard). The request is consumed in every case. True when a teleport ended the tick.
        private bool HandleOutOfBounds()
        {
            oobRequested = false;
            Vector3 spawn = Vector3.zero;
            float spawnYaw = 0f;
            bool hasSpawn = avatar != null && avatar.TryGetOwnSpawnPose(out spawn, out spawnYaw);
            OutOfBoundsTarget choice = MotorMath.ChooseOutOfBoundsTarget(CurrentBoundsRule, hasSpawn, spawn, hasLastGood, lastGoodFeet,
                                                                         out Vector3 target);
            int slot = avatar != null && avatar.Controller != null ? avatar.Controller.LocalSlot : -1;
            if (choice != OutOfBoundsTarget.Spawn && hasSpawn && !warnedOobPose)
            {
                warnedOobPose = true;
                Debug.LogError($"[PlayerMotor] spawn pose {spawn} for slot {slot} is itself out of bounds (below floor - fallBelowFloor " +
                               "or outside PlayBounds); fix the spawn points or venueBounds", this);
            }
            else if (!hasSpawn && !warnedNoPose)
            {
                warnedNoPose = true;
                Debug.LogWarning($"[PlayerMotor] out of bounds with no spawn pose for slot {slot}; using the last grounded position", this);
            }
            if (choice == OutOfBoundsTarget.None) return false; // stays put; the tick goes on
            return TeleportTo(target, choice == OutOfBoundsTarget.Spawn ? spawnYaw : (view != null ? view.Yaw : 0f), TeleportReason.OutOfBounds);
        }

        // A non-finite pose or velocity: the spawn pose, else the last finite grounded position (Recovery).
        private void Recover()
        {
            if (!warnedNonFinite) { warnedNonFinite = true; Debug.LogError($"[PlayerMotor] non-finite position or velocity at {rb.position}; recovering", this); }
            if (avatar != null && avatar.TryGetOwnSpawnPose(out Vector3 feet, out float yaw) && TeleportTo(feet, yaw, TeleportReason.Recovery)) return;
            if (hasLastGood && TeleportTo(lastGoodFeet, view != null ? view.Yaw : 0f, TeleportReason.Recovery)) return;
            if (!rb.isKinematic) { rb.velocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
        }

        private OutOfBoundsRule CurrentBoundsRule =>
            new OutOfBoundsRule(hasWorld, floorY, config.fallBelowFloor, config.respawnOutsidePlayBounds, playBounds);

        // c 3 step 3 plus review 4 finding 11: free of colliders (own capsule skipped, other players included) and of the
        // active world's resting balls, lifting in teleportLiftStep up to teleportLiftMax. Disabled without the Player layer.
        private Vector3 FindFreeSpot(Vector3 feet, out bool blocked)
        {
            blocked = false;
            bool colliders = OverlapChecksEnabled;
            PitWorld w = avatar != null && avatar.Controller != null ? avatar.Controller.World : null;
            if ((!colliders && w == null) || capsule == null) return feet;
            float step = config.teleportLiftStep, max = config.teleportLiftMax;
            for (float lift = 0f; lift <= max + 1e-4f; lift += step)
            {
                Vector3 p = feet + Vector3.up * lift;
                if ((!colliders || !IsCapsuleBlocked(p, capsule.radius, true)) && !RestingBallsInCapsule(w, p)) return p;
            }
            blocked = true;
            return feet;
        }

        private void ResolveComponents()
        {
            if (rb == null) rb = GetComponent<Rigidbody>();
            if (capsule == null) capsule = GetComponent<CapsuleCollider>();
            if (netTransform == null) netTransform = GetComponent<NetworkTransform>();
        }

        // collisionMask = (DefaultRaycastLayers | ball) minus Player, PlayerBody and the excluded layers (c 7).
        private void BuildMasks()
        {
            int player = PlayerLayers.Player, body = PlayerLayers.PlayerBody;
            int mask = Physics.DefaultRaycastLayers | (1 << ballLayer);
            if (player >= 0) mask &= ~(1 << player);
            if (body >= 0) mask &= ~(1 << body);
            mask &= ~config.excludedLayers.value;
            collisionMask = mask;
            environmentMask = mask & ~(1 << ballLayer);
            playerLayer = player;
            minGroundDot = Mathf.Cos(config.slopeLimit * Mathf.Deg2Rad);
            ownLayerOk = player >= 0 && gameObject.layer == player;
            if (!ownLayerOk && !warnedLayer)
            {
                warnedLayer = true;
                Debug.LogError("[PlayerMotor] the 'Player' layer is missing or the player root is not on it; step-up, mantle and " +
                               "teleport overlap checks are off (they would hit the own capsule)", this);
            }
            if (body < 0 && !warnedBodyLayer)
            {
                warnedBodyLayer = true;
                Debug.LogWarning("[PlayerMotor] the 'PlayerBody' layer is missing; its mask bit is skipped", this);
            }
        }

        private void RaiseTeleport(System.Action<TeleportReason> handlers, TeleportReason reason)
        {
            if (handlers == null) return;
            foreach (System.Delegate d in handlers.GetInvocationList())
            {
                try { ((System.Action<TeleportReason>)d)(reason); }
                catch (System.Exception e) { Debug.LogException(e, this); }
            }
        }
    }
}

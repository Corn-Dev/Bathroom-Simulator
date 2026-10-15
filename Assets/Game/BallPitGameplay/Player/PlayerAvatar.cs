// PlayerAvatar: the player object's composition root (deliverable A, file plan P5; design c 2, c 3, d.8, d.12, M3).
// The only player component with NGO lifecycle code. Awake hands one runtime PlayerConfig to the motor, climber and view
// on every instance; OnNetworkSpawn sets the role (Local or Remote) and initializes the parts explicitly; the session
// gate in Update keeps the local player frozen until the world is ready, then places and releases it. Reacts to every
// world build (brief 11.4). Never names PlayerSpawner or PlayerInteractor (file plan N3): the spawner reaches it through
// SpawnPoses and All, the interactor wires itself on AnySpawned.
using System.Collections.Generic;
using BallPitFinal;
using Unity.Netcode;
using UnityEngine;

namespace BallPitGameplay
{
    public enum PlayerRole { None, Local, Remote }
    public enum TeleportReason { Placement, Respawn, OutOfBounds, Recovery }
    /// <summary>The session gate's state (design d.12). Remote and None avatars stay Inactive.</summary>
    public enum GateState { Inactive, WaitingForWorld, Released }

    public interface IPlayerSpawnPoses
    {
        /// <summary>The pose for a slot under the active site's rules. ignore = the requester's own capsule, so a player
        /// standing on its own spawn point does not block itself.</summary>
        bool TryGetSpawnPose(int slot, Collider ignore, out Vector3 feet, out float yaw);
    }

    [DisallowMultipleComponent]
    public sealed class PlayerAvatar : NetworkBehaviour
    {
        [Tooltip("Every player dimension and tunable. Empty = PlayerConfig's defaults (one Warning).")]
        [SerializeField] private PlayerConfig config;
        [SerializeField] private Rigidbody body;
        [SerializeField] private CapsuleCollider capsule;
        [SerializeField] private PlayerMotor motor;
        [SerializeField] private PlayerClimber climber;
        [SerializeField] private PlayerView view;
        [SerializeField] private PlayerInputReader inputReader;
        [SerializeField] private PlayerCapabilities capabilities;
        [SerializeField] private PlayerSlot slot;

        private static readonly List<PlayerAvatar> all = new List<PlayerAvatar>();
        private static readonly HashSet<System.Delegate> loggedHandlers = new HashSet<System.Delegate>();
        private static bool warnedNoConfig;

        private bool ownsConfig, inert, listed, tornDown, subscribedWorld, driftLogged;
        private PitWorld placedFor;
        private float nextGateWarning;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            all.Clear(); loggedHandlers.Clear();
            Local = null; SpawnPoses = null; AnySpawned = null; AnyDespawned = null;
            warnedNoConfig = false;
        }

        /// <summary>The owner's avatar on this peer, or null.</summary>
        public static PlayerAvatar Local { get; private set; }
        /// <summary>Every spawned avatar on this peer; no player-count cap.</summary>
        public static IReadOnlyList<PlayerAvatar> All => all;
        /// <summary>Raised as the last step of OnNetworkSpawn (the role is final) and at despawn. Handlers are isolated.</summary>
        public static event System.Action<PlayerAvatar> AnySpawned, AnyDespawned;
        /// <summary>Set by the spawn service (PlayerSpawner.BeginSession); null = none.</summary>
        public static IPlayerSpawnPoses SpawnPoses { get; set; }

        /// <summary>The serialized config (may be null). Prefab-time readers only; runtime code reads Config.</summary>
        public PlayerConfig ConfigAsset => config;
        /// <summary>The runtime config, resolved in Awake on every instance: ConfigAsset, else a default; Sanitize()d.</summary>
        public PlayerConfig Config { get; private set; }
        /// <summary>Found in OnNetworkSpawn; null if the lookup failed (a local avatar then stays frozen).</summary>
        public BallPitFinalController Controller { get; private set; }
        public PlayerRole Role { get; private set; }
        /// <summary>Local only: released to input and physics (the Head camera is the render camera).</summary>
        public bool IsReleased { get; private set; }
        /// <summary>True on release, false on freeze or despawn. Handlers are isolated.</summary>
        public event System.Action<bool> ReleasedChanged;
        public GateState Gate { get; private set; }
        public PlayerMotor Motor => motor;
        public PlayerView View => view;
        public PlayerInputReader InputReader => inputReader;
        public PlayerClimber Climber => climber;
        public PlayerCapabilities Capabilities => capabilities;
        public PlayerSlot Slot => slot;
        public CapsuleCollider Capsule => capsule;

        // ------------------------------------------------------------------ Awake: the one config hand-off

        private void Awake()
        {
            ResolveReferences();
            Config = ResolveConfig();
            ApplyDimensions(Config);
            if (motor != null) motor.SetConfig(Config);
            if (climber != null) climber.SetConfig(Config);
            if (view != null) view.SetConfig(Config);
        }

        private PlayerConfig ResolveConfig()
        {
            PlayerConfig c = config;
            if (c == null)
            {
                if (!warnedNoConfig) { warnedNoConfig = true; Debug.LogWarning("[PlayerAvatar] no PlayerConfig assigned; using the defaults", this); }
                c = ScriptableObject.CreateInstance<PlayerConfig>();
                c.hideFlags = HideFlags.DontSave;
                ownsConfig = true;
            }
            if (c.Sanitize()) Debug.LogWarning($"[PlayerAvatar] PlayerConfig '{c.name}' had out-of-range values; they were clamped", this);
            return c;
        }

        // Capsule (height, radius, center at half height), Head at the eye height, HoldPoint at the hold offset.
        private void ApplyDimensions(PlayerConfig c)
        {
            if (capsule != null)
            {
                capsule.height = c.capsuleHeight;
                capsule.radius = c.capsuleRadius;
                capsule.center = new Vector3(0f, c.capsuleHeight * 0.5f, 0f);
            }
            if (view == null) return;
            if (view.Head != null) view.Head.localPosition = new Vector3(0f, c.eyeHeight, 0f);
            if (view.HoldPoint != null) view.HoldPoint.localPosition = c.holdOffset;
        }

        // ------------------------------------------------------------------ Spawn and roles

        public override void OnNetworkSpawn()
        {
            tornDown = false;
            inert = !CheckWiring();
            Controller = Object.FindObjectOfType<BallPitFinalController>();
            if (Controller == null) Debug.LogError("[PlayerAvatar] no BallPitFinalController in the scene; the local player stays frozen", this);
            SetupRole();
            if (!all.Contains(this)) all.Add(this);
            listed = true;
            RaiseStatic(AnySpawned, this); // last: every handler sees the final role, capsule, slot and controller
        }

        private void SetupRole()
        {
            if (inert) { Role = PlayerRole.None; Gate = GateState.Inactive; return; }
            if (!IsOwner) { SetupRemote(); return; }
            if (Local != null && Local != this)
            {
                Debug.LogError($"[PlayerAvatar] a second local avatar '{name}' spawned; it is treated as Remote", this);
                SetupRemote();
                return;
            }
            Role = PlayerRole.Local;
            Local = this;
            if (Controller != null) // no controller: no listener, no camera; the gate never releases
            {
                inputReader.InitLocal();
                view.InitLocal(Controller, inputReader, motor);
                climber.InitLocal(this, motor, inputReader, capabilities);
                motor.InitLocal(this, Controller, inputReader, view, climber, capabilities);
            }
            Freeze("spawn");
            Gate = GateState.WaitingForWorld;
            nextGateWarning = Time.unscaledTime + 5f;
            SubscribeWorld();
            if (Controller != null && Controller.IsPitWorldReady && Controller.World != null)
                OnPitWorldBuilt(Controller.World, Controller.ActiveSite); // late join, or a host whose world was built first
        }

        private void SetupRemote()
        {
            Role = PlayerRole.Remote;
            Gate = GateState.Inactive;
            motor.InitRemote();
            view.InitRemote();
        }

        public override void OnGainedOwnership() => OwnershipChanged();
        public override void OnLostOwnership() => OwnershipChanged();

        // Only after OnNetworkSpawn (listed), and only when this peer's role must actually change.
        private void OwnershipChanged()
        {
            if (inert || !IsSpawned || !listed || (Role == PlayerRole.Local) == IsOwner) return;
            Debug.LogWarning($"[PlayerAvatar] ownership of '{name}' changed; player objects never change owner. Re-running the role setup.", this);
            if (Role == PlayerRole.Local) { ReleaseLocal(); SetupRemote(); }
            else SetupRole();
        }

        // ------------------------------------------------------------------ Release, freeze, teleport

        /// <summary>Local only: teleport (Placement), then release: Driven body, Head camera registered, input on. If the
        /// avatar is already released, only teleports.</summary>
        public void PlaceAndRelease(Vector3 feet, float yaw)
        {
            if (Role != PlayerRole.Local) { Debug.LogWarning("[PlayerAvatar] PlaceAndRelease ignored on a non-local avatar", this); return; }
            if (!motor.TeleportTo(feet, yaw, TeleportReason.Placement)) return;
            if (IsReleased) return;
            motor.Unfreeze();
            view.Activate();
            inputReader.SetActive(true);
            IsReleased = true;
            RaiseReleased(true);
        }

        /// <summary>Local only, idempotent: input off, camera unregistered (the loading view returns), climb cancelled,
        /// body kinematic.</summary>
        public void Freeze(string reason)
        {
            if (Role != PlayerRole.Local) return;
            bool was = IsReleased;
            inputReader.SetActive(false);
            view.Deactivate();
            climber.Cancel();
            motor.Freeze();
            IsReleased = false;
            if (was) { Debug.Log($"[PlayerAvatar] local player frozen ({reason})", this); RaiseReleased(false); }
        }

        /// <summary>Local only: the motor's teleport protocol (drops any held ball first).</summary>
        public bool Teleport(Vector3 feet, float yaw, TeleportReason reason)
        {
            if (Role != PlayerRole.Local) { Debug.LogWarning($"[PlayerAvatar] {reason} teleport ignored on a non-local avatar", this); return false; }
            return motor.TeleportTo(feet, yaw, reason);
        }

        /// <summary>The spawn pose for this peer's pit slot (controller.LocalSlot) from SpawnPoses, finite, yaw wrapped.</summary>
        internal bool TryGetOwnSpawnPose(out Vector3 feet, out float yaw)
        {
            feet = Vector3.zero; yaw = 0f;
            IPlayerSpawnPoses poses = SpawnPoses;
            if (poses == null || Controller == null) return false;
            int s = Controller.LocalSlot;
            if (s < 0) return false;
            bool ok;
            try { ok = poses.TryGetSpawnPose(s, capsule, out feet, out yaw); }
            catch (System.Exception e) { Debug.LogException(e, this); return false; }
            if (!ok || !MotorMath.Finite(feet) || float.IsNaN(yaw) || float.IsInfinity(yaw)) return false;
            yaw = LookMath.WrapYaw(yaw);
            return true;
        }

        // ------------------------------------------------------------------ The session gate (design d.12)

        // Polls instead of subscribing, so it is immune to the order of world events, spawns and late joins.
        private void Update()
        {
            if (Role != PlayerRole.Local) return;
            BallPitFinalController c = Controller;
            if (c == null || SpawnPoses == null) return;
            if (Gate == GateState.Released)
            {
                if (c.IsPitWorldReady && ReferenceEquals(c.World, placedFor)) return;
                Freeze("world changed"); // also catches a dispose and rebuild within one frame
                Gate = GateState.WaitingForWorld;
                nextGateWarning = Time.unscaledTime + 5f;
            }
            if (Gate != GateState.WaitingForWorld || !c.IsPitWorldReady) return;
            int localSlot = c.LocalSlot;
            if (localSlot < 0) { GateWarning("the world is ready but this peer has no pit slot yet"); return; }
            CheckSlotDrift(localSlot);
            if (!TryGetOwnSpawnPose(out Vector3 feet, out float yaw)) { GateWarning($"no spawn pose for slot {localSlot} yet"); return; }
            PlaceAndRelease(feet, yaw);
            if (!IsReleased) return; // the placement failed; retried next frame
            placedFor = c.World;
            Gate = GateState.Released;
        }

        private void GateWarning(string what)
        {
            if (Time.unscaledTime < nextGateWarning) return;
            nextGateWarning = Time.unscaledTime + 5f;
            Debug.LogWarning("[PlayerAvatar] waiting to place the local player: " + what, this);
        }

        private void CheckSlotDrift(int netSlot)
        {
            if (driftLogged || slot == null || slot.Index < 0 || slot.Index == netSlot) return;
            driftLogged = true;
            Debug.LogError($"[PlayerAvatar] PitSlotMirror drifted from NetPit's slot rule: PlayerSlot {slot.Index}, NetPit {netSlot}; " +
                           "using NetPit's slot", this);
        }

        // ------------------------------------------------------------------ World events (every build, brief 11.4)

        private void SubscribeWorld()
        {
            if (Controller == null || subscribedWorld) return;
            Controller.PitWorldBuilt += OnPitWorldBuilt;
            Controller.PitWorldDisposed += OnPitWorldDisposed;
            subscribedWorld = true;
        }

        private void UnsubscribeWorld()
        {
            if (!subscribedWorld) return;
            subscribedWorld = false;
            if (ReferenceEquals(Controller, null)) return;
            Controller.PitWorldBuilt -= OnPitWorldBuilt;
            Controller.PitWorldDisposed -= OnPitWorldDisposed;
        }

        // Refreshes the world context only; releasing is the gate's job.
        private void OnPitWorldBuilt(PitWorld world, IPitSite site)
        {
            if (Role != PlayerRole.Local || world == null) return;
            var pitSite = site as PitSite;
            motor.SetWorldContext(world.S.ballLayer, world.Diameter, world.PlayBounds, world.FloorY, pitSite);
            climber.SetWorldContext(world, pitSite, world.Diameter);
            view.SetWorldContext(world.Diameter);
        }

        // The colliders under the player are about to vanish.
        private void OnPitWorldDisposed(PitWorld world)
        {
            if (Role == PlayerRole.Local) Freeze("world disposed");
        }

        // ------------------------------------------------------------------ Despawn

        public override void OnNetworkDespawn() => TearDown();

        public override void OnDestroy()
        {
            TearDown();
            if (ownsConfig && Config != null) Destroy(Config);
            base.OnDestroy();
        }

        // Idempotent: the freeze work for a local avatar, then out of All and AnyDespawned.
        private void TearDown()
        {
            if (tornDown) return;
            tornDown = true;
            if (Role == PlayerRole.Local) ReleaseLocal();
            all.Remove(this);
            Gate = GateState.Inactive;
            if (!listed) return;
            listed = false;
            RaiseStatic(AnyDespawned, this);
        }

        private void ReleaseLocal()
        {
            Freeze("despawn");
            inputReader.Shutdown();
            view.Shutdown();
            motor.Shutdown();
            if (Local == this) Local = null;
            UnsubscribeWorld();
            Role = PlayerRole.None;
        }

        // ------------------------------------------------------------------ Wiring and raising

        private void ResolveReferences()
        {
            if (body == null) body = GetComponent<Rigidbody>();
            if (capsule == null) capsule = GetComponent<CapsuleCollider>();
            if (motor == null) motor = GetComponent<PlayerMotor>();
            if (climber == null) climber = GetComponent<PlayerClimber>();
            if (view == null) view = GetComponent<PlayerView>();
            if (inputReader == null) inputReader = GetComponent<PlayerInputReader>();
            if (capabilities == null) capabilities = GetComponent<PlayerCapabilities>();
            if (slot == null) slot = GetComponent<PlayerSlot>();
        }

        // Spawn step 1: a missing reference leaves the avatar inert (kinematic, no listener, no camera).
        private bool CheckWiring()
        {
            string missing =
                body == null ? "Rigidbody" : capsule == null ? "CapsuleCollider" : motor == null ? "PlayerMotor"
                : climber == null ? "PlayerClimber" : view == null ? "PlayerView" : inputReader == null ? "PlayerInputReader"
                : capabilities == null ? "PlayerCapabilities" : slot == null ? "PlayerSlot"
                : view.Head == null ? "PlayerView.head" : view.HeadCamera == null ? "PlayerView.headCamera"
                : view.HoldPoint == null ? "PlayerView.holdPoint" : view.Body == null ? "PlayerView.body" : null;
            if (missing == null) return true;
            // The body keeps the prefab's Is Kinematic ON (the avatar never writes the Rigidbody; the motor does).
            Debug.LogError($"[PlayerAvatar] '{name}': {missing} is missing; this avatar stays inert", this);
            return false;
        }

        private void RaiseReleased(bool released)
        {
            System.Action<bool> handlers = ReleasedChanged;
            if (handlers == null) return;
            foreach (System.Delegate d in handlers.GetInvocationList())
            {
                try { ((System.Action<bool>)d)(released); }
                catch (System.Exception e) { LogHandler(d, e); }
            }
        }

        private static void RaiseStatic(System.Action<PlayerAvatar> handlers, PlayerAvatar avatar)
        {
            if (handlers == null) return;
            foreach (System.Delegate d in handlers.GetInvocationList())
            {
                try { ((System.Action<PlayerAvatar>)d)(avatar); }
                catch (System.Exception e) { LogHandler(d, e); }
            }
        }

        // Each handler's exception is logged once, so a throwing subscriber never aborts NGO's spawn or despawn work.
        private static void LogHandler(System.Delegate handler, System.Exception e)
        {
            if (loggedHandlers.Add(handler)) Debug.LogException(e);
        }

#if UNITY_EDITOR
        [System.NonSerialized] private string lastValidation;

        private void Reset() => ResolveReferences();

        // Warns when the prefab's dimensions differ from the config (Awake overwrites them at runtime).
        private void OnValidate()
        {
            string text = null;
            if (config != null && capsule != null &&
                (!Mathf.Approximately(capsule.height, config.capsuleHeight) || !Mathf.Approximately(capsule.radius, config.capsuleRadius) ||
                 capsule.center != new Vector3(0f, config.capsuleHeight * 0.5f, 0f)))
                text = "the CapsuleCollider differs from the PlayerConfig";
            else if (config != null && view != null && view.Head != null && !Mathf.Approximately(view.Head.localPosition.y, config.eyeHeight))
                text = "Head's height differs from PlayerConfig.eyeHeight";
            else if (config != null && view != null && view.HoldPoint != null && view.HoldPoint.localPosition != config.holdOffset)
                text = "HoldPoint differs from PlayerConfig.holdOffset";
            if (text == lastValidation) return;
            lastValidation = text;
            if (text != null)
                Debug.LogWarning($"[PlayerAvatar] '{name}': {text}; Awake applies the config at runtime " +
                                 "(Ball Pit/Player/Apply PlayerConfig to Selected Prefab)", this);
        }
#endif
    }
}

// PlayerInteractor: the local player's interaction pipeline (deliverable A, file plan P11 and section 8; brief 6.7, 7).
// Each frame while released: the one local view, then a center-ray aim at resting balls, moving balls as drawn and B's
// interactables (the environment occludes), the highlight, and the active IPlayerTool. ArmsTool is today's grab, drop and
// throw; moving balls are interactable like resting ones. Wires itself from PlayerAvatar.AnySpawned (file plan N3, 8.5).
using System.Collections.Generic;
using BallPitFinal;
using UnityEngine;

namespace BallPitGameplay
{
    public enum InteractionTargetKind { None, Ball, Container, Machine, Terminal }

    /// <summary>What the center ray points at this frame.</summary>
    public struct InteractionTarget
    {
        public InteractionTargetKind Kind;
        public int BallId;                  // Ball: the ball id; otherwise -1
        public Vector3 Point;               // the hit point on the ray
        public float Distance;              // along the ray
        public IInteractable Interactable;  // Container, Machine, Terminal (deliverable B)

        public static InteractionTarget None => new InteractionTarget { Kind = InteractionTargetKind.None, BallId = -1 };
    }

    /// <summary>B's containers, machines and terminals: on a collider (or a parent) on a layer in interactableMask.</summary>
    public interface IInteractable
    {
        InteractionTargetKind Kind { get; }
        bool CanTarget(PlayerInteractor who);
    }

    /// <summary>A pluggable hand tool: arms now; B's vacuum and others later.</summary>
    public interface IPlayerTool
    {
        bool IsHolding { get; }                                    // authoritative (arms: controller.LocalHeldBall >= 0)
        void OnEquip(PlayerInteractor owner);
        void OnUnequip();
        void Tick(PlayerInteractor owner, in InteractionTarget target, bool primary, bool secondary); // pressed edges
        void GetNetTool(out byte tool, out Vector3 toolPoint);     // NetPit tool fields for SetLocalView (arms: ToolNone)
        void OnBeforeTeleport();                                   // freeze, teleport, world dispose: release what is held
    }

    /// <summary>Shows the targeted ball; Show is called every frame it is shown, with the center as drawn.</summary>
    public interface IBallHighlighter
    {
        void Show(int ballId, Vector3 center);
        void Hide();
    }

    /// <summary>Today's highlight: controller.DrawHighlight (a one-frame draw), at most once per frame.</summary>
    public sealed class PitBallHighlighter : IBallHighlighter
    {
        private readonly BallPitFinalController controller;
        private int drawnFrame = -1;
        public PitBallHighlighter(BallPitFinalController controller) { this.controller = controller; }

        public void Show(int ballId, Vector3 center)
        {
            if (controller == null || drawnFrame == Time.frameCount) return;
            drawnFrame = Time.frameCount;
            controller.DrawHighlight(center);
        }

        public void Hide() { }
    }

    /// <summary>The arms (design 8.3 step 8): LMB grabs the target ball when nothing is held, LMB drops with the player's
    /// velocity, RMB throws along the view. Host: PitAuthority; client: NetPit intents. Releases go through ThrowHeld.</summary>
    public sealed class ArmsTool : IPlayerTool
    {
        private PlayerInteractor owner;

        public bool IsHolding => owner != null && owner.Controller != null && owner.Controller.LocalHeldBall >= 0;
        public void OnEquip(PlayerInteractor who) => owner = who;
        public void OnUnequip() { OnBeforeTeleport(); owner = null; } // releases a held ball (zero velocity) first
        public void GetNetTool(out byte tool, out Vector3 toolPoint) { tool = NetPit.ToolNone; toolPoint = Vector3.zero; }

        public void Tick(PlayerInteractor who, in InteractionTarget target, bool primary, bool secondary)
        {
            BallPitFinalController c = who != null ? who.Controller : null;
            if (c == null) return;
            if (c.LocalHeldBall < 0)
            {
                if (primary && target.Kind == InteractionTargetKind.Ball && target.BallId >= 0) Grab(c, target.BallId);
                return;
            }
            PlayerMotor motor = who.Motor;
            Vector3 body = motor != null ? motor.Velocity : Vector3.zero;
            if (primary) Release(c, body);
            else if (secondary)
            {
                PlayerConfig cfg = who.Config;
                PlayerCapabilities caps = who.Capabilities;
                if (cfg == null) return;
                Vector3 inherit = cfg.throwInheritsVelocity ? body : Vector3.zero;
                Release(c, ThrowVelocity(who.View.AimRay.direction, cfg.throwSpeed, cfg.throwUpBias,
                                         caps != null ? caps.ThrowPower : 1f, inherit));
            }
        }

        public void OnBeforeTeleport()
        {
            BallPitFinalController c = owner != null ? owner.Controller : null;
            if (c != null && c.LocalHeldBall >= 0) Release(c, Vector3.zero);
        }

        /// <summary>normalize(forward + up * upBias / max(1, speed)) * speed * power + inherit; non-finite gives zero.</summary>
        public static Vector3 ThrowVelocity(Vector3 forward, float speed, float upBias, float power, Vector3 inherit)
        {
            Vector3 dir = (forward.normalized + Vector3.up * (upBias / Mathf.Max(1f, speed))).normalized;
            Vector3 v = dir * (speed * power) + inherit;
            return MotorMath.Finite(v) ? v : Vector3.zero;
        }

        // Null-safe: with no authority and no client NetPit (session teardown) nothing is sent; NetPit's own disconnect
        // handler throws a leaving client's ball.
        private static void Grab(BallPitFinalController c, int id)
        {
            PitAuthority a = c.Authority;
            int slot = c.LocalSlot;
            if (a != null) { if ((uint)slot < PitAuthority.MaxPlayers) a.TryGrab(slot, id); }
            else if (c.Net != null && !c.Net.IsHost) c.Net.SendGrab(id);
        }

        private static void Release(BallPitFinalController c, Vector3 velocity)
        {
            PitAuthority a = c.Authority;
            int slot = c.LocalSlot;
            if (a != null) { if ((uint)slot < PitAuthority.MaxPlayers) a.ThrowHeld(slot, velocity); }
            else if (c.Net != null && !c.Net.IsHost) c.Net.SendThrow(velocity);
        }
    }

    [DefaultExecutionOrder(-40)]
    [DisallowMultipleComponent]
    public sealed class PlayerInteractor : MonoBehaviour, IPitStepListener
    {
        [Tooltip("Layers of B's interactables (containers, machines, terminals). Nothing in deliverable A.")]
        [SerializeField] private LayerMask interactableMask = 0;

        private PlayerAvatar avatar;
        private PlayerMotor motor;
        private PlayerView view;
        private IPlayerInput input;
        private PlayerCapabilities caps;
        private PlayerConfig cfg;
        private BallPitFinalController controller;
        private readonly List<IPlayerTool> tools = new List<IPlayerTool>();
        private readonly ArmsTool arms = new ArmsTool();
        private readonly HashSet<object> loggedThrowers = new HashSet<object>();
        private IPlayerTool activeTool;
        private IBallHighlighter highlighter, defaultHighlighter;
        private bool wired, staticsSubscribed, highlightShown;
        private int lastPrimary, lastSecondary;
        private InteractionTarget target = InteractionTarget.None;
        private Vector3 targetCenter;

        public PlayerAvatar Avatar => avatar;
        public BallPitFinalController Controller => controller;
        public PlayerMotor Motor => motor;
        public PlayerView View => view;
        public IPlayerInput Input => input;
        public PlayerCapabilities Capabilities => caps;
        public PlayerConfig Config => cfg;
        public int Slot => controller != null ? controller.LocalSlot : -1; // this peer's pit slot; -1 unknown
        public bool IsWired => wired;
        public InteractionTarget CurrentTarget => target;
        public IPlayerTool ActiveTool => activeTool;
        public IReadOnlyList<IPlayerTool> Tools => tools;
        public ArmsTool Arms => arms;
        public LayerMask InteractableMask { get => interactableMask; set => interactableMask = value; }
        /// <summary>PlayerConfig.reach x PlayerCapabilities.Reach.</summary>
        public float Reach => (cfg != null ? cfg.reach : 4f) * (caps != null ? caps.Reach : 1f);

        /// <summary>The highlight hook; null restores the default (PitBallHighlighter).</summary>
        public IBallHighlighter Highlighter { get => highlighter; set { HideHighlight(); highlighter = value ?? defaultHighlighter; } }

        /// <summary>Adds a tool to the ordered list (idempotent). Nothing assumes four players or a fixed set of holders.</summary>
        public void Register(IPlayerTool tool) { if (tool != null && !tools.Contains(tool)) tools.Add(tool); }

        /// <summary>Registers and equips a tool; the previous one is unequipped (A's arms release what they hold).</summary>
        public bool Equip(IPlayerTool tool)
        {
            if (tool == null) return false;
            Register(tool);
            if (ReferenceEquals(tool, activeTool)) return true;
            IPlayerTool old = activeTool;
            activeTool = null;
            if (old != null) Safe(old, () => old.OnUnequip());
            activeTool = tool;
            Safe(tool, () => tool.OnEquip(this));
            return true;
        }

        /// <summary>The nearest candidate (design 8.3 step 6): 0 resting, 1 moving, 2 interactable, -1 none. A negative or
        /// non-finite distance means no candidate; ties keep the earlier one.</summary>
        public static int PickNearest(float resting, float moving, float interactable, float maxDist)
        {
            int pick = -1;
            float best = float.PositiveInfinity;
            Consider(0, resting, maxDist, ref pick, ref best);
            Consider(1, moving, maxDist, ref pick, ref best);
            Consider(2, interactable, maxDist, ref pick, ref best);
            return pick;
        }

        private static void Consider(int index, float d, float maxDist, ref int pick, ref float best)
        {
            if (d < 0f || float.IsNaN(d) || float.IsInfinity(d) || d > maxDist || d >= best) return;
            pick = index;
            best = d;
        }

        // ------------------------------------------------------------------ Lifecycle (design 8.5)

        private void Awake()
        {
            avatar = GetComponent<PlayerAvatar>();
            if (avatar == null) { Debug.LogError("[PlayerInteractor] no PlayerAvatar on this object; interaction is off", this); enabled = false; return; }
            PlayerAvatar.AnySpawned += OnAnySpawned;
            PlayerAvatar.AnyDespawned += OnAnyDespawned;
            staticsSubscribed = true;
            Register(arms);
        }

        // The avatar raises AnySpawned last in OnNetworkSpawn, so its role is final and it is frozen. Remotes never wire.
        private void OnAnySpawned(PlayerAvatar a)
        {
            if (wired || a == null || a != avatar || a.Role != PlayerRole.Local) return;
            motor = a.Motor; view = a.View; input = a.InputReader; caps = a.Capabilities; cfg = a.Config;
            controller = a.Controller;
            if (controller == null || view == null || input == null) return; // the avatar has already logged why
            controller.AddStepListener(this, PitStepOrder.PlayerInteraction);
            a.ReleasedChanged += OnReleasedChanged;
            if (motor != null) motor.BeforeTeleport += OnBeforeTeleport;
            controller.PitWorldDisposed += OnPitWorldDisposed;
            defaultHighlighter = new PitBallHighlighter(controller);
            if (highlighter == null) highlighter = defaultHighlighter;
            SyncPresses();
            wired = true;
            Equip(activeTool ?? arms);
        }

        // The avatar's freeze already raised ReleasedChanged(false), so the tool dropped before this unwires.
        private void OnAnyDespawned(PlayerAvatar a) { if (ReferenceEquals(a, avatar)) Unwire(); }

        private void Unwire()
        {
            if (!wired) return;
            wired = false;
            if (!ReferenceEquals(controller, null))
            {
                controller.RemoveStepListener(this);
                controller.PitWorldDisposed -= OnPitWorldDisposed;
            }
            if (!ReferenceEquals(avatar, null)) avatar.ReleasedChanged -= OnReleasedChanged;
            if (!ReferenceEquals(motor, null)) motor.BeforeTeleport -= OnBeforeTeleport;
            IPlayerTool tool = activeTool;
            activeTool = null;
            if (tool != null) Safe(tool, () => tool.OnUnequip());
            ClearTarget();
        }

        private void OnDestroy()
        {
            Unwire();
            if (!staticsSubscribed) return;
            PlayerAvatar.AnySpawned -= OnAnySpawned;
            PlayerAvatar.AnyDespawned -= OnAnyDespawned;
            staticsSubscribed = false;
        }

        private void OnReleasedChanged(bool released)
        {
            if (released) SyncPresses();
            else DropAndClear();
        }

        private void OnBeforeTeleport(TeleportReason reason) => DropAndClear();

        // Also drops every reference into the old world (the target's ball id).
        private void OnPitWorldDisposed(PitWorld world) => DropAndClear();

        private void DropAndClear()
        {
            IPlayerTool tool = activeTool;
            if (tool != null) Safe(tool, () => tool.OnBeforeTeleport());
            ClearTarget();
        }

        // ------------------------------------------------------------------ Per frame (design 8.3, owner only)

        private void Update()
        {
            if (!wired) return;
            if (avatar == null || !avatar.IsReleased || controller == null) { ClearTarget(); return; }

            // 2. The one local view per frame, even while input is blocked, so the hand keeps following the camera.
            IPlayerTool tool = activeTool;
            byte netTool = NetPit.ToolNone;
            Vector3 toolPoint = Vector3.zero;
            if (tool != null)
            {
                try { tool.GetNetTool(out netTool, out toolPoint); }
                catch (System.Exception e) { LogOnce(tool, e); netTool = NetPit.ToolNone; toolPoint = Vector3.zero; }
            }
            Ray aim = view.AimRay;
            controller.SetLocalView(aim.origin, view.GetHoldPoint(HoldPointTime.Render), netTool, toolPoint);

            // 3. Kit menu, modal or a panel's cursor claim: no aim, edges stay unconsumed.
            if (!input.Active) { ClearTarget(); return; }

            // 4-6. Aim; 7. highlight and crosshair; 8. the tool.
            PitWorld world = controller.World;
            target = world != null && controller.IsPitWorldReady ? Aim(world, aim) : InteractionTarget.None;
            bool holding = tool != null && SafeIsHolding(tool);
            if (target.Kind == InteractionTargetKind.Ball && target.BallId >= 0 && !holding) ShowHighlight(target.BallId, targetCenter);
            else HideHighlight();
            PlayerHud hud = view.Hud;
            if (hud != null)
                hud.SetCrosshair(holding ? CrosshairState.Holding
                                 : target.Kind != InteractionTargetKind.None ? CrosshairState.Target : CrosshairState.Idle);

            int p = input.PressCount(PlayerButton.Primary), s = input.PressCount(PlayerButton.Secondary);
            bool primary = p != lastPrimary, secondary = s != lastSecondary;
            lastPrimary = p; lastSecondary = s;
            if (tool == null) return;
            InteractionTarget t = target;
            try { tool.Tick(this, in t, primary, secondary); }
            catch (System.Exception e) { LogOnce(tool, e); }
        }

        // The environment occludes (players are out of EnvMask, so a teammate never blocks a pick). Moving balls are
        // tested where they were drawn last frame, which is what the player sees.
        private InteractionTarget Aim(PitWorld world, Ray ray)
        {
            float reach = Reach;
            float maxDist = world.RaycastEnvironment(ray, reach, out RaycastHit env) ? env.distance : reach;

            float dRest = -1f;
            int restId = -1;
            if (world.Raycast(ray, maxDist, out int id, out float t)) { restId = id; dRest = t; }

            float dMove = -1f;
            int moveId = -1;
            Vector3 moveCenter = Vector3.zero;
            int n = controller.GetDrawnDynamic(out BallInstance[] inst, out int[] ids);
            if (n > 0 && inst != null && ids != null &&
                PitWorld.RaycastSpheres(ray, inst, ids, n, world.VisualRadius, maxDist, out int liveId, out float lt) &&
                liveId != controller.LocalHeldBall)
            {
                moveId = liveId; dMove = lt;
                for (int i = 0; i < n; i++) if (ids[i] == liveId) { moveCenter = inst[i].pos; break; }
            }

            float dInter = -1f;
            IInteractable interactable = null;
            if (interactableMask.value != 0 &&
                Physics.Raycast(ray, out RaycastHit hit, reach, interactableMask.value, QueryTriggerInteraction.Collide) &&
                hit.distance <= maxDist + 1e-3f) // an interactable may itself be the occluding environment hit
            {
                interactable = hit.collider.GetComponentInParent<IInteractable>();
                if (interactable != null && SafeCanTarget(interactable)) dInter = Mathf.Min(hit.distance, maxDist);
                else interactable = null;
            }

            switch (PickNearest(dRest, dMove, dInter, maxDist))
            {
                case 0:
                    targetCenter = world.Store.pos[restId];
                    return new InteractionTarget { Kind = InteractionTargetKind.Ball, BallId = restId, Point = ray.GetPoint(dRest), Distance = dRest };
                case 1:
                    targetCenter = moveCenter;
                    return new InteractionTarget { Kind = InteractionTargetKind.Ball, BallId = moveId, Point = ray.GetPoint(dMove), Distance = dMove };
                case 2:
                    targetCenter = ray.GetPoint(dInter);
                    return new InteractionTarget { Kind = interactable.Kind, BallId = -1, Point = targetCenter, Distance = dInter, Interactable = interactable };
                default:
                    return InteractionTarget.None;
            }
        }

        // ------------------------------------------------------------------ Host hand (step listener)

        /// <summary>Host: HandTarget[LocalSlot] from the physics-time hold point, before PitAuthority.Step. A client, no
        /// authority, a frozen avatar or a slot outside the pit's range: nothing.</summary>
        public void BeforePhysicsStep(float dt)
        {
            if (!wired || controller == null || avatar == null || !avatar.IsReleased) return;
            PitAuthority a = controller.Authority;
            if (a == null) return;
            int slot = controller.LocalSlot;
            if ((uint)slot >= PitAuthority.MaxPlayers) return;
            a.HandTarget[slot] = view.GetHoldPoint(HoldPointTime.Simulation);
        }

        public void AfterPhysicsStep(float dt) { }

        // ------------------------------------------------------------------ Helpers

        private void ClearTarget()
        {
            HideHighlight();
            target = InteractionTarget.None;
        }

        private void SyncPresses()
        {
            if (input == null) return;
            lastPrimary = input.PressCount(PlayerButton.Primary);
            lastSecondary = input.PressCount(PlayerButton.Secondary);
        }

        private void ShowHighlight(int ballId, Vector3 center)
        {
            if (highlighter == null) return;
            try { highlighter.Show(ballId, center); highlightShown = true; }
            catch (System.Exception e) { LogOnce(highlighter, e); }
        }

        private void HideHighlight()
        {
            if (!highlightShown || highlighter == null) return;
            highlightShown = false;
            try { highlighter.Hide(); }
            catch (System.Exception e) { LogOnce(highlighter, e); }
        }

        private bool SafeIsHolding(IPlayerTool tool)
        {
            try { return tool.IsHolding; }
            catch (System.Exception e) { LogOnce(tool, e); return false; }
        }

        private bool SafeCanTarget(IInteractable i)
        {
            try { return i.CanTarget(this); }
            catch (System.Exception e) { LogOnce(i, e); return false; }
        }

        private void Safe(object source, System.Action action)
        {
            try { action(); }
            catch (System.Exception e) { LogOnce(source, e); }
        }

        // A throwing tool, highlighter or interactable is logged once and never breaks the frame.
        private void LogOnce(object source, System.Exception e)
        {
            if (loggedThrowers.Add(source)) Debug.LogException(e, this);
        }
    }
}

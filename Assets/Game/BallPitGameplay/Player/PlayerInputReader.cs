// PlayerInputReader: the local player's input (deliverable A, file plan P2; design c 5). A swappable device source
// (LegacyInputSource reads the legacy Input Manager) under a reader that applies blocking (kit menu or modal, gameplay
// cursor claims, focus), the cursor policy, sensitivity and invert, and press counting. Gameplay reads IPlayerInput
// only. Presses are counters: each consumer keeps its own last-seen value, so no press is lost between frames and
// fixed steps, and no consumer steals another's edge. Escape is never read: the kit's session menu owns it.
using System.Collections.Generic;
using BallPitFinal;
using MultiplayerKit.UI;
using UnityEngine;

namespace BallPitGameplay
{
    /// <summary>Interact (E) and Shop (Tab) are counted but reserved for deliverable B. Hotbar1..5 (deliverable B,
    /// additive — appended so existing indices are unchanged) select tools; ToolWheel scrolls through them.</summary>
    public enum PlayerButton { Jump, Sprint, Primary, Secondary, Respawn, Interact, Shop, Hotbar1, Hotbar2, Hotbar3, Hotbar4, Hotbar5 }

    [System.Flags]
    public enum PlayerInputBlock { None = 0, Inactive = 1, Unfocused = 2, KitModal = 4, CursorClaimed = 8 }

    /// <summary>What gameplay reads. While blocked: Move and LookDelta are zero, nothing is held, no press is counted.</summary>
    public interface IPlayerInput
    {
        bool Active { get; }                 // the reader is active (local avatar released) and not blocked
        Vector2 Move { get; }                // x = strafe, y = forward; magnitude <= 1
        Vector2 LookDelta { get; }           // degrees this frame (x = yaw right, y = pitch up); sensitivity and invert applied
        bool IsHeld(PlayerButton button);
        int PressCount(PlayerButton button); // only grows; each consumer keeps its own last-seen value
        float ToolWheel { get; }             // deliverable B: mouse-wheel delta this frame (0 while blocked)
    }

    /// <summary>Device layer: LegacyInputSource now; an Input System or gamepad source later, with no gameplay change.</summary>
    public interface IPlayerInputSource
    {
        Vector2 ReadMove();                        // raw; the reader clamps it to the unit disc
        Vector2 ReadLookDegrees(float unscaledDt); // before user sensitivity. Mouse: units x degreesPerUnit. Stick: deg/s x dt
        bool ReadHeld(PlayerButton button);
        bool ReadPressed(PlayerButton button);     // went down this frame
        float ReadWheel();                         // deliverable B: scroll-wheel delta this frame (+ up / - down)
    }

    [System.Serializable]
    public sealed class LegacyInputSource : IPlayerInputSource
    {
        public string moveXAxis = "Horizontal", moveYAxis = "Vertical", lookXAxis = "Mouse X", lookYAxis = "Mouse Y";
        [Tooltip("Degrees per Mouse X/Y unit. Those axes are per-frame deltas: never multiplied by deltaTime.")]
        public float degreesPerMouseUnit = 2f;
        public KeyCode jump = KeyCode.Space, sprint = KeyCode.LeftShift, respawn = KeyCode.R, interact = KeyCode.E, shop = KeyCode.Tab;
        public KeyCode hotbar1 = KeyCode.Alpha1, hotbar2 = KeyCode.Alpha2, hotbar3 = KeyCode.Alpha3, hotbar4 = KeyCode.Alpha4, hotbar5 = KeyCode.Alpha5;
        public int primaryMouseButton = 0, secondaryMouseButton = 1;

        [System.NonSerialized] private bool moveAxes = true, lookAxes = true;

        /// <summary>Probes the four axis names once (GetAxisRaw throws for an axis missing from the Input Manager). A
        /// missing move axis falls back to W/A/S/D; a missing look axis gives zero look. One Error each.</summary>
        public void Probe(Object context)
        {
            moveAxes = AxisExists(moveXAxis, context) & AxisExists(moveYAxis, context);
            lookAxes = AxisExists(lookXAxis, context) & AxisExists(lookYAxis, context);
            if (!moveAxes) Debug.LogError("[PlayerInput] a move axis is missing; using W/A/S/D", context);
            if (!lookAxes) Debug.LogError("[PlayerInput] a look axis is missing; mouse look is off", context);
        }

        public Vector2 ReadMove()
        {
            if (moveAxes) return new Vector2(Input.GetAxisRaw(moveXAxis), Input.GetAxisRaw(moveYAxis));
            float x = (Input.GetKey(KeyCode.D) ? 1f : 0f) - (Input.GetKey(KeyCode.A) ? 1f : 0f);
            float y = (Input.GetKey(KeyCode.W) ? 1f : 0f) - (Input.GetKey(KeyCode.S) ? 1f : 0f);
            return new Vector2(x, y);
        }

        public Vector2 ReadLookDegrees(float unscaledDt) =>
            lookAxes ? new Vector2(Input.GetAxisRaw(lookXAxis), Input.GetAxisRaw(lookYAxis)) * degreesPerMouseUnit : Vector2.zero;

        public bool ReadHeld(PlayerButton button)
        {
            switch (button)
            {
                case PlayerButton.Primary: return Input.GetMouseButton(primaryMouseButton);
                case PlayerButton.Secondary: return Input.GetMouseButton(secondaryMouseButton);
                default: return Input.GetKey(KeyOf(button));
            }
        }

        public bool ReadPressed(PlayerButton button)
        {
            switch (button)
            {
                case PlayerButton.Primary: return Input.GetMouseButtonDown(primaryMouseButton);
                case PlayerButton.Secondary: return Input.GetMouseButtonDown(secondaryMouseButton);
                default: return Input.GetKeyDown(KeyOf(button));
            }
        }

        private KeyCode KeyOf(PlayerButton b)
        {
            switch (b)
            {
                case PlayerButton.Jump: return jump;
                case PlayerButton.Sprint: return sprint;
                case PlayerButton.Respawn: return respawn;
                case PlayerButton.Interact: return interact;
                case PlayerButton.Shop: return shop;
                case PlayerButton.Hotbar1: return hotbar1;
                case PlayerButton.Hotbar2: return hotbar2;
                case PlayerButton.Hotbar3: return hotbar3;
                case PlayerButton.Hotbar4: return hotbar4;
                case PlayerButton.Hotbar5: return hotbar5;
                default: return KeyCode.None;
            }
        }

        public float ReadWheel() => Input.mouseScrollDelta.y;

        private static bool AxisExists(string axis, Object context)
        {
            try { Input.GetAxisRaw(axis); return true; }
            catch (System.ArgumentException) { Debug.LogError($"[PlayerInput] axis '{axis}' is not in the Input Manager", context); return false; }
        }
    }

    /// <summary>The pure blocking and cursor decision (design c 5), unit-testable.</summary>
    public static class InputBlockPolicy
    {
        public static PlayerInputBlock Compute(bool active, bool focused, bool kitModal, bool menuOpen, bool claimed)
        {
            PlayerInputBlock b = PlayerInputBlock.None;
            if (!active) b |= PlayerInputBlock.Inactive;
            if (!focused) b |= PlayerInputBlock.Unfocused;
            if (kitModal || menuOpen) b |= PlayerInputBlock.KitModal;
            if (claimed) b |= PlayerInputBlock.CursorClaimed;
            return b;
        }

        /// <summary>The cursor the reader wants, or false when it leaves the cursor alone (inactive, or only unfocused).</summary>
        public static bool DesiredCursor(PlayerInputBlock b, out CursorLockMode lockMode, out bool visible)
        {
            lockMode = CursorLockMode.None; visible = true;
            if ((b & PlayerInputBlock.Inactive) != 0) return false;
            if ((b & (PlayerInputBlock.KitModal | PlayerInputBlock.CursorClaimed)) != 0) return true;
            if ((b & PlayerInputBlock.Unfocused) != 0) return false;
            lockMode = CursorLockMode.Locked; visible = false;
            return true;
        }

        /// <summary>What the dev harness's InputBlocked hook reports: a menu, modal or panel owns the cursor.</summary>
        public static bool BlocksDevKeys(PlayerInputBlock b) => (b & (PlayerInputBlock.KitModal | PlayerInputBlock.CursorClaimed)) != 0;

        /// <summary>Unit disc clamp; a non-finite value becomes zero.</summary>
        public static Vector2 SanitizeMove(Vector2 m)
        {
            if (!Finite(m)) return Vector2.zero;
            return m.sqrMagnitude > 1f ? m.normalized : m;
        }

        public static Vector2 SanitizeLook(Vector2 l, float sensitivity, bool invertY)
        {
            if (!Finite(l)) return Vector2.zero;
            l *= sensitivity;
            if (invertY) l.y = -l.y;
            return l;
        }

        private static bool Finite(Vector2 v) => !float.IsNaN(v.x) && !float.IsInfinity(v.x) && !float.IsNaN(v.y) && !float.IsInfinity(v.y);
    }

    /// <summary>Cursor claims by gameplay panels (B's shop): while any owner claims it, the reader unlocks the cursor.</summary>
    public static class GameplayCursor
    {
        private static readonly List<Object> owners = new List<Object>();

        /// <summary>Raised when a claim is added or released; the reader re-evaluates on it.</summary>
        public static event System.Action Changed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { owners.Clear(); Changed = null; }

        /// <summary>Idempotent per owner.</summary>
        public static void Claim(Object owner)
        {
            if (owner == null || IndexOf(owner) >= 0) return;
            owners.Add(owner);
            Raise();
        }

        /// <summary>Idempotent.</summary>
        public static void Release(Object owner)
        {
            int i = IndexOf(owner);
            if (i < 0) return;
            owners.RemoveAt(i);
            Raise();
        }

        /// <summary>True while a live owner claims the cursor; destroyed owners are pruned here.</summary>
        public static bool IsClaimed
        {
            get
            {
                for (int i = owners.Count - 1; i >= 0; i--) if (owners[i] == null) owners.RemoveAt(i);
                return owners.Count > 0;
            }
        }

        private static int IndexOf(Object owner)
        {
            if (ReferenceEquals(owner, null)) return -1;
            for (int i = 0; i < owners.Count; i++) if (ReferenceEquals(owners[i], owner)) return i;
            return -1;
        }

        private static void Raise()
        {
            System.Action handlers = Changed;
            if (handlers == null) return;
            foreach (System.Delegate d in handlers.GetInvocationList())
            {
                try { ((System.Action)d)(); }
                catch (System.Exception e) { Debug.LogException(e); }
            }
        }
    }

    [DefaultExecutionOrder(-100)]
    [DisallowMultipleComponent]
    public sealed class PlayerInputReader : MonoBehaviour, IPlayerInput
    {
        private const int ButtonCount = 12; // PlayerButton values (deliverable B appended Hotbar1..5)

        [SerializeField] private LegacyInputSource legacy = new LegacyInputSource();
        [Tooltip("Optional: a component implementing IPlayerInputSource (Input System, gamepad). Empty = the legacy source.")]
        [SerializeField] private MonoBehaviour sourceOverride;

        private IPlayerInputSource source;
        private PlayerAvatar avatar;
        private BallPitFinalController controller;
        private System.Func<bool> installedBlock;
        private bool initialized, active, menuOpen, wasBlocked = true, wroteLocked, sourceFailed;
        private int menuClosedFrame = -1;
        private PlayerInputBlock block = PlayerInputBlock.Inactive;
        private Vector2 move, look;
        private float wheel;
        private readonly bool[] held = new bool[ButtonCount];
        private readonly int[] presses = new int[ButtonCount];

        public bool Active => block == PlayerInputBlock.None;
        public Vector2 Move => Active ? move : Vector2.zero;
        public Vector2 LookDelta => Active ? look : Vector2.zero;
        public bool IsHeld(PlayerButton button) => Active && held[(int)button];
        public int PressCount(PlayerButton button) => presses[(int)button];
        public float ToolWheel => Active ? wheel : 0f;
        /// <summary>Why input is blocked this frame (None while active).</summary>
        public PlayerInputBlock BlockReasons => block;

        private void Awake() => avatar = GetComponent<PlayerAvatar>();

        /// <summary>Owner only (PlayerAvatar, after Controller is set): resolves the source, starts inactive, and installs
        /// the controller's InputBlocked hook so dev keys never fire under the kit menu or a gameplay panel.</summary>
        public void InitLocal()
        {
            if (avatar == null) avatar = GetComponent<PlayerAvatar>();
            controller = avatar != null ? avatar.Controller : null;
            source = ResolveSource();
            initialized = true;
            active = false;
            wasBlocked = true;
            if (controller != null)
            {
                installedBlock = IsGameplayInputBlocked;
                controller.InputBlocked = installedBlock;
            }
            Evaluate();
        }

        /// <summary>PlayerAvatar: true on release (locks the cursor unless blocked), false on freeze or despawn.</summary>
        public void SetActive(bool value)
        {
            if (!initialized) { active = false; return; }
            bool was = active;
            active = value;
            if (value && !was) wasBlocked = true;
            Evaluate();
            if (!value) ReleaseCursorIfLocked();
        }

        /// <summary>Despawn: inactive, cursor released, and the controller hook removed while it is still ours.</summary>
        internal void Shutdown()
        {
            if (initialized) SetActive(false);
            ReleaseCursorIfLocked();
            if (controller != null && installedBlock != null && controller.InputBlocked == installedBlock) controller.InputBlocked = null;
            installedBlock = null;
            controller = null;
            initialized = false;
        }

        private bool IsGameplayInputBlocked() => InputBlockPolicy.BlocksDevKeys(block);

        private void OnEnable()
        {
            SessionMenuView.OpenChanged += OnMenuOpenChanged;
            GameplayCursor.Changed += OnClaimsChanged;
        }

        private void OnDisable()
        {
            SessionMenuView.OpenChanged -= OnMenuOpenChanged;
            GameplayCursor.Changed -= OnClaimsChanged;
            ClearSample();
            ReleaseCursorIfLocked();
        }

        private void OnDestroy() => Shutdown();

        private void OnApplicationFocus(bool focus)
        {
            if (focus && initialized) Evaluate(); // re-apply the cursor after alt-tab
        }

        private void OnMenuOpenChanged(bool open)
        {
            menuOpen = open;
            if (!open) menuClosedFrame = Time.frameCount; // the kit just restored its saved cursor: leave it alone this frame
            if (initialized) Evaluate(); // same-frame reaction: drops held buttons, moves the cursor
        }

        private void OnClaimsChanged()
        {
            if (initialized) Evaluate();
        }

        private void Update()
        {
            if (!initialized) return;
            Evaluate();
            if (block != PlayerInputBlock.None) { wasBlocked = true; return; }
            try
            {
                Vector2 m = InputBlockPolicy.SanitizeMove(source.ReadMove());
                Vector2 l = InputBlockPolicy.SanitizeLook(source.ReadLookDegrees(Time.unscaledDeltaTime),
                                                          PlayerViewPrefs.Sensitivity, PlayerViewPrefs.InvertY);
                if (wasBlocked) { l = Vector2.zero; wasBlocked = false; } // absorbs the jump when the cursor re-locks
                move = m;
                look = l;
                float w = source.ReadWheel();
                wheel = float.IsNaN(w) || float.IsInfinity(w) ? 0f : w;
                for (int i = 0; i < ButtonCount; i++)
                {
                    held[i] = source.ReadHeld((PlayerButton)i);
                    if (source.ReadPressed((PlayerButton)i)) presses[i]++;
                }
            }
            catch (System.Exception e)
            {
                ClearSample();
                if (!sourceFailed) { sourceFailed = true; Debug.LogException(e, this); }
            }
        }

        // Recomputes the block (immediately on the kit and claim events too) and applies the cursor policy.
        private void Evaluate()
        {
            block = InputBlockPolicy.Compute(active, Application.isFocused, KitOverlay.IsModalOpen, menuOpen, GameplayCursor.IsClaimed);
            if (block != PlayerInputBlock.None) ClearSample();
            // IsModalOpen stays true for the menu's closing frame; unlocking then would flash the cursor for one frame.
            if (Time.frameCount == menuClosedFrame) return;
            if (!InputBlockPolicy.DesiredCursor(block, out CursorLockMode lockMode, out bool visible)) return;
            if (Cursor.lockState != lockMode) { Cursor.lockState = lockMode; wroteLocked = lockMode == CursorLockMode.Locked; }
            if (Cursor.visible != visible) Cursor.visible = visible;
        }

        // Only undoes a lock this reader wrote (the kit restores its own saved state).
        private void ReleaseCursorIfLocked()
        {
            if (!wroteLocked) return;
            wroteLocked = false;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void ClearSample()
        {
            move = look = Vector2.zero;
            wheel = 0f;
            for (int i = 0; i < ButtonCount; i++) held[i] = false;
        }

        private IPlayerInputSource ResolveSource()
        {
            if (sourceOverride != null)
            {
                if (sourceOverride is IPlayerInputSource custom) return custom;
                Debug.LogError($"[PlayerInput] sourceOverride '{sourceOverride.GetType().Name}' does not implement IPlayerInputSource; " +
                               "using the legacy Input Manager", this);
            }
            if (legacy == null) legacy = new LegacyInputSource();
            legacy.Probe(this);
            return legacy;
        }
    }
}

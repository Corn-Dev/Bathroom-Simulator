// PlayerView: mouse look, the manual camera and body interpolation, eye smoothing, the hold point, the owner's body
// mode and the HUD (deliverable A, file plan P9; design c 2, c 6, c 9-c 11). The root moves only inside Physics.Simulate,
// so the view draws Head and Body at alpha = (Time.time - Time.fixedTime) / fixedDeltaTime between the last two physics
// poses, the same alpha the controller uses for the active balls. It never touches the root. Activate/Deactivate are
// the only SetViewCamera calls. Also OwnerBodyMode, HoldPointTime, PoseInterpolator, LookMath and PlayerViewPrefs.
using BallPitFinal;
using UnityEngine;
using UnityEngine.Rendering;

namespace BallPitGameplay
{
    public enum OwnerBodyMode { ShadowsOnly, CullLayer }
    public enum HoldPointTime { Simulation, Render }

    [DefaultExecutionOrder(-50)]
    [DisallowMultipleComponent]
    public sealed class PlayerView : MonoBehaviour
    {
        [SerializeField] private Transform head;
        [SerializeField] private Transform body;
        [SerializeField] private Transform holdPoint;
        [SerializeField] private Camera headCamera;
        [SerializeField] private AudioListener audioListener;
        [Tooltip("PlayerHud.prefab: instantiated for the local player at its first release.")]
        [SerializeField] private PlayerHud hudPrefab;
        [Tooltip("ShadowsOnly: the owner's body renderers cast shadows but are invisible to the owner. CullLayer: the Head " +
                 "camera culls PlayerBody (the owner's shadow is lost too).")]
        [SerializeField] private OwnerBodyMode ownerBodyMode = OwnerBodyMode.ShadowsOnly;

        private PlayerConfig config;
        private BallPitFinalController controller;
        private IPlayerInput input;
        private PlayerAvatar avatar;
        private Rigidbody rb;
        private bool isLocal, active, warnedNoHud, hasStepped;
        private float yaw, pitch, eyeOffset, ballRadius = 0.25f;
        private PoseInterpolator interp;
        private Vector3 smoothedNormal = Vector3.up, renderedFeet;
        private PlayerHud hud;

#if UNITY_EDITOR
        [System.NonSerialized] private string lastValidation;
#endif

        public Transform Head => head;
        public Transform Body => body;
        public Transform HoldPoint => holdPoint;
        public Camera HeadCamera => headCamera;
        public AudioListener Listener => audioListener;
        public float Yaw => yaw;
        public float Pitch => pitch;
        public Quaternion AimRotation => Quaternion.Euler(pitch, yaw, 0f);
        /// <summary>The Head camera's center ray at this frame's rendered pose.</summary>
        public Ray AimRay
        {
            get
            {
                Transform t = headCamera != null ? headCamera.transform : head != null ? head : transform;
                return new Ray(t.position, t.forward);
            }
        }
        public Vector3 RenderedFeet => renderedFeet;
        /// <summary>The local avatar's HUD instance; null until the first release, and after despawn.</summary>
        public PlayerHud Hud => hud;

        /// <summary>PlayerAvatar.Awake, every instance. Null is ignored.</summary>
        public void SetConfig(PlayerConfig value) { if (value != null) config = value; }

        /// <summary>Owner (design c 3 step 3). controller may be null in tests: Activate/Deactivate then skip SetViewCamera.
        /// The motor feeds the view through OnPhysicsStepped; the view reads the body pose for the Simulation hold point.</summary>
        public void InitLocal(BallPitFinalController pitController, IPlayerInput playerInput, PlayerMotor playerMotor)
        {
            controller = pitController; input = playerInput;
            avatar = GetComponent<PlayerAvatar>();
            rb = GetComponent<Rigidbody>();
            isLocal = true;
            yaw = LookMath.WrapYaw(transform.eulerAngles.y);
            pitch = 0f;
            interp.Snap(transform.position);
            renderedFeet = transform.position;
            if (headCamera != null) headCamera.enabled = false;
            if (audioListener != null) audioListener.enabled = false;
            PlayerViewPrefs.Changed -= ApplyFov;
            PlayerViewPrefs.Changed += ApplyFov;
        }

        /// <summary>Non-owner: Head at (0, eyeHeight, 0), Body at identity, camera and listener off; no Update work.</summary>
        public void InitRemote()
        {
            isLocal = false;
            active = false;
            if (headCamera != null) headCamera.enabled = false;
            if (audioListener != null) audioListener.enabled = false;
            RestoreLocalPoses();
        }

        /// <summary>Every PitWorldBuilt (local avatar): the ball radius for the hold point's outward resolve.</summary>
        public void SetWorldContext(float ballDiameter)
        {
            if (float.IsNaN(ballDiameter) || float.IsInfinity(ballDiameter) || ballDiameter < 0.05f || ballDiameter > 2f)
            {
                Debug.LogWarning($"[PlayerView] ball diameter {ballDiameter} is outside 0.05..2 m; using 0.5", this);
                ballDiameter = 0.5f;
            }
            ballRadius = ballDiameter * 0.5f;
        }

        /// <summary>Release: camera and listener on, MainCamera tag, FOV and body mode, SetViewCamera, the HUD shown
        /// (instantiated the first time).</summary>
        public void Activate()
        {
            if (!isLocal) return;
            active = true;
            ApplyRenderPose(1f);
            if (headCamera != null)
            {
                headCamera.enabled = true;
                headCamera.gameObject.tag = "MainCamera";
                ApplyFov();
            }
            if (audioListener != null) audioListener.enabled = true;
            ApplyOwnerBodyMode();
            if (controller != null && headCamera != null) controller.SetViewCamera(headCamera);
            if (hud == null && hudPrefab != null)
            {
                hud = Instantiate(hudPrefab); // scene root, not a child of the NetworkObject
                hud.name = hudPrefab.name;
                hud.Bind(avatar);
            }
            else if (hud == null && !warnedNoHud) { warnedNoHud = true; Debug.LogWarning("[PlayerView] no hudPrefab; running without a HUD", this); }
            if (hud != null) hud.gameObject.SetActive(true);
        }

        /// <summary>Freeze: SetViewCamera(null) (the loading view returns), camera and listener off, untagged, HUD hidden.</summary>
        public void Deactivate()
        {
            if (!isLocal) return;
            active = false;
            if (controller != null) controller.SetViewCamera(null);
            if (headCamera != null) { headCamera.enabled = false; headCamera.gameObject.tag = "Untagged"; }
            if (audioListener != null) audioListener.enabled = false;
            if (hud != null) hud.gameObject.SetActive(false);
            RestoreLocalPoses();
        }

        /// <summary>Despawn: the freeze work, then the HUD is destroyed.</summary>
        internal void Shutdown()
        {
            Deactivate();
            PlayerViewPrefs.Changed -= ApplyFov;
            if (hud != null) Destroy(hud.gameObject);
            hud = null;
            isLocal = false;
        }

        private void OnDestroy()
        {
            PlayerViewPrefs.Changed -= ApplyFov;
            if (hud != null) Destroy(hud.gameObject);
        }

        /// <summary>Teleport and placement: both interpolation ends become the target, pitch 0, eye offset cleared.</summary>
        public void SnapTo(Vector3 feet, float newYaw)
        {
            interp.Snap(feet);
            renderedFeet = feet;
            eyeOffset = 0f;
            smoothedNormal = Vector3.up;
            hasStepped = false;
            yaw = LookMath.WrapYaw(newYaw);
            pitch = 0f;
        }

        /// <summary>PlayerMotor.AfterPhysicsStep. grounded: this step and the previous one were grounded, in Driven mode.
        /// Absorbs ball bumps and step-ups into the eye offset (never jumps, falls or landings).</summary>
        public void OnPhysicsStepped(Vector3 position, bool grounded, Vector3 groundNormal)
        {
            if (!isLocal || config == null) return;
            Vector3 old = interp.Current;
            bool hadPose = interp.HasValue;
            interp.Push(position);
            if (!grounded) { hasStepped = false; return; }
            float k = config.groundNormalSmoothTime > 0f ? 1f - Mathf.Exp(-Time.fixedDeltaTime / config.groundNormalSmoothTime) : 1f;
            smoothedNormal = hasStepped ? Vector3.Lerp(smoothedNormal, groundNormal, k).normalized : groundNormal;
            hasStepped = true;
            if (!hadPose || config.eyeSmoothTime <= 0f) return;
            float bump = EyeSmoothing.Bump(position - old, smoothedNormal, config.stepHeight + 0.05f);
            interp.ShiftPrevious(Vector3.up * bump);
            eyeOffset = Mathf.Clamp(eyeOffset + bump, -0.5f, 0.5f);
        }

        /// <summary>Render: the HoldPoint transform written this frame (draws, NetPit.SetView, LocalHand). Simulation: from the
        /// physics pose (HandTarget in BeforePhysicsStep). Both resolved outward of the own capsule.</summary>
        public Vector3 GetHoldPoint(HoldPointTime time)
        {
            Vector3 offset = config != null ? config.holdOffset : new Vector3(0.25f, -0.28f, 0.55f);
            float eye = config != null ? config.eyeHeight : 1.62f;
            Vector3 feet, p;
            if (time == HoldPointTime.Render)
            {
                feet = renderedFeet;
                p = holdPoint != null ? holdPoint.position : feet + Vector3.up * eye + AimRotation * offset;
            }
            else
            {
                feet = rb != null ? rb.position : transform.position;
                p = feet + Vector3.up * eye + AimRotation * offset;
            }
            return ResolveOutward(p, feet);
        }

        private void Update()
        {
            if (!isLocal || !active || config == null) return;
            if (input != null) LookMath.Integrate(ref yaw, ref pitch, input.LookDelta, config.pitchLimit);
            eyeOffset = EyeSmoothing.Decay(eyeOffset, Time.deltaTime, config.eyeSmoothTime);
            ApplyRenderPose(PoseInterpolator.Alpha(Time.time, Time.fixedTime, Time.fixedDeltaTime));
        }

        // root = interp(alpha) - up * eyeOffset; Head at root + eyeHeight with the look; Body at root with the yaw.
        private void ApplyRenderPose(float alpha)
        {
            Vector3 root = interp.Evaluate(alpha) - Vector3.up * eyeOffset;
            renderedFeet = root;
            float eye = config != null ? config.eyeHeight : 1.62f;
            if (head != null) head.SetPositionAndRotation(root + Vector3.up * eye, Quaternion.Euler(pitch, yaw, 0f));
            if (body != null) body.SetPositionAndRotation(root, Quaternion.Euler(0f, yaw, 0f));
        }

        private void RestoreLocalPoses()
        {
            float eye = config != null ? config.eyeHeight : 1.62f;
            if (head != null) { head.localPosition = new Vector3(0f, eye, 0f); head.localRotation = Quaternion.identity; }
            if (body != null) { body.localPosition = Vector3.zero; body.localRotation = Quaternion.identity; }
        }

        // Pushes a point near the capsule axis out horizontally to capsuleRadius + ballRadius + holdClearance.
        private Vector3 ResolveOutward(Vector3 p, Vector3 feet)
        {
            float r = config != null ? config.capsuleRadius : 0.35f, h = config != null ? config.capsuleHeight : 1.8f;
            float clearance = config != null ? config.holdClearance : 0.03f;
            if (p.y < feet.y - ballRadius || p.y > feet.y + h + ballRadius) return p;
            Vector3 axis = new Vector3(feet.x, p.y, feet.z), off = p - axis;
            float minR = r + ballRadius + clearance, d = off.magnitude;
            if (d >= minR) return p;
            Vector3 dir = d > 1e-4f ? off / d : Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            return axis + dir * minR;
        }

        private void ApplyFov()
        {
            if (headCamera != null && isLocal) headCamera.fieldOfView = PlayerViewPrefs.VerticalFov;
        }

        // Owner only: ShadowsOnly on every renderer under Body, or the PlayerBody bit cleared from the Head camera.
        private void ApplyOwnerBodyMode()
        {
            if (ownerBodyMode == OwnerBodyMode.CullLayer)
            {
                int layer = PlayerLayers.PlayerBody;
                if (headCamera != null && layer >= 0) headCamera.cullingMask &= ~(1 << layer);
                return;
            }
            if (body == null) return;
            foreach (Renderer r in body.GetComponentsInChildren<Renderer>(true)) r.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            string text = null;
            int layer = LayerMask.NameToLayer(PlayerLayers.PlayerBodyName);
            if (body != null && layer >= 0)
                foreach (Renderer r in body.GetComponentsInChildren<Renderer>(true))
                    if (r.gameObject.layer != layer) { text = $"renderer '{r.name}' under Body is not on the PlayerBody layer"; break; }
            if (text == lastValidation) return;
            lastValidation = text;
            if (text != null) Debug.LogWarning("[PlayerView] " + text, this);
        }
#endif
    }

    /// <summary>Pure two-pose interpolation for the manual camera and body (design c 9).</summary>
    public struct PoseInterpolator
    {
        private Vector3 previous, current;
        private bool hasValue;

        public Vector3 Previous => previous;
        public Vector3 Current => current;
        public bool HasValue => hasValue;

        /// <summary>After each physics step: previous = current, current = p (the first push sets both).</summary>
        public void Push(Vector3 p)
        {
            if (!hasValue) { previous = current = p; hasValue = true; return; }
            previous = current;
            current = p;
        }

        public void Snap(Vector3 p) { previous = current = p; hasValue = true; }
        /// <summary>Eye smoothing: this step's absorbed motion is not animated.</summary>
        public void ShiftPrevious(Vector3 d) => previous += d;
        public Vector3 Evaluate(float alpha) => Vector3.LerpUnclamped(previous, current, Mathf.Clamp01(alpha));

        /// <summary>The controller's LateUpdate expression for the active balls.</summary>
        public static float Alpha(float time, float fixedTime, float fixedDt) => fixedDt > 0f ? Mathf.Clamp01((time - fixedTime) / fixedDt) : 1f;
    }

    /// <summary>Pure eye smoothing (design c 9): the vertical bump the ground plane does not predict, and its decay.</summary>
    public static class EyeSmoothing
    {
        /// <summary>dp.y minus the slope-predicted rise -(n.x dp.x + n.z dp.z) / n.y, clamped to +-limit.</summary>
        public static float Bump(Vector3 dp, Vector3 n, float limit)
        {
            float expected = n.y > 0.1f ? -(n.x * dp.x + n.z * dp.z) / n.y : 0f;
            return Mathf.Clamp(dp.y - expected, -limit, limit);
        }

        /// <summary>offset * exp(-dt / tau), zero below 1 mm or with tau 0, capped at +-0.5 m.</summary>
        public static float Decay(float offset, float dt, float tau)
        {
            if (tau <= 0f) return 0f;
            offset *= Mathf.Exp(-dt / tau);
            return Mathf.Abs(offset) < 0.001f ? 0f : Mathf.Clamp(offset, -0.5f, 0.5f);
        }
    }

    /// <summary>Pure look math (design c 6): yaw wrapped to [0, 360), pitch positive down and clamped.</summary>
    public static class LookMath
    {
        public static float WrapYaw(float yaw)
        {
            float w = Mathf.Repeat(yaw, 360f);
            return w >= 360f ? 0f : w;
        }

        public static float ClampPitch(float pitch, float limit) => Mathf.Clamp(pitch, -limit, limit);

        /// <summary>yaw += delta.x, pitch -= delta.y (delta already has sensitivity and invert); non-finite input is ignored.</summary>
        public static void Integrate(ref float yaw, ref float pitch, Vector2 lookDelta, float pitchLimit)
        {
            if (float.IsNaN(lookDelta.x) || float.IsInfinity(lookDelta.x) || float.IsNaN(lookDelta.y) || float.IsInfinity(lookDelta.y)) return;
            yaw = WrapYaw(yaw + lookDelta.x);
            pitch = ClampPitch(pitch - lookDelta.y, pitchLimit);
        }
    }

    /// <summary>Per-machine view prefs (design c 11), never host settings. Loaded lazily, cached; setters write PlayerPrefs.</summary>
    public static class PlayerViewPrefs
    {
        public const string HorizontalFovKey = "BallPit.View.HorizontalFov";     // float degrees at a 16:9 reference
        public const string SensitivityKey = "BallPit.Input.MouseSensitivity";   // float multiplier
        public const string InvertYKey = "BallPit.Input.InvertY";                // int 0/1
        public const float DefaultHorizontalFov = 90f, MinHorizontalFov = 60f, MaxHorizontalFov = 120f;
        public const float DefaultSensitivity = 1f, MinSensitivity = 0.05f, MaxSensitivity = 10f;

        private static bool loaded, invertY;
        private static float horizontalFov = DefaultHorizontalFov, sensitivity = DefaultSensitivity;

        /// <summary>Raised by every setter; PlayerView re-applies the FOV.</summary>
        public static event System.Action Changed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { loaded = false; Changed = null; }

        public static float HorizontalFov
        {
            get { Load(); return horizontalFov; }
            set { Load(); horizontalFov = Sanitize(value, DefaultHorizontalFov, MinHorizontalFov, MaxHorizontalFov); PlayerPrefs.SetFloat(HorizontalFovKey, horizontalFov); Raise(); }
        }

        public static float Sensitivity
        {
            get { Load(); return sensitivity; }
            set { Load(); sensitivity = Sanitize(value, DefaultSensitivity, MinSensitivity, MaxSensitivity); PlayerPrefs.SetFloat(SensitivityKey, sensitivity); Raise(); }
        }

        public static bool InvertY
        {
            get { Load(); return invertY; }
            set { Load(); invertY = value; PlayerPrefs.SetInt(InvertYKey, value ? 1 : 0); Raise(); }
        }

        /// <summary>The camera's vertical fieldOfView for the horizontal pref at 16:9 (Hor+ on wider screens).</summary>
        public static float VerticalFov => Camera.HorizontalToVerticalFieldOfView(HorizontalFov, 16f / 9f);

        /// <summary>PlayerPrefs.Save(), for a future options screen's Apply.</summary>
        public static void Save() => PlayerPrefs.Save();

        private static void Load()
        {
            if (loaded) return;
            loaded = true;
            horizontalFov = Read(HorizontalFovKey, DefaultHorizontalFov, MinHorizontalFov, MaxHorizontalFov);
            sensitivity = Read(SensitivityKey, DefaultSensitivity, MinSensitivity, MaxSensitivity);
            invertY = PlayerPrefs.GetInt(InvertYKey, 0) != 0;
        }

        // A stored value that is not finite or out of range is reset or clamped, with one Warning per key (per load).
        private static float Read(string key, float def, float min, float max)
        {
            float v = PlayerPrefs.GetFloat(key, def), s = Sanitize(v, def, min, max);
            if (s != v) Debug.LogWarning($"[PlayerViewPrefs] stored {key} = {v} is not usable; using {s}");
            return s;
        }

        private static float Sanitize(float v, float def, float min, float max) =>
            float.IsNaN(v) || float.IsInfinity(v) ? def : Mathf.Clamp(v, min, max);

        private static void Raise()
        {
            System.Action handlers = Changed;
            if (handlers == null) return;
            try { handlers(); } catch (System.Exception e) { Debug.LogException(e); }
        }
    }
}

// BallContainer: an authored sorting container (brief §4.1-§4.2; game-design §2). Placed anywhere by the owner, tied to
// one colour. Balls enter through its TOP OPENING only — an oriented local-space box (NOT a physics trigger); the host's
// DepositDriver tests active ball centres against it each step. A correct colour sells; a wrong colour is rejected back
// to the pit. Implements A's IInteractable (Kind = Container) so the player's aim ray can target it (HUD shows its
// colour/fill). Fill = stored(colour)/total(colour), pushed to an IContainerFillView. The "funnel" upgrade hook scales
// the intake box. Code never draws the container; the owner's art does.
using UnityEngine;
using UnityEngine.Events;

namespace BallPitGameplay
{
    [DisallowMultipleComponent]
    public sealed class BallContainer : MonoBehaviour, IInteractable
    {
        [Header("Identity")]
        [Tooltip("Stable key, ^[a-z0-9_.]{1,64}$, unique among containers (used by the save's container entries).")]
        [SerializeField] private string id = "container";
        [Tooltip("The colour index this container accepts (0-based, below the pit's colour count).")]
        [Min(0)] [SerializeField] private int colorIndex = 0;
        [Tooltip("Optional: the PitSite this container belongs to; empty = whichever site is active.")]
        [SerializeField] private PitSite site;

        [Header("Top-opening intake (local space)")]
        [Tooltip("Centre of the top-opening box, local to this transform.")]
        [SerializeField] private Vector3 intakeCenterLocal = new Vector3(0f, 1f, 0f);
        [Tooltip("Full size of the top-opening box, local to this transform.")]
        [SerializeField] private Vector3 intakeSizeLocal = new Vector3(1f, 0.5f, 1f);

        [Header("Fill view (optional)")]
        [Tooltip("Component implementing IContainerFillView. Empty = the DefaultFillView on this object, if any.")]
        [SerializeField] private MonoBehaviour fillViewOverride;

        [Header("Events (world position of the ball)")]
        public UnityEvent<Vector3> onSold;
        public UnityEvent<Vector3> onRejected;

        private IContainerFillView fillView;
        private float intakeScale = 1f;
        private float lastFill = -1f;

        /// <summary>Raised (C#) with the ball's world position when a correct ball is sold here.</summary>
        public event System.Action<Vector3> Sold;
        /// <summary>Raised (C#) with the ball's world position when a wrong-colour ball is rejected here.</summary>
        public event System.Action<Vector3> Rejected;

        public string Id => id;
        public int ColorIndex => colorIndex;
        public PitSite Site => site;

        // IInteractable (A): the player's aim ray can target the container.
        public InteractionTargetKind Kind => InteractionTargetKind.Container;
        public bool CanTarget(PlayerInteractor who) => true;

        /// <summary>True when this container counts for the active site (an unlinked container applies everywhere).</summary>
        public bool AppliesTo(PitSite activeSite) => site == null || site == activeSite;

        /// <summary>The "funnel" upgrade scales the intake opening (brief §4.3 named hook). 1 = authored size.</summary>
        public float IntakeScale
        {
            get => intakeScale;
            set => intakeScale = float.IsNaN(value) || value <= 0f ? 1f : value;
        }

        private void OnEnable()
        {
            ResolveFillView();
            ContainerRegistry.Register(this);
        }

        private void OnDisable() => ContainerRegistry.Unregister(this);

        private void ResolveFillView()
        {
            if (fillViewOverride is IContainerFillView ov) { fillView = ov; return; }
            fillView = GetComponent<IContainerFillView>();
        }

        /// <summary>
        /// True when a ball at <paramref name="worldBallPos"/> is inside the top-opening box (centre test). The box is
        /// oriented by this transform's rotation and scaled by IntakeScale. Not a physics trigger — pure geometry so it
        /// never feeds a container through pushing tools (game-design §2).
        /// </summary>
        public bool TryIntake(Vector3 worldBallPos)
        {
            Vector3 local = transform.InverseTransformPoint(worldBallPos) - intakeCenterLocal;
            Vector3 half = intakeSizeLocal * 0.5f * Mathf.Max(0.01f, intakeScale);
            return Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y && Mathf.Abs(local.z) <= half.z;
        }

        /// <summary>Host calls on a correct deposit. Raises both the C# event and the UnityEvent.</summary>
        public void NotifySold(Vector3 worldPos)
        {
            Sold?.Invoke(worldPos);
            onSold?.Invoke(worldPos);
        }

        /// <summary>Host calls on a wrong-colour deposit. Raises both the C# event and the UnityEvent.</summary>
        public void NotifyRejected(Vector3 worldPos)
        {
            Rejected?.Invoke(worldPos);
            onRejected?.Invoke(worldPos);
        }

        /// <summary>Every peer: updates the fill view when the stored fraction changed meaningfully.</summary>
        public void SetFill(float fraction)
        {
            float f = Mathf.Clamp01(float.IsNaN(fraction) ? 0f : fraction);
            if (Mathf.Abs(f - lastFill) < 0.0005f) return;
            lastFill = f;
            if (fillView == null) ResolveFillView();
            fillView?.SetFill(f);
        }

        /// <summary>The world-space centre of the intake opening (feed point, sale popup anchor).</summary>
        public Vector3 IntakeWorldCenter => transform.TransformPoint(intakeCenterLocal);

        /// <summary>Editor: the intake box as a local-space Bounds (gizmo + validator).</summary>
        public Bounds IntakeLocalBounds => new Bounds(intakeCenterLocal, intakeSizeLocal * Mathf.Max(0.01f, intakeScale));

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (!BallPitSession.BallPitGameState.IsValidKey(id, BallPitSession.BallPitGameState.MaxIdLength))
                Debug.LogWarning($"[BallContainer] '{name}': id '{id}' must match ^[a-z0-9_.]{{1,64}}$", this);
            if (intakeSizeLocal.x <= 0f || intakeSizeLocal.y <= 0f || intakeSizeLocal.z <= 0f)
                Debug.LogWarning($"[BallContainer] '{name}': intake size must be positive on every axis", this);
        }
#endif
    }
}

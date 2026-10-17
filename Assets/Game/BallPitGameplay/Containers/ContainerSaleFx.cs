// ContainerSaleFx: optional sale juice on a container (brief §3.3; game-design §2 "a bit of sale juice (plonk/pop)").
// It subscribes to the container's EXISTING Sold C# event (no new event or path) and, per sale, plays a placeholder
// one-shot AudioClip and emits an optional ParticleSystem pop at the intake centre. Author-attached beside a
// BallContainer; the owner swaps the clip/particle for final art. Runs on every peer that raises Sold (host raises it
// on the deposit sweep); it is pure local presentation and touches no gameplay/host state.
using UnityEngine;

namespace BallPitGameplay
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BallContainer))]
    public sealed class ContainerSaleFx : MonoBehaviour
    {
        [Tooltip("Placeholder sale sound (plonk/pop). Owner replaces. Empty = no sound.")]
        [SerializeField] private AudioClip saleClip;
        [Range(0f, 1f)] [SerializeField] private float volume = 0.8f;
        [Tooltip("Optional particle burst played at the intake centre on each sale. Owner replaces. Empty = none.")]
        [SerializeField] private ParticleSystem salePop;

        private BallContainer container;
        private AudioSource source;

        private void Awake()
        {
            container = GetComponent<BallContainer>();
            // Pooled one-shot source so overlapping sales do not cut each other off (PlayOneShot layers on one source).
            source = GetComponent<AudioSource>();
            if (source == null) source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 1f; // 3D: the plonk comes from the container
        }

        private void OnEnable()
        {
            if (container != null) container.Sold += OnSold;
        }

        private void OnDisable()
        {
            if (container != null) container.Sold -= OnSold;
        }

        private void OnSold(Vector3 worldPos)
        {
            if (saleClip != null && source != null) source.PlayOneShot(saleClip, volume);
            if (salePop != null)
            {
                Vector3 at = container != null ? container.IntakeWorldCenter : worldPos;
                salePop.transform.position = at;
                salePop.Play();
            }
        }
    }
}

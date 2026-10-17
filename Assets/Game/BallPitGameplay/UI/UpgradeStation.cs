// UpgradeStation: the diegetic, in-world upgrade point (design §2 — upgrades are a station the player walks up to and
// interacts with, NOT a Tab/hotkey menu). It is an IInteractable the center ray can target (Kind => Terminal) and an
// IInteractableActivate: a primary press while aimed at it opens the ShopPanel (which claims the cursor); the primary
// edge is consumed by PlayerInteractor so opening the shop never drops or throws a held ball.
//
// Closing: the ShopPanel owns its own close keys (Escape) because the cursor claim freezes the interactor's aim, so a
// station cannot close through the aim target. This station ALSO auto-closes on proximity: while the panel is open it
// measures the distance from PlayerAvatar.Local (the local avatar) to the station and closes the panel once the player
// walks beyond closeRadius. Proximity runs in a plain Update + direct distance check, so it works under the claim and
// never reads the (frozen) interactor aim target.
//
// Author-placed per area (design §1b — each tier's shop is its own station). Rough placeholder art sits over it; the
// collider must be on a layer in the PlayerInteractor's interactableMask so the ray can target it.
using UnityEngine;

namespace BallPitGameplay
{
    [DisallowMultipleComponent]
    public sealed class UpgradeStation : MonoBehaviour, IInteractable, IInteractableActivate
    {
        [Tooltip("The shop panel this station opens. Required; if unset the station logs once and does nothing.")]
        [SerializeField] private ShopPanel shopPanel;
        [Tooltip("Metres: while the shop is open, the local player walking beyond this distance from the station auto-" +
                 "closes it. The player can also close with Escape (the panel owns that key).")]
        [SerializeField, Min(0.1f)] private float closeRadius = 4f;

        private bool opened; // true between our Activate and our proximity close (we only auto-close what we opened)
        private bool warned;

        // IInteractable: the ray treats the station as a Terminal; it is always targetable (no ball, no held-state gate).
        public InteractionTargetKind Kind => InteractionTargetKind.Terminal;
        public bool CanTarget(PlayerInteractor who) => true;

        private void Awake()
        {
            if (shopPanel == null) shopPanel = FindObjectOfType<ShopPanel>();
        }

        // IInteractableActivate: the primary edge opens the shop. PlayerInteractor has already consumed the edge, so the
        // held ball is safe. SetOpen(true) claims the cursor; a second activate while already open is a no-op.
        public void Activate(PlayerInteractor who)
        {
            if (shopPanel == null)
            {
                if (!warned) { warned = true; Debug.LogWarning("[UpgradeStation] no ShopPanel assigned; interact does nothing.", this); }
                return;
            }
            shopPanel.SetOpen(true);
            opened = true;
        }

        // Proximity auto-close: independent of the interactor aim (which is frozen under the cursor claim) and of input,
        // so it runs even while the panel blocks the player's keys. We only close the panel if WE opened it this round.
        private void Update()
        {
            if (!opened || shopPanel == null) return;
            PlayerAvatar local = PlayerAvatar.Local;
            if (local == null) return;
            float sqr = (local.transform.position - transform.position).sqrMagnitude;
            if (sqr > closeRadius * closeRadius)
            {
                shopPanel.SetOpen(false);
                opened = false;
            }
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.5f);
            Gizmos.DrawWireSphere(transform.position, closeRadius);
        }
    }
}

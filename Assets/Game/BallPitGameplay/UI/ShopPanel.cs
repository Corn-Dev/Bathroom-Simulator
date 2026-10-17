// ShopPanel: the placeholder team shop (brief §4.6; game-design §2 shared upgrades). Opens via SetOpen(true) from an
// in-world UpgradeStation (design §2 — upgrades are a diegetic station, not a Tab/hotkey menu); a dev-only devToggleKey
// re-enables the old Tab toggle for station-less testing. Closes on Escape (read directly under the cursor claim).
// Lists the catalog with level/cost/prereqs, and a Buy button that sends a buy INTENT
// through GameplaySession (GameplayNet ServerRpc in a session, core.TryBuy offline — identical validation for every
// buyer). While open it claims the cursor (GameplayCursor), which makes PlayerInputReader free the mouse, suspend
// aiming and block the dev harness keys. Reads only IGameplayReadModel. Any player can buy (no host privilege).
using System.Collections.Generic;
using UnityEngine;

namespace BallPitGameplay
{
    [DisallowMultipleComponent]
    public sealed class ShopPanel : MonoBehaviour
    {
        [SerializeField] private GameplaySession session;
        [Tooltip("Root toggled open/closed.")]
        [SerializeField] private GameObject panelRoot;
        [Tooltip("Parent the rows are instantiated under (a vertical layout).")]
        [SerializeField] private RectTransform rowContainer;
        [Tooltip("ShopRow prefab (one per upgrade).")]
        [SerializeField] private ShopRow rowPrefab;
        [Tooltip("Refresh interval while open (s).")]
        [SerializeField, Min(0f)] private float refreshInterval = 0.2f;
        [Tooltip("DEV ONLY: when on, Tab still toggles the shop open for station-less testing. The shipped flow opens " +
                 "the shop ONLY from an in-world UpgradeStation (via SetOpen); leave this off (design §2).")]
        [SerializeField] private bool devToggleKey = false;

        private readonly List<ShopRow> rows = new List<ShopRow>();
        private bool open, cursorClaimed;
        private float nextRefresh;

        private void Awake()
        {
            if (session == null) session = FindObjectOfType<GameplaySession>();
            SetOpen(false);
        }

        private void OnDisable() => SetOpen(false); // release the cursor claim if we are torn down open

        private void Update()
        {
            PlayerAvatar local = PlayerAvatar.Local;
            // Keys are read DIRECTLY (not through the input seam): once the shop claims the cursor, the reader blocks
            // its press sampling AND PlayerInteractor early-returns at `if (!input.Active)`, so neither a seam read nor
            // the interactor could ever see a key again to CLOSE the shop. A plain Update + direct Input read still
            // runs under the claim, which is why the panel owns its own close keys. Aiming stays suspended by the claim.
            bool releasedLocal = local != null && local.IsReleased;
            // OPEN: shipped flow opens only via SetOpen(true) from an in-world UpgradeStation. Tab is a dev-only shortcut
            // for station-less testing, gated behind devToggleKey. (It toggles so dev testers can also close with Tab.)
            if (devToggleKey && releasedLocal && Input.GetKeyDown(KeyCode.Tab)) SetOpen(!open);
            if (!open) return;
            if (!releasedLocal) { SetOpen(false); return; } // the player was frozen (world change): close + release the cursor
            // CLOSE: Escape always closes while open (runs under the cursor claim, as above).
            if (Input.GetKeyDown(KeyCode.Escape)) { SetOpen(false); return; }
            if (Time.unscaledTime >= nextRefresh) { nextRefresh = Time.unscaledTime + Mathf.Max(0.05f, refreshInterval); Rebuild(); }
        }

        public void SetOpen(bool value)
        {
            if (open == value)
            {
                if (panelRoot != null && panelRoot.activeSelf != value) panelRoot.SetActive(value);
                return;
            }
            open = value;
            if (panelRoot != null) panelRoot.SetActive(value);
            if (value)
            {
                // Claiming the cursor makes PlayerInputReader free the mouse, suspend aiming, AND block the dev harness
                // keys (its InputBlocked hook returns true while a claim is live). No need to touch controller.InputBlocked.
                GameplayCursor.Claim(this); cursorClaimed = true;
                Rebuild();
            }
            else if (cursorClaimed) { GameplayCursor.Release(this); cursorClaimed = false; }
        }

        private void Rebuild()
        {
            IGameplayReadModel m = session != null ? session.ReadModel : null;
            if (m == null || rowPrefab == null || rowContainer == null) return;
            IReadOnlyList<UpgradeView> views = m.Upgrades;
            while (rows.Count < views.Count) rows.Add(Instantiate(rowPrefab, rowContainer));
            for (int i = 0; i < rows.Count; i++)
            {
                bool used = i < views.Count;
                rows[i].gameObject.SetActive(used);
                if (used) rows[i].Bind(views[i], OnBuy);
            }
        }

        private void OnBuy(string upgradeId) => session?.RequestBuy(upgradeId);
    }
}

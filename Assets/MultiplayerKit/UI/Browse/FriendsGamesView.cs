// Friends' games screen (05 §6, §9.2): friends' lobbies over RowPool<FriendLobbyRowView>, Refresh with a spinner,
// Back, and the empty/loading message. A thin view over FriendsGamesPresenter: LateUpdate flushes the presenter's
// dirty list (one rebuild per frame), and row Join clicks go through this view, so a stale presenter is never called
// after unbind. Refresh stays clickable while a scan runs (the presenter ignores it then), so keyboard focus on it is
// never dropped. Back goes through the router, which ignores it outside MainMenu.
// Design: 05 §6, §9.2 (hierarchy, inspector fields), §10; 06 U33.

using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MultiplayerKit.UI
{
    public sealed class FriendsGamesView : KitView, IFriendsGamesView
    {
        // Same rate as the busy overlay's default, in unscaled time.
        private const float SpinnerDegreesPerSecond = 270f;

        [SerializeField] private FriendLobbyRowView rowPrefab;
        [SerializeField] private RectTransform content;
        [SerializeField] private Button refreshButton;
        [SerializeField] private GameObject refreshSpinner;
        [SerializeField] private TMP_Text emptyMessage;
        [SerializeField] private Button backButton;
        [SerializeField] private MenuRouter router;

        private FriendsGamesPresenter presenter;
        private RowPool<FriendLobbyRowView> pool;

        private void Awake()
        {
            if (refreshButton != null) refreshButton.onClick.AddListener(OnRefreshClicked);
            if (backButton != null) backButton.onClick.AddListener(OnBackClicked);
        }

        protected override bool ValidateReferences()
        {
            return Require(rowPrefab, nameof(rowPrefab)) & Require(content, nameof(content))
                & Require(refreshButton, nameof(refreshButton)) & Require(refreshSpinner, nameof(refreshSpinner))
                & Require(emptyMessage, nameof(emptyMessage)) & Require(backButton, nameof(backButton))
                & Require(router, nameof(router));
        }

        protected override void OnBind(KitServices services)
        {
            presenter = new FriendsGamesPresenter(services, this, KitOverlay.Dialogs);
            presenter.Bind();
        }

        protected override void OnUnbind()
        {
            if (presenter != null) presenter.Unbind();
            presenter = null;
        }

        private void Update()
        {
            if (refreshSpinner != null && refreshSpinner.activeSelf)
            {
                refreshSpinner.transform.Rotate(0f, 0f, -SpinnerDegreesPerSecond * Time.unscaledDeltaTime);
            }
        }

        // Entries resolved during this frame are rendered once, after every Update.
        private void LateUpdate()
        {
            if (presenter != null) presenter.FlushIfDirty();
        }

        // IFriendsGamesView

        public void ShowRows(IReadOnlyList<FriendLobbyRowModel> rows)
        {
            if (pool == null)
            {
                if (rowPrefab == null || content == null) return;   // logged by ValidateReferences
                pool = new RowPool<FriendLobbyRowView>(rowPrefab, content);
            }

            int count = rows != null ? rows.Count : 0;
            pool.Ensure(count);
            IPlayerIdentity identity = Services != null ? Services.Identity : null;
            for (int i = 0; i < count; i++) pool[i].Bind(rows[i], identity, OnRowJoin);
        }

        public void SetRefreshing(bool refreshing)
        {
            if (refreshSpinner != null && refreshSpinner.activeSelf != refreshing) refreshSpinner.SetActive(refreshing);
        }

        public void SetEmpty(string message)
        {
            if (emptyMessage == null) return;
            emptyMessage.text = message ?? "";
            SetShown(emptyMessage.gameObject, message != null);
        }

        // Clicks, forwarded to the current presenter.

        private void OnRefreshClicked()
        {
            if (presenter != null) presenter.OnRefresh();
        }

        private void OnBackClicked()
        {
            if (router != null) router.Show(MenuScreen.MainMenu);
        }

        private void OnRowJoin(LobbyId lobby, string lobbyName)
        {
            if (presenter != null) presenter.OnJoin(lobby, lobbyName);
        }

        private static void SetShown(GameObject target, bool shown)
        {
            if (target != null && target.activeSelf != shown) target.SetActive(shown);
        }
    }
}

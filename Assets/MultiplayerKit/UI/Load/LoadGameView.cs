// Load game screen (05 §8, §9.2): the save slots over RowPool<SaveSlotRowView>, the empty or error message and Back.
// A thin view over LoadGamePresenter, which lists the slots on every bind (the screen binds each time it is shown).
// Row Load and Delete clicks go through this view, so a stale presenter is never called after unbind. Back goes
// through the router, which ignores it outside MainMenu.
// Design: 05 §8, §9.2 (hierarchy, inspector fields), §10; 06 U41.

using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MultiplayerKit.UI
{
    public sealed class LoadGameView : KitView, ILoadGameView
    {
        [SerializeField] private SaveSlotRowView rowPrefab;
        [SerializeField] private RectTransform content;
        [SerializeField] private TMP_Text message;
        [SerializeField] private Button backButton;
        [SerializeField] private MenuRouter router;

        private LoadGamePresenter presenter;
        private RowPool<SaveSlotRowView> pool;

        private void Awake()
        {
            if (backButton != null) backButton.onClick.AddListener(OnBackClicked);
        }

        protected override bool ValidateReferences()
        {
            return Require(rowPrefab, nameof(rowPrefab)) & Require(content, nameof(content))
                & Require(message, nameof(message)) & Require(backButton, nameof(backButton))
                & Require(router, nameof(router));
        }

        protected override void OnBind(KitServices services)
        {
            presenter = new LoadGamePresenter(services, this, KitOverlay.Dialogs);
            presenter.Bind();
        }

        protected override void OnUnbind()
        {
            if (presenter != null) presenter.Unbind();
            presenter = null;
        }

        // ILoadGameView

        public void ShowRows(IReadOnlyList<SaveSlotRowModel> rows)
        {
            if (pool == null)
            {
                if (rowPrefab == null || content == null) return;   // logged by ValidateReferences
                pool = new RowPool<SaveSlotRowView>(rowPrefab, content);
            }

            int count = rows != null ? rows.Count : 0;
            pool.Ensure(count);
            for (int i = 0; i < count; i++) pool[i].Bind(rows[i], OnRowLoad, OnRowDelete);
        }

        public void SetMessage(string text)
        {
            if (message == null) return;
            message.text = text ?? "";
            SetShown(message.gameObject, text != null);
        }

        // Clicks, forwarded to the current presenter.

        private void OnBackClicked()
        {
            if (router != null) router.Show(MenuScreen.MainMenu);
        }

        private void OnRowLoad(string slotId)
        {
            if (presenter != null) presenter.OnLoad(slotId);
        }

        private void OnRowDelete(string slotId, string slotName)
        {
            if (presenter != null) presenter.OnDelete(slotId, slotName);
        }

        private static void SetShown(GameObject target, bool shown)
        {
            if (target != null && target.activeSelf != shown) target.SetActive(shown);
        }
    }
}

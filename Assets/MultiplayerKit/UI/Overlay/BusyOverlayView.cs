// Full-screen busy overlay (05 §3.4): a dim Image that blocks raycasts, a spinner turned with unscaled time (it keeps
// turning while the game is paused), the message and an optional Cancel. Driven by OverlayPresenter through
// KitOverlay. Shown and hidden through its CanvasGroup; keep the GameObject active in the prefab.
// Design: 05 §3.2 (busy table), §3.4, §9.1 (inspector fields); 06 U10.

using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MultiplayerKit.UI
{
    public sealed class BusyOverlayView : KitView
    {
        [SerializeField] private CanvasGroup group;
        [SerializeField] private RectTransform spinner;
        [SerializeField] private float spinnerDegreesPerSecond = 270f;
        [SerializeField] private TMP_Text message;
        [SerializeField] private Button cancelButton;

        private Action onCancel;
        private bool visible;

        public bool IsVisible => visible;

        private void Awake()
        {
            if (cancelButton != null) cancelButton.onClick.AddListener(OnCancelClicked);
            SetPanelVisible(group, visible);   // Show may already have run before this widget woke up
        }

        protected override bool ValidateReferences()
        {
            return Require(group, nameof(group)) & Require(spinner, nameof(spinner)) & Require(message, nameof(message))
                & Require(cancelButton, nameof(cancelButton));
        }

        // KitOverlay's OverlayPresenter drives this widget; it has nothing of its own to bind.
        protected override void OnBind(KitServices services)
        {
        }

        protected override void OnUnbind()
        {
        }

        /// <summary>Shows or updates the overlay. Cancel is shown only when canCancel.</summary>
        public void Show(string text, bool canCancel, Action cancel)
        {
            visible = true;
            onCancel = canCancel ? cancel : null;
            if (message != null) message.text = text ?? "";
            if (cancelButton != null) cancelButton.gameObject.SetActive(canCancel);
            SetPanelVisible(group, true);
        }

        public void Hide()
        {
            visible = false;
            onCancel = null;
            SetPanelVisible(group, false);
        }

        // The presenter's CancelJoin or Leave changes the state, and the state change hides the overlay.
        private void OnCancelClicked()
        {
            if (!visible) return;
            SafeInvoke(onCancel, "Busy overlay cancel");
        }

        private void Update()
        {
            if (!visible || spinner == null) return;
            spinner.Rotate(0f, 0f, -spinnerDegreesPerSecond * Time.unscaledDeltaTime);   // clockwise
        }
    }
}

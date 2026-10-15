// The error popup (05 §3.4): message and OK, drawn above every other overlay panel (05 §9.1). Shows only the text it
// is given; OverlayPresenter passes SessionError.UserMessage and never Detail, which may contain ids. OK and Escape
// (routed by KitOverlay) acknowledge. Shown and hidden through its CanvasGroup.
// Design: 05 §3.2 (error rule), §3.4, §9.1 (inspector fields), §10; 06 U13.

using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MultiplayerKit.UI
{
    public sealed class ErrorPopupView : KitView
    {
        [SerializeField] private CanvasGroup group;
        [SerializeField] private TMP_Text message;
        [SerializeField] private Button okButton;

        private Action onOk;
        private bool open;
        private int closedFrame = -1;
        private GameObject previousSelection;

        public bool IsOpen => open;

        /// <summary>Open, or closed during the current frame (KitOverlay.IsModalOpen).</summary>
        public bool IsOpenThisFrame => open || closedFrame == Time.frameCount;

        private void Awake()
        {
            if (okButton != null) okButton.onClick.AddListener(Acknowledge);
            if (message != null) message.richText = false;
            if (!open) SetPanelVisible(group, false);
        }

        protected override bool ValidateReferences()
        {
            return Require(group, nameof(group)) & Require(message, nameof(message)) & Require(okButton, nameof(okButton));
        }

        // Driven by KitOverlay's OverlayPresenter; nothing of its own to bind.
        protected override void OnBind(KitServices services)
        {
        }

        protected override void OnUnbind()
        {
        }

        /// <summary>Shows or replaces the message; ok runs once when the user acknowledges.</summary>
        public void Show(string text, Action ok)
        {
            bool wasOpen = open;
            onOk = ok;
            open = true;
            if (message != null) message.text = text ?? "";
            SetPanelVisible(group, true);

            GameObject before = SelectAndRemember(okButton);
            if (!wasOpen) previousSelection = before;
        }

        /// <summary>Hides without running the OK callback (the state already left Error).</summary>
        public void Hide()
        {
            if (!open) return;
            open = false;
            onOk = null;
            closedFrame = Time.frameCount;
            SetPanelVisible(group, false);
            RestoreSelection(previousSelection);
            previousSelection = null;
        }

        /// <summary>OK button or Escape.</summary>
        public void Acknowledge()
        {
            if (!open) return;
            Action ok = onOk;
            Hide();
            SafeInvoke(ok, "Error popup OK");
        }
    }
}

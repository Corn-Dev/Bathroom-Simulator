// The shared confirm dialog (05 §3.4): title, body, confirm button with its label, and Cancel. One dialog at a time:
// a newer Show first cancels the open one (its onCancel runs), so the old caller is never left hanging. Escape is
// cancel, routed by KitOverlay because it knows the draw order. Keyboard focus starts on Cancel, so Enter never
// confirms a kick, a save delete or "End game" by accident. Shown and hidden through its CanvasGroup.
// Design: 05 §3.1, §3.4, §9.1 (inspector fields), §10, §11; 06 U12.

using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MultiplayerKit.UI
{
    public sealed class ConfirmDialogView : KitView
    {
        private const string DefaultConfirmLabel = "OK";
        private const string CancelText = "Cancel";

        [SerializeField] private CanvasGroup group;
        [SerializeField] private TMP_Text title;
        [SerializeField] private TMP_Text body;
        [SerializeField] private Button confirmButton;
        [SerializeField] private TMP_Text confirmLabel;
        [SerializeField] private Button cancelButton;
        [SerializeField] private TMP_Text cancelLabel;

        private Action onConfirm;
        private Action onCancel;
        private bool open;
        private int closedFrame = -1;
        private GameObject previousSelection;

        public bool IsOpen => open;

        /// <summary>Open, or closed during the current frame (KitOverlay.IsModalOpen).</summary>
        public bool IsOpenThisFrame => open || closedFrame == Time.frameCount;

        private void Awake()
        {
            if (confirmButton != null) confirmButton.onClick.AddListener(OnConfirmClicked);
            if (cancelButton != null) cancelButton.onClick.AddListener(Cancel);

            // Titles and bodies can carry player or save names; never parse them as tags.
            if (title != null) title.richText = false;
            if (body != null) body.richText = false;
            if (!open) SetPanelVisible(group, false);
        }

        protected override bool ValidateReferences()
        {
            return Require(group, nameof(group)) & Require(title, nameof(title)) & Require(body, nameof(body))
                & Require(confirmButton, nameof(confirmButton)) & Require(confirmLabel, nameof(confirmLabel))
                & Require(cancelButton, nameof(cancelButton)) & Require(cancelLabel, nameof(cancelLabel));
        }

        // Used through KitOverlay (IKitDialogs and IOverlayView); nothing of its own to bind.
        protected override void OnBind(KitServices services)
        {
        }

        protected override void OnUnbind()
        {
        }

        public void Show(string titleText, string bodyText, string confirmText, Action confirm, Action cancel)
        {
            bool replacing = open;
            GameObject restoreTo = previousSelection;

            // Newest question wins; every replaced caller hears cancel. A cancel callback that opens another
            // dialog is replaced in turn.
            while (open)
            {
                Action replaced = onCancel;
                open = false;
                onConfirm = null;
                onCancel = null;
                SafeInvoke(replaced, "Replaced confirm dialog cancel");
            }

            onConfirm = confirm;
            onCancel = cancel;
            open = true;
            if (title != null) title.text = titleText ?? "";
            if (body != null) body.text = bodyText ?? "";
            if (confirmLabel != null) confirmLabel.text = string.IsNullOrEmpty(confirmText) ? DefaultConfirmLabel : confirmText;
            if (cancelLabel != null) cancelLabel.text = CancelText;
            SetPanelVisible(group, true);

            GameObject before = SelectAndRemember(cancelButton);
            previousSelection = replacing ? restoreTo : before;
        }

        /// <summary>Cancel button, Escape, an error arriving (05 §10) or a replacement: runs onCancel.</summary>
        public void Cancel()
        {
            if (!open) return;
            Action cancel = onCancel;
            Close();
            SafeInvoke(cancel, "Confirm dialog cancel");
        }

        private void OnConfirmClicked()
        {
            if (!open) return;
            Action confirm = onConfirm;
            Close();
            SafeInvoke(confirm, "Confirm dialog confirm");
        }

        // State first, then the callback, so a callback may open the next dialog.
        private void Close()
        {
            open = false;
            onConfirm = null;
            onCancel = null;
            closedFrame = Time.frameCount;
            SetPanelVisible(group, false);
            RestoreSelection(previousSelection);
            previousSelection = null;
        }
    }
}

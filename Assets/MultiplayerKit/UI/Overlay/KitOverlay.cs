// Persistent overlay canvas (05 §3.1): a DontDestroyOnLoad singleton that survives the NGO scene loads (the copy
// in every reloaded menu scene deactivates and destroys itself before its children bind). Owns the shared
// OverlayPresenter and forwards its IOverlayView calls to the child widgets, is the IKitDialogs every screen uses,
// routes the back key to the topmost dialog and reports IsModalOpen (05 §10).
// Design: 05 §3.1-§3.4, §9.1 (hierarchy, draw order, inspector fields), §10; 06 U8.

using System;
using UnityEngine;

namespace MultiplayerKit.UI
{
    public sealed class KitOverlay : KitView, IKitDialogs, IOverlayView
    {
        [SerializeField] private BusyOverlayView busy;
        [SerializeField] private ToastView toast;
        [SerializeField] private SessionMenuView sessionMenu;
        [SerializeField] private PasswordPromptView passwordPrompt;
        [SerializeField] private ConfirmDialogView confirmDialog;
        [SerializeField] private ErrorPopupView errorPopup;

        private OverlayPresenter presenter;
        private bool duplicate;

        public static KitOverlay Instance { get; private set; }

        /// <summary>
        /// True while PasswordPrompt, ConfirmDialog, ErrorPopup or SessionMenu is visible, and for the rest of the
        /// frame in which one of them closed, so one back-key press never closes a modal and also navigates.
        /// MenuRouter reads it (05 §2.4).
        /// </summary>
        public static bool IsModalOpen
        {
            get
            {
                KitOverlay overlay = Instance;
                if (overlay == null) return false;
                return IsDialogOpen || (overlay.sessionMenu != null && overlay.sessionMenu.IsOpenThisFrame);
            }
        }

        /// <summary>IsModalOpen without the session menu: the "no other modal is open" test of SessionMenuView (05 §3.6).</summary>
        internal static bool IsDialogOpen
        {
            get
            {
                KitOverlay overlay = Instance;
                if (overlay == null) return false;
                return (overlay.errorPopup != null && overlay.errorPopup.IsOpenThisFrame)
                    || (overlay.confirmDialog != null && overlay.confirmDialog.IsOpenThisFrame)
                    || (overlay.passwordPrompt != null && overlay.passwordPrompt.IsOpenThisFrame);
            }
        }

        /// <summary>The overlay, or NullKitDialogs when the prefab is missing (05 §10). Screen views pass this to presenters.</summary>
        public static IKitDialogs Dialogs => Instance != null ? (IKitDialogs)Instance : NullKitDialogs.Instance;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                // The menu scene is reloaded after every session. Deactivating first keeps this copy and its
                // children from enabling, binding presenters or taking the back key before Destroy runs.
                duplicate = true;
                gameObject.SetActive(false);
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        protected override void OnEnable()
        {
            if (duplicate) return;
            base.OnEnable();
        }

        protected override bool ValidateReferences()
        {
            return Require(busy, nameof(busy)) & Require(toast, nameof(toast)) & Require(sessionMenu, nameof(sessionMenu))
                & Require(passwordPrompt, nameof(passwordPrompt)) & Require(confirmDialog, nameof(confirmDialog))
                & Require(errorPopup, nameof(errorPopup));
        }

        protected override void OnBind(KitServices services)
        {
            presenter = new OverlayPresenter(services.Session, this);
            presenter.Bind();
        }

        protected override void OnUnbind()
        {
            if (presenter != null) presenter.Unbind();
            presenter = null;
        }

        // 05 §10: Escape closes only the topmost dialog. Draw order (05 §9.1): ErrorPopup over ConfirmDialog over
        // PasswordPrompt. The session menu toggles itself and the router navigates; both check IsModalOpen first.
        private void Update()
        {
            if (!Input.GetKeyDown(KeyCode.Escape)) return;
            if (errorPopup != null && errorPopup.IsOpen) errorPopup.Acknowledge();
            else if (confirmDialog != null && confirmDialog.IsOpen) confirmDialog.Cancel();
            else if (passwordPrompt != null && passwordPrompt.IsOpen) passwordPrompt.Cancel();
        }

        // IKitDialogs, used by every screen presenter (05 §3.1). Work without a bound presenter.

        /// <summary>UI-originated toast; session toasts arrive through SessionController.Toast.</summary>
        public void ShowToast(ToastSeverity severity, string text)
        {
            Toast(severity, text);
        }

        public void Toast(ToastSeverity severity, string text)
        {
            if (toast == null)
            {
                NullKitDialogs.Instance.Toast(severity, text);
                return;
            }
            toast.Enqueue(severity, text);
        }

        public void Confirm(string title, string body, string confirmLabel, Action onConfirm, Action onCancel = null)
        {
            if (confirmDialog == null)
            {
                NullKitDialogs.Instance.Confirm(title, body, confirmLabel, onConfirm, onCancel);
                return;
            }
            confirmDialog.Show(title, body, confirmLabel, onConfirm, onCancel);
        }

        public void SetPendingJoinLabel(LobbyId lobby, string label)
        {
            if (presenter != null) presenter.SetPendingJoinLabel(lobby, label);
        }

        // IOverlayView, called only by OverlayPresenter; each call goes to the matching child widget.

        void IOverlayView.ShowBusy(string message, bool canCancel)
        {
            if (busy == null) return;
            if (message == null) busy.Hide();
            else busy.Show(message, canCancel, OnBusyCancel);
        }

        void IOverlayView.ShowPasswordPrompt(string lobbyLabel, bool previousWrong)
        {
            if (passwordPrompt != null) passwordPrompt.Show(lobbyLabel, previousWrong, OnPasswordSubmit, OnPasswordCancel);
        }

        void IOverlayView.HidePasswordPrompt()
        {
            if (passwordPrompt != null) passwordPrompt.Hide();
        }

        // 05 §10: an error replaces an open confirm dialog, whose onCancel runs, and draws over everything else.
        void IOverlayView.ShowError(string message)
        {
            if (confirmDialog != null) confirmDialog.Cancel();
            if (errorPopup != null) errorPopup.Show(message, OnErrorOk);
        }

        void IOverlayView.HideError()
        {
            if (errorPopup != null) errorPopup.Hide();
        }

        void IOverlayView.EnqueueToast(ToastSeverity severity, string text)
        {
            Toast(severity, text);
        }

        void IOverlayView.ShowConfirm(string title, string body, string confirmLabel, Action onConfirm, Action onCancel)
        {
            Confirm(title, body, confirmLabel, onConfirm, onCancel);
        }

        // Widget callbacks back into the presenter. The widget has already hidden itself.

        private void OnBusyCancel()
        {
            if (presenter != null) presenter.OnBusyCancelClicked();
        }

        private void OnPasswordSubmit(string password)
        {
            if (presenter != null) presenter.OnPasswordSubmitted(password);
        }

        private void OnPasswordCancel()
        {
            if (presenter != null) presenter.OnPasswordCancelled();
        }

        private void OnErrorOk()
        {
            if (presenter != null) presenter.OnErrorAcknowledged();
        }
    }
}

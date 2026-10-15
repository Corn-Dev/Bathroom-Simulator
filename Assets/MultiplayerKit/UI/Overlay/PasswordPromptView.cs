// The password prompt (05 §3.3), the only password UI (02 §3d.5): a masked single-line TMP_InputField limited to the
// password definition's 32 characters, OK enabled only for trimmed non-empty text, Enter submits, Cancel and Escape
// (routed by KitOverlay) cancel. The field is cleared the moment the text is submitted or cancelled, so the plain
// password does not linger in a UI object; the callback hands it straight to SessionController.SubmitPassword.
// Design: 05 §3.2, §3.3, §9.1 (inspector fields), §10 (input validation); 06 U14.

using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MultiplayerKit.UI
{
    public sealed class PasswordPromptView : KitView
    {
        public const string TitleText = "Password required";
        public const string WrongPasswordText = "Incorrect password. Try again.";

        private const string FallbackLabel = "this game";

        [SerializeField] private CanvasGroup group;
        [SerializeField] private TMP_Text title;
        [SerializeField] private TMP_Text body;
        [SerializeField] private TMP_Text errorLine;
        [SerializeField] private TMP_InputField passwordInput;
        [SerializeField] private Button okButton;
        [SerializeField] private Button cancelButton;

        private Action<string> onSubmit;
        private Action onCancel;
        private bool open;
        private int closedFrame = -1;
        private GameObject previousSelection;

        public bool IsOpen => open;

        /// <summary>Open, or closed during the current frame (KitOverlay.IsModalOpen).</summary>
        public bool IsOpenThisFrame => open || closedFrame == Time.frameCount;

        private void Awake()
        {
            if (passwordInput != null)
            {
                // Enforced in code as well as in the prefab (05 §3.3). The Password content type forces a single line.
                passwordInput.contentType = TMP_InputField.ContentType.Password;
                passwordInput.characterLimit = CoreSettings.PasswordMaxLength;
                passwordInput.onValueChanged.AddListener(OnTextChanged);
                passwordInput.onSubmit.AddListener(OnInputSubmitted);
            }
            if (okButton != null) okButton.onClick.AddListener(Submit);
            if (cancelButton != null) cancelButton.onClick.AddListener(Cancel);
            if (body != null) body.richText = false;   // carries the lobby name, which other players choose
            if (!open) SetPanelVisible(group, false);
        }

        protected override bool ValidateReferences()
        {
            return Require(group, nameof(group)) & Require(title, nameof(title)) & Require(body, nameof(body))
                & Require(errorLine, nameof(errorLine)) & Require(passwordInput, nameof(passwordInput))
                & Require(okButton, nameof(okButton)) & Require(cancelButton, nameof(cancelButton));
        }

        // Driven by KitOverlay's OverlayPresenter; nothing of its own to bind.
        protected override void OnBind(KitServices services)
        {
        }

        protected override void OnUnbind()
        {
        }

        /// <summary>Shows the empty prompt with the input focused; previousWrong adds the "Incorrect password" line.</summary>
        public void Show(string lobbyLabel, bool previousWrong, Action<string> submit, Action cancel)
        {
            bool wasOpen = open;
            onSubmit = submit;
            onCancel = cancel;
            open = true;

            if (title != null) title.text = TitleText;
            if (body != null) body.text = "Enter the password for " + (string.IsNullOrEmpty(lobbyLabel) ? FallbackLabel : lobbyLabel) + ".";
            if (errorLine != null)
            {
                errorLine.text = WrongPasswordText;
                errorLine.gameObject.SetActive(previousWrong);
            }
            if (passwordInput != null) passwordInput.SetTextWithoutNotify("");
            UpdateOk("");
            SetPanelVisible(group, true);

            GameObject before = SelectAndRemember(passwordInput);
            if (!wasOpen) previousSelection = before;
            if (passwordInput != null) passwordInput.ActivateInputField();
        }

        /// <summary>Hides and clears the field without running a callback (the state left Joining).</summary>
        public void Hide()
        {
            if (!open) return;
            open = false;
            onSubmit = null;
            onCancel = null;
            closedFrame = Time.frameCount;
            ClearInput();
            SetPanelVisible(group, false);
            RestoreSelection(previousSelection);
            previousSelection = null;
        }

        /// <summary>Cancel button or Escape.</summary>
        public void Cancel()
        {
            if (!open) return;
            Action cancel = onCancel;
            Hide();
            SafeInvoke(cancel, "Password prompt cancel");
        }

        // OK button or Enter. Blank text does nothing; the presenter receives the text after the field is cleared.
        private void Submit()
        {
            if (!open || passwordInput == null) return;
            string password = passwordInput.text;
            if (string.IsNullOrWhiteSpace(password)) return;

            Action<string> submit = onSubmit;
            Hide();
            SafeInvoke(submit, password, "Password prompt submit");
        }

        private void OnInputSubmitted(string text)
        {
            Submit();
        }

        private void OnTextChanged(string text)
        {
            UpdateOk(text);
        }

        private void UpdateOk(string text)
        {
            if (okButton != null) okButton.interactable = !string.IsNullOrWhiteSpace(text);
        }

        private void ClearInput()
        {
            if (passwordInput == null) return;
            passwordInput.SetTextWithoutNotify("");
            passwordInput.DeactivateInputField();
            UpdateOk("");
        }
    }
}

// The in-game session menu (05 §3.6) on the persistent KitOverlay: Resume, Invite, Save Now, Save & Quit, Leave or
// End game, and the player list with kick buttons. Opens with toggleKey (default Escape) when openWithKey is on and
// no dialog is open; games that own Escape turn openWithKey off and call SessionMenuView.Instance.Toggle(). Stores
// and restores the cursor around opening and raises OpenChanged so the game can suspend its own input.
// Roster contract with U21/U23: RosterView is parent-driven. This view calls roster.Attach(identity, onKick) once
// and roster.ShowRows(rows); SessionMenuPresenter owns the RosterPresenter (showReadyColumn false, 05 §5.2).
// Design: 05 §3.6, §9.1 (inspector fields), §10, §11; 06 U15.

using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MultiplayerKit.UI
{
    public sealed class SessionMenuView : KitView, ISessionMenuView
    {
        [SerializeField] private CanvasGroup group;
        [SerializeField] private Button resumeButton;
        [SerializeField] private Button inviteButton;
        [SerializeField] private Button saveNowButton;
        [SerializeField] private Button saveAndQuitButton;
        [SerializeField] private Button leaveButton;
        [SerializeField] private TMP_Text leaveLabel;
        [SerializeField] private RosterView roster;
        [SerializeField] private bool openWithKey = true;
        [SerializeField] private KeyCode toggleKey = KeyCode.Escape;

        private SessionMenuPresenter presenter;
        private bool open;
        private int closedFrame = -1;
        private CursorLockMode savedLockState;
        private bool savedCursorVisible;

        /// <summary>The persistent menu; a duplicate overlay in a reloaded menu scene never replaces it.</summary>
        public static SessionMenuView Instance { get; private set; }

        /// <summary>true when the menu opens, false when it closes.</summary>
        public static event Action<bool> OpenChanged;

        public bool IsOpen => open;

        /// <summary>Open, or closed during the current frame (KitOverlay.IsModalOpen).</summary>
        public bool IsOpenThisFrame => open || closedFrame == Time.frameCount;

        /// <summary>For games that handle the key themselves (openWithKey off). Opens only while in game.</summary>
        public void Toggle()
        {
            if (presenter != null) presenter.Toggle();
        }

        private void Awake()
        {
            if (Instance == null) Instance = this;
            if (resumeButton != null) resumeButton.onClick.AddListener(OnResumeClicked);
            if (inviteButton != null) inviteButton.onClick.AddListener(OnInviteClicked);
            if (saveNowButton != null) saveNowButton.onClick.AddListener(OnSaveNowClicked);
            if (saveAndQuitButton != null) saveAndQuitButton.onClick.AddListener(OnSaveAndQuitClicked);
            if (leaveButton != null) leaveButton.onClick.AddListener(OnLeaveClicked);
            if (!open) SetPanelVisible(group, false);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        protected override bool ValidateReferences()
        {
            return Require(group, nameof(group)) & Require(resumeButton, nameof(resumeButton))
                & Require(inviteButton, nameof(inviteButton)) & Require(saveNowButton, nameof(saveNowButton))
                & Require(saveAndQuitButton, nameof(saveAndQuitButton)) & Require(leaveButton, nameof(leaveButton))
                & Require(leaveLabel, nameof(leaveLabel)) & Require(roster, nameof(roster));
        }

        protected override void OnBind(KitServices services)
        {
            roster.Attach(services.Identity, OnRosterKick);
            presenter = new SessionMenuPresenter(services, this, KitOverlay.Dialogs);
            presenter.Bind();
        }

        protected override void OnUnbind()
        {
            if (presenter != null) presenter.Unbind();   // closes the menu, which restores the cursor
            presenter = null;
        }

        // A dialog drawn above the menu takes the key first (05 §10); IsDialogOpen also covers the frame it closed in.
        private void Update()
        {
            if (!openWithKey || presenter == null || !Input.GetKeyDown(toggleKey)) return;
            if (KitOverlay.IsDialogOpen) return;
            presenter.Toggle();
        }

        // ISessionMenuView

        public void SetOpen(bool isOpen)
        {
            if (open == isOpen) return;
            open = isOpen;

            if (open)
            {
                savedLockState = Cursor.lockState;
                savedCursorVisible = Cursor.visible;
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                SetPanelVisible(group, true);
                // Save & Quit is the primary action of a host with saves (05 §3.6); everyone else starts on Resume.
                bool primarySave = saveAndQuitButton != null && saveAndQuitButton.gameObject.activeSelf;
                SelectAndRemember(primarySave ? saveAndQuitButton : resumeButton);
            }
            else
            {
                closedFrame = Time.frameCount;
                SetPanelVisible(group, false);
                RestoreSelection(null);   // clear it, so gameplay keys never press a hidden button
                Cursor.lockState = savedLockState;
                Cursor.visible = savedCursorVisible;
            }
            RaiseOpenChanged(open);
        }

        public void SetButtons(bool saveNow, bool saveAndQuit, bool leave, bool invite)
        {
            SetShown(saveNowButton, saveNow);
            SetShown(saveAndQuitButton, saveAndQuit);
            SetShown(leaveButton, leave);
            SetShown(inviteButton, invite);

            // Primary means first in the list as well as focused (05 §3.6). Hidden for clients, so order is moot there.
            if (saveAndQuit && saveAndQuitButton != null && saveAndQuitButton.transform.GetSiblingIndex() != 0)
            {
                saveAndQuitButton.transform.SetAsFirstSibling();
            }
        }

        public void SetLeaveLabel(string label)
        {
            if (leaveLabel != null) leaveLabel.text = label ?? "";
        }

        public void ShowRoster(IReadOnlyList<RosterRowModel> rows)
        {
            if (roster != null) roster.ShowRows(rows);
        }

        // Clicks, forwarded to the presenter.

        private void OnResumeClicked()
        {
            if (presenter != null) presenter.OnResume();
        }

        private void OnInviteClicked()
        {
            if (presenter != null) presenter.OnInvite();
        }

        private void OnSaveNowClicked()
        {
            if (presenter != null) presenter.OnSaveNow();
        }

        private void OnSaveAndQuitClicked()
        {
            if (presenter != null) presenter.OnSaveAndQuit();
        }

        private void OnLeaveClicked()
        {
            if (presenter != null) presenter.OnLeave();
        }

        private void OnRosterKick(PlayerId player)
        {
            if (presenter != null) presenter.OnKick(player);
        }

        private static void SetShown(Button button, bool shown)
        {
            if (button != null && button.gameObject.activeSelf != shown) button.gameObject.SetActive(shown);
        }

        // Game handlers: one that throws must not keep the others from hearing the change.
        private static void RaiseOpenChanged(bool isOpen)
        {
            Action<bool> handlers = OpenChanged;
            if (handlers == null) return;
            foreach (Delegate handler in handlers.GetInvocationList())
            {
                try
                {
                    ((Action<bool>)handler)(isOpen);
                }
                catch (Exception e)
                {
                    KitLog.Error("SessionMenuView.OpenChanged handler threw.", e);
                }
            }
        }
    }
}

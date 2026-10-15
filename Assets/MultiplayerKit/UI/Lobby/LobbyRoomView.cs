// The lobby room screen (05 §5, §9.2): header (lobby name, privacy, player count, resume label), roster, settings
// panel and the action bar (Leave, Invite, Ready, Start and its blocked reason). One screen for host and clients;
// the presenters decide what is visible and editable. OnBind constructs and binds all three presenters (05 §5):
// RosterPresenter over the parent-driven RosterView, SettingsPanelPresenter over SettingsPanelView, and
// LobbyRoomPresenter over this view. Start first flushes a debounced slider, so the game starts with what the host
// sees. Kick and setting callbacks go through this view, so a stale presenter is never called after unbind.
// Design: 05 §5, §9.2 (hierarchy, inspector fields), §10; 06 U19.

using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MultiplayerKit.UI
{
    public sealed class LobbyRoomView : KitView, ILobbyRoomView
    {
        [SerializeField] private TMP_Text lobbyName;
        [SerializeField] private TMP_Text privacy;
        [SerializeField] private TMP_Text playerCount;
        [SerializeField] private TMP_Text resumeLabel;
        [SerializeField] private RosterView roster;
        [SerializeField] private SettingsPanelView settingsPanel;
        [SerializeField] private Button leaveButton;
        [SerializeField] private Button inviteButton;
        [SerializeField] private Toggle readyToggle;
        [SerializeField] private TMP_Text startBlockedReason;
        [SerializeField] private Button startButton;

        private LobbyRoomPresenter presenter;
        private RosterPresenter rosterPresenter;
        private SettingsPanelPresenter settingsPresenter;

        private void Awake()
        {
            if (leaveButton != null) leaveButton.onClick.AddListener(OnLeaveClicked);
            if (inviteButton != null) inviteButton.onClick.AddListener(OnInviteClicked);
            if (startButton != null) startButton.onClick.AddListener(OnStartClicked);
            if (readyToggle != null) readyToggle.onValueChanged.AddListener(OnReadyToggled);

            // Lobby and save names are player-chosen: never parsed as tags, cut with an ellipsis (05 §10).
            PrepareNameText(lobbyName);
            PrepareNameText(resumeLabel);
        }

        protected override bool ValidateReferences()
        {
            return Require(lobbyName, nameof(lobbyName)) & Require(privacy, nameof(privacy))
                & Require(playerCount, nameof(playerCount)) & Require(resumeLabel, nameof(resumeLabel))
                & Require(roster, nameof(roster)) & Require(settingsPanel, nameof(settingsPanel))
                & Require(leaveButton, nameof(leaveButton)) & Require(inviteButton, nameof(inviteButton))
                & Require(readyToggle, nameof(readyToggle)) & Require(startBlockedReason, nameof(startBlockedReason))
                & Require(startButton, nameof(startButton));
        }

        protected override void OnBind(KitServices services)
        {
            IKitDialogs dialogs = KitOverlay.Dialogs;

            rosterPresenter = new RosterPresenter(services, roster, dialogs, roster.ShowReadyColumn);
            roster.Attach(services.Identity, OnRosterKick);

            settingsPresenter = new SettingsPanelPresenter(services, settingsPanel, dialogs);
            settingsPanel.Attach(OnSettingEdited);

            presenter = new LobbyRoomPresenter(services, this, dialogs);

            rosterPresenter.Bind();
            settingsPresenter.Bind();
            presenter.Bind();
        }

        protected override void OnUnbind()
        {
            if (presenter != null) presenter.Unbind();
            if (settingsPresenter != null) settingsPresenter.Unbind();
            if (rosterPresenter != null) rosterPresenter.Unbind();
            if (settingsPanel != null) settingsPanel.Detach();
            presenter = null;
            settingsPresenter = null;
            rosterPresenter = null;
        }

        // ILobbyRoomView

        public void SetHeader(string nameText, string privacyText, string countText, string resumeText)
        {
            if (lobbyName != null) lobbyName.text = nameText ?? "";
            if (privacy != null) privacy.text = privacyText ?? "";
            if (playerCount != null) playerCount.text = countText ?? "";
            if (resumeLabel != null)
            {
                resumeLabel.text = resumeText ?? "";
                SetShown(resumeLabel.gameObject, resumeText != null);
            }
        }

        public void SetStart(bool visible, bool interactable, string blockedReason)
        {
            if (startButton != null)
            {
                SetShown(startButton.gameObject, visible);
                startButton.interactable = interactable;
            }
            if (startBlockedReason != null)
            {
                startBlockedReason.text = blockedReason ?? "";
                SetShown(startBlockedReason.gameObject, blockedReason != null);
            }
        }

        public void SetReady(bool visible, bool isOn)
        {
            if (readyToggle == null) return;
            readyToggle.SetIsOnWithoutNotify(isOn);
            SetShown(readyToggle.gameObject, visible);
        }

        public void SetInviteInteractable(bool interactable)
        {
            if (inviteButton != null) inviteButton.interactable = interactable;
        }

        public void SetInteractable(bool interactable)
        {
            if (leaveButton != null) leaveButton.interactable = interactable;
            if (readyToggle != null) readyToggle.interactable = interactable;
        }

        // Clicks and row callbacks, forwarded to the current presenters.

        private void OnStartClicked()
        {
            if (presenter == null) return;
            if (settingsPanel != null) settingsPanel.FlushPending();
            presenter.OnStart();
        }

        private void OnReadyToggled(bool isOn)
        {
            if (presenter != null) presenter.OnReadyChanged(isOn);
        }

        private void OnInviteClicked()
        {
            if (presenter != null) presenter.OnInvite();
        }

        private void OnLeaveClicked()
        {
            if (presenter != null) presenter.OnLeave();
        }

        private void OnRosterKick(PlayerId player)
        {
            if (rosterPresenter != null) rosterPresenter.OnKickClicked(player);
        }

        private void OnSettingEdited(string key, string value)
        {
            if (settingsPresenter != null) settingsPresenter.OnUserChanged(key, value);
        }

        private static void PrepareNameText(TMP_Text text)
        {
            if (text == null) return;
            text.richText = false;
            text.overflowMode = TextOverflowModes.Ellipsis;
        }

        private static void SetShown(GameObject target, bool shown)
        {
            if (target != null && target.activeSelf != shown) target.SetActive(shown);
        }
    }
}

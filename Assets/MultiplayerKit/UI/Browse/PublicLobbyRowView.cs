// One row of the public browser (05 §7, §9.3): lobby name, published-settings line, region, player count, a padlock
// iff the lobby has a password, the status text and Join (interactable iff CanJoin). The owner avatar that 06 U38
// lists is an optional field outside the 05 §9.3 prefab: assign an AvatarView to show it, or leave it empty. Pooled
// by PublicBrowserView and rebound with other models, so Bind clears the Join listener first (05 §9.3). Lobby names
// are player-chosen: rich text off, ellipsis overflow (05 §10).
// Design: 05 §7, §9.3 (hierarchy, inspector fields), §10; 06 U38.

using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MultiplayerKit.UI
{
    public sealed class PublicLobbyRowView : MonoBehaviour
    {
        [SerializeField] private TMP_Text nameText;
        [SerializeField] private TMP_Text settingsLine;
        [SerializeField] private TMP_Text region;
        [SerializeField] private TMP_Text playerCount;
        [SerializeField] private GameObject passwordIcon;
        [SerializeField] private TMP_Text status;
        [SerializeField] private Button joinButton;

        [Tooltip("Optional: the lobby owner's avatar (06 U38). Not part of the 05 §9.3 prefab.")]
        [SerializeField] private AvatarView ownerAvatar;

        private LobbyId lobby;
        private string lobbyLabel;
        private Action<LobbyId, string> onJoin;

        /// <summary>The lobby of the bound model; None after an empty Bind.</summary>
        public LobbyId Lobby => lobby;

        /// <summary>Renders one model. join receives the lobby and its shown name (the password prompt's label).</summary>
        public void Bind(PublicLobbyRowModel row, IPlayerIdentity identity, Action<LobbyId, string> join)
        {
            if (joinButton != null) joinButton.onClick.RemoveAllListeners();
            if (row == null)
            {
                lobby = LobbyId.None;
                lobbyLabel = null;
                onJoin = null;
                if (ownerAvatar != null) ownerAvatar.Clear();
                return;
            }

            lobby = row.Lobby;
            lobbyLabel = row.Name;
            onJoin = join;

            if (nameText != null)
            {
                nameText.richText = false;
                nameText.overflowMode = TextOverflowModes.Ellipsis;
                nameText.text = row.Name ?? "";
            }
            SetText(settingsLine, row.SettingsLine);
            SetText(region, row.RegionLabel);
            SetText(playerCount, row.PlayerCount);
            SetShown(passwordIcon, row.HasPassword);
            SetText(status, row.Status);
            if (ownerAvatar != null)
            {
                if (row.Owner.IsValid) ownerAvatar.Show(identity, row.Owner);
                else ownerAvatar.Clear();
            }

            if (joinButton != null)
            {
                joinButton.interactable = row.CanJoin;
                if (row.CanJoin) joinButton.onClick.AddListener(OnJoinClicked);
            }
        }

        private void OnJoinClicked()
        {
            Action<LobbyId, string> join = onJoin;
            if (join == null || !lobby.IsValid) return;
            try
            {
                join(lobby, lobbyLabel);
            }
            catch (Exception e)
            {
                KitLog.Error("Public lobby join callback threw.", e);
            }
        }

        private static void SetText(TMP_Text text, string value)
        {
            if (text != null) text.text = value ?? "";
        }

        private static void SetShown(GameObject target, bool shown)
        {
            if (target != null && target.activeSelf != shown) target.SetActive(shown);
        }
    }
}

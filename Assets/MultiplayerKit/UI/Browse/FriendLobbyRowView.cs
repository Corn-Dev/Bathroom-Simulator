// One row of the friends' games list (05 §6, §9.3): the friend's avatar and name, the lobby name, player count,
// privacy label, a padlock iff the lobby has a password, the status text and Join (interactable iff CanJoin).
// Pooled by FriendsGamesView and rebound with other models, so Bind clears the Join listener first (05 §9.3).
// Friend and lobby names are player-chosen: rich text off, ellipsis overflow (05 §10).
// Design: 05 §6, §9.3 (hierarchy, inspector fields), §10; 06 U35.

using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MultiplayerKit.UI
{
    public sealed class FriendLobbyRowView : MonoBehaviour
    {
        [SerializeField] private AvatarView avatar;
        [SerializeField] private TMP_Text friendName;
        [SerializeField] private TMP_Text lobbyName;
        [SerializeField] private TMP_Text playerCount;
        [SerializeField] private TMP_Text privacy;
        [SerializeField] private GameObject passwordIcon;
        [SerializeField] private TMP_Text status;
        [SerializeField] private Button joinButton;

        private LobbyId lobby;
        private string lobbyLabel;
        private Action<LobbyId, string> onJoin;

        /// <summary>The lobby of the bound model; None after an empty Bind.</summary>
        public LobbyId Lobby => lobby;

        /// <summary>Renders one model. join receives the lobby and its shown name (the password prompt's label).</summary>
        public void Bind(FriendLobbyRowModel row, IPlayerIdentity identity, Action<LobbyId, string> join)
        {
            if (joinButton != null) joinButton.onClick.RemoveAllListeners();
            if (row == null)
            {
                lobby = LobbyId.None;
                lobbyLabel = null;
                onJoin = null;
                if (avatar != null) avatar.Clear();
                return;
            }

            lobby = row.Lobby;
            lobbyLabel = row.LobbyName;
            onJoin = join;

            if (avatar != null) avatar.Show(identity, row.Friend);
            SetName(friendName, row.FriendName);
            SetName(lobbyName, row.LobbyName);
            SetText(playerCount, row.PlayerCount);
            SetText(privacy, row.PrivacyLabel);
            SetShown(passwordIcon, row.HasPassword);
            SetText(status, row.Status);

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
                KitLog.Error("Friend lobby join callback threw.", e);
            }
        }

        private static void SetName(TMP_Text text, string value)
        {
            if (text == null) return;
            text.richText = false;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.text = value ?? "";
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

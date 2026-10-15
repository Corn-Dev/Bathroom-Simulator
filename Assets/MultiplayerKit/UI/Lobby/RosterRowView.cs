// One roster row (05 §5.2, §9.3): avatar, name, "You" tag, host crown, ready icon + text, kick button. The ready
// column doubles as the status slot: "Removing…" replaces it while a kicked member is still listed (IsRemoving), so
// no extra prefab child is needed (06 §13, N5). Pooled by RosterView and rebound with other members, so Bind clears
// the kick listener first (05 §9.3). Names are player-chosen: rich text off, ellipsis overflow (05 §10).
// Design: 05 §5.2, §9.3 (hierarchy, inspector fields), §10; 06 U22.

using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MultiplayerKit.UI
{
    public sealed class RosterRowView : MonoBehaviour
    {
        public const string ReadyText = "Ready";
        public const string NotReadyText = "Not ready";
        public const string RemovingText = "Removing\u2026";

        [SerializeField] private AvatarView avatar;
        [SerializeField] private TMP_Text nameText;
        [SerializeField] private GameObject youTag;
        [SerializeField] private GameObject crown;
        [SerializeField] private GameObject readyRoot;
        [SerializeField] private Image readyIcon;
        [SerializeField] private TMP_Text readyText;
        [SerializeField] private Sprite readySprite;
        [SerializeField] private Sprite notReadySprite;
        [SerializeField] private Button kickButton;

        private PlayerId player;
        private Action<PlayerId> onKick;

        public PlayerId Player => player;

        /// <summary>Renders one model. showReadyColumn false (session menu) hides readiness but still shows "Removing…".</summary>
        public void Bind(RosterRowModel row, IPlayerIdentity identity, bool showReadyColumn, Action<PlayerId> kick)
        {
            if (kickButton != null) kickButton.onClick.RemoveAllListeners();
            if (row == null)
            {
                player = PlayerId.None;
                onKick = null;
                return;
            }

            player = row.Player;
            onKick = kick;

            if (avatar != null) avatar.Show(identity, row.Player);
            if (nameText != null)
            {
                nameText.richText = false;
                nameText.overflowMode = TextOverflowModes.Ellipsis;
                nameText.text = row.Name ?? "";
            }
            SetShown(youTag, row.IsLocal);
            SetShown(crown, row.IsHost);
            RenderStatus(row, showReadyColumn);

            if (kickButton != null)
            {
                SetShown(kickButton.gameObject, row.CanKick);
                if (row.CanKick) kickButton.onClick.AddListener(OnKickClicked);
            }
        }

        // Ready column: "Removing…" first (it never blocks Start), else Ready / Not ready when the model shows it.
        private void RenderStatus(RosterRowModel row, bool showReadyColumn)
        {
            bool removing = row.IsRemoving;
            bool ready = !removing && showReadyColumn && row.ShowReady;
            SetShown(readyRoot, removing || ready);
            if (!removing && !ready) return;

            if (readyIcon != null)
            {
                Sprite sprite = ready ? (row.IsReady ? readySprite : notReadySprite) : null;
                readyIcon.sprite = sprite;
                readyIcon.enabled = sprite != null;
            }
            if (readyText != null) readyText.text = removing ? RemovingText : row.IsReady ? ReadyText : NotReadyText;
        }

        private void OnKickClicked()
        {
            Action<PlayerId> kick = onKick;
            if (kick == null || !player.IsValid) return;
            try
            {
                kick(player);
            }
            catch (Exception e)
            {
                KitLog.Error("Roster kick callback threw.", e);
            }
        }

        private static void SetShown(GameObject target, bool shown)
        {
            if (target != null && target.activeSelf != shown) target.SetActive(shown);
        }
    }
}

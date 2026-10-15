// One save-slot row of the load screen (05 §8, §9.3): name, a details line "playtime · last saved", the settings
// line, a version warning icon and text that never block loading, Load and Delete (interactable iff the model says
// so, which is State == MainMenu). Pooled by LoadGameView and rebound with other slots, so Bind clears both button
// listeners first (05 §9.3). Save names come from lobby names, which players choose: rich text off, ellipsis (05 §10).
// Design: 05 §8, §9.3 (hierarchy, inspector fields), §10; 06 U43.

using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MultiplayerKit.UI
{
    public sealed class SaveSlotRowView : MonoBehaviour
    {
        [SerializeField] private TMP_Text nameText;
        [SerializeField] private TMP_Text details;
        [SerializeField] private TMP_Text settingsLine;
        [SerializeField] private GameObject versionWarning;
        [SerializeField] private TMP_Text versionText;
        [SerializeField] private Button loadButton;
        [SerializeField] private Button deleteButton;

        private string slotId;
        private string slotName;
        private Action<string> onLoad;
        private Action<string, string> onDelete;

        /// <summary>The slot of the bound model; null after an empty Bind.</summary>
        public string SlotId => slotId;

        /// <summary>Renders one slot. load receives the slot id; delete the slot id and its shown name.</summary>
        public void Bind(SaveSlotRowModel row, Action<string> load, Action<string, string> delete)
        {
            if (loadButton != null) loadButton.onClick.RemoveAllListeners();
            if (deleteButton != null) deleteButton.onClick.RemoveAllListeners();
            if (row == null)
            {
                slotId = null;
                slotName = null;
                onLoad = null;
                onDelete = null;
                return;
            }

            slotId = row.SlotId;
            slotName = row.Name;
            onLoad = load;
            onDelete = delete;

            if (nameText != null)
            {
                nameText.richText = false;
                nameText.overflowMode = TextOverflowModes.Ellipsis;
                nameText.text = row.Name ?? "";
            }
            SetText(details, UiFormat.Join(row.Playtime, row.Updated));
            SetText(settingsLine, row.SettingsLine);
            SetShown(versionWarning, row.VersionMismatch);
            if (versionText != null)
            {
                versionText.text = row.VersionText ?? "";
                SetShown(versionText.gameObject, row.VersionMismatch);
            }

            if (loadButton != null)
            {
                loadButton.interactable = row.Interactable;
                loadButton.onClick.AddListener(OnLoadClicked);
            }
            if (deleteButton != null)
            {
                deleteButton.interactable = row.Interactable;
                deleteButton.onClick.AddListener(OnDeleteClicked);
            }
        }

        private void OnLoadClicked()
        {
            Action<string> load = onLoad;
            if (load == null || string.IsNullOrEmpty(slotId)) return;
            try
            {
                load(slotId);
            }
            catch (Exception e)
            {
                KitLog.Error("Save slot load callback threw.", e);
            }
        }

        private void OnDeleteClicked()
        {
            Action<string, string> delete = onDelete;
            if (delete == null || string.IsNullOrEmpty(slotId)) return;
            try
            {
                delete(slotId, slotName);
            }
            catch (Exception e)
            {
                KitLog.Error("Save slot delete callback threw.", e);
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

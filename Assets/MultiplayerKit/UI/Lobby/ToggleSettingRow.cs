// Bool setting row (05 §5.3): a Toggle that commits on change as the canonical "1" / "0". Rendering goes through
// SetIsOnWithoutNotify, so a SettingChanged echo never commits again.
// Design: 05 §5.3 (control table, commit policy), §9.3 (inspector fields); 06 U28.

using UnityEngine;
using UnityEngine.UI;

namespace MultiplayerKit.UI
{
    public sealed class ToggleSettingRow : SettingRowView
    {
        [SerializeField] private Toggle toggle;

        protected override void BindControl(SettingRowSpec spec)
        {
            if (toggle == null)
            {
                KitLog.Error("ToggleSettingRow on '" + name + "': required field 'toggle' is not assigned.");
                return;
            }
            toggle.onValueChanged.RemoveAllListeners();
            toggle.onValueChanged.AddListener(OnToggled);
        }

        public override void Render(string raw, string display)
        {
            if (toggle == null) return;
            SettingCodec.TryParseBool(raw, out bool on);
            toggle.SetIsOnWithoutNotify(on);
        }

        protected override void SetInteractable(bool interactable)
        {
            if (toggle != null) toggle.interactable = interactable;
        }

        private void OnToggled(bool on)
        {
            Commit(SettingCodec.FormatBool(on));
        }
    }
}

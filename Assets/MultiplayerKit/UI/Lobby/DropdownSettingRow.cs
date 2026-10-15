// Choice setting row (05 §5.3): a TMP_Dropdown whose options are the choice labels (value = index), committed on
// change as the choice's stable value, which is what lobby metadata and saves store. Rendering uses
// SetValueWithoutNotify, so an echo never commits again. A value that is not a choice shows the first option
// (SettingControlRules.ChoiceIndex logs it).
// Design: 05 §5.3 (control table, commit policy), §9.3 (inspector fields); 06 U30.

using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace MultiplayerKit.UI
{
    public sealed class DropdownSettingRow : SettingRowView
    {
        [SerializeField] private TMP_Dropdown dropdown;

        private readonly List<string> optionLabels = new List<string>();

        protected override void BindControl(SettingRowSpec spec)
        {
            if (dropdown == null)
            {
                KitLog.Error("DropdownSettingRow on '" + name + "': required field 'dropdown' is not assigned.");
                return;
            }

            dropdown.onValueChanged.RemoveAllListeners();
            optionLabels.Clear();
            IReadOnlyList<SettingChoice> choices = spec.Definition.Choices;
            for (int i = 0; i < choices.Count; i++) optionLabels.Add(choices[i].Label);
            dropdown.ClearOptions();
            dropdown.AddOptions(optionLabels);
            dropdown.onValueChanged.AddListener(OnChoiceChanged);
        }

        public override void Render(string raw, string display)
        {
            SettingDefinition def = Definition;
            if (dropdown == null || def == null) return;
            dropdown.SetValueWithoutNotify(SettingControlRules.ChoiceIndex(def, raw));
            dropdown.RefreshShownValue();
        }

        protected override void SetInteractable(bool interactable)
        {
            if (dropdown != null) dropdown.interactable = interactable;
        }

        private void OnChoiceChanged(int index)
        {
            SettingDefinition def = Definition;
            if (def == null || index < 0 || index >= def.Choices.Count) return;
            Commit(def.Choices[index].Value);
        }
    }
}

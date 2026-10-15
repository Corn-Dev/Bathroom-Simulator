// One generated published-setting filter of the public browser (05 §7, §9.3): a label and a TMP_Dropdown with "Any"
// plus the setting's values, from a BrowserFilterSpec. PublicBrowserView pools these in the Filters grid after the
// five fixed controls. Pooled rows are rebound, so Bind clears the listener first (05 §9.3), and the selection is
// rendered without notify, so rendering never reports a change. The reported index is resolved to a value by
// BrowserQueryBuilder, never here. Design: 05 §7, §9.2-§9.3 (hierarchy, inspector fields); 06 U39.

using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace MultiplayerKit.UI
{
    public sealed class BrowserFilterRowView : MonoBehaviour
    {
        [SerializeField] private TMP_Text label;
        [SerializeField] private TMP_Dropdown dropdown;

        private readonly List<string> options = new List<string>();
        private string key;
        private Action<string, int> onChanged;

        /// <summary>Schema key of the bound filter; null after an empty Bind.</summary>
        public string Key => key;

        /// <summary>Renders the spec; changed receives (key, selected option index) on every user change.</summary>
        public void Bind(BrowserFilterSpec spec, Action<string, int> changed)
        {
            if (dropdown != null) dropdown.onValueChanged.RemoveAllListeners();
            key = spec != null ? spec.Key : null;
            onChanged = changed;
            if (spec == null) return;

            if (label != null)
            {
                label.richText = false;
                label.text = spec.Label ?? "";
            }
            if (dropdown == null)
            {
                KitLog.Error("BrowserFilterRowView on '" + name + "': required field 'dropdown' is not assigned.");
                return;
            }

            options.Clear();
            if (spec.Options != null)
            {
                for (int i = 0; i < spec.Options.Count; i++) options.Add(spec.Options[i] ?? "");
            }
            dropdown.ClearOptions();
            dropdown.AddOptions(options);
            dropdown.SetValueWithoutNotify(spec.Selected);
            dropdown.RefreshShownValue();
            dropdown.onValueChanged.AddListener(OnValueChanged);
        }

        private void OnValueChanged(int index)
        {
            Action<string, int> changed = onChanged;
            if (changed == null || key == null) return;
            try
            {
                changed(key, index);
            }
            catch (Exception e)
            {
                KitLog.Error("Browser filter callback threw.", e);
            }
        }
    }
}

// Base of the four setting rows (05 §5.3, §9.3): label, padlock, the bound spec and the value-changed callback.
// Rows are pooled by SettingsPanelView and rebound with other specs, so Bind replaces the callback and every subclass
// clears its control's listeners before adding its own (05 §9.3). Rendering always goes through the control's
// ...WithoutNotify setter, so a SettingChanged echo never re-enters the write path. Not a KitView: the panel drives
// it. Read-only values stay legible because the row prefabs set ColorBlock.disabledColor close to normalColor.
// Design: 05 §5.3, §9.3 (inspector fields), §10; 06 U27.

using System;
using TMPro;
using UnityEngine;

namespace MultiplayerKit.UI
{
    public abstract class SettingRowView : MonoBehaviour
    {
        [SerializeField] private TMP_Text label;
        [SerializeField] private GameObject lockIcon;

        private Action<string, string> onChanged;

        public TMP_Text Label => label;
        public GameObject LockIcon => lockIcon;

        /// <summary>The spec of the last successful Bind; null before it.</summary>
        public SettingRowSpec Spec { get; private set; }

        protected SettingDefinition Definition => Spec != null ? Spec.Definition : null;

        /// <summary>Binds the row to a setting spec. changed receives (key, value) for every user commit.</summary>
        public void Bind(SettingRowSpec spec, Action<string, string> changed)
        {
            if (spec == null || spec.IsHeader || spec.Definition == null)
            {
                Spec = null;
                onChanged = null;
                KitLog.Error(GetType().Name + " on '" + name + "': Bind needs a setting row spec.");
                return;
            }

            Spec = spec;
            onChanged = changed;
            if (label != null)
            {
                label.richText = false;
                label.text = spec.Definition.Label;
            }
            SetLockVisible(false);
            BindControl(spec);
        }

        /// <summary>Shows the stored value (canonical raw, display text) without raising the control's callback.</summary>
        public abstract void Render(string raw, string display);

        /// <summary>editable: the control is interactable. showLock: the padlock (host, row locked by Core).</summary>
        public void SetEditable(bool editable, bool showLock)
        {
            SetLockVisible(showLock);
            SetInteractable(editable);
        }

        /// <summary>Commits a value the row still holds back (the slider's debounce). No-op for the other rows.</summary>
        public virtual void FlushPending()
        {
        }

        /// <summary>Configures the control for the spec (options, range, content type) and adds its listener.</summary>
        protected abstract void BindControl(SettingRowSpec spec);

        protected abstract void SetInteractable(bool interactable);

        /// <summary>Hands a user value to the panel, which forwards it to SettingsPanelPresenter.OnUserChanged.</summary>
        protected void Commit(string value)
        {
            SettingRowSpec spec = Spec;
            Action<string, string> callback = onChanged;
            if (spec == null || callback == null) return;
            try
            {
                callback(spec.Definition.Key, value);
            }
            catch (Exception e)
            {
                KitLog.Error(GetType().Name + " change callback threw for '" + spec.Definition.Key + "'.", e);
            }
        }

        private void SetLockVisible(bool visible)
        {
            if (lockIcon != null && lockIcon.activeSelf != visible) lockIcon.SetActive(visible);
        }
    }
}

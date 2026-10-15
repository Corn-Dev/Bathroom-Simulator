// Int / Float setting row (05 §5.3): a Slider over the step index (Int, Float with a step) or over Min..Max
// (continuous Float), with a value label that follows the drag live. Writes are debounced to keep lobby-metadata
// traffic low (Steam rate-limits SetLobbyData): the row commits commitDelay seconds of unscaled time after the last
// change, so a drag across 100 positions sends one write. A held-back value is committed at once in OnDisable and
// when the panel flushes before Start. Rendering uses SetValueWithoutNotify, so an echo never commits again.
// Design: 05 §5.3 (control table, step-index mapping, slider label, commit policy), §9.3 (inspector fields); 06 U29.

using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MultiplayerKit.UI
{
    public sealed class SliderSettingRow : SettingRowView
    {
        [SerializeField] private Slider slider;
        [SerializeField] private TMP_Text valueText;
        [SerializeField] private float commitDelay = 0.25f;

        private bool pending;
        private string pendingValue;
        private float lastChange;

        protected override void BindControl(SettingRowSpec spec)
        {
            pending = false;
            pendingValue = null;
            if (slider == null)
            {
                KitLog.Error("SliderSettingRow on '" + name + "': required field 'slider' is not assigned.");
                return;
            }

            // Listeners go first, so the range changes below cannot commit anything; Render sets the value next.
            slider.onValueChanged.RemoveAllListeners();
            SettingControlRules.SliderRange(spec.Definition, out float min, out float max, out bool wholeNumbers);
            slider.wholeNumbers = wholeNumbers;
            slider.minValue = min;
            slider.maxValue = max;
            slider.onValueChanged.AddListener(OnSliderChanged);
        }

        /// <summary>The stored value wins over a value still held back.</summary>
        public override void Render(string raw, string display)
        {
            pending = false;
            pendingValue = null;
            SettingDefinition def = Definition;
            if (slider != null && def != null) slider.SetValueWithoutNotify(SettingControlRules.CanonicalToSlider(def, raw));
            SetValueText(display);
        }

        public override void FlushPending()
        {
            if (!pending) return;
            pending = false;
            string value = pendingValue;
            pendingValue = null;
            Commit(value);
        }

        protected override void SetInteractable(bool interactable)
        {
            if (slider != null) slider.interactable = interactable;
        }

        private void OnSliderChanged(float sliderValue)
        {
            SettingDefinition def = Definition;
            if (def == null) return;
            pendingValue = SettingControlRules.SliderToCanonical(def, sliderValue);
            pending = true;
            lastChange = Time.unscaledTime;
            SetValueText(SettingControlRules.FormatRaw(def, pendingValue));
        }

        // Debounce: one commit once the value has rested for commitDelay.
        private void Update()
        {
            if (pending && Time.unscaledTime - lastChange >= commitDelay) FlushPending();
        }

        private void OnDisable()
        {
            FlushPending();
        }

        private void SetValueText(string text)
        {
            if (valueText != null) valueText.text = text ?? "";
        }
    }
}

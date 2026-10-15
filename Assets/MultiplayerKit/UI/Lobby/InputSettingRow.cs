// Text and wide-Int setting row (05 §5.3): one single-line TMP_InputField for both the Input and IntInput controls.
// Text: characterLimit = MaxLength, and the Core password key is masked (contentType Password). IntInput (an Int
// range too wide for a slider, such as worldSeed): contentType IntegerNumber, and an empty or unparsable entry
// reverts without calling Core (05 §10). Commits on onEndEdit (focus lost or Enter); Escape while editing reverts to
// the stored value without committing. Core trims, strips, truncates and clamps, and the stored value comes back
// through Render. A row locked mid-edit drops the typed text (SetInteractable). Rich text is off because values can
// be player-chosen and reach clients from lobby metadata.
// Design: 05 §5.3 (control table, commit policy), §9.3 (inspector fields), §10 (input validation); 06 U31.

using System;
using TMPro;
using UnityEngine;

namespace MultiplayerKit.UI
{
    public sealed class InputSettingRow : SettingRowView
    {
        [SerializeField] private TMP_InputField input;

        private string storedRaw = "";

        protected override void BindControl(SettingRowSpec spec)
        {
            storedRaw = "";
            if (input == null)
            {
                KitLog.Error("InputSettingRow on '" + name + "': required field 'input' is not assigned.");
                return;
            }

            input.onEndEdit.RemoveAllListeners();
            bool integer = spec.Control == SettingControl.IntInput;

            // Line type before content type: changing the line type afterwards turns a Password or IntegerNumber
            // field into Custom. A pooled row can switch between a password and a plain text setting on rebuild.
            input.lineType = TMP_InputField.LineType.SingleLine;
            if (integer) input.contentType = TMP_InputField.ContentType.IntegerNumber;
            else if (SettingControlRules.IsPassword(spec.Definition)) input.contentType = TMP_InputField.ContentType.Password;
            else input.contentType = TMP_InputField.ContentType.Standard;
            input.characterLimit = integer ? SettingControlRules.IntInputCharacterLimit : spec.Definition.MaxLength;
            input.richText = false;
            input.onEndEdit.AddListener(OnEndEdit);
            input.ForceLabelUpdate();   // a changed content type re-masks text that did not change
        }

        public override void Render(string raw, string display)
        {
            storedRaw = raw ?? "";
            if (input != null) input.SetTextWithoutNotify(storedRaw);
        }

        // A row can lock while the host is typing in it: the password locks as soon as a second member is in the room
        // (02 §2c). Turning the field off deselects it, and TMP raises no onEndEdit for a field that is no longer
        // interactable, so neither Commit nor the Escape revert would run and the typed text would stay next to the
        // padlock. Reverting first means a read-only field always shows the stored value, never text Core did not take.
        protected override void SetInteractable(bool interactable)
        {
            if (input == null) return;
            if (!interactable) Revert();
            input.interactable = interactable;
        }

        private void OnEndEdit(string text)
        {
            if (Definition == null || input == null) return;

            // Escape ends the edit in the frame it is pressed, and TMP raises onEndEdit for it as well.
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                Revert();
                return;
            }

            string value = text ?? "";
            if (Spec.Control == SettingControl.IntInput)
            {
                if (!SettingCodec.TryParseInt(value, out _))
                {
                    Revert();
                    return;
                }
                value = value.Trim();
            }

            // Focusing and leaving without a change writes nothing.
            if (string.Equals(value, storedRaw, StringComparison.Ordinal)) return;
            Commit(value);
        }

        private void Revert()
        {
            if (input != null) input.SetTextWithoutNotify(storedRaw);
        }
    }
}

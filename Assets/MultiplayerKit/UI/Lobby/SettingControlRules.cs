// Pure rules behind the schema-driven settings panel (05 §5.3): the control each definition gets (the 100-step
// slider threshold), visibility (HostOnly rows hidden on clients, joinInProgress hidden without the capability),
// group labels, display text and the slider step-index mapping in both directions. Static, stateless, no
// UnityEngine.UI, unit-tested (05 §11). The only keys it knows are the Core keys password (masked) and
// joinInProgress (capability hide). Core still normalizes every value; nothing here is a validity guard.
// Design: 05 §5.3 (control table, visibility, slider label), §10 (input validation), §11; 06 U26.

using System;
using System.Globalization;

namespace MultiplayerKit.UI
{
    public enum SettingControl { Toggle, Slider, Dropdown, Input, IntInput }

    public static class SettingControlRules
    {
        /// <summary>An Int whose range has more step positions than this gets an integer input instead of a slider.</summary>
        public const int MaxSliderSteps = 100;

        /// <summary>Header of the "" group.</summary>
        public const string GeneralGroupLabel = "General";

        /// <summary>Character limit of an integer input: a sign plus the ten digits of int.MaxValue.</summary>
        public const int IntInputCharacterLimit = 11;

        public const string OnText = "On";
        public const string OffText = "Off";

        // Display text of a set password; the value itself never reaches a label.
        private const string MaskedPassword = "\u2022\u2022\u2022\u2022\u2022\u2022";

        // Same tolerance SettingCodec uses when it counts Float step positions.
        private const double FloatStepEpsilon = 1e-9;

        /// <summary>05 §5.3 table: Bool → Toggle, Int → Slider or IntInput, Float → Slider, Choice → Dropdown, Text → Input.</summary>
        public static SettingControl ControlFor(SettingDefinition d)
        {
            if (d == null) throw new ArgumentNullException(nameof(d));
            switch (d.Kind)
            {
                case SettingKind.Bool:
                    return SettingControl.Toggle;
                case SettingKind.Int:
                    // A slider over a range like worldSeed 0..2147483647 is unusable.
                    return (d.Max - d.Min) / Math.Max(d.Step, 1d) <= MaxSliderSteps ? SettingControl.Slider : SettingControl.IntInput;
                case SettingKind.Float:
                    return SettingControl.Slider;
                case SettingKind.Choice:
                    return SettingControl.Dropdown;
                default:
                    return SettingControl.Input;
            }
        }

        /// <summary>
        /// Hidden: HostOnly on a client (clients never see host-local values such as password or autosaveMinutes),
        /// and joinInProgress without the JoinInProgress capability. "public" is never filtered here: the schema
        /// already removed that choice when PublicLobbies is off (01 §5.3).
        /// </summary>
        public static bool IsVisible(SettingDefinition d, bool isHost, GameCapabilities caps)
        {
            if (d == null) return false;
            if (d.HostOnly && !isHost) return false;
            if (string.Equals(d.Key, CoreSettingKeys.JoinInProgress, StringComparison.Ordinal)
                && (caps & GameCapabilities.JoinInProgress) == 0)
            {
                return false;
            }
            return true;
        }

        /// <summary>"" → "General"; any other group is its own label.</summary>
        public static string GroupLabel(string group)
        {
            return string.IsNullOrEmpty(group) ? GeneralGroupLabel : group;
        }

        /// <summary>The Core password setting: a masked input, and never shown as text.</summary>
        public static bool IsPassword(SettingDefinition d)
        {
            return d != null && string.Equals(d.Key, CoreSettingKeys.Password, StringComparison.Ordinal);
        }

        /// <summary>Display text of the stored value: slider value label and read-only text (05 §5.3).</summary>
        public static string FormatValue(SettingDefinition d, IReadOnlyHostSettings s)
        {
            if (d == null || s == null) return "";
            return FormatRaw(d, s.GetRaw(d.Key));
        }

        /// <summary>
        /// Display text of a canonical value. Int: invariant integer. Float: "0.##" in the current culture. Bool: On/Off.
        /// Choice: the choice label. Text: the text, except a set password, which shows as dots.
        /// </summary>
        public static string FormatRaw(SettingDefinition d, string raw)
        {
            if (d == null) return "";
            string value = raw ?? "";
            switch (d.Kind)
            {
                case SettingKind.Bool:
                    return SettingCodec.TryParseBool(value, out bool on) && on ? OnText : OffText;
                case SettingKind.Int:
                    return SettingCodec.TryParseInt(value, out long whole) ? whole.ToString(CultureInfo.InvariantCulture) : value;
                case SettingKind.Float:
                    return SettingCodec.TryParseFloat(value, out double number) ? number.ToString("0.##", CultureInfo.CurrentCulture) : value;
                case SettingKind.Choice:
                    for (int i = 0; i < d.Choices.Count; i++)
                    {
                        if (string.Equals(d.Choices[i].Value, value, StringComparison.Ordinal)) return d.Choices[i].Label;
                    }
                    return value;
                default:
                    if (IsPassword(d)) return value.Length == 0 ? "" : MaskedPassword;
                    return value;
            }
        }

        /// <summary>
        /// Step positions above the first, so the slider runs over the step index 0..n: Int, and Float with Step &gt; 0.
        /// -1 for a continuous Float (Step 0) and for kinds without a slider.
        /// </summary>
        public static long StepCount(SettingDefinition d)
        {
            if (d == null) return -1;
            if (d.Kind == SettingKind.Int)
            {
                long step = Math.Max(1L, (long)d.Step);
                return ((long)d.Max - (long)d.Min) / step;
            }
            if (d.Kind == SettingKind.Float && d.Step > 0)
            {
                return (long)Math.Floor((d.Max - d.Min) / d.Step + FloatStepEpsilon);
            }
            return -1;
        }

        /// <summary>
        /// Slider setup: the step index 0..n with whole numbers (Int; Float with a step, for example gameSpeed
        /// 0.5..2.0 step 0.25 → 7 positions), or Min..Max without whole numbers for a continuous Float.
        /// </summary>
        public static void SliderRange(SettingDefinition d, out float min, out float max, out bool wholeNumbers)
        {
            long n = StepCount(d);
            if (n >= 0)
            {
                min = 0f;
                max = n;
                wholeNumbers = true;
                return;
            }
            min = d != null ? (float)d.Min : 0f;
            max = d != null ? (float)d.Max : 0f;
            wholeNumbers = false;
        }

        /// <summary>
        /// Slider position → canonical value. Stepped: Min + i * Step for the nearest index i, so steps other than 1
        /// snap exactly and Core stores the value unchanged. Continuous: the value clamped to Min..Max.
        /// </summary>
        public static string SliderToCanonical(SettingDefinition d, float sliderValue)
        {
            if (d == null) throw new ArgumentNullException(nameof(d));
            long n = StepCount(d);

            if (d.Kind == SettingKind.Int)
            {
                long step = Math.Max(1L, (long)d.Step);
                return SettingCodec.FormatInt((int)((long)d.Min + ClampIndex(sliderValue, n) * step));
            }
            if (d.Kind != SettingKind.Float) return d.Default;   // no slider for other kinds

            double v = n >= 0 ? d.Min + ClampIndex(sliderValue, n) * d.Step : sliderValue;
            if (double.IsNaN(v) || v < d.Min) v = d.Min;
            if (v > d.Max) v = d.Max;
            return SettingCodec.FormatFloat((float)v);
        }

        /// <summary>Canonical value → slider position (step index, or the value for a continuous Float). Unreadable → Default.</summary>
        public static float CanonicalToSlider(SettingDefinition d, string raw)
        {
            if (d == null) return 0f;
            if (!SettingCodec.TryParseFloat(raw, out double v) && !SettingCodec.TryParseFloat(d.Default, out v)) v = d.Min;
            if (v < d.Min) v = d.Min;
            if (v > d.Max) v = d.Max;

            long n = StepCount(d);
            if (n < 0) return (float)v;

            double step = d.Kind == SettingKind.Int ? Math.Max(1d, d.Step) : d.Step;
            double k = Math.Round((v - d.Min) / step, MidpointRounding.AwayFromZero);
            if (k < 0) k = 0;
            if (k > n) k = n;
            return (float)k;
        }

        /// <summary>Dropdown index of a Choice value. A value that is not a choice shows the first one and logs Warning.</summary>
        public static int ChoiceIndex(SettingDefinition d, string value)
        {
            if (d == null || d.Choices.Count == 0) return 0;
            for (int i = 0; i < d.Choices.Count; i++)
            {
                if (string.Equals(d.Choices[i].Value, value, StringComparison.Ordinal)) return i;
            }
            KitLog.Warning("Setting '" + d.Key + "': value '" + KitLog.Truncate(value) + "' is not one of its choices; showing the first.");
            return 0;
        }

        // Nearest step index in 0..n.
        private static long ClampIndex(float sliderValue, long n)
        {
            if (n <= 0 || float.IsNaN(sliderValue)) return 0;
            double r = Math.Round(sliderValue, MidpointRounding.AwayFromZero);
            if (r <= 0) return 0;
            if (r >= n) return n;
            return (long)r;
        }
    }
}

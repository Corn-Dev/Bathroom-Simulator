// Host setting definitions: key, label, kind, default, range/choices, visibility and edit flags (decision 3).
// Design: 01 §5.1 (fields, factories, validation, flag semantics), 01 §5.4 (canonical default form).
// Definitions are immutable. Factory validation throws ArgumentException (programmer error, fatal at
// bootstrap). Default is always canonical: SettingCodec.TryNormalize returns it unchanged, so the host and
// clients can never disagree about a setting nobody touched.

using System;
using System.Collections.Generic;
using System.Globalization;

namespace MultiplayerKit
{
    public enum SettingKind { Bool, Int, Float, Choice, Text }

    /// <summary>One dropdown entry. Value is stored in lobby metadata and saves; Label is display only.</summary>
    public sealed class SettingChoice
    {
        public const int MaxValueLength = 24;

        /// <summary>^[a-zA-Z0-9_\-]{1,24}$.</summary>
        public string Value { get; }

        /// <summary>Shown in the dropdown; Value when constructed with a null or empty label.</summary>
        public string Label { get; }

        public SettingChoice(string value, string label)
        {
            if (!IsValidValue(value))
            {
                throw new ArgumentException(
                    "Choice value must match ^[a-zA-Z0-9_\\-]{1,24}$: '" + value + "'.", nameof(value));
            }
            Value = value;
            Label = string.IsNullOrEmpty(label) ? value : label;
        }

        internal static bool IsValidValue(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length > MaxValueLength) return false;
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                bool ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_' || c == '-';
                if (!ok) return false;
            }
            return true;
        }
    }

    public sealed class SettingDefinition
    {
        public const int MaxKeyLength = 32;
        public const int MaxChoices = 32;
        public const int MaxTextLength = 256;
        public const int DefaultTextLength = 64;

        private static readonly IReadOnlyList<SettingChoice> NoChoices = Array.AsReadOnly(new SettingChoice[0]);

        /// <summary>^[a-zA-Z][a-zA-Z0-9_]{0,31}$, unique in the schema. Stored as "s." + Key in metadata.</summary>
        public string Key { get; }

        /// <summary>Key when constructed with a null or empty label.</summary>
        public string Label { get; }

        public SettingKind Kind { get; }

        /// <summary>Canonical string form (01 §5.4).</summary>
        public string Default { get; }

        /// <summary>Int/Float only; 0 for other kinds.</summary>
        public double Min { get; }

        public double Max { get; }

        /// <summary>Int/Float only. Float: 0 = continuous. Int: never 0 (a 0 passed to the factory means 1).</summary>
        public double Step { get; }

        /// <summary>Choice only, 1..32 entries; empty (never null) for other kinds.</summary>
        public IReadOnlyList<SettingChoice> Choices { get; }

        /// <summary>Text only, 1..256 UTF-16 chars; 0 for other kinds.</summary>
        public int MaxLength { get; }

        /// <summary>Host-local: never written to lobby metadata, never shown to clients; still in the save snapshot (except password).</summary>
        public bool HostOnly { get; }

        /// <summary>False: SessionController.TrySetSetting returns LockedAfterStart once the phase is not Room.</summary>
        public bool EditableAfterStart { get; }

        /// <summary>Shown by browsers and the friends list, and accepted as a browser filter. Requires !HostOnly.</summary>
        public bool PublishToLobby { get; }

        /// <summary>UI section header; "" means "General".</summary>
        public string Group { get; }

        /// <summary>
        /// Set only by HostSettingsSchema when a capability is off (joinInProgress without JoinInProgress):
        /// every value normalizes to Default, so the setting cannot be switched on by any path.
        /// </summary>
        internal bool IsFixed { get; }

        private SettingDefinition(string key, string label, SettingKind kind, string defaultValue,
            double min, double max, double step, IReadOnlyList<SettingChoice> choices, int maxLength,
            bool hostOnly, bool editableAfterStart, bool publish, string group, bool isFixed)
        {
            if (!IsValidKey(key))
            {
                throw new ArgumentException("Setting key must match ^[a-zA-Z][a-zA-Z0-9_]{0,31}$: '" + key + "'.", nameof(key));
            }
            if (publish && hostOnly)
            {
                throw new ArgumentException("Setting '" + key + "': PublishToLobby requires HostOnly = false.");
            }

            Key = key;
            Label = string.IsNullOrEmpty(label) ? key : label;
            Kind = kind;
            HostOnly = hostOnly;
            EditableAfterStart = editableAfterStart;
            PublishToLobby = publish;
            Group = group ?? "";
            IsFixed = isFixed;
            Choices = NoChoices;

            switch (kind)
            {
                case SettingKind.Bool:
                    break;
                case SettingKind.Int:
                case SettingKind.Float:
                    ValidateRange(key, kind, min, max, step);
                    Min = min;
                    Max = max;
                    Step = kind == SettingKind.Int && step == 0 ? 1 : step;
                    break;
                case SettingKind.Choice:
                    Choices = CopyChoices(key, choices);
                    break;
                case SettingKind.Text:
                    if (maxLength < 1 || maxLength > MaxTextLength)
                    {
                        throw new ArgumentException("Setting '" + key + "': MaxLength must be 1.." + MaxTextLength + ", was " + maxLength + ".");
                    }
                    MaxLength = maxLength;
                    break;
                default:
                    throw new ArgumentException("Setting '" + key + "': unknown SettingKind " + kind + ".");
            }

            Default = defaultValue ?? throw new ArgumentException("Setting '" + key + "': default must not be null.");
            ValidateDefault();
        }

        // Factories (01 §5.1). Parameter names are part of the API: callers use named arguments.

        public static SettingDefinition Bool(string key, string label, bool def, string group = "",
            bool hostOnly = false, bool editableAfterStart = false, bool publish = false)
        {
            return new SettingDefinition(key, label, SettingKind.Bool, SettingCodec.FormatBool(def), 0, 0, 0, null, 0,
                hostOnly, editableAfterStart, publish, group, false);
        }

        public static SettingDefinition Int(string key, string label, int def, int min, int max, int step = 1,
            string group = "", bool hostOnly = false, bool editableAfterStart = false, bool publish = false)
        {
            return new SettingDefinition(key, label, SettingKind.Int, SettingCodec.FormatInt(def), min, max, step, null, 0,
                hostOnly, editableAfterStart, publish, group, false);
        }

        public static SettingDefinition Float(string key, string label, float def, float min, float max, float step = 0f,
            string group = "", bool hostOnly = false, bool editableAfterStart = false, bool publish = false)
        {
            if (float.IsNaN(def) || float.IsInfinity(def))
            {
                throw new ArgumentException("Setting '" + key + "': default must be a finite number.");
            }
            return new SettingDefinition(key, label, SettingKind.Float, SettingCodec.FormatFloat(def), min, max, step, null, 0,
                hostOnly, editableAfterStart, publish, group, false);
        }

        public static SettingDefinition Choice(string key, string label, string def, IReadOnlyList<SettingChoice> choices,
            string group = "", bool hostOnly = false, bool editableAfterStart = false, bool publish = false)
        {
            return new SettingDefinition(key, label, SettingKind.Choice, def, 0, 0, 0, choices, 0,
                hostOnly, editableAfterStart, publish, group, false);
        }

        public static SettingDefinition Text(string key, string label, string def, int maxLength = DefaultTextLength,
            string group = "", bool hostOnly = false, bool editableAfterStart = false, bool publish = false)
        {
            return new SettingDefinition(key, label, SettingKind.Text, def ?? "", 0, 0, 0, null, maxLength,
                hostOnly, editableAfterStart, publish, group, false);
        }

        /// <summary>Copy with other choices (relabel, or drop "public"). The default must stay one of them.</summary>
        public SettingDefinition WithChoices(IReadOnlyList<SettingChoice> choices)
        {
            if (Kind != SettingKind.Choice)
            {
                throw new InvalidOperationException("Setting '" + Key + "': WithChoices needs a Choice setting, not " + Kind + ".");
            }
            return new SettingDefinition(Key, Label, Kind, Default, 0, 0, 0, choices, 0,
                HostOnly, EditableAfterStart, PublishToLobby, Group, IsFixed);
        }

        /// <summary>Copy with another range (used to cap maxPlayers). The default must stay inside it and on the step grid.</summary>
        public SettingDefinition WithRange(double min, double max)
        {
            if (Kind != SettingKind.Int && Kind != SettingKind.Float)
            {
                throw new InvalidOperationException("Setting '" + Key + "': WithRange needs an Int or Float setting, not " + Kind + ".");
            }
            return new SettingDefinition(Key, Label, Kind, Default, min, max, Step, null, 0,
                HostOnly, EditableAfterStart, PublishToLobby, Group, IsFixed);
        }

        /// <summary>Copy whose value is pinned to Default (HostSettingsSchema, capability off).</summary>
        internal SettingDefinition AsFixed()
        {
            return new SettingDefinition(Key, Label, Kind, Default, Min, Max, Step, Choices, MaxLength,
                HostOnly, EditableAfterStart, PublishToLobby, Group, true);
        }

        /// <summary>True if value is one of Choices (ordinal). Always false for non-Choice kinds.</summary>
        public bool HasChoice(string value)
        {
            for (int i = 0; i < Choices.Count; i++)
            {
                if (string.Equals(Choices[i].Value, value, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        internal static bool IsValidKey(string key)
        {
            if (string.IsNullOrEmpty(key) || key.Length > MaxKeyLength) return false;
            for (int i = 0; i < key.Length; i++)
            {
                char c = key[i];
                bool letter = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');
                bool ok = i == 0 ? letter : letter || (c >= '0' && c <= '9') || c == '_';
                if (!ok) return false;
            }
            return true;
        }

        private static void ValidateRange(string key, SettingKind kind, double min, double max, double step)
        {
            if (!IsFinite(min) || !IsFinite(max) || !IsFinite(step))
            {
                throw new ArgumentException("Setting '" + key + "': min, max and step must be finite numbers.");
            }
            if (min > max)
            {
                throw new ArgumentException("Setting '" + key + "': min " + Num(min) + " is greater than max " + Num(max) + ".");
            }
            if (step < 0)
            {
                throw new ArgumentException("Setting '" + key + "': step must not be negative, was " + Num(step) + ".");
            }
            if (kind == SettingKind.Int)
            {
                bool whole = Math.Floor(min) == min && Math.Floor(max) == max && Math.Floor(step) == step;
                if (!whole || min < int.MinValue || max > int.MaxValue || step > int.MaxValue)
                {
                    throw new ArgumentException("Setting '" + key + "': Int min, max and step must be whole numbers in the int range.");
                }
            }
            else if (Math.Abs(min) > float.MaxValue || Math.Abs(max) > float.MaxValue || step > float.MaxValue)
            {
                throw new ArgumentException("Setting '" + key + "': Float min, max and step must fit in a float.");
            }
        }

        private static IReadOnlyList<SettingChoice> CopyChoices(string key, IReadOnlyList<SettingChoice> choices)
        {
            if (choices == null || choices.Count == 0 || choices.Count > MaxChoices)
            {
                throw new ArgumentException("Setting '" + key + "': a Choice needs 1.." + MaxChoices + " choices.");
            }
            var copy = new SettingChoice[choices.Count];
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < choices.Count; i++)
            {
                SettingChoice choice = choices[i];
                if (choice == null)
                {
                    throw new ArgumentException("Setting '" + key + "': choice " + i + " is null.");
                }
                if (!seen.Add(choice.Value))
                {
                    throw new ArgumentException("Setting '" + key + "': duplicate choice value '" + choice.Value + "'.");
                }
                copy[i] = choice;
            }
            return Array.AsReadOnly(copy);
        }

        // Runs last in the constructor, when every field is assigned.
        private void ValidateDefault()
        {
            switch (Kind)
            {
                case SettingKind.Int:
                case SettingKind.Float:
                    if (!SettingCodec.TryParseFloat(Default, out double v) || v < Min || v > Max)
                    {
                        throw new ArgumentException("Setting '" + Key + "': default " + Default + " is outside " + Num(Min) + ".." + Num(Max) + ".");
                    }
                    break;
                case SettingKind.Choice:
                    if (!HasChoice(Default))
                    {
                        throw new ArgumentException("Setting '" + Key + "': default '" + Default + "' is not one of the choices.");
                    }
                    break;
                case SettingKind.Text:
                    if (Default.Length > MaxLength)
                    {
                        throw new ArgumentException("Setting '" + Key + "': default is longer than MaxLength " + MaxLength + ".");
                    }
                    break;
            }

            if (!SettingCodec.TryNormalize(this, Default, out string canonical, out bool adjusted)
                || adjusted || !string.Equals(canonical, Default, StringComparison.Ordinal))
            {
                throw new ArgumentException("Setting '" + Key + "': default '" + Default
                    + "' is not canonical (off the step grid, untrimmed, or containing control characters).");
            }
        }

        private static bool IsFinite(double v)
        {
            return !double.IsNaN(v) && !double.IsInfinity(v);
        }

        private static string Num(double v)
        {
            return v.ToString("R", CultureInfo.InvariantCulture);
        }
    }
}

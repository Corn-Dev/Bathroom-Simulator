// Canonical string forms of setting values (invariant culture), normalization (clamp, snap to step, choice
// membership, text sanitizing), the "s." metadata prefix and control-character handling.
// Design: 01 §5.4 (canonical forms, parse fallback), 01 §5.3 (password rejects control characters).
// Every value HostSettings stores, publishes or saves has passed TryNormalize, and normalizing a canonical
// value returns it unchanged, so host and clients converge on identical strings.

using System;
using System.Globalization;
using System.Text;

namespace MultiplayerKit
{
    public static class SettingCodec
    {
        /// <summary>"s.", the lobby metadata prefix of setting values.</summary>
        public const string Prefix = KitKeys.SettingPrefix;

        /// <summary>"s." + key.</summary>
        public static string ToLobbyKey(string key)
        {
            return Prefix + key;
        }

        /// <summary>"s.difficulty" → "difficulty". False for null, other prefixes and a bare "s.".</summary>
        public static bool TryFromLobbyKey(string lobbyKey, out string key)
        {
            if (lobbyKey != null && lobbyKey.Length > Prefix.Length && lobbyKey.StartsWith(Prefix, StringComparison.Ordinal))
            {
                key = lobbyKey.Substring(Prefix.Length);
                return true;
            }
            key = null;
            return false;
        }

        // Canonical forms (01 §5.4).

        /// <summary>"1" / "0".</summary>
        public static string FormatBool(bool value)
        {
            return value ? KitKeys.FlagOn : KitKeys.FlagOff;
        }

        /// <summary>Invariant decimal.</summary>
        public static string FormatInt(int value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>Invariant round-trip ("R") form; negative zero is written as "0".</summary>
        public static string FormatFloat(float value)
        {
            if (value == 0f) value = 0f;
            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        // Parsing. Leading/trailing whitespace is ignored; nothing here throws.

        /// <summary>"1", "0", "true", "false" (case-insensitive; true/false for hand-written snapshots).</summary>
        public static bool TryParseBool(string text, out bool value)
        {
            value = false;
            if (text == null) return false;
            string t = text.Trim();
            if (t == KitKeys.FlagOn || string.Equals(t, "true", StringComparison.OrdinalIgnoreCase))
            {
                value = true;
                return true;
            }
            return t == KitKeys.FlagOff || string.Equals(t, "false", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Optional sign and decimal digits only (no decimal point, exponent or separators).</summary>
        public static bool TryParseInt(string text, out long value)
        {
            value = 0;
            if (text == null) return false;
            return long.TryParse(text.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);
        }

        /// <summary>Invariant decimal or exponent form. NaN and infinities are rejected.</summary>
        public static bool TryParseFloat(string text, out double value)
        {
            value = 0;
            if (text == null) return false;
            if (!double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value)) return false;
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        // Normalization.

        /// <summary>
        /// Converts a canonical or user-entered value to the definition's canonical form. False when it cannot be
        /// read for the kind (bad number, NaN, unknown choice, null for non-Text, control characters in password).
        /// adjusted is true when the value itself changed (clamped, snapped, trimmed, stripped, truncated or
        /// pinned by a fixed definition), not when only its spelling did ("true" → "1", "007" → "7").
        /// </summary>
        public static bool TryNormalize(SettingDefinition def, string input, out string canonical, out bool adjusted)
        {
            if (def == null) throw new ArgumentNullException(nameof(def));

            bool ok;
            switch (def.Kind)
            {
                case SettingKind.Bool:
                    adjusted = false;
                    ok = TryParseBool(input, out bool b);
                    canonical = ok ? FormatBool(b) : null;
                    break;
                case SettingKind.Int:
                    ok = NormalizeInt(def, input, out canonical, out adjusted);
                    break;
                case SettingKind.Float:
                    ok = NormalizeFloat(def, input, out canonical, out adjusted);
                    break;
                case SettingKind.Choice:
                    adjusted = false;
                    canonical = input == null ? null : input.Trim();
                    ok = canonical != null && def.HasChoice(canonical);
                    break;
                case SettingKind.Text:
                    ok = NormalizeText(def, input, out canonical, out adjusted);
                    break;
                default:
                    canonical = null;
                    adjusted = false;
                    ok = false;
                    break;
            }

            if (!ok)
            {
                canonical = null;
                adjusted = false;
                return false;
            }
            if (def.IsFixed && !string.Equals(canonical, def.Default, StringComparison.Ordinal))
            {
                canonical = def.Default;
                adjusted = true;
            }
            return true;
        }

        /// <summary>
        /// Canonical form of untrusted input (lobby data, snapshots); the definition's Default when it cannot be
        /// read (usedDefault = true, the caller logs Warning once per key, 01 §5.4).
        /// </summary>
        public static string NormalizeOrDefault(SettingDefinition def, string input, out bool usedDefault)
        {
            if (TryNormalize(def, input, out string canonical, out _))
            {
                usedDefault = false;
                return canonical;
            }
            usedDefault = true;
            return def.Default;
        }

        // Text helpers.

        /// <summary>
        /// Display-safe text: control characters removed, trimmed, cut to maxLength UTF-16 chars without splitting
        /// a surrogate pair, trailing whitespace trimmed again. Null → "". Applying it twice changes nothing.
        /// </summary>
        public static string SanitizeText(string text, int maxLength)
        {
            if (string.IsNullOrEmpty(text)) return "";
            string s = StripControlCharacters(text).Trim();
            if (maxLength < 0) maxLength = 0;
            if (s.Length > maxLength)
            {
                int cut = maxLength;
                if (cut > 0 && char.IsHighSurrogate(s[cut - 1])) cut--;
                s = s.Substring(0, cut).TrimEnd();
            }
            return s;
        }

        /// <summary>Removes every char.IsControl character (including tab and newlines). Null → "".</summary>
        public static string StripControlCharacters(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            if (!ContainsControlCharacters(text)) return text;
            var sb = new StringBuilder(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                if (!char.IsControl(text[i])) sb.Append(text[i]);
            }
            return sb.ToString();
        }

        public static bool ContainsControlCharacters(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            for (int i = 0; i < text.Length; i++)
            {
                if (char.IsControl(text[i])) return true;
            }
            return false;
        }

        // Kind-specific normalizers.

        // Clamp to [Min, Max], then snap to Min + k * Step (nearest, halves up), never above Max. Integer math only.
        private static bool NormalizeInt(SettingDefinition def, string input, out string canonical, out bool adjusted)
        {
            canonical = null;
            adjusted = false;
            if (!TryParseInt(input, out long parsed)) return false;

            long min = (long)def.Min;
            long max = (long)def.Max;
            long step = Math.Max(1L, (long)def.Step);
            long v = parsed < min ? min : (parsed > max ? max : parsed);

            long k = (v - min + step / 2) / step;   // v - min is 0..2^32-1, so no overflow
            long maxK = (max - min) / step;
            if (k > maxK) k = maxK;
            v = min + k * step;

            adjusted = v != parsed;
            canonical = FormatInt((int)v);
            return true;
        }

        // Clamp, snap when Step > 0, then round to float: the canonical value is always an exact float.
        private static bool NormalizeFloat(SettingDefinition def, string input, out string canonical, out bool adjusted)
        {
            canonical = null;
            adjusted = false;
            if (!TryParseFloat(input, out double parsed)) return false;

            double v = parsed < def.Min ? def.Min : (parsed > def.Max ? def.Max : parsed);
            if (def.Step > 0)
            {
                double k = Math.Round((v - def.Min) / def.Step, MidpointRounding.AwayFromZero);
                double maxK = Math.Floor((def.Max - def.Min) / def.Step + 1e-9);
                if (k > maxK) k = maxK;
                if (k < 0) k = 0;
                v = def.Min + k * def.Step;
                if (v > def.Max) v = def.Max;   // floating-point guard
                if (v < def.Min) v = def.Min;
            }

            float f = (float)v;
            adjusted = f != (float)parsed;
            canonical = FormatFloat(f);
            return true;
        }

        // Text: strip, trim, truncate. password instead rejects control characters, so the user never gets a
        // silently different password (01 §5.4).
        private static bool NormalizeText(SettingDefinition def, string input, out string canonical, out bool adjusted)
        {
            string raw = input ?? "";
            if (string.Equals(def.Key, CoreSettingKeys.Password, StringComparison.Ordinal) && ContainsControlCharacters(raw))
            {
                canonical = null;
                adjusted = false;
                return false;
            }
            canonical = SanitizeText(raw, def.MaxLength);
            adjusted = !string.Equals(canonical, raw, StringComparison.Ordinal);
            return true;
        }
    }
}

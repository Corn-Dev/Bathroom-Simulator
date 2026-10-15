// Setting values for one session: canonical strings keyed by schema key, plus the lobby-metadata and
// save-snapshot conversions. Design: 01 §5.2 (API), 01 §5.4 (serialization and parse fallbacks), 01 §5.3
// (password). A plain value container: NotOwner / LockedAfterStart / LockedByResume come from
// SessionController.TrySetSetting. The password never leaves this object through an output: ToLobbyData skips
// host-only keys and password, ToSnapshot skips password, and neither Apply method ever reads one.

using System;
using System.Collections.Generic;
using System.Globalization;

namespace MultiplayerKit
{
    /// <summary>Read side handed to the game adapter and the UI.</summary>
    public interface IReadOnlyHostSettings
    {
        HostSettingsSchema Schema { get; }

        /// <summary>Unknown key or wrong kind: logs Error once per key and returns false.</summary>
        bool GetBool(string key);

        /// <summary>Unknown key or wrong kind: logs Error once per key and returns 0.</summary>
        int GetInt(string key);

        /// <summary>Unknown key or wrong kind: logs Error once per key and returns 0.</summary>
        float GetFloat(string key);

        /// <summary>Choice value or Text. Unknown key or wrong kind: logs Error once per key and returns "".</summary>
        string GetString(string key);

        /// <summary>Canonical string of any kind; "" (Error once) for an unknown key.</summary>
        string GetRaw(string key);

        /// <summary>Key exists in the schema (lets Core read optional CommonSettings).</summary>
        bool Has(string key);
    }

    public enum SettingSetResult { Ok, Unchanged, UnknownKey, WrongKind, NotOwner, LockedAfterStart, LockedByResume, Clamped }

    public sealed class HostSettings : IReadOnlyHostSettings
    {
        private readonly Dictionary<string, string> values;
        private readonly HashSet<string> errorLogged = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> warningLogged = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>Every setting starts at its definition's Default.</summary>
        public HostSettings(HostSettingsSchema schema)
        {
            Schema = schema ?? throw new ArgumentNullException(nameof(schema));
            values = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (SettingDefinition def in schema.Definitions) values[def.Key] = def.Default;
        }

        public HostSettingsSchema Schema { get; }

        /// <summary>Key whose stored value changed. Not raised for Unchanged, nor for Clamped when the stored value stays the same.</summary>
        public event Action<string> Changed;

        public bool Has(string key)
        {
            return Schema.Has(key);
        }

        // Read side.

        public bool GetBool(string key)
        {
            return TryGetTyped(key, SettingKind.Bool, "GetBool", out string raw) && raw == KitKeys.FlagOn;
        }

        public int GetInt(string key)
        {
            if (!TryGetTyped(key, SettingKind.Int, "GetInt", out string raw)) return 0;
            return int.TryParse(raw, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int v) ? v : 0;
        }

        public float GetFloat(string key)
        {
            if (!TryGetTyped(key, SettingKind.Float, "GetFloat", out string raw)) return 0f;
            return float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : 0f;
        }

        public string GetString(string key)
        {
            if (!Schema.TryGet(key, out SettingDefinition def))
            {
                LogErrorOnce(key, "GetString: unknown setting");
                return "";
            }
            if (def.Kind != SettingKind.Choice && def.Kind != SettingKind.Text)
            {
                LogErrorOnce(key, "GetString on a " + def.Kind + " setting");
                return "";
            }
            return values[def.Key];
        }

        public string GetRaw(string key)
        {
            if (Schema.TryGet(key, out SettingDefinition def)) return values[def.Key];
            LogErrorOnce(key, "GetRaw: unknown setting");
            return "";
        }

        // Write side (host). See SettingCodec.TryNormalize for what counts as Clamped.

        /// <summary>
        /// Normalizes a canonical or user-entered value (01 §5.4) and stores it. UnknownKey: not in the schema.
        /// WrongKind: unreadable for the kind (value unchanged). Unchanged: same canonical value as stored.
        /// Clamped: the value was adjusted (clamped, snapped, trimmed, truncated or pinned) and the adjusted value
        /// is stored, so the UI re-reads GetRaw. Ok: stored as given.
        /// </summary>
        public SettingSetResult TrySet(string key, string canonicalOrUserValue)
        {
            if (!Schema.TryGet(key, out SettingDefinition def)) return SettingSetResult.UnknownKey;
            if (!SettingCodec.TryNormalize(def, canonicalOrUserValue, out string canonical, out bool adjusted))
            {
                return SettingSetResult.WrongKind;
            }
            bool changed = Store(def.Key, canonical);
            if (adjusted) return SettingSetResult.Clamped;
            return changed ? SettingSetResult.Ok : SettingSetResult.Unchanged;
        }

        /// <summary>WrongKind unless the setting is Bool.</summary>
        public SettingSetResult TrySetBool(string key, bool v)
        {
            return TrySetKind(key, SettingKind.Bool, SettingCodec.FormatBool(v));
        }

        /// <summary>WrongKind unless the setting is Int.</summary>
        public SettingSetResult TrySetInt(string key, int v)
        {
            return TrySetKind(key, SettingKind.Int, SettingCodec.FormatInt(v));
        }

        /// <summary>WrongKind unless the setting is Float, and for NaN or infinities.</summary>
        public SettingSetResult TrySetFloat(string key, float v)
        {
            if (!Schema.TryGet(key, out SettingDefinition def)) return SettingSetResult.UnknownKey;
            if (def.Kind != SettingKind.Float || float.IsNaN(v) || float.IsInfinity(v)) return SettingSetResult.WrongKind;
            return TrySet(key, SettingCodec.FormatFloat(v));
        }

        // Lobby metadata (01 §5.4).

        /// <summary>"s."-prefixed canonical values of every setting that is not HostOnly. Never contains the password.</summary>
        public IReadOnlyDictionary<string, string> ToLobbyData()
        {
            var data = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (SettingDefinition def in Schema.Definitions)
            {
                if (!IsPublishable(def)) continue;
                data[SettingCodec.ToLobbyKey(def.Key)] = values[def.Key];
            }
            return data;
        }

        /// <summary>
        /// Client side: reads the "s." values of every non-HostOnly setting and raises Changed per difference.
        /// Unreadable values fall back to the default (Warning once per key). Host-only keys and the password are
        /// never read from metadata. A missing Text key means "" (Steam deletes a key set to "", 03 §4); a missing
        /// key of any other kind has not been published yet and keeps its current value.
        /// </summary>
        public void ApplyLobbyData(IReadOnlyDictionary<string, string> data)
        {
            if (data == null) return;
            foreach (SettingDefinition def in Schema.Definitions)
            {
                if (!IsPublishable(def)) continue;
                if (!data.TryGetValue(SettingCodec.ToLobbyKey(def.Key), out string raw))
                {
                    if (def.Kind != SettingKind.Text) continue;
                    raw = "";
                }
                Store(def.Key, NormalizeUntrusted(def, raw, "lobby data"));
            }
        }

        // Save snapshot (04 §2.2 settingKeys/settingValues).

        /// <summary>Unprefixed canonical values of every setting, host-only ones included, except the password.</summary>
        public IReadOnlyDictionary<string, string> ToSnapshot()
        {
            var snapshot = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (SettingDefinition def in Schema.Definitions)
            {
                if (IsPassword(def.Key)) continue;
                snapshot[def.Key] = values[def.Key];
            }
            return snapshot;
        }

        /// <summary>
        /// Resume: applies the snapshot's values for keys in the schema and raises Changed per difference. Unknown
        /// keys are ignored, keys the snapshot lacks keep their value, unreadable values fall back to the default
        /// (Warning once per key), and a password in the snapshot is never restored.
        /// </summary>
        public void ApplySnapshot(IReadOnlyDictionary<string, string> snapshot)
        {
            if (snapshot == null) return;
            foreach (SettingDefinition def in Schema.Definitions)
            {
                if (IsPassword(def.Key)) continue;
                if (!snapshot.TryGetValue(def.Key, out string raw)) continue;
                Store(def.Key, NormalizeUntrusted(def, raw, "save snapshot"));
            }
        }

        // Internals.

        private static bool IsPassword(string key)
        {
            return string.Equals(key, CoreSettingKeys.Password, StringComparison.Ordinal);
        }

        // HostOnly values stay on the host; password is excluded again in case a definition ever lost the flag.
        private static bool IsPublishable(SettingDefinition def)
        {
            return !def.HostOnly && !IsPassword(def.Key);
        }

        private SettingSetResult TrySetKind(string key, SettingKind kind, string canonical)
        {
            if (!Schema.TryGet(key, out SettingDefinition def)) return SettingSetResult.UnknownKey;
            if (def.Kind != kind) return SettingSetResult.WrongKind;
            return TrySet(key, canonical);
        }

        private string NormalizeUntrusted(SettingDefinition def, string raw, string source)
        {
            string canonical = SettingCodec.NormalizeOrDefault(def, raw, out bool usedDefault);
            if (usedDefault && warningLogged.Add(def.Key))
            {
                KitLog.Warning("Setting '" + def.Key + "': unreadable value '" + KitLog.Truncate(raw) + "' in "
                    + source + "; using the default.");
            }
            return canonical;
        }

        // Returns true and raises Changed when the stored value differs.
        private bool Store(string key, string canonical)
        {
            if (string.Equals(values[key], canonical, StringComparison.Ordinal)) return false;
            values[key] = canonical;

            Action<string> handler = Changed;
            if (handler != null)
            {
                try
                {
                    handler(key);
                }
                catch (Exception e)
                {
                    KitLog.Error("HostSettings.Changed handler threw for '" + key + "'.", e);
                }
            }
            return true;
        }

        private bool TryGetTyped(string key, SettingKind kind, string getter, out string raw)
        {
            raw = null;
            if (!Schema.TryGet(key, out SettingDefinition def))
            {
                LogErrorOnce(key, getter + ": unknown setting");
                return false;
            }
            if (def.Kind != kind)
            {
                LogErrorOnce(key, getter + " on a " + def.Kind + " setting");
                return false;
            }
            raw = values[def.Key];
            return true;
        }

        private void LogErrorOnce(string key, string message)
        {
            string safeKey = key ?? "(null)";
            if (errorLogged.Add(safeKey)) KitLog.Error("HostSettings." + message + " '" + KitLog.Truncate(safeKey) + "'.");
        }
    }
}

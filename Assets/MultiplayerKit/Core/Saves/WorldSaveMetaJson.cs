// World save metadata file "ws_<slotId>.json", the commit record of a slot: a JsonUtility DTO with exactly the
// 04 §2.2 fields, its validation, and conversion to and from WorldSaveSlotInfo. Design: 04 §2.2, §2.4, §9.3
// (the only JsonUtility use in Core), 06 C43. The snapshot is two parallel arrays (JsonUtility has no
// dictionaries). The password key is stripped on write and on read, although HostSettings.ToSnapshot already
// excludes it. Public, not internal as sketched in 04 §2.2, because SteamWorldSaveStore (Steam assembly) reads
// and writes the same file.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace MultiplayerKit
{
    [Serializable]
    public sealed class WorldSaveMetaJson
    {
        public const int CurrentFormat = 1;
        public const int MaxDisplayNameLength = 48;

        /// <summary>Metadata files are under 4 KB; anything over this is rejected unread.</summary>
        public const int MaxFileBytes = 64 * 1024;

        // Serialized fields. Names and types are the file format (04 §2.2); never rename them.
        public int format = CurrentFormat;
        public string slotId;
        public string displayName;
        public string gameVersion;
        public long createdUtcTicks;
        public long updatedUtcTicks;
        public double playtimeSeconds;
        public string[] settingKeys;
        public string[] settingValues;
        public long payloadBytes;
        public uint payloadCrc32;

        /// <summary>Metadata for a payload about to be written. Strips the password, sorts keys, cuts the name to 48.</summary>
        public static WorldSaveMetaJson Create(string slotId, string displayName, DateTime createdUtc, DateTime updatedUtc,
            double playtimeSeconds, string gameVersion, IReadOnlyDictionary<string, string> settingsSnapshot,
            long payloadBytes, uint payloadCrc32)
        {
            var meta = new WorldSaveMetaJson
            {
                format = CurrentFormat,
                slotId = slotId,
                displayName = SettingCodec.SanitizeText(displayName, MaxDisplayNameLength),
                gameVersion = gameVersion ?? "",
                createdUtcTicks = ToUtc(createdUtc).Ticks,
                updatedUtcTicks = ToUtc(updatedUtc).Ticks,
                playtimeSeconds = SanitizePlaytime(playtimeSeconds),
                payloadBytes = payloadBytes,
                payloadCrc32 = payloadCrc32,
            };

            var keys = new List<string>();
            if (settingsSnapshot != null)
            {
                foreach (KeyValuePair<string, string> pair in settingsSnapshot)
                {
                    if (pair.Key == null || IsPassword(pair.Key)) continue;
                    keys.Add(pair.Key);
                }
            }
            keys.Sort(StringComparer.Ordinal);
            meta.settingKeys = keys.ToArray();
            meta.settingValues = new string[keys.Count];
            for (int i = 0; i < keys.Count; i++) meta.settingValues[i] = settingsSnapshot[keys[i]] ?? "";
            return meta;
        }

        /// <summary>Metadata for an existing slot description (same rules as Create).</summary>
        public static WorldSaveMetaJson FromSlotInfo(WorldSaveSlotInfo info, uint payloadCrc32)
        {
            if (info == null) throw new ArgumentNullException(nameof(info));
            return Create(info.SlotId, info.DisplayName, info.CreatedUtc, info.UpdatedUtc, info.PlaytimeSeconds,
                info.GameVersion, info.SettingsSnapshot, info.PayloadBytes, payloadCrc32);
        }

        /// <summary>Validated conversion. Throws InvalidOperationException when Validate fails.</summary>
        public WorldSaveSlotInfo ToSlotInfo()
        {
            if (!Validate(out string failure))
            {
                throw new InvalidOperationException("World save metadata is invalid: " + failure);
            }

            var snapshot = new Dictionary<string, string>(StringComparer.Ordinal);
            int count = settingKeys == null ? 0 : settingKeys.Length;
            for (int i = 0; i < count; i++)
            {
                string key = settingKeys[i];
                if (key == null || IsPassword(key)) continue;
                snapshot[key] = settingValues[i] ?? "";
            }

            return new WorldSaveSlotInfo(slotId,
                SettingCodec.SanitizeText(displayName, MaxDisplayNameLength),
                new DateTime(createdUtcTicks, DateTimeKind.Utc),
                new DateTime(updatedUtcTicks, DateTimeKind.Utc),
                SanitizePlaytime(playtimeSeconds),
                gameVersion ?? "",
                snapshot,
                payloadBytes);
        }

        /// <summary>
        /// Structural rules: format 1, valid slot id, ticks inside the DateTime range, payloadBytes 1..64 MiB,
        /// parallel arrays of equal length (both missing is an empty snapshot). Lenient fields (name, version,
        /// playtime) are sanitized by ToSlotInfo instead of failing.
        /// </summary>
        public bool Validate(out string failure)
        {
            if (format != CurrentFormat)
            {
                failure = "metadata format " + format.ToString(CultureInfo.InvariantCulture);
                return false;
            }
            if (!WorldSaveNames.IsValidSlotId(slotId))
            {
                failure = "invalid slot id";
                return false;
            }
            if (!IsValidTicks(createdUtcTicks) || !IsValidTicks(updatedUtcTicks))
            {
                failure = "timestamp out of range";
                return false;
            }
            if (payloadBytes <= 0 || payloadBytes > WorldSaveEnvelope.MaxPayloadBytes)
            {
                failure = "payloadBytes out of range";
                return false;
            }
            int keyCount = settingKeys == null ? 0 : settingKeys.Length;
            int valueCount = settingValues == null ? 0 : settingValues.Length;
            if (keyCount != valueCount)
            {
                failure = "settingKeys and settingValues differ in length";
                return false;
            }
            failure = null;
            return true;
        }

        public string ToJson()
        {
            return UnityEngine.JsonUtility.ToJson(this);
        }

        /// <summary>UTF-8 without a byte order mark.</summary>
        public byte[] ToUtf8Bytes()
        {
            return Encoding.UTF8.GetBytes(ToJson());
        }

        /// <summary>Parses and validates. Never throws; failure is log text (no file contents).</summary>
        public static bool TryParse(string json, out WorldSaveMetaJson meta, out string failure)
        {
            meta = null;
            if (string.IsNullOrEmpty(json))
            {
                failure = "empty metadata";
                return false;
            }
            if (json[0] == '\uFEFF') json = json.Substring(1);

            WorldSaveMetaJson parsed;
            try
            {
                parsed = UnityEngine.JsonUtility.FromJson<WorldSaveMetaJson>(json);
            }
            catch (Exception e)
            {
                failure = "unparseable JSON (" + e.GetType().Name + ")";
                return false;
            }
            if (parsed == null)
            {
                failure = "unparseable JSON";
                return false;
            }
            if (!parsed.Validate(out failure)) return false;
            meta = parsed;
            return true;
        }

        public static bool TryParseUtf8(byte[] bytes, out WorldSaveMetaJson meta, out string failure)
        {
            meta = null;
            if (bytes == null || bytes.Length == 0 || bytes.Length > MaxFileBytes)
            {
                failure = "metadata file size out of range";
                return false;
            }
            string json;
            try
            {
                json = new UTF8Encoding(false, true).GetString(bytes);
            }
            catch (ArgumentException)
            {
                failure = "metadata is not valid UTF-8";
                return false;
            }
            return TryParse(json, out meta, out failure);
        }

        private static bool IsPassword(string key)
        {
            return string.Equals(key, CoreSettingKeys.Password, StringComparison.Ordinal);
        }

        private static bool IsValidTicks(long ticks)
        {
            return ticks >= 0 && ticks <= DateTime.MaxValue.Ticks;
        }

        private static DateTime ToUtc(DateTime value)
        {
            return value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : value;
        }

        private static double SanitizePlaytime(double seconds)
        {
            return double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0 ? 0 : seconds;
        }
    }
}

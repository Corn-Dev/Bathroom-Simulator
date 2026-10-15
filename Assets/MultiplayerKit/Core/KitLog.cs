// "[Kit]"-prefixed logging for every kit assembly, plus Truncate for untrusted text (lobby data, chat,
// launch args, payload strings). Design: 06 C8, 01 §2 (untrusted ids logged truncated to 64 chars).
// UnityEngine.Debug is written fully qualified because the KitLog.Debug method hides the type name here.

using System;

namespace MultiplayerKit
{
    public static class KitLog
    {
        public const string Prefix = "[Kit] ";

        /// <summary>Enables <see cref="Debug"/> output. Set by the bootstrap from its verboseLogging field.</summary>
        public static bool Verbose;

        public static void Info(string message)
        {
            UnityEngine.Debug.Log(Prefix + message);
        }

        public static void Warning(string message)
        {
            UnityEngine.Debug.LogWarning(Prefix + message);
        }

        public static void Error(string message)
        {
            UnityEngine.Debug.LogError(Prefix + message);
        }

        /// <summary>Error with the exception type, message and stack trace (adapter callback failures, 01 §4.8).</summary>
        public static void Error(string message, Exception exception)
        {
            UnityEngine.Debug.LogError(Prefix + message + (exception == null ? "" : "\n" + exception));
        }

        /// <summary>Diagnostic detail; only written when <see cref="Verbose"/> is true.</summary>
        public static void Debug(string message)
        {
            if (!Verbose) return;
            UnityEngine.Debug.Log(Prefix + message);
        }

        /// <summary>
        /// Log-safe form of untrusted text: null → "", control characters (including newlines) → '?',
        /// cut to <paramref name="max"/> characters with "..." appended when cut.
        /// </summary>
        public static string Truncate(string text, int max = 64)
        {
            if (string.IsNullOrEmpty(text)) return "";
            if (max < 1) max = 1;

            bool cut = text.Length > max;
            int length = cut ? max : text.Length;
            char[] buffer = new char[length];
            for (int i = 0; i < length; i++)
            {
                char c = text[i];
                buffer[i] = char.IsControl(c) ? '?' : c;
            }
            string safe = new string(buffer);
            return cut ? safe + "..." : safe;
        }
    }
}

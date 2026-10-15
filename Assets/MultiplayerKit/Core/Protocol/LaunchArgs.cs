// Parser for the Steam join launch argument "+connect_lobby <id>" (also "+connect_lobby=<id>"), used for the
// cold-start command line and for the rich-presence connect string of GameRichPresenceJoinRequested_t.
// Design: 03 §8.5 (TryConsumeLaunchLobby), 02 §8 (cold start), 03 §16 (malformed id → Warning, no join).
// Ids go through LobbyId.TryParse, so signs, hex, overflow and "0" are rejected. Pure apart from that Warning.

using System;

namespace MultiplayerKit
{
    public static class LaunchArgs
    {
        public const string ConnectLobby = "+connect_lobby";

        /// <summary>
        /// One string such as "+connect_lobby 123", "+connect_lobby=123" or "  +connect_lobby  123  extra".
        /// The key is matched case-insensitively; only the first token after it is read. False for null, a bare
        /// id, a missing or invalid id, and keys with other suffixes ("+connect_lobbyX").
        /// </summary>
        public static bool TryParseConnectLobby(string text, out LobbyId lobby)
        {
            lobby = LobbyId.None;
            if (text == null) return false;

            string t = text.Trim();
            if (!t.StartsWith(ConnectLobby, StringComparison.OrdinalIgnoreCase)) return false;

            string rest = t.Substring(ConnectLobby.Length);
            if (rest.Length == 0) return false;
            if (rest[0] == '=') rest = rest.Substring(1);
            else if (char.IsWhiteSpace(rest[0])) rest = rest.TrimStart();
            else return false;

            return LobbyId.TryParse(FirstToken(rest), out lobby);
        }

        /// <summary>
        /// Command-line scan (Environment.GetCommandLineArgs()): "+connect_lobby" followed by an id argument, or a
        /// single "+connect_lobby=&lt;id&gt;" / "+connect_lobby &lt;id&gt;" argument. Returns the first valid occurrence.
        /// False when absent or when every occurrence is malformed; a malformed one is logged once as Warning.
        /// </summary>
        public static bool TryFind(string[] args, out LobbyId lobby)
        {
            lobby = LobbyId.None;
            if (args == null) return false;

            string malformed = null;
            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i] == null ? "" : args[i].Trim();
                if (!arg.StartsWith(ConnectLobby, StringComparison.OrdinalIgnoreCase)) continue;

                if (arg.Length == ConnectLobby.Length)
                {
                    // Two-token form: the id is the next argument.
                    string next = i + 1 < args.Length ? args[i + 1] : null;
                    if (LobbyId.TryParse(next, out lobby)) return true;
                    malformed = malformed ?? (arg + " " + (next ?? ""));
                    continue;
                }

                if (TryParseConnectLobby(arg, out lobby)) return true;
                malformed = malformed ?? arg;
            }

            lobby = LobbyId.None;
            if (malformed != null)
            {
                KitLog.Warning("Ignoring malformed launch argument '" + KitLog.Truncate(malformed) + "'.");
            }
            return false;
        }

        private static string FirstToken(string s)
        {
            for (int i = 0; i < s.Length; i++)
            {
                if (char.IsWhiteSpace(s[i])) return s.Substring(0, i);
            }
            return s;
        }
    }
}

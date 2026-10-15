// Kit lobby chat lines for the password check (decision 5) and kick (decision 6). Design: 01 §7 (grammar,
// verbs, codec API), 03 §6 (Steam carriage, sender checks, kick reason rule).
// Field order: exactly five '|'-separated fields, the last may be empty, whole line <= 256 chars:
//   1. "mk"      literal tag
//   2. proto     KitVersion.Protocol, invariant decimal
//   3. verb      "auth" | "authok" | "kick"
//   4. target    PlayerId, invariant decimal, non-zero
//   5. arg       auth: PasswordHasher.Hash; authok: ""; kick: (int)KitError as decimal
// The sender is never part of the line; it always comes from the platform (ILobbyService.ChatReceived).

using System;
using System.Globalization;

namespace MultiplayerKit
{
    public static class LobbyChatCodec
    {
        /// <summary>Wire text: "auth", "authok", "kick".</summary>
        public enum Verb { Auth, AuthOk, Kick }

        public const string Tag = "mk";
        public const char Separator = '|';
        public const int MaxLength = 256;

        private const int FieldCount = 5;
        private const string VerbAuth = "auth";
        private const string VerbAuthOk = "authok";
        private const string VerbKick = "kick";

        private static readonly string ProtoText = KitVersion.Protocol.ToString(CultureInfo.InvariantCulture);
        private static readonly string LinePrefix = Tag + Separator;

        /// <summary>
        /// "mk|proto|verb|target|arg". arg null or "" when unused; kick passes
        /// ((int)reason).ToString(CultureInfo.InvariantCulture). Throws ArgumentException for an invalid target,
        /// an arg containing '|' or control characters, or a line over 256 chars, so it never produces a line
        /// TryDecode would drop.
        /// </summary>
        public static string Encode(Verb verb, PlayerId target, string arg)
        {
            if (!target.IsValid)
            {
                throw new ArgumentException("Chat target must be a valid PlayerId.", nameof(target));
            }
            string a = arg ?? "";
            if (a.IndexOf(Separator) >= 0 || SettingCodec.ContainsControlCharacters(a))
            {
                throw new ArgumentException("Chat arg must not contain '|' or control characters.", nameof(arg));
            }

            string line = LinePrefix + ProtoText + Separator + VerbText(verb) + Separator + target.ToString() + Separator + a;
            if (line.Length > MaxLength)
            {
                throw new ArgumentException("Chat line is longer than " + MaxLength + " chars.", nameof(arg));
            }
            return line;
        }

        /// <summary>
        /// False (outputs default) for null, over 256 chars, no "mk|" tag, not exactly five fields, another
        /// protocol, an unknown verb or an unparsable target. Never throws; ordinary text chat simply fails.
        /// </summary>
        public static bool TryDecode(string line, out Verb verb, out PlayerId target, out string arg)
        {
            verb = Verb.Auth;
            target = PlayerId.None;
            arg = null;

            if (string.IsNullOrEmpty(line) || line.Length > MaxLength) return false;
            if (!line.StartsWith(LinePrefix, StringComparison.Ordinal)) return false;

            string[] fields = line.Split(Separator);
            if (fields.Length != FieldCount) return false;
            if (!string.Equals(fields[1], ProtoText, StringComparison.Ordinal)) return false;
            if (!TryParseVerb(fields[2], out Verb parsedVerb)) return false;
            if (!PlayerId.TryParse(fields[3], out PlayerId parsedTarget)) return false;

            verb = parsedVerb;
            target = parsedTarget;
            arg = fields[4];
            return true;
        }

        /// <summary>
        /// Reason carried by a kick arg (03 §6): Kicked, WrongPassword, Banned or IncompatibleVersion as a decimal
        /// KitError value; anything else, including other defined codes, maps to Kicked.
        /// </summary>
        public static KitError ParseKickReason(string arg)
        {
            if (int.TryParse(arg, NumberStyles.None, CultureInfo.InvariantCulture, out int code)
                && Enum.IsDefined(typeof(KitError), code))
            {
                var reason = (KitError)code;
                switch (reason)
                {
                    case KitError.Kicked:
                    case KitError.WrongPassword:
                    case KitError.Banned:
                    case KitError.IncompatibleVersion:
                        return reason;
                }
            }
            return KitError.Kicked;
        }

        public static string VerbText(Verb verb)
        {
            switch (verb)
            {
                case Verb.Auth: return VerbAuth;
                case Verb.AuthOk: return VerbAuthOk;
                case Verb.Kick: return VerbKick;
                default: throw new ArgumentOutOfRangeException(nameof(verb), verb, "Unknown chat verb.");
            }
        }

        // Exact, case-sensitive wire text.
        private static bool TryParseVerb(string text, out Verb verb)
        {
            switch (text)
            {
                case VerbAuth: verb = Verb.Auth; return true;
                case VerbAuthOk: verb = Verb.AuthOk; return true;
                case VerbKick: verb = Verb.Kick; return true;
                default: verb = Verb.Auth; return false;
            }
        }
    }
}

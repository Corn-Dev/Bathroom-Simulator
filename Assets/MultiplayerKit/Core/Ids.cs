// Platform-neutral lobby and player ids: readonly structs wrapping a ulong.
// Design: 01 §2. The Steam assembly maps CSteamID <-> ulong (SteamIds.cs); no other conversion exists.
// ToString is decimal, invariant culture. That exact text is used in lobby metadata, chat messages,
// rich presence "+connect_lobby <id>" and the password salt. Untrusted text always goes through TryParse.

using System;
using System.Globalization;

namespace MultiplayerKit
{
    /// <summary>Platform-neutral lobby id. <see cref="None"/> (Value 0) means "no lobby".</summary>
    public readonly struct LobbyId : IEquatable<LobbyId>
    {
        public readonly ulong Value;

        /// <summary>Value == 0; IsValid is false.</summary>
        public static readonly LobbyId None = default;

        public LobbyId(ulong value) { Value = value; }

        public bool IsValid => Value != 0UL;

        public bool Equals(LobbyId other) => Value == other.Value;
        public override bool Equals(object obj) => obj is LobbyId o && o.Value == Value;
        public override int GetHashCode() => Value.GetHashCode();
        public static bool operator ==(LobbyId a, LobbyId b) => a.Value == b.Value;
        public static bool operator !=(LobbyId a, LobbyId b) => a.Value != b.Value;

        /// <summary>Decimal, invariant culture.</summary>
        public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);

        /// <summary>
        /// Parses trimmed text of 1-20 ASCII digits. Returns false (id = None) for null, empty,
        /// signs, inner whitespace, hex, overflow and "0". Never throws.
        /// </summary>
        public static bool TryParse(string s, out LobbyId id)
        {
            if (IdText.TryParseNonZero(s, out ulong value))
            {
                id = new LobbyId(value);
                return true;
            }
            id = None;
            return false;
        }
    }

    /// <summary>Platform-neutral player id. <see cref="None"/> (Value 0) means "no player".</summary>
    public readonly struct PlayerId : IEquatable<PlayerId>
    {
        public readonly ulong Value;

        /// <summary>Value == 0; IsValid is false.</summary>
        public static readonly PlayerId None = default;

        public PlayerId(ulong value) { Value = value; }

        public bool IsValid => Value != 0UL;

        public bool Equals(PlayerId other) => Value == other.Value;
        public override bool Equals(object obj) => obj is PlayerId o && o.Value == Value;
        public override int GetHashCode() => Value.GetHashCode();
        public static bool operator ==(PlayerId a, PlayerId b) => a.Value == b.Value;
        public static bool operator !=(PlayerId a, PlayerId b) => a.Value != b.Value;

        /// <summary>Decimal, invariant culture.</summary>
        public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);

        /// <summary>
        /// Parses trimmed text of 1-20 ASCII digits. Returns false (id = None) for null, empty,
        /// signs, inner whitespace, hex, overflow and "0". Never throws.
        /// </summary>
        public static bool TryParse(string s, out PlayerId id)
        {
            if (IdText.TryParseNonZero(s, out ulong value))
            {
                id = new PlayerId(value);
                return true;
            }
            id = None;
            return false;
        }
    }

    /// <summary>Shared parsing rule for both id types (01 §2).</summary>
    internal static class IdText
    {
        /// <summary>ulong.MaxValue has 20 decimal digits.</summary>
        internal const int MaxDigits = 20;

        internal static bool TryParseNonZero(string s, out ulong value)
        {
            value = 0UL;
            if (s == null) return false;

            string t = s.Trim();
            if (t.Length == 0 || t.Length > MaxDigits) return false;

            // ASCII digits only: rejects signs, inner whitespace, hex prefixes and non-ASCII digits.
            for (int i = 0; i < t.Length; i++)
            {
                char c = t[i];
                if (c < '0' || c > '9') return false;
            }

            // Fails only on overflow (20 digits above ulong.MaxValue).
            if (!ulong.TryParse(t, NumberStyles.None, CultureInfo.InvariantCulture, out ulong parsed)) return false;
            if (parsed == 0UL) return false;

            value = parsed;
            return true;
        }
    }
}

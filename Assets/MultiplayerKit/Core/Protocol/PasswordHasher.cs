// Lobby password hash sent in the chat "auth" line and in ConnectionPayload.PasswordHash, and its
// constant-time comparison. Design: 01 §7 (hash definition, trust limits), 02 header (Matches), 03 §6, §15.
// Hash = lowercase hex SHA-256 over UTF-8 of "<lobby id decimal>:<trimmed password>", "" for no password.
// The lobby-id salt makes a hash useless in any other lobby, but it is replayable inside the same lobby;
// NGO approval, not this hash alone, is the real gate (03 §15).

using System.Security.Cryptography;
using System.Text;

namespace MultiplayerKit
{
    public static class PasswordHasher
    {
        /// <summary>Length of a non-empty hash (64 lowercase hex chars).</summary>
        public const int HashLength = 64;

        private const string HexDigits = "0123456789abcdef";

        /// <summary>"" when the trimmed password is empty (no password); otherwise 64 lowercase hex chars.</summary>
        public static string Hash(LobbyId lobby, string password)
        {
            string trimmed = password == null ? "" : password.Trim();
            if (trimmed.Length == 0) return "";

            byte[] input = Encoding.UTF8.GetBytes(lobby.ToString() + ":" + trimmed);
            byte[] digest;
            using (SHA256 sha = SHA256.Create())
            {
                digest = sha.ComputeHash(input);
            }

            var hex = new char[digest.Length * 2];
            for (int i = 0; i < digest.Length; i++)
            {
                hex[i * 2] = HexDigits[digest[i] >> 4];
                hex[i * 2 + 1] = HexDigits[digest[i] & 0x0F];
            }
            return new string(hex);
        }

        /// <summary>
        /// Ordinal equality in time independent of where the strings differ (null counts as ""). Only the length
        /// is not hidden, and every non-empty hash has the same length.
        /// </summary>
        public static bool Matches(string a, string b)
        {
            string x = a ?? "";
            string y = b ?? "";
            if (x.Length != y.Length) return false;

            int diff = 0;
            for (int i = 0; i < x.Length; i++)
            {
                diff |= x[i] ^ y[i];
            }
            return diff == 0;
        }
    }
}

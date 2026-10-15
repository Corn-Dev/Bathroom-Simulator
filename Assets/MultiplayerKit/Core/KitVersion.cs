// Kit wire-protocol version. Design: 01 §7 (k.proto), 01 §4.6 (ConnectionPayload.KitProtocol),
// 01 §7 chat lines "mk|<proto>|...". Bump when lobby keys, chat lines or the approval payload change
// incompatibly; peers with a different value never see each other's lobbies and are rejected at approval.

namespace MultiplayerKit
{
    public static class KitVersion
    {
        /// <summary>Written as decimal to "k.proto", sent in the approval payload and in every kit chat line.</summary>
        public const int Protocol = 1;
    }
}

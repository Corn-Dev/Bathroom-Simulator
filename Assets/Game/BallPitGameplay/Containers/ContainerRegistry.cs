// ContainerRegistry: the live set of BallContainers in the scene (brief §4.1). Containers add/remove themselves in
// OnEnable/OnDisable, so the host's DepositDriver and the UI iterate them without a scene search. No player-count or
// layout assumptions (brief §5): any number of containers, any colour, placed anywhere.
using System.Collections.Generic;

namespace BallPitGameplay
{
    public static class ContainerRegistry
    {
        private static readonly List<BallContainer> all = new List<BallContainer>();

        /// <summary>Enabled containers.</summary>
        public static IReadOnlyList<BallContainer> All => all;

        public static void Register(BallContainer c) { if (c != null && !all.Contains(c)) all.Add(c); }
        public static void Unregister(BallContainer c) { if (c != null) all.Remove(c); }
    }
}

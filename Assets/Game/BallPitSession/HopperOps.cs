// Hopper backend seam for HopperOwnership (04 §12): production wraps the new PitAuthority members (04 §9.1); tests
// (06 T20) use a fake. Design: 04 §5, §9.1, §12; 06 G5. Member names and semantics are exactly PitAuthority's.

using System;
using System.Collections.Generic;
using BallPitFinal;

namespace BallPitSession
{
    public interface IHopperOps
    {
        /// <summary>Length of slot 0's protected prefix (parked tanks).</summary>
        int ParkedHopperCount { get; }

        /// <summary>Hopper list of a slot 0..3, in list order (the last id is the next one Dump pours).</summary>
        IReadOnlyList<int> Hopper(int player);

        /// <summary>Restore only (before NetPit.StartHost): parked balls first, then own balls.</summary>
        bool RestoreHopperBall(int player, int id, bool parked);

        /// <summary>Moves hopper[fromPlayer] (1..3) to the end of the parked prefix; returns the count moved.</summary>
        int ParkHopper(int fromPlayer, List<int> moved);

        /// <summary>Moves the selected parked ids to the end of hopper[toPlayer]; returns the count moved.</summary>
        int UnparkHopper(int toPlayer, Predicate<int> select, List<int> moved);
    }

    /// <summary>Thin pass-through to the live PitAuthority (host only).</summary>
    public sealed class PitAuthorityHopperOps : IHopperOps
    {
        public readonly PitAuthority Authority;

        public PitAuthorityHopperOps(PitAuthority authority)
        {
            Authority = authority ?? throw new ArgumentNullException(nameof(authority));
        }

        public int ParkedHopperCount => Authority.ParkedHopperCount;

        public IReadOnlyList<int> Hopper(int player) => Authority.Hopper(player);

        public bool RestoreHopperBall(int player, int id, bool parked) => Authority.RestoreHopperBall(player, id, parked);

        public int ParkHopper(int fromPlayer, List<int> moved) => Authority.ParkHopper(fromPlayer, moved);

        public int UnparkHopper(int toPlayer, Predicate<int> select, List<int> moved) =>
            Authority.UnparkHopper(toPlayer, select, moved);
    }
}

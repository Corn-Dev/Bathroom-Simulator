// DTOs of the ball-pit world save payload: the fill parameters (section FILL), one hopper tank per PlayerId (HOPR) and
// the whole save with parallel record arrays (BALL), plus the format constants BallPitSaveWriter and BallPitSaveReader
// share. Design: 04 §3.1-3.2 (byte layout and read rules), §4.1 (DTO); 06 G7. GAME limits and the key rule live in
// BallPitGameState. The field order is listed at the top of BallPitSaveWriter.cs and BallPitSaveReader.cs.

using System.Collections.Generic;
using BallPitFinal;
using UnityEngine;

namespace BallPitSession
{
    /// <summary>
    /// Section FILL: exactly the inputs that determine ball ids, colors and fill poses (FillFcc, RandomFill, FinishFill,
    /// Quantizer), plus the exact play bounds the Q21 poses were encoded against, the fill checksum and the ball count.
    /// A resume rebuilds the fill from these and compares checksum and count before it applies any record (04 §6.3).
    /// </summary>
    public struct PitFillParams
    {
        public byte FillType;                    // (byte)BallPitFinal.FillType: 0 Fcc, 1 Random
        public int PitX, PitY, PitZ;
        public float BallDiameter;
        public int ColorCount, Seed;
        public float ColliderRadiusScale, Jitter, MaxSupportGapDegrees;
        public bool PitWalls;
        public Vector3 BoundsCenter, BoundsSize; // PitWorld.PlayBounds
        public ulong FillChecksum;               // PitWorld.FillChecksum after FinishFill
        public int BallCount;                    // Store.Count after the fill, == PitX * PitY * PitZ
        public Vector3 PitAnchor;                // [v2+] PitSettings.pitAnchor (the 12th fill field); v1 saves read (0, 0, 0)
        public string SiteId;                    // [v2+] the PitSite the world was built at; null = none (site-less, or v1). Never applied to PitSettings

        public Bounds Bounds => new Bounds(BoundsCenter, BoundsSize);

        /// <summary>
        /// Reads w.S, w.PlayBounds, w.FillChecksum and w.Store.Count. w.S is the inspector-editable settings object, so
        /// the adapter takes this once when the world is built and passes it to every BallPitSaveWriter.Capture.
        /// </summary>
        public static PitFillParams FromWorld(PitWorld w)
        {
            PitSettings s = w.S;
            return new PitFillParams
            {
                FillType = (byte)s.fillType,
                PitX = s.pitX,
                PitY = s.pitY,
                PitZ = s.pitZ,
                BallDiameter = s.ballDiameter,
                ColorCount = s.colorCount,
                Seed = s.seed,
                ColliderRadiusScale = s.colliderRadiusScale,
                Jitter = s.jitter,
                MaxSupportGapDegrees = s.maxSupportGapDegrees,
                PitWalls = s.pitWalls,
                BoundsCenter = w.PlayBounds.center,
                BoundsSize = w.PlayBounds.size,
                FillChecksum = w.FillChecksum,
                BallCount = w.Store.Count,
                PitAnchor = s.pitAnchor,
                SiteId = null, // PitWorld knows no sites: the adapter sets it (OnHostPitReady)
            };
        }

        /// <summary>
        /// A Clone() of the inspector settings with the 12 fill fields overwritten: fillType, pitX, pitY, pitZ,
        /// ballDiameter, colorCount, seed, colliderRadiusScale, jitter, maxSupportGapDegrees, pitWalls, pitAnchor. Physics
        /// and tool tunables keep this build's values. Bounds, checksum and ball count are compared after the rebuild, not
        /// applied; SiteId is never applied (the adapter checks it against the scene's sites).
        /// </summary>
        public PitSettings ApplyTo(PitSettings inspectorBase)
        {
            PitSettings s = (inspectorBase ?? new PitSettings()).Clone();
            s.fillType = (BallPitFinal.FillType)FillType;
            s.pitX = PitX;
            s.pitY = PitY;
            s.pitZ = PitZ;
            s.ballDiameter = BallDiameter;
            s.colorCount = ColorCount;
            s.seed = Seed;
            s.colliderRadiusScale = ColliderRadiusScale;
            s.jitter = Jitter;
            s.maxSupportGapDegrees = MaxSupportGapDegrees;
            s.pitWalls = PitWalls;
            s.pitAnchor = PitAnchor;
            return s;
        }

        /// <summary>
        /// The FILL read rules (04 §3.2), or null when they all hold. The reader applies them on load and Capture before
        /// writing, so no save is written that its reader rejects. pitWalls is a raw byte 0..1 on the wire; the reader
        /// checks that byte itself.
        /// </summary>
        public string Validate()
        {
            if (FillType > 1) return "fillType " + FillType + " is not 0 (Fcc) or 1 (Random)";
            const int axis = BallPitWorldSave.MaxPitAxis;
            if (PitX < 1 || PitX > axis || PitY < 1 || PitY > axis || PitZ < 1 || PitZ > axis)
                return "pit " + PitX + "x" + PitY + "x" + PitZ + " has an axis outside 1.." + axis;
            long n = (long)PitX * PitY * PitZ;
            if (n > BallPitWorldSave.MaxBallCount) return "pit holds " + n + " balls, more than " + BallPitWorldSave.MaxBallCount;
            if (!InRange(BallDiameter, 0.01f, 10f)) return "ballDiameter " + BallDiameter + " outside 0.01..10";
            if (ColorCount < 1 || ColorCount > BallPitWorldSave.MaxColors)
                return "colorCount " + ColorCount + " outside 1.." + BallPitWorldSave.MaxColors;
            if (!InRange(ColliderRadiusScale, 0.9f, 1f)) return "colliderRadiusScale " + ColliderRadiusScale + " outside 0.9..1";
            if (!InRange(Jitter, 0f, 1f)) return "jitter " + Jitter + " outside 0..1";
            if (!InRange(MaxSupportGapDegrees, 120f, 180f))
                return "maxSupportGapDegrees " + MaxSupportGapDegrees + " outside 120..180";
            if (!Finite(BoundsCenter.x) || !Finite(BoundsCenter.y) || !Finite(BoundsCenter.z))
                return "play bounds center " + BoundsCenter + " is not finite";
            if (!Positive(BoundsSize.x) || !Positive(BoundsSize.y) || !Positive(BoundsSize.z))
                return "play bounds size " + BoundsSize + " is not finite and positive";
            if (BallCount != n) return "ball count " + BallCount + " != pitX * pitY * pitZ = " + n;
            // A resume builds with these bounds directly (never through ComputeBounds): SpatialGrid's cell index is an int.
            double cells = PitWorld.GridCellCount(BoundsSize, BallDiameter);
            if (cells > PitWorld.MaxGridCells)
                return "play bounds " + BoundsSize + " at diameter " + BallDiameter + " need " + cells.ToString("0", System.Globalization.CultureInfo.InvariantCulture) +
                       " grid cells, above the grid's int index";
            if (!Finite(PitAnchor.x) || !Finite(PitAnchor.y) || !Finite(PitAnchor.z) ||
                Mathf.Abs(PitAnchor.x) > MaxAnchorCoordinate || Mathf.Abs(PitAnchor.y) > MaxAnchorCoordinate || Mathf.Abs(PitAnchor.z) > MaxAnchorCoordinate)
                return "pit anchor " + PitAnchor + " is not finite or outside +-" + MaxAnchorCoordinate + " m";
            if (SiteId != null && !BallPitGameState.IsValidKey(SiteId, BallPitGameState.MaxTagLength))
                return "site id '" + SiteId + "' is not 1.." + BallPitGameState.MaxTagLength + " characters of [a-z0-9_.]";
            return null;
        }

        /// <summary>Largest saved anchor coordinate (m); equal to PitSite.MaxAnchorCoordinate.</summary>
        public const float MaxAnchorCoordinate = 10000f;

        private static bool InRange(float v, float min, float max) => v >= min && v <= max; // false for NaN
        private static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
        private static bool Positive(float v) => v > 0f && !float.IsInfinity(v);       // false for NaN
    }

    /// <summary>Section HOPR: one tank's contents, keyed by PlayerId (04 §3.2, §5.4).</summary>
    public sealed class HopperEntry
    {
        /// <summary>Bit 0: the saving host's own tank (at most one entry). Bit 1: parked at save time. Others must be 0.</summary>
        public const byte FlagHost = 1, FlagParked = 2;

        public ulong PlayerId;         // 0 = orphaned contents, merged into the host's tank on load
        public byte Flags;
        public long LastSeenUtcTicks;  // when that player was last connected; the host: save time
        public int[] Ids;              // hopper-list order: index 0 entered first, the last id is the next one Dump pours
    }

    /// <summary>
    /// One decoded or captured ball-pit world save. Records are parallel arrays (three arrays instead of one object per
    /// ball for a 2.35M-ball pit), in ModifiedAt order.
    /// </summary>
    public sealed class BallPitWorldSave
    {
        public const ushort CurrentVersion = 2; // 2: FILL carries the pit anchor and the site id (deliverable A)

        // Format constants shared by the writer and the reader (04 §3.1-3.2).
        public const string Magic = "BPWS";              // header bytes 42 50 57 53
        public const byte CompressionDeflate = 1;        // raw Deflate (DeflateStream, no zlib header)
        public const int HeaderLength = 12;
        public const int MaxBodyLength = 536870912;      // inflated body, 512 MiB
        public const int MaxPitAxis = 1024;
        public const int MaxBallCount = 16777215;        // NetPit encodes ball ids in 3 bytes
        public const int MaxColors = 16;
        public const int MaxHopperEntries = 64;
        public const string TagFill = "FILL", TagSess = "SESS", TagLedg = "LEDG", TagBall = "BALL",
                            TagHopr = "HOPR", TagSupq = "SUPQ", TagGame = "GAME", TagEnd = "BPND";

        public ushort Version = CurrentVersion;

        public PitFillParams Fill;                       // FILL

        public ulong HostPlayer;                         // SESS: host PlayerId.Value at save time (0: offline dev save)
        public long SavedUtcTicks;
        public int StepIndex;                            // PitAuthority.StepIndex, diagnostic only
        public byte NetProtocol;                         // NetPit.Protocol, diagnostic only

        /// <summary>LEDG: BallStore.CountOf at [state * LedgerColors + color]; the Held row is 0 (folded into Active).</summary>
        public byte LedgerStates, LedgerColors;
        public int[] Ledger;

        /// <summary>BALL: every ball in PitWorld's modified-since-fill list.</summary>
        public int RecordCount;
        public int[] RecordIds;
        public byte[] RecordStates;                      // (byte)BallStore.State, never 2: Held balls are saved as Active
        public ulong[] RecordPoses;                      // Quantizer Q21 for 0 Resting / 1 Active; 0 for the other states

        public readonly List<HopperEntry> Hoppers = new List<HopperEntry>(); // HOPR

        /// <summary>SUPQ: ball ids with a pending support test, ascending. May include unmodified fill balls.</summary>
        public int[] PendingSupport;

        /// <summary>
        /// GAME. Here UnclaimedExtensions holds every extension blob of the save (participant captures and blobs carried
        /// over from an earlier load); the restorer hands each one to its registered participant or keeps it unclaimed.
        /// </summary>
        public BallPitGameState Game = new BallPitGameState();
    }
}

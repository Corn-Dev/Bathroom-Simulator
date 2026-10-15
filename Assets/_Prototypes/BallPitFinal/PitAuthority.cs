using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

namespace BallPitFinal
{
    /// <summary>Everything a replica needs to mirror the host. All poses are Q21 values (exact on every peer).</summary>
    public interface IPitEvents
    {
        void Thawed(int id);                              // Resting -> Active, starting at its resting pose
        void Froze(int id, ulong q);                      // -> Resting at pose q
        void Gone(int id, BallStore.State s, int player); // -> Stored / Lost / Hopper(player)
        void Spawned(int id, ulong q);                    // -> Active at pose q (dump from a hopper)
        void Held(int id, int player, ulong q);           // -> Held by player (from Resting or Active)
        void Released(int id);                            // Held -> Active (thrown)
    }

    /// <summary>
    /// Host-only rules: thaw/freeze, support-loss collapse, impacts, tools, budget. Owns the active pool.
    /// Every transition goes through PitWorld.ToResting/ToLive/ToGone and is reported to Events.
    ///
    /// Collapse model (milestone 3):
    ///  * Budget = concurrent rigidbodies only (maxActive, optionally capped adaptively by measured Physics.Simulate
    ///    time). The per-step thaw/check caps are frame-cost smoothing, sized so they never set the collapse speed.
    ///  * The support queue is regional: XZ columns drained round-robin, lowest-first inside each column, so every
    ///    stretch of a long tunnel collapses at the same time.
    ///  * Free-fall cascade: a ball that loses support over an open drop is flagged falling; falling live balls are not
    ///    supports, and its dependents are re-tested in the same step. A whole roof over a cavity drops together
    ///    instead of one layer per motion event; a vacancy inside the pile (no open drop) still propagates through
    ///    real motion only, so a single grab does not thaw the cone above it.
    ///  * Freeze is jitter-robust (slow for freezeSeconds) and slow balls get settle drag, so the active set drains.
    ///  * Interaction: grab/vacuum accept moving balls (the same body is handed to the hand / removed).
    /// </summary>
    public sealed partial class PitAuthority : System.IDisposable
    {
        public const int MaxPlayers = 4;

        public readonly PitWorld W;
        public readonly PitSettings S;
        public readonly BallStore Store;
        public readonly ActivePhysics Active;
        public IPitEvents Events;

        // Optional gameplay hooks (host only). Each defaults to null = today's behavior; a null hook costs one check.

        /// <summary>
        /// Optional host grab validation (player, ball id), checked first in TryGrab, before its own checks; false
        /// rejects the grab silently. NetPit's intent handler calls TryGrab for client grabs, so it must not throw.
        /// </summary>
        public System.Func<int, int, bool> GrabFilter;

        /// <summary>
        /// Optional gameplay hook, host only. Default null = today's behavior, bit for bit. FreezeBottomUp and
        /// PressureFreeze call it just before a live ball would freeze, with the ball id and its post-step pose.
        /// Return true to keep the ball Active this step. ActivePhysics.PostStep makes it a candidate again next
        /// step, so it is simply asked again. Must be cheap, must not throw, must not change pit state.
        /// </summary>
        public System.Func<int, Vector3, bool> FreezeBlocker;

        /// <summary>Optional. Consulted at the top of Excavate and Dump, right after the range check; false makes the call
        /// return 0. Null (default) = unchanged behavior.</summary>
        public System.Func<int, bool> ToolFilter;

        /// <summary>
        /// Optional gameplay hook, host only (deliverable B). Default null = today's behavior, bit for bit. Consulted at
        /// the TOP of Lose(id): returning true means "the game handled this ball" (it routed it back into the pit), so
        /// Lose does NOT mark it Lost. The economy sets it to send strays through ReturnToPit instead of blocking the
        /// win with Lost balls (game-design §10). Must be cheap and must not throw.
        /// </summary>
        public System.Func<int, bool> LossHook;

        public int StepIndex { get; private set; }
        public readonly int[] HeldId = { -1, -1, -1, -1 };
        public readonly Vector3[] HandTarget = new Vector3[MaxPlayers];
        public int QueueLength => queue.Count;
        public int QueueRegions => queue.ActiveRegions;
        public int HopperCount(int player) => hopper[player].Count;
        public IReadOnlyList<int> Hopper(int player) => hopper[player];

        // Per-step stats
        public int StatSupportThaws, StatImpactThaws, StatFreezes, StatMotionEvents, StatSupportChecks, StatFreeFall, StatRegionsVisited;
        public int StatPressureFreezes;
        /// <summary>Consecutive steps where thawing was blocked and almost nothing froze (the milestone-3 deadlock signature).</summary>
        public int StallSteps { get; private set; }
        public int TotalPressureFreezes;
        public bool StatBudgetBlocked, StatAdaptiveBlocked, StatTimeBudgetHit;
        public double LastSimulateMs, LastStepMs, LastDrainMs;
        public float SimMsEma { get; private set; }
        // Cumulative (reset with ResetCounters)
        public int TotalLost, TotalThaws, TotalFreezes, TotalExcavated, TotalSpawned, HangMaxSteps, HangCount;
        public long HangSumSteps;

        private readonly SupportQueue queue;
        private readonly int[] fallingStep;
        private int fallingMarkSteps = 10;
        private readonly List<int>[] hopper = new List<int>[MaxPlayers];
        private int parkedCount; // slot 0's protected prefix: absent players' parked tanks (PitAuthority.Restore.cs)
        private readonly List<int> scratchA = new List<int>(512), scratchB = new List<int>(256), scratchC = new List<int>(256);
        private readonly List<int> movedIds = new List<int>(4096), freezeCands = new List<int>(4096), lostIds = new List<int>(64);
        private readonly List<Vector3> movedFrom = new List<Vector3>(4096), removedFrom = new List<Vector3>(1024);
        private readonly List<int> removedIds = new List<int>(1024);
        private readonly List<int> pressureCands = new List<int>(4096);
        private readonly List<Vector4> toolSources = new List<Vector4>(8);
        private readonly float[] angles = new float[40];
        private float[] sortKeys = new float[1024];
        private int[] sortIds = new int[1024];
        private readonly Collider[] envHits = new Collider[16];
        private readonly SphereCollider probe;
        private readonly HeightComparer heightComparer;
        private readonly System.Random rng;
        private readonly Stopwatch sw = new Stopwatch();

        private float ContactDist => W.ColliderRadius * 2f * 1.04f + 0.01f;
        private float SupportDy => W.ColliderRadius * 0.1f;

        /// <summary>Concurrent-active cap for collapse thaws this step (hard cap minus reserve, paused while sim is over target).</summary>
        public int EffectiveCollapseLimit
        {
            get
            {
                int hard = Mathf.Max(1, S.maxActive - S.collapseReserve);
                if (S.targetSimulateMs > 0f && SimMsEma > S.targetSimulateMs && Active.Count >= S.minActive)
                    return Mathf.Min(hard, Active.Count); // no growth while physics is over budget
                return hard;
            }
        }

        public PitAuthority(PitWorld world)
        {
            W = world; S = world.S; Store = world.Store;
            if (W.Colliders == null) throw new System.ArgumentException("PitAuthority needs a PitWorld created withColliders = true");
            Active = new ActivePhysics(Store.Capacity, S.maxActive, Mathf.Min(S.activePrewarm, S.maxActive), W.ColliderRadius, W.Material,
                                       S.linearDrag, S.angularDrag, S.maxDepenetrationVelocity, S.sleepThreshold,
                                       S.ballLayer, S.solverIterations);
            queue = new SupportQueue(W.PlayBounds, S.regionSizeDiameters * W.Diameter, Store.Capacity);
            fallingStep = new int[Store.Capacity];
            for (int i = 0; i < fallingStep.Length; i++) fallingStep[i] = int.MinValue / 2;
            for (int p = 0; p < MaxPlayers; p++) hopper[p] = new List<int>(1024);
            heightComparer = new HeightComparer(Store);
            rng = new System.Random(S.seed ^ 0x5bd1e995);

            // Probe for environment support tests (ComputePenetration needs a collider). Far away, ball layer, trigger.
            var go = new GameObject("BallPitFinal_SupportProbe");
            go.layer = S.ballLayer;
            go.transform.position = new Vector3(0f, -10000f, 0f);
            probe = go.AddComponent<SphereCollider>();
            probe.isTrigger = true;
            probe.radius = W.ColliderRadius + W.Diameter * 0.03f;
        }

        public void ResetCounters()
        {
            TotalLost = TotalThaws = TotalFreezes = TotalExcavated = TotalSpawned = HangMaxSteps = HangCount = TotalPressureFreezes = 0;
            HangSumSteps = 0;
            W.Colliders.ResetCapHits();
        }

        // ------------------------------------------------------------------ Interaction (resting AND moving balls)

        /// <summary>
        /// Any ball a player may target: resting, or moving (Active). Held balls are excluded unless stealHeldBalls.
        /// Every player tool should go through this (or TryGrab) so the "moving balls are interactable" rule holds.
        /// </summary>
        public bool IsInteractable(int id)
        {
            if ((uint)id >= (uint)Store.Count) return false;
            switch (Store.state[id])
            {
                case BallStore.State.Resting: return true;
                case BallStore.State.Active: return true;
                case BallStore.State.Held: return S.stealHeldBalls;
                default: return false;
            }
        }

        /// <summary>Resting + moving balls within radius of center (exact distance test).</summary>
        public void GatherInteractable(Vector3 center, float radius, List<int> results, bool includeActive)
        {
            W.Resting.Gather(center, radius, Store.pos, results);
            if (!includeActive) return;
            W.Live.Gather(center, radius, Store.pos, scratchC);
            for (int i = 0; i < scratchC.Count; i++)
            {
                int id = scratchC[i];
                if (Store.state[id] == BallStore.State.Active) results.Add(id);
            }
        }

        public bool TryGrab(int player, int id)
        {
            if (GrabFilter != null && !GrabFilter(player, id)) return false;
            if ((uint)player >= MaxPlayers || HeldId[player] >= 0) return false;
            if ((uint)id >= (uint)Store.Count) return false;
            BallStore.State st = Store.state[id];
            Vector3 p = Store.pos[id];
            if (st == BallStore.State.Resting)
            {
                if (!Thaw(id, Vector3.zero, player, collapse: false)) return false;
            }
            else if (st == BallStore.State.Active && S.grabActiveBalls)
            {
                Active.SetCarried(id);                       // the moving body itself goes to the hand
                W.ToLive(id, BallStore.State.Held, p);
                Events?.Held(id, player, W.Q.Encode(p));
            }
            else if (st == BallStore.State.Held && S.stealHeldBalls)
            {
                for (int q = 0; q < MaxPlayers; q++) if (HeldId[q] == id) HeldId[q] = -1;
                Events?.Held(id, player, W.Q.Encode(p));
            }
            else return false;
            HeldId[player] = id;
            QueueDependents(p, id);
            return true;
        }

        public void ThrowHeld(int player, Vector3 velocity)
        {
            if ((uint)player >= MaxPlayers) return;
            int id = HeldId[player];
            if (id < 0) return;
            HeldId[player] = -1;
            Active.Release(id, velocity);
            W.ToLive(id, BallStore.State.Active, Store.pos[id]);
            Events?.Released(id);
        }

        /// <summary>
        /// Vacuum / undermine: removes up to max interactable balls inside the sphere (resting and, if vacuumActiveBalls,
        /// moving), nearest first, into the player's hopper as DATA, then queues everything that rested on them.
        /// </summary>
        public int Excavate(int player, Vector3 center, float radius, int max)
        {
            if ((uint)player >= MaxPlayers || max <= 0) return 0;
            if (ToolFilter != null && !ToolFilter(player)) return 0;
            GatherInteractable(center, radius, scratchA, S.vacuumActiveBalls);
            int n = scratchA.Count;
            if (n == 0) return 0;
            if (sortKeys.Length < n) { sortKeys = new float[Mathf.NextPowerOfTwo(n)]; sortIds = new int[sortKeys.Length]; }
            for (int i = 0; i < n; i++) { int id = scratchA[i]; sortIds[i] = id; sortKeys[i] = (Store.pos[id] - center).sqrMagnitude; }
            System.Array.Sort(sortKeys, sortIds, 0, n);
            int take = Mathf.Min(n, max);
            removedIds.Clear(); removedFrom.Clear();
            for (int i = 0; i < take; i++)
            {
                int id = sortIds[i];
                removedIds.Add(id); removedFrom.Add(Store.pos[id]);
                if (Active.IsActive(id)) Active.Demote(id);
                W.ToGone(id, BallStore.State.Hopper);
                hopper[player].Add(id);
                Events?.Gone(id, BallStore.State.Hopper, player);
            }
            for (int i = 0; i < removedIds.Count; i++) QueueDependents(removedFrom[i], removedIds[i]);
            TotalExcavated += take;
            return take;
        }

        /// <summary>Pour up to max balls from the player's hopper at point (they fall and pile with real physics).</summary>
        public int Dump(int player, Vector3 point, int max)
        {
            if ((uint)player >= MaxPlayers) return 0;
            if (ToolFilter != null && !ToolFilter(player)) return 0;
            var h = hopper[player];
            int n = 0;
            float clear = W.ColliderRadius * 2.02f;
            Active.MaxActive = S.maxActive;
            while (n < max && h.Count > (player == 0 ? parkedCount : 0) && Active.CanPromote)
            {
                Vector3 p = default;
                bool placed = false;
                for (int attempt = 0; attempt < 4 && !placed; attempt++)
                {
                    float ang = (float)rng.NextDouble() * Mathf.PI * 2f;
                    float rad = Mathf.Sqrt((float)rng.NextDouble()) * S.dumpSpread;
                    p = W.Q.Snap(point + new Vector3(Mathf.Cos(ang) * rad, 0f, Mathf.Sin(ang) * rad));
                    W.Resting.Gather(p, clear, Store.pos, scratchB);
                    if (scratchB.Count > 0) continue;
                    W.Live.Gather(p, clear, Store.pos, scratchB);
                    placed = scratchB.Count == 0;
                }
                if (!placed) break; // spout is blocked this step
                int id = h[h.Count - 1];
                h.RemoveAt(h.Count - 1);
                if (!Active.Promote(id, p, new Vector3(0f, -1f, 0f), carried: false)) { h.Add(id); break; }
                W.ToLive(id, BallStore.State.Active, p);
                Events?.Spawned(id, W.Q.Encode(p));
                n++;
            }
            TotalSpawned += n;
            return n;
        }

        /// <summary>A ball enters a sorting container: leaves play, ledger moves it to Stored.</summary>
        public void Deposit(int id)
        {
            if (Active.IsActive(id)) Active.Demote(id);
            for (int p = 0; p < MaxPlayers; p++) if (HeldId[p] == id) HeldId[p] = -1;
            W.ToGone(id, BallStore.State.Stored);
            Events?.Gone(id, BallStore.State.Stored, -1);
        }

        /// <summary>Extra collider sources for this step (cursors, hands, nozzles). Cleared after each Step.</summary>
        public void AddToolSource(Vector3 p, float radius) => toolSources.Add(new Vector4(p.x, p.y, p.z, radius));

        // ------------------------------------------------------------------ Step

        public void Step(float dt)
        {
            sw.Restart();
            StepIndex++;
            StatSupportThaws = StatImpactThaws = StatFreezes = StatMotionEvents = StatSupportChecks = StatFreeFall = StatRegionsVisited = 0;
            StatPressureFreezes = 0;
            StatBudgetBlocked = StatAdaptiveBlocked = StatTimeBudgetHit = false;

            // live tuning
            Active.MaxActive = S.maxActive;
            Active.LinearDrag = S.linearDrag; Active.AngularDrag = S.angularDrag;
            Active.SettleSpeed = S.settleSpeed; Active.SettleDrag = S.settleDrag; Active.SettleAngularDrag = S.settleAngularDrag;
            Active.SpeedSmoothingSeconds = S.speedSmoothingSeconds;
            fallingMarkSteps = Mathf.Max(1, Mathf.RoundToInt(S.fallingMarkSeconds / dt));

            for (int p = 0; p < MaxPlayers; p++) if (HeldId[p] >= 0) Active.MoveHeld(HeldId[p], HandTarget[p]);
            ThawAheadOfFastMovers(dt);
            DrainSupportQueue();
            RefreshColliders(dt);

            Physics.SyncTransforms();
            long t0 = Stopwatch.GetTimestamp();
            Physics.Simulate(dt);
            LastSimulateMs = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
            SimMsEma = SimMsEma <= 0f ? (float)LastSimulateMs : Mathf.Lerp(SimMsEma, (float)LastSimulateMs, 0.1f);

            movedIds.Clear(); movedFrom.Clear(); freezeCands.Clear(); lostIds.Clear();
            Active.PostStep(dt, W.VisualRadius * S.thawMoveFraction, S.freezeSpeed, S.freezeSeconds,
                            W.Diameter * S.freezeMaxDriftFraction, Store, W.Live, movedIds, movedFrom, freezeCands);
            StatMotionEvents = movedIds.Count;
            for (int i = 0; i < movedIds.Count; i++) QueueDependents(movedFrom[i], movedIds[i]);

            FreezeBottomUp();

            // Stall breaker: the adaptive cap only throttles THAWING; demotion must always make progress. If thawing is
            // blocked (budget or physics over target) while almost nothing freezes, force-freeze the slowest supported balls.
            bool pressured = (StatBudgetBlocked && queue.Count > 0) ||
                             (S.targetSimulateMs > 0f && SimMsEma > S.targetSimulateMs && Active.Count >= S.minActive);
            if (pressured && StatFreezes < S.stallMinFreezesPerStep) StallSteps++; else StallSteps = 0;
            if (StallSteps >= S.stallBreakSteps) PressureFreeze();

            float killY = W.KillY; // pit floor + S.killY (== S.killY with a zero anchor)
            for (int i = 0; i < Active.Count; i++) if (Active.PosAt(i).y < killY) lostIds.Add(Active.IdAt(i));
            for (int i = 0; i < lostIds.Count; i++) Lose(lostIds[i]);

            int keepSteps = Mathf.Max(1, Mathf.RoundToInt(S.colliderKeepSeconds / dt));
            W.Colliders.Expire(StepIndex, keepSteps, S.colliderReleasePerStep);
            toolSources.Clear();
            LastStepMs = sw.Elapsed.TotalMilliseconds;
        }

        // ------------------------------------------------------------------ Transitions

        private bool Thaw(int id, Vector3 velocity, int carriedBy, bool collapse)
        {
            if (Store.state[id] != BallStore.State.Resting) return false;
            if (collapse ? !Active.CanPromoteWithin(EffectiveCollapseLimit) : !Active.CanPromote) return false;
            Vector3 p = Store.pos[id];
            bool carried = carriedBy >= 0;
            if (!Active.Promote(id, p, velocity, carried)) return false;
            W.ToLive(id, carried ? BallStore.State.Held : BallStore.State.Active, p);
            if (carried) Events?.Held(id, carriedBy, W.Q.Encode(p)); else Events?.Thawed(id);
            TotalThaws++;
            return true;
        }

        private void Freeze(int id)
        {
            Vector3 pose = W.Q.Snap(Active.Demote(id)); // exact rest pose, snapped once (<0.1 mm) so every peer agrees
            W.ToResting(id, pose);
            W.Colliders.Ensure(id, pose, StepIndex);  // never leave a gap under balls resting on it
            Events?.Froze(id, W.Q.Encode(pose));
            StatFreezes++; TotalFreezes++;
        }

        private void Lose(int id)
        {
            if (LossHook != null && LossHook(id)) return; // deliverable B: the game returned it to the pit; do not mark Lost
            if (Active.IsActive(id)) Active.Demote(id);
            for (int p = 0; p < MaxPlayers; p++) if (HeldId[p] == id) HeldId[p] = -1;
            W.ToGone(id, BallStore.State.Lost);
            Events?.Gone(id, BallStore.State.Lost, -1);
            TotalLost++;
        }

        // ------------------------------------------------------------------ Rules

        private bool IsFalling(int id) =>
            StepIndex - fallingStep[id] <= fallingMarkSteps || Active.VelocityOf(id).y < -S.fallingSpeed;

        /// <summary>Queue resting balls that were resting on a ball at 'at' (in contact and above its center).</summary>
        private void QueueDependents(Vector3 at, int exclude)
        {
            W.Resting.Gather(at, ContactDist, Store.pos, scratchB, exclude);
            float dy = SupportDy;
            for (int i = 0; i < scratchB.Count; i++)
            {
                int n = scratchB[i];
                if (Store.pos[n].y - at.y > dy) queue.Push(n, Store.pos[n], StepIndex);
            }
        }

        /// <summary>
        /// Round-robin over collapse regions; each region pops lowest-first up to a fair share per visit. Stops on the
        /// concurrent-active budget (the only real limit), or on the per-step smoothing caps / time budget.
        /// </summary>
        private void DrainSupportQueue()
        {
            if (queue.Count == 0) return;
            long start = Stopwatch.GetTimestamp();
            long deadline = start + (long)(S.collapseRulesBudgetMs * 0.001 * Stopwatch.Frequency);
            int thawBudget = S.supportThawsPerStep, checks = S.maxSupportChecksPerStep;
            int limit = EffectiveCollapseLimit;
            if (limit < S.maxActive - S.collapseReserve) StatAdaptiveBlocked = true;
            float freeFall = S.freeFallDiameters * W.Diameter;

            while (queue.Count > 0 && thawBudget > 0 && checks > 0)
            {
                int regions = queue.ActiveRegions;
                if (regions == 0) break;
                int share = Mathf.Max(S.minRegionShare, thawBudget / regions);
                bool progress = false;
                for (int r = 0; r < regions && thawBudget > 0 && checks > 0; r++)
                {
                    int region = queue.NextRegion();
                    if (region < 0) break;
                    StatRegionsVisited++;
                    int thawedHere = 0;
                    while (thawedHere < share && checks > 0 && thawBudget > 0)
                    {
                        if (!Active.CanPromoteWithin(limit)) { StatBudgetBlocked = true; goto Done; }
                        if ((checks & 31) == 0 && Stopwatch.GetTimestamp() > deadline) { StatTimeBudgetHit = true; goto Done; }
                        if (!queue.TryPop(region, out int id, out int queuedAt)) break;
                        progress = true;
                        if (Store.state[id] != BallStore.State.Resting) continue;
                        checks--; StatSupportChecks++;
                        if (IsSupported(id)) continue;
                        Vector3 p = Store.pos[id];
                        if (!Thaw(id, Vector3.zero, -1, collapse: true)) { queue.Push(id, p, queuedAt); goto Done; }
                        thawedHere++; thawBudget--; StatSupportThaws++;
                        int hang = StepIndex - queuedAt;
                        HangCount++; HangSumSteps += hang;
                        if (hang > HangMaxSteps) HangMaxSteps = hang;

                        // Free-fall cascade: open drop below => it cannot support anything; re-test its dependents now.
                        if (DropDistance(p, id, freeFall) >= freeFall)
                        {
                            fallingStep[id] = StepIndex;
                            StatFreeFall++;
                            QueueDependents(p, id);
                        }
                    }
                }
                if (!progress) break;
            }
            Done:
            LastDrainMs = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
        }

        /// <summary>
        /// How far a ball could drop straight down before touching something that will hold it (resting balls,
        /// non-falling live balls, the floor, environment), capped at maxDrop.
        /// </summary>
        private float DropDistance(Vector3 p, int self, float maxDrop)
        {
            float rc = W.ColliderRadius, d = 2f * rc, d2 = d * d;
            float best = Mathf.Min(maxDrop, p.y - rc - W.FloorY);
            if (best <= 0f) return 0f;
            if (W.NearEnvironment(p + Vector3.down * (best * 0.5f), rc + best * 0.5f) &&
                Physics.SphereCast(p, rc * 0.98f, Vector3.down, out RaycastHit hit, best, W.EnvMask, QueryTriggerInteraction.Ignore))
                best = Mathf.Min(best, hit.distance);

            for (int pass = 0; pass < 2 && best > 0f; pass++)
            {
                SpatialGrid g = pass == 0 ? W.Resting : W.Live;
                int cx = g.CellX(p.x), cz = g.CellZ(p.z), cyTop = g.CellY(p.y), cyBot = g.CellY(p.y - best - d);
                for (int cy = cyTop; cy >= cyBot; cy--)
                for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int x = cx + dx, z = cz + dz;
                    if (x < 0 || z < 0 || x >= g.Nx || z >= g.Nz) continue;
                    for (int q = g.Head(x, cy, z); q >= 0; q = g.Next(q))
                    {
                        if (q == self) continue;
                        Vector3 o = Store.pos[q];
                        if (o.y >= p.y) continue;
                        float hx = o.x - p.x, hz = o.z - p.z, h2 = hx * hx + hz * hz;
                        if (h2 >= d2) continue;
                        if (pass == 1 && (Store.state[q] != BallStore.State.Active || IsFalling(q))) continue;
                        float drop = p.y - (o.y + Mathf.Sqrt(d2 - h2));
                        if (drop < best) best = Mathf.Max(0f, drop);
                    }
                }
            }
            return best;
        }

        /// <summary>
        /// Frictionless static stability: the horizontal directions to the supporting contacts (balls below the center
        /// that will hold, any environment collider) must surround the center (no angular gap above
        /// maxSupportGapDegrees), with at least one support below. Falling or held live balls are not supports.
        /// Errs toward "unsupported": a needless thaw just re-freezes at the same pose.
        /// </summary>
        private bool IsSupported(int id)
        {
            Vector3 p = Store.pos[id];
            float dy = SupportDy, cd = ContactDist;
            int na = 0, below = 0;

            if (W.NearEnvironment(p, probe.radius))
            {
                int ne = Physics.OverlapSphereNonAlloc(p, probe.radius, envHits, W.EnvMask, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < ne; i++)
                {
                    Collider c = envHits[i];
                    Transform t = c.transform;
                    if (!Physics.ComputePenetration(probe, p, Quaternion.identity, c, t.position, t.rotation, out Vector3 dir, out _)) continue;
                    Vector3 toContact = -dir;
                    if (toContact.y < -0.95f) return true; // flat ground directly underneath
                    if (toContact.y < 0.3f && na < angles.Length)
                    {
                        if (toContact.y < -0.1f) below++;
                        angles[na++] = Mathf.Atan2(toContact.z, toContact.x);
                    }
                }
            }

            W.Resting.Gather(p, cd, Store.pos, scratchC, id);
            for (int i = 0; i < scratchC.Count && na < angles.Length; i++)
            {
                Vector3 d = Store.pos[scratchC[i]] - p;
                if (d.y < -dy) { below++; if (AddAngle(d, ref na)) return true; }
            }
            W.Live.Gather(p, cd, Store.pos, scratchC, id);
            for (int i = 0; i < scratchC.Count && na < angles.Length; i++)
            {
                int q = scratchC[i];
                if (Store.state[q] != BallStore.State.Active || IsFalling(q)) continue;
                Vector3 d = Store.pos[q] - p;
                if (d.y < -dy) { below++; if (AddAngle(d, ref na)) return true; }
            }
            if (below == 0 || na < 2) return false;

            for (int i = 1; i < na; i++)
            {
                float v = angles[i]; int k = i - 1;
                while (k >= 0 && angles[k] > v) { angles[k + 1] = angles[k]; k--; }
                angles[k + 1] = v;
            }
            float maxGap = angles[0] + 2f * Mathf.PI - angles[na - 1];
            for (int i = 1; i < na; i++) maxGap = Mathf.Max(maxGap, angles[i] - angles[i - 1]);
            return maxGap < S.maxSupportGapDegrees * Mathf.Deg2Rad;
        }

        /// <returns>true if this contact is directly underneath (full support on its own).</returns>
        private bool AddAngle(Vector3 d, ref int na)
        {
            float h2 = d.x * d.x + d.z * d.z;
            float lim = W.ColliderRadius * 0.05f;
            if (h2 < lim * lim) return true;
            angles[na++] = Mathf.Atan2(d.z, d.x);
            return false;
        }

        /// <summary>
        /// Thaw resting balls in the swept path of fast, ISOLATED active balls (throws, dumps) so impacts are
        /// dynamic-vs-dynamic. Balls inside a mass flow are skipped (a cave-in landing on the crater floor does not need
        /// the floor to move).
        /// </summary>
        private void ThawAheadOfFastMovers(float dt)
        {
            int budget = S.impactThawsPerStep;
            int n = Active.Count;
            float fast2 = S.fastSpeed * S.fastSpeed;
            for (int i = 0; i < n && budget > 0; i++)
            {
                if (Active.CarriedAt(i)) continue;
                Vector3 v = Active.VelAt(i);
                if (v.sqrMagnitude < fast2) continue;
                Vector3 p = Active.PosAt(i);
                W.Live.Gather(p, W.Diameter * 1.2f, Store.pos, scratchB, Active.IdAt(i));
                if (scratchB.Count > S.impactMaxNeighbors) continue;
                W.Resting.Gather(p + v * dt, ContactDist, Store.pos, scratchA);
                for (int k = 0; k < scratchA.Count && budget > 0; k++)
                {
                    int r = scratchA[k];
                    if (Vector3.Dot(Store.pos[r] - p, v) <= 0f) continue;
                    if (!Thaw(r, Vector3.zero, -1, collapse: true)) { budget = 0; break; }
                    budget--; StatImpactThaws++;
                }
            }
        }

        private void RefreshColliders(float dt)
        {
            int step = StepIndex;
            int maxAge = Mathf.Max(1, Mathf.RoundToInt(S.colliderKeepSeconds / dt) / 2);
            float minMove = W.Diameter * 0.25f, minMove2 = minMove * minMove;
            float reach = W.Diameter * S.colliderReachDiameters;
            for (int i = 0; i < Active.Count; i++)
            {
                if (Active.CarriedAt(i)) continue;
                if (!Active.NeedsSourceRefresh(i, step, minMove2, maxAge)) continue;
                Vector3 p = Active.PosAt(i);
                float r = reach + Active.VelAt(i).magnitude * dt * 2f + minMove;
                W.Resting.Gather(p, r, Store.pos, scratchA);
                for (int k = 0; k < scratchA.Count; k++) W.Colliders.Ensure(scratchA[k], Store.pos[scratchA[k]], step);
                Active.MarkSourced(i, step);
            }
            for (int s = 0; s < toolSources.Count; s++)
            {
                Vector4 t = toolSources[s];
                W.Resting.Gather(new Vector3(t.x, t.y, t.z), t.w, Store.pos, scratchA);
                for (int k = 0; k < scratchA.Count; k++) W.Colliders.Ensure(scratchA[k], Store.pos[scratchA[k]], step);
            }
        }

        /// <summary>Freeze candidates bottom-up; a ball waits while a live ball below it is still moving.</summary>
        private void FreezeBottomUp()
        {
            if (freezeCands.Count == 0) return;
            freezeCands.Sort(heightComparer);
            float dy = SupportDy, cd = ContactDist;
            for (int i = 0; i < freezeCands.Count; i++)
            {
                int id = freezeCands[i];
                if (Store.state[id] != BallStore.State.Active) continue;
                Vector3 p = Store.pos[id];
                W.Live.Gather(p, cd, Store.pos, scratchA, id);
                bool liveBelow = false;
                for (int k = 0; k < scratchA.Count; k++)
                    if (Store.pos[scratchA[k]].y < p.y - dy) { liveBelow = true; break; }
                if (liveBelow) continue; // something under it is still live; it freezes later (same step if that one froze first)
                if (FreezeBlocker != null && FreezeBlocker(id, p)) continue; // gameplay hook (touching a player): stays Active
                Freeze(id);
            }
        }

        /// <summary>
        /// Forced demotion under pressure: slowest balls (smoothed speed below pressureFreezeSpeed), bottom-up, skipping
        /// any ball with a fast or held live ball under it. Freezes at the exact pose like a normal freeze; a ball frozen
        /// over a still-settling neighbour is re-queued by that neighbour's next motion event, so nothing ends up floating.
        /// </summary>
        private void PressureFreeze()
        {
            pressureCands.Clear();
            float ps = S.pressureFreezeSpeed;
            for (int i = 0; i < Active.Count; i++)
            {
                if (Active.CarriedAt(i)) continue;
                if (Active.SmoothedSpeedAt(i) < ps) pressureCands.Add(Active.IdAt(i));
            }
            if (pressureCands.Count == 0) return;
            pressureCands.Sort(heightComparer);
            int budget = S.pressureFreezePerStep;
            float dy = SupportDy, cd = ContactDist;
            for (int k = 0; k < pressureCands.Count && budget > 0; k++)
            {
                int id = pressureCands[k];
                if (Store.state[id] != BallStore.State.Active) continue;
                Vector3 p = Store.pos[id];
                W.Live.Gather(p, cd, Store.pos, scratchA, id);
                bool unsafeBelow = false;
                for (int j = 0; j < scratchA.Count; j++)
                {
                    int q = scratchA[j];
                    if (Store.pos[q].y >= p.y - dy) continue;
                    if (Store.state[q] == BallStore.State.Held || Active.SmoothedSpeedOf(q) >= ps) { unsafeBelow = true; break; }
                }
                if (unsafeBelow) continue;
                if (FreezeBlocker != null && FreezeBlocker(id, p)) continue; // gameplay hook: a blocked ball uses no budget
                Freeze(id);
                budget--; StatPressureFreezes++; TotalPressureFreezes++;
            }
        }

        public bool VerifyAll(out string error)
        {
            if (!W.Verify(out error)) return false;
            int live = Store.StateTotal(BallStore.State.Active) + Store.StateTotal(BallStore.State.Held);
            if (Active.Count != live) { error = $"active pool {Active.Count} vs ledger {live}"; return false; }
            int hop = 0;
            for (int p = 0; p < MaxPlayers; p++) hop += hopper[p].Count;
            if (hop != Store.StateTotal(BallStore.State.Hopper)) { error = $"hoppers {hop} vs ledger {Store.StateTotal(BallStore.State.Hopper)}"; return false; }
            return true;
        }

        public void Dispose()
        {
            Active.Dispose();
            if (probe != null) Object.Destroy(probe.gameObject);
        }

        private sealed class HeightComparer : IComparer<int>
        {
            private readonly BallStore store;
            public HeightComparer(BallStore s) { store = s; }
            public int Compare(int a, int b) => store.pos[a].y.CompareTo(store.pos[b].y);
        }
    }
}

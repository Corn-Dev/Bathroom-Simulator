using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace BallPitFinal
{
    public enum BenchScenario { Undermine, VacuumCrater, DumpPile }

    /// <summary>
    /// Repeatable load generator + recorder for the collapse test. Drives the same PitAuthority tools a player uses
    /// (so a networked run measures the same load), writes one CSV row per physics step, and logs a summary once the
    /// pit is quiet (no active balls, empty support queue for 1 s) or after a timeout.
    ///   Undermine:    instant cavity (undermineRadius) undermineDepth below the surface at the pit center -> cave-in.
    ///   VacuumCrater: vacuum the crater bottom at the pit center for 15 s at vacuumPerStep -> walls slump in.
    ///   DumpPile:     vacuum 3000 balls, then pour them on the floor outside the pit at dumpPerStep -> loose pile.
    /// </summary>
    public sealed class CollapseBench
    {
        public bool Running { get; private set; }
        public string Summary { get; private set; } = "";
        public string Status => Running ? $"BENCH {scenario} t={t:0.0}s phase {phase}" : "";

        private const float Timeout = 240f, QuietSeconds = 1f, VacuumSeconds = 15f;
        private const int DumpBalls = 3000;
        private PitAuthority A;
        private BenchScenario scenario;
        private float t, quiet, lastFrameMs;
        private int phase;
        private readonly StringBuilder csv = new StringBuilder(1 << 20);
        private int steps, frames, peakActive, peakQueue, blockedSteps, peakColliders;
        private double sumSim, maxSim, sumStep, maxStep;
        private float sumFrame, maxFrame;
        private float settleStart = -1f;

        public void Start(PitAuthority authority, BenchScenario s)
        {
            A = authority; scenario = s;
            t = 0f; quiet = 0f; phase = 0; settleStart = -1f;
            steps = frames = peakActive = peakQueue = blockedSteps = peakColliders = 0;
            sumSim = maxSim = sumStep = maxStep = 0; sumFrame = maxFrame = 0f;
            A.ResetCounters();
            csv.Clear();
            csv.AppendLine("t,active,queue,supportThaws,impactThaws,freezes,motionEvents,supportChecks,budgetBlocked,colliders,capHits,simMs,stepMs,frameMs");
            Running = true;
            Summary = "";
            Debug.Log($"[BallPitBench] start {s}");
        }

        public void Stop() { if (Running) Finish("stopped"); }

        public void OnFrame(float frameMs)
        {
            lastFrameMs = frameMs;
            if (!Running) return;
            frames++; sumFrame += frameMs; if (frameMs > maxFrame) maxFrame = frameMs;
        }

        private bool SurfaceAt(float x, float z, out Vector3 point)
        {
            var ray = new Ray(new Vector3(x, A.W.WallTop + 5f, z), Vector3.down);
            if (A.W.Raycast(ray, 1000f, out _, out float hitT)) { point = ray.GetPoint(hitT); return true; }
            point = new Vector3(x, A.W.FloorY, z);
            return false;
        }

        /// <summary>Drive the scenario's tools. Call in FixedUpdate before authority.Step.</summary>
        public void PreStep(float dt)
        {
            if (!Running) return;
            t += dt;
            Vector3 c = A.W.PitCenter;
            switch (scenario)
            {
                case BenchScenario.Undermine:
                    if (phase == 0 && t >= 0.5f)
                    {
                        SurfaceAt(c.x, c.z, out Vector3 top);
                        int n = A.Excavate(0, top - Vector3.up * A.S.undermineDepth, A.S.undermineRadius, A.S.undermineMax);
                        Debug.Log($"[BallPitBench] cavity removed {n:n0} balls");
                        phase = 1; settleStart = t;
                    }
                    break;
                case BenchScenario.VacuumCrater:
                    if (phase == 0 && t >= 0.5f)
                    {
                        if (SurfaceAt(c.x, c.z, out Vector3 bottom)) A.Excavate(0, bottom, A.S.vacuumRadius, A.S.vacuumPerStep);
                        if (t >= 0.5f + VacuumSeconds) { phase = 1; settleStart = t; }
                    }
                    break;
                case BenchScenario.DumpPile:
                    if (phase == 0 && t >= 0.5f)
                    {
                        SurfaceAt(A.W.WallMinX + 4f, A.W.WallMinZ + 4f, out Vector3 corner);
                        int n = A.Excavate(0, corner, 6f, DumpBalls);
                        Debug.Log($"[BallPitBench] collected {n:n0} balls for the dump");
                        phase = 1;
                    }
                    else if (phase == 1)
                    {
                        var spout = new Vector3(A.W.WallMaxX + 6f, A.W.FloorY + A.S.dumpHeight, c.z);
                        A.Dump(0, spout, A.S.dumpPerStep);
                        if (A.HopperCount(0) == 0) { phase = 2; settleStart = t; }
                    }
                    break;
            }
        }

        /// <summary>Record this step. Call in FixedUpdate after authority.Step.</summary>
        public void PostStep(float dt)
        {
            if (!Running) return;
            steps++;
            int active = A.Active.Count, queue = A.QueueLength, cols = A.W.Colliders.Live;
            if (active > peakActive) peakActive = active;
            if (queue > peakQueue) peakQueue = queue;
            if (cols > peakColliders) peakColliders = cols;
            if (A.StatBudgetBlocked) blockedSteps++;
            sumSim += A.LastSimulateMs; if (A.LastSimulateMs > maxSim) maxSim = A.LastSimulateMs;
            sumStep += A.LastStepMs; if (A.LastStepMs > maxStep) maxStep = A.LastStepMs;
            csv.Append(t.ToString("0.000", CultureInfo.InvariantCulture)).Append(',')
               .Append(active).Append(',').Append(queue).Append(',')
               .Append(A.StatSupportThaws).Append(',').Append(A.StatImpactThaws).Append(',').Append(A.StatFreezes).Append(',')
               .Append(A.StatMotionEvents).Append(',').Append(A.StatSupportChecks).Append(',').Append(A.StatBudgetBlocked ? 1 : 0).Append(',')
               .Append(cols).Append(',').Append(A.W.Colliders.CapHits).Append(',')
               .Append(A.LastSimulateMs.ToString("0.000", CultureInfo.InvariantCulture)).Append(',')
               .Append(A.LastStepMs.ToString("0.000", CultureInfo.InvariantCulture)).Append(',')
               .Append(lastFrameMs.ToString("0.00", CultureInfo.InvariantCulture)).AppendLine();

            bool lastPhase = (scenario == BenchScenario.DumpPile) ? phase == 2 : phase == 1;
            if (lastPhase && active == 0 && queue == 0) quiet += dt; else quiet = 0f;
            if (lastPhase && quiet >= QuietSeconds) Finish("quiet");
            else if (t >= Timeout) Finish("TIMEOUT");
        }

        private void Finish(string reason)
        {
            Running = false;
            float dt = Time.fixedDeltaTime;
            float settle = settleStart >= 0f ? (t - settleStart - (reason == "quiet" ? QuietSeconds : 0f)) : -1f;
            string path = Path.Combine(Application.persistentDataPath, $"BallPitBench_{scenario}_{System.DateTime.Now:yyyyMMdd_HHmmss}.csv");
            try { File.WriteAllText(path, csv.ToString()); } catch (System.Exception e) { path = "(csv write failed: " + e.Message + ")"; }
            float hangAvg = A.HangCount > 0 ? (float)(A.HangSumSteps / (double)A.HangCount) * dt : 0f;
            Summary =
                $"[BallPitBench] {scenario} {reason}: balls {A.Store.Count:n0}, run {t:0.0}s, settle {settle:0.0}s after last tool action\n" +
                $"  excavated {A.TotalExcavated:n0}  spawned {A.TotalSpawned:n0}  thawed {A.TotalThaws:n0}  frozen {A.TotalFreezes:n0}  lost {A.TotalLost}\n" +
                $"  peak active {peakActive:n0}/{A.S.maxActive}  peak queue {peakQueue:n0}  budget-blocked steps {blockedSteps:n0}/{steps:n0}\n" +
                $"  hang (queued -> thawed) avg {hangAvg * 1000f:0} ms  max {A.HangMaxSteps * dt:0.00}s  (time unsupported balls waited, i.e. floating)\n" +
                $"  Physics.Simulate avg {(steps > 0 ? sumSim / steps : 0):0.00} ms  max {maxSim:0.00} ms | whole step avg {(steps > 0 ? sumStep / steps : 0):0.00} ms  max {maxStep:0.00} ms\n" +
                $"  frame avg {(frames > 0 ? sumFrame / frames : 0):0.0} ms ({(frames > 0 && sumFrame > 0 ? 1000f * frames / sumFrame : 0):0} fps)  worst {maxFrame:0.0} ms\n" +
                $"  colliders peak {peakColliders:n0}/{A.W.Colliders.Capacity:n0}  capHits {A.W.Colliders.CapHits}\n" +
                $"  csv: {path}";
            Debug.Log(Summary);
        }
    }
}

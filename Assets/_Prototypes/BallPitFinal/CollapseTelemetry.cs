using System.IO;
using System.Text;
using UnityEngine;

namespace BallPitFinal
{
    /// <summary>P-key collapse snapshot: one text block per press, appended to BallPitCollapseReport.txt.</summary>
    public static class CollapseTelemetry
    {
        public static string FilePath => Path.Combine(Application.persistentDataPath, "BallPitCollapseReport.txt");

        public static string Snapshot(PitAuthority a, float t, float fpsAvg, float fpsWorst)
        {
            PitSettings s = a.S;
            BallStore st = a.Store;
            float dt = Time.fixedDeltaTime;
            float hangAvgMs = a.HangCount > 0 ? (float)(a.HangSumSteps / (double)a.HangCount) * dt * 1000f : 0f;
            int live = st.StateTotal(BallStore.State.Active) + st.StateTotal(BallStore.State.Held);
            var sb = new StringBuilder(1024);
            sb.AppendLine($"==== collapse snapshot {System.DateTime.Now:HH:mm:ss}  (t={t:0.0}s since last tool action) ====");
            sb.AppendLine($"fill={s.fillType} balls={st.Count:n0} pit={s.pitX}x{s.pitY}x{s.pitZ}");
            sb.AppendLine($"STATE  resting={st.StateTotal(BallStore.State.Resting):n0} active={st.StateTotal(BallStore.State.Active):n0} " +
                          $"held={st.StateTotal(BallStore.State.Held)} hopper={st.StateTotal(BallStore.State.Hopper):n0} lost={st.StateTotal(BallStore.State.Lost)}");
            sb.AppendLine($"COLLAPSE  queue={a.QueueLength:n0} regions={a.QueueRegions} live={live:n0}/{s.maxActive} collapseLimit={a.EffectiveCollapseLimit:n0} " +
                          $"bodiesCreated={a.Active.Created:n0} supportThaws/step={a.StatSupportThaws} freeFall/step={a.StatFreeFall} " +
                          $"impactThaws/step={a.StatImpactThaws} freezes/step={a.StatFreezes} motionEvents/step={a.StatMotionEvents} " +
                          $"supportChecks/step={a.StatSupportChecks} regionsVisited/step={a.StatRegionsVisited} budgetBlocked={a.StatBudgetBlocked} " +
                          $"adaptiveBlocked={a.StatAdaptiveBlocked} timeBudgetHit={a.StatTimeBudgetHit}");
            sb.AppendLine($"CUMULATIVE  thaws={a.TotalThaws:n0} freezes={a.TotalFreezes:n0} excavated={a.TotalExcavated:n0} lost={a.TotalLost} " +
                          $"hangMaxSteps={a.HangMaxSteps} ({a.HangMaxSteps * dt:0.0}s) hangAvg={hangAvgMs:0}ms hangCount={a.HangCount:n0}");
            sb.AppendLine($"PERF  sim={a.LastSimulateMs:0.00}ms simEma={a.SimMsEma:0.00}ms step={a.LastStepMs:0.00}ms drain={a.LastDrainMs:0.00}ms " +
                          $"fps(avg/worst)={fpsAvg:0}/{fpsWorst:0} colliders={a.W.Colliders.Live:n0}/{a.W.Colliders.Capacity:n0} capHits={a.W.Colliders.CapHits}");
            sb.AppendLine($"TUNING  maxActive={s.maxActive} targetSimulateMs={s.targetSimulateMs} minActive={s.minActive} collapseReserve={s.collapseReserve} " +
                          $"supportThawsPerStep={s.supportThawsPerStep} maxSupportChecksPerStep={s.maxSupportChecksPerStep} collapseRulesBudgetMs={s.collapseRulesBudgetMs} " +
                          $"regionSizeDiameters={s.regionSizeDiameters} minRegionShare={s.minRegionShare} freeFallDiameters={s.freeFallDiameters} " +
                          $"fallingSpeed={s.fallingSpeed} fallingMarkSeconds={s.fallingMarkSeconds} freezeSpeed={s.freezeSpeed} freezeSeconds={s.freezeSeconds} " +
                          $"freezeMaxDriftFraction={s.freezeMaxDriftFraction} settleSpeed={s.settleSpeed} settleDrag={s.settleDrag} settleAngularDrag={s.settleAngularDrag} " +
                          $"sleepThreshold={s.sleepThreshold} linearDrag={s.linearDrag} angularDrag={s.angularDrag} maxSupportGapDegrees={s.maxSupportGapDegrees} " +
                          $"thawMoveFraction={s.thawMoveFraction} solverIterations={s.solverIterations} physicsHz={s.physicsHz}");
            return sb.ToString();
        }

        public static string Append(PitAuthority a, float t, float fpsAvg, float fpsWorst)
        {
            string text = Snapshot(a, t, fpsAvg, fpsWorst);
            try { File.AppendAllText(FilePath, text + "\n"); }
            catch (System.Exception e) { Debug.LogWarning("[BallPitFinal] could not write collapse report: " + e.Message); }
            Debug.Log(text + "-> " + FilePath);
            return text;
        }
    }
}

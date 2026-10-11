using System.Diagnostics;
using UnityEngine;

namespace BallPitProto
{
    /// <summary>
    /// PROTOTYPE — not final.
    ///
    /// Lightweight timing + logging for the ball-pit prototype so we can see
    /// where frame time actually goes and how it scales with pit size:
    ///   - frame time (min / avg / max over a rolling window)
    ///   - last renderer Rebuild() cost (ms)
    ///   - last surface collider Rebuild() cost (ms)
    ///   - active physics ball count
    ///
    /// Call BeginSample/EndSample around a measured block; call Tick() once per
    /// frame; read the public fields for an overlay; call LogSnapshot() to dump
    /// a line to the Unity console.
    /// </summary>
    public sealed class ProtoProfiler
    {
        private readonly Stopwatch sw = new Stopwatch();

        // Rolling frame-time window.
        private const int Window = 120;
        private readonly float[] frameMs = new float[Window];
        private int frameIdx;
        private int frameFilled;

        public float LastRebuildMs { get; private set; }
        public float LastColliderMs { get; private set; }
        public int ActiveBalls { get; set; }

        public float FrameMsAvg { get; private set; }
        public float FrameMsMin { get; private set; }
        public float FrameMsMax { get; private set; }
        public float FrameMsLast { get; private set; }

        // Worst (peak) rebuild/collider cost seen since last reset — the number
        // that actually tells us if a big dig causes a hitch.
        public float PeakRebuildMs { get; private set; }
        public float PeakColliderMs { get; private set; }

        public void BeginSample() => sw.Restart();

        public float EndSampleMs()
        {
            sw.Stop();
            return (float)sw.Elapsed.TotalMilliseconds;
        }

        public void RecordRebuild(float ms)
        {
            LastRebuildMs = ms;
            if (ms > PeakRebuildMs) PeakRebuildMs = ms;
        }

        public void RecordCollider(float ms)
        {
            LastColliderMs = ms;
            if (ms > PeakColliderMs) PeakColliderMs = ms;
        }

        public void ResetPeaks()
        {
            PeakRebuildMs = 0f;
            PeakColliderMs = 0f;
        }

        public void Tick(float unscaledDeltaTime)
        {
            float ms = unscaledDeltaTime * 1000f;
            FrameMsLast = ms;
            frameMs[frameIdx] = ms;
            frameIdx = (frameIdx + 1) % Window;
            if (frameFilled < Window) frameFilled++;

            float sum = 0f, min = float.MaxValue, max = 0f;
            for (int i = 0; i < frameFilled; i++)
            {
                float v = frameMs[i];
                sum += v;
                if (v < min) min = v;
                if (v > max) max = v;
            }
            FrameMsAvg = sum / Mathf.Max(1, frameFilled);
            FrameMsMin = min;
            FrameMsMax = max;
        }

        public float AvgFps => FrameMsAvg > 0.0001f ? 1000f / FrameMsAvg : 0f;
        public float MinFps => FrameMsMax > 0.0001f ? 1000f / FrameMsMax : 0f; // worst frame

        public void LogSnapshot(string tag, int ballsRemaining, int totalCells, int drawCalls, int drawnBalls = -1)
        {
            UnityEngine.Debug.Log(
                $"[BallPitProto]{(string.IsNullOrEmpty(tag) ? "" : " " + tag)} " +
                $"cells={ballsRemaining:n0}/{totalCells:n0} " +
                (drawnBalls >= 0 ? $"drawn={drawnBalls:n0} " : "") +
                $"draws={drawCalls} " +
                $"fps(avg/worst)={AvgFps:n0}/{MinFps:n0} " +
                $"frameMs(avg/max)={FrameMsAvg:n2}/{FrameMsMax:n2} " +
                $"rebuildMs(last/peak)={LastRebuildMs:n2}/{PeakRebuildMs:n2} " +
                $"colliderMs(last/peak)={LastColliderMs:n2}/{PeakColliderMs:n2} " +
                $"activeBalls={ActiveBalls}");
        }
    }
}

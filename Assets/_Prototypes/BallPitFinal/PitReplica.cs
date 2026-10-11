using UnityEngine;

namespace BallPitFinal
{
    /// <summary>
    /// Client-side mirror of the host's live (active/held) balls. Each live ball keeps its last few (host time, pose)
    /// samples from events and snapshots and is drawn interpolated at playback time (host time minus a delay).
    /// Clients never simulate balls; when the host's Freeze event arrives the ball has already been still for the
    /// sleep window, so the interpolated pose and the frozen pose coincide (no snap).
    /// </summary>
    public sealed class PitReplica
    {
        private const int H = 4;
        private const float MaxExtrapolation = 0.1f;

        private readonly PitWorld W;
        private readonly int[] slotOfBall;
        private int[] ids, count, head;
        private float[] times;
        private Vector3[] pts;
        private int n;

        public int Count => n;
        public int LocalHeld = -1;
        public Vector3 LocalHand;

        public PitReplica(PitWorld world, int initialCapacity)
        {
            W = world;
            slotOfBall = new int[world.Store.Capacity];
            for (int i = 0; i < slotOfBall.Length; i++) slotOfBall[i] = -1;
            Allocate(Mathf.Max(64, initialCapacity));
        }

        private void Allocate(int cap)
        {
            System.Array.Resize(ref ids, cap);
            System.Array.Resize(ref count, cap);
            System.Array.Resize(ref head, cap);
            System.Array.Resize(ref times, cap * H);
            System.Array.Resize(ref pts, cap * H);
        }

        public bool IsLive(int id) => slotOfBall[id] >= 0;

        /// <summary>Ball became live (thaw / spawn / grab) at pose p, host time t.</summary>
        public void Begin(int id, Vector3 p, float t)
        {
            int s = slotOfBall[id];
            if (s < 0)
            {
                if (n == ids.Length) Allocate(n * 2);
                s = n++;
                ids[s] = id; count[s] = 0; head[s] = 0;
                slotOfBall[id] = s;
            }
            Push(s, p, t);
        }

        public void End(int id)
        {
            int s = slotOfBall[id];
            if (s < 0) return;
            int last = n - 1;
            if (s != last)
            {
                ids[s] = ids[last]; count[s] = count[last]; head[s] = head[last];
                System.Array.Copy(times, last * H, times, s * H, H);
                System.Array.Copy(pts, last * H, pts, s * H, H);
                slotOfBall[ids[s]] = s;
            }
            n--;
            slotOfBall[id] = -1;
            if (LocalHeld == id) LocalHeld = -1;
        }

        public void AddSample(int id, Vector3 p, float t)
        {
            int s = slotOfBall[id];
            if (s >= 0) Push(s, p, t);
        }

        private void Push(int s, Vector3 p, float t)
        {
            int c = count[s], b = s * H;
            if (c > 0)
            {
                int last = b + (head[s] + c - 1) % H;
                float lt = times[last];
                if (t < lt - 1e-4f) return;                        // stale (unreliable reordering)
                if (t - lt < 1e-4f) { pts[last] = p; return; }     // same tick: overwrite
            }
            if (c < H)
            {
                int idx = b + (head[s] + c) % H;
                times[idx] = t; pts[idx] = p; count[s] = c + 1;
            }
            else
            {
                int idx = b + head[s];
                times[idx] = t; pts[idx] = p; head[s] = (head[s] + 1) % H;
            }
        }

        private Vector3 Sample(int s, float t)
        {
            int c = count[s], b = s * H, h0 = head[s];
            if (c == 0) return W.Store.pos[ids[s]];
            int first = b + h0;
            if (t <= times[first]) return pts[first];
            for (int k = 0; k < c - 1; k++)
            {
                int i0 = b + (h0 + k) % H, i1 = b + (h0 + k + 1) % H;
                if (t <= times[i1])
                {
                    float span = Mathf.Max(1e-5f, times[i1] - times[i0]);
                    return Vector3.LerpUnclamped(pts[i0], pts[i1], (t - times[i0]) / span);
                }
            }
            int il = b + (h0 + c - 1) % H;
            if (c >= 2)
            {
                int ip = b + (h0 + c - 2) % H;
                float dt = times[il] - times[ip];
                if (dt > 1e-4f)
                {
                    Vector3 v = (pts[il] - pts[ip]) / dt;
                    return pts[il] + v * Mathf.Min(t - times[il], MaxExtrapolation);
                }
            }
            return pts[il];
        }

        /// <summary>Interpolated poses at playback time + ball ids (for picking moving balls on the client).</summary>
        public int FillInstances(BallInstance[] dst, int[] outIds, float playbackTime)
        {
            int m = Mathf.Min(n, Mathf.Min(dst.Length, outIds.Length));
            for (int s = 0; s < m; s++)
            {
                int id = ids[s];
                Vector3 p = id == LocalHeld ? LocalHand : Sample(s, playbackTime);
                dst[s] = new BallInstance { pos = p, packed = W.Store.color[id] | GpuBallRenderer.DrawableBit };
                outIds[s] = id;
            }
            return m;
        }
    }
}

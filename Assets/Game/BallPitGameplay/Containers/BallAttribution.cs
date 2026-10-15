// BallAttribution: a tiny host-side map of "who last handled ball id, and how", for sale credit (brief §4.2). The Hands
// tool records a throw/drop; the DepositDriver reads it when a ball sells, then clears it. Host only; no replication
// (sale events already carry the resolved name to clients). Keyed by ball id; bounded by the ball count.
using UnityEngine;

namespace BallPitGameplay
{
    public sealed class BallAttribution
    {
        private struct Entry { public string Name; public bool Thrown; public Vector3 From; public int Frame; }

        private readonly System.Collections.Generic.Dictionary<int, Entry> byBall =
            new System.Collections.Generic.Dictionary<int, Entry>();

        /// <summary>Record that a player released ball <paramref name="id"/> (thrown or dropped) from <paramref name="from"/>.</summary>
        public void Record(int id, string playerName, bool thrown, Vector3 from)
        {
            byBall[id] = new Entry { Name = playerName, Thrown = thrown, From = from, Frame = Time.frameCount };
        }

        /// <summary>Reads and removes the credit for a ball. Returns false if none was recorded.</summary>
        public bool Consume(int id, Vector3 soldAt, out string name, out bool thrown, out float distance)
        {
            name = null; thrown = false; distance = 0f;
            if (!byBall.TryGetValue(id, out Entry e)) return false;
            byBall.Remove(id);
            name = e.Name; thrown = e.Thrown;
            distance = Vector3.Distance(e.From, soldAt);
            return true;
        }

        public void Clear() => byBall.Clear();
    }
}

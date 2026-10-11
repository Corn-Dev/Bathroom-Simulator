using UnityEngine;

namespace BallPitFinal
{
    /// <summary>
    /// Source of truth for every ball. Ids are STABLE for the lifetime of a pit: there is no
    /// swap-remove. A ball that leaves play changes state (Stored / Lost) instead of being deleted,
    /// so grids, colliders, the GPU buffer, saves and network messages can all key on the id.
    /// The ledger (per state, per color) is updated on every transition and is exact by construction.
    /// </summary>
    public sealed class BallStore
    {
        public enum State : byte { Resting = 0, Active = 1, Held = 2, Stored = 3, Lost = 4 }
        public const int StateCount = 5;

        public readonly Vector3[] pos;   // exact continuous position (THE truth; for Active balls it is the last physics pose)
        public readonly byte[] color;
        public readonly State[] state;
        public readonly int Capacity;
        public readonly int Colors;
        public int Count { get; private set; }

        private readonly int[] ledger;      // [state * Colors + color]
        private readonly int[] stateTotals; // [state]
        private readonly int[] colorTotals; // [color]

        public BallStore(int capacity, int colors)
        {
            Capacity = capacity;
            Colors = Mathf.Clamp(colors, 1, 16);
            pos = new Vector3[capacity];
            color = new byte[capacity];
            state = new State[capacity];
            ledger = new int[StateCount * Colors];
            stateTotals = new int[StateCount];
            colorTotals = new int[Colors];
        }

        public int Add(Vector3 p, int c, State s)
        {
            if (Count >= Capacity) throw new System.InvalidOperationException("BallStore capacity exceeded");
            if ((uint)c >= (uint)Colors) throw new System.ArgumentOutOfRangeException(nameof(c));
            int id = Count++;
            pos[id] = p; color[id] = (byte)c; state[id] = s;
            ledger[(int)s * Colors + c]++;
            stateTotals[(int)s]++;
            colorTotals[c]++;
            return id;
        }

        public void SetState(int id, State s)
        {
            State old = state[id];
            if (old == s) return;
            int c = color[id];
            ledger[(int)old * Colors + c]--; stateTotals[(int)old]--;
            ledger[(int)s * Colors + c]++; stateTotals[(int)s]++;
            state[id] = s;
        }

        public int CountOf(State s, int c) => ledger[(int)s * Colors + c];
        public int StateTotal(State s) => stateTotals[(int)s];
        public int ColorTotal(int c) => colorTotals[c];

        /// <summary>Balls of color c still in play (not deposited in a container, not lost).</summary>
        public int InPlay(int c) => colorTotals[c] - CountOf(State.Stored, c) - CountOf(State.Lost, c);

        /// <summary>Full recount from the records. Dev-only; O(Count).</summary>
        public bool VerifyLedger(out string error)
        {
            error = null;
            var l = new int[StateCount * Colors];
            for (int i = 0; i < Count; i++) l[(int)state[i] * Colors + color[i]]++;
            for (int s = 0; s < StateCount; s++)
            {
                int st = 0;
                for (int c = 0; c < Colors; c++)
                {
                    int k = s * Colors + c;
                    st += l[k];
                    if (l[k] != ledger[k]) { error = $"{(State)s}/color{c}: ledger {ledger[k]} vs recount {l[k]}"; return false; }
                }
                if (st != stateTotals[s]) { error = $"{(State)s}: total {stateTotals[s]} vs recount {st}"; return false; }
            }
            return true;
        }
    }
}

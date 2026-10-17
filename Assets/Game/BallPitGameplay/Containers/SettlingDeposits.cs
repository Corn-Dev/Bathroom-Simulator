// SettlingDeposits: the host-only "sell on cross, settle after" deferral (brief §3.1-§3.2; DESIGN-C §1-§2;
// game-design §2 "Deposit is PHYSICAL; sell-on-cross, settle-after"). When a correct ball crosses a container's
// intake the economy credits IMMEDIATELY (DepositDriver.Sell), but the ball is NOT demoted there — it keeps falling
// as a real physics ball so the player sees it land and settle. This class holds those dwelling ids and demotes each
// one (PitAuthority.Deposit, which moves it to Stored so the fill grows) once it has had its moment: either the dwell
// elapsed OR it has gone still. Pure plain C#, host only; owned by DepositDriver. It never calls Deposit on a ball
// that is no longer active (a dropped/grabbed/returned ball mid-dwell), so it never double-demotes a reused id.
using System.Collections.Generic;
using BallPitFinal;

namespace BallPitGameplay
{
    public sealed class SettlingDeposits
    {
        /// <summary>Default dwell before a crossed ball is demoted if it has not already gone still (seconds).</summary>
        public const float DefaultDwellSeconds = 0.6f;

        // Below this smoothed speed the ball is considered settled and demoted early (so a quick drop sells+fills fast).
        private const float StillnessSpeed = 0.15f;

        private struct Entry { public int Id; public float DwellEndTime; }

        private readonly PitAuthority authority;
        private readonly float dwellSeconds;
        private readonly List<Entry> entries = new List<Entry>(64);
        private readonly HashSet<int> owned = new HashSet<int>();

        public SettlingDeposits(PitAuthority authority, float dwellSeconds = DefaultDwellSeconds)
        {
            this.authority = authority;
            this.dwellSeconds = dwellSeconds < 0f ? 0f : dwellSeconds;
        }

        /// <summary>True while id is dwelling (backs DepositDriver's Phase-1 skip so a dwelling ball is not re-sold).</summary>
        public bool Owns(int id) => owned.Contains(id);

        /// <summary>A correct ball crossed and was credited; defer its demote until it settles or the dwell elapses.</summary>
        public void Register(int id, float now)
        {
            if (owned.Add(id)) entries.Add(new Entry { Id = id, DwellEndTime = now + dwellSeconds });
        }

        /// <summary>Each step: demote any dwelling ball whose dwell elapsed or that has gone still. A ball that is no
        /// longer active (dropped/grabbed/returned mid-dwell) is dropped WITHOUT a second Deposit.</summary>
        public void Tick(float now)
        {
            if (entries.Count == 0 || authority == null) return;
            ActivePhysics active = authority.Active;
            for (int i = entries.Count - 1; i >= 0; i--)
            {
                Entry e = entries[i];
                if (!active.IsActive(e.Id))
                {
                    // No longer a live ball — never Deposit it again (a reused id would be wrongly demoted).
                    RemoveAt(i, e.Id);
                    continue;
                }
                if (now >= e.DwellEndTime || active.SmoothedSpeedOf(e.Id) < StillnessSpeed)
                {
                    authority.Deposit(e.Id);
                    RemoveAt(i, e.Id);
                }
            }
        }

        /// <summary>Demote every remaining still-active dwelling ball (world teardown). Safe on a disposed world via
        /// the IsActive guard; callers wrap it so a thrown exception cannot escape teardown.</summary>
        public void Flush()
        {
            if (authority == null) { entries.Clear(); owned.Clear(); return; }
            ActivePhysics active = authority.Active;
            for (int i = 0; i < entries.Count; i++)
                if (active.IsActive(entries[i].Id)) authority.Deposit(entries[i].Id);
            entries.Clear();
            owned.Clear();
        }

        private void RemoveAt(int index, int id)
        {
            int last = entries.Count - 1;
            entries[index] = entries[last];
            entries.RemoveAt(last);
            owned.Remove(id);
        }
    }
}

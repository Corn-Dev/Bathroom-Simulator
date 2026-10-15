# Collapse-behavior findings for the other Kiro (milestone 2 follow-up)

Milestone 2 integrated and runs. Rendering/physics are great: **2.3M+ balls at
130+ fps idle.** Two problems are in the collapse/interaction rules layer
(`PitAuthority` + `SupportQueue`), not in rendering, physics cost, or networking.
Both are design decisions to revisit, not tuning. We tried tuning first (below)
and it did not fix either — confirming they're structural.

---

## Finding 1 (MAIN): a long collapse runs serially, taking minutes

### What the user saw
Dug a long diagonal tunnel in a 1M–2.3M pit whose entire roof should have lost
support at once. Instead:
- Only the **far/lowest end** of the tunnel collapsed first.
- The rest of the roof stayed **frozen** (visibly floating — unsupported but not
  yet thawed), sitting in the queue.
- The collapse then crept along the tunnel **one region at a time, very slowly**
  — a cave that should finish in ~30 s was still going 5 minutes later with
  12,000–15,000 balls still queued.
- The collapse itself, where it happens, **looks correct** (balls roll in and
  settle nicely). It demotes/activates/re-queues in a slow fight near the active
  front only.

### Root cause (confirmed in code)
`SupportQueue` is a **single global min-heap keyed by absolute height, lowest
first** ("void migration"). `DrainSupportQueue` pops the globally lowest
unsupported ball first, capped at `supportThawsPerStep` per step.

So when a whole roof loses support simultaneously, every ball is correctly
queued, but they're released strictly bottom-to-top across the ENTIRE pit. The
far-low end drains first; everything else waits its turn behind every
physically-lower ball, even though it's already unsupported. `QueueDependents`
re-inserts newly-exposed balls back into the same global heap, so the wave is
forced to stay local and march. Result: the collapse is **artificially
serialized** — throttled by collapse ordering + per-step cap, NOT by physics cost
(active count stays small while fps tanks from the per-step rules work running for
minutes).

### Why it's a design bug, not tuning
The lowest-first global order is fine for a small localized dig but wrong for a
large simultaneous roof failure. A real roof drops in big sections at once; this
processes it like a single-file line. Raising `supportThawsPerStep`,
`maxSupportChecksPerStep`, `maxActive`, and lowering `maxSupportGapDegrees` /
`stillSeconds` (we tried all of these) does not change the serial ordering — it
only lets each local step do slightly more.

### Fix direction (the other Kiro's call — it designed this queue)
Let collapse run **in parallel across all disturbed regions** instead of
serialized by absolute height. Candidates to weigh:
- Process the queue as **disturbance-local waves** (FIFO within a collapse event)
  rather than one global min-heap, so independent regions collapse concurrently.
- Allow **multiple simultaneous fronts**, each with its own budget.
- **Batch-thaw a whole contiguous unsupported region** in a step rather than one
  ball per pop.
- Keep a per-step cap, but make it bound **total concurrent active balls** (a real
  perf limit) rather than **collapse speed** (an artificial one).

### Numbers to confirm (user will attach P-key captures)
Expected signature: **huge `queue`, small `active`, `supportThaws/step` pinned at
the cap, `budgetBlocked` often true, fps low for minutes.** That pattern = throttled
by ordering, not physics. (`BallPitCollapseReport.txt` from the P key is appended
below / alongside.)

---

## Finding 2: active balls must be interactable (currently they aren't)

Design clarification from the user: **there is no reason a moving/active ball
shouldn't be grabbable/vacuumable.** It should be interactable like any other
ball. The current behavior (can only interact once a ball has frozen back to
Resting) was an implementation shortcut, not an intended rule.

### Cause (in code)
- Hover/selection uses `PitWorld.Raycast`, which only ray-tests the **resting**
  grid. Active balls live in the `Live` grid and are never hit-tested.
- `PitAuthority.TryGrab` early-returns unless state == `Resting`.
- `Excavate` gathers only from the resting grid.

### Desired behavior
Grab/vacuum should also hit **active** (and held-by-others?) balls. For a grab of
an active ball, hand off the pooled rigidbody to the carrier (or convert to Held)
rather than requiring it to freeze first. Needs a raycast against the `Live` grid
too, and `TryGrab`/`Excavate` to accept non-resting states. (Networking: an active
ball already has an id + is replicated, so grabbing it is an intent like any
other.)

Note: the current tools (`U` undermine, `V` vacuum, `G` dump / `Excavate` +
`Dump`) read as **test instruments**, not the final carry-one-ball game loop.
We'll redesign the real player tools later; this note is only about the
interactable-while-active rule, which should hold for whatever the final tools are.

---

## Tuning we applied (keep or revert as you like — did NOT fix the above)
In `PitSettings.cs`, settle-focused values (originals in comments):
`maxSupportGapDegrees` 170→150, `stillSeconds` 0.5→0.2, `sleepThreshold`
0.01→0.05, `linearDrag` 0.05→0.2, `angularDrag` 1.2→3, `supportThawsPerStep`
200→400, `maxSupportChecksPerStep` 2000→4000. These improved local settle look
and slightly helped throughput but did not change the serial-collapse behavior.

---

## Finding 1b (SHARP): the active set is pinned at ~1,600 and never grows

The P-key captures below (2.35M FCC pit, a long diagonal tunnel undermined, user
had raised budgets to maxActive=4100, supportThawsPerStep=1500,
maxSupportChecksPerStep=8000, collapseReserve=200) show a dead-giveaway pattern:

| t (s) | queue | live/max | supportThaws/step | budgetBlocked | fps |
|------|-------|----------|-------------------|---------------|-----|
| 25.3 | 3,931 | **1,600**/4100 | 4 | True | 34 |
| 44.6 | 3,478 | **1,600**/4100 | 0 | True | 33 |
| 65.4 | 3,303 | **1,599**/4100 | 0 | True | 31 |
| 97.2 | 3,135 | **1,600**/4100 | 1 | True | 30 |
| 124.7 | 2,911 | **1,600**/4100 | 0 | True | 29 |
| 151.1 | 2,832 | **1,596**/4100 | 0 | True | 27 |

Read the pattern:
- **`live` is nailed to ~1,600 the whole time, never approaching the 4,100 cap.**
  Yet `budgetBlocked=True` on every single snapshot and the queue has 2,800–3,900
  balls waiting. So promotion is being refused while 2,500 pool slots look free.
- **`supportThaws/step` is 0–4**, nowhere near `supportThawsPerStep=1500`. Barely
  anything is being promoted per step.
- Queue drains ~1,100 balls over **126 s** (~9 balls/s). `hangMaxSteps` climbs to
  6,617 — at 50 Hz that's a ball sitting unsupported (visibly floating) for ~132 s.
- fps sags 34 → 27 the longer it runs.

### The likely bug
`DrainSupportQueue` gates on `Active.CanPromoteWithin(CollapseLimit)` where
`CollapseLimit = maxActive - collapseReserve = 3900`. Since live is only 1,600,
that gate should pass — so the block is almost certainly the OTHER promote
precondition: `ActivePhysics.CanPromote` → `count < MaxActive && free.Count > 0`,
with **`MaxActive = Mathf.Min(maxActive, poolSize)`** in the ctor. If
`activePoolSize` is ~1,600, then `MaxActive` is clamped to ~1,600 no matter that
`maxActive=4100`, and the rigidbody **pool** (`free`) is the true ceiling. The
collapse is strangled by an undersized pool, and `budgetBlocked` is firing because
`CanPromoteWithin` returns false once count hits that clamped ~1,600 — NOT because
of `collapseReserve`.

Net: TWO independent throttles are stacked:
1. **Pool/`MaxActive` clamp** holds the active set at ~1,600 regardless of the
   `maxActive` the user set (needs `activePoolSize` raised too, or the clamp
   reconsidered). This is why bigger `maxActive` alone did nothing.
2. **Serial height-ordered queue** (Finding 1) means even the ~1,600 slots are
   fed bottom-to-top across the whole pit, so one tunnel end collapses at a time.

Both must change for a long roof to drop at once. Please also sanity-check whether
`activePoolSize` SHOULD just track `maxActive` (why are they separate?), and
whether ~1,600 concurrent rigidbodies is actually the perf-safe max on this
hardware (fps was still 27–34 WITH the serial overhead; a parallel collapse with a
bigger pool may be fine, or may need the per-step promotion cost profiled).

---

## P-key captures (verbatim)
2.35M FCC, long diagonal tunnel undermined. Note TUNING line = user's raised
budgets; live still pinned at 1,600.

```
==== collapse snapshot 22:16:24  (t=25.3s) ====
fill=Fcc balls=2,352,637 pit=133x133x133
STATE  resting=2,345,333 active=1,600 held=0 hopper=5,704 lost=0
COLLAPSE  queue=3,931 live(active+held)=1,600/4100 supportThaws/step=4 impactThaws/step=2 freezes/step=0 motionEvents/step=309 supportChecks/step=5 budgetBlocked=True
CUMULATIVE  thaws=2,027 freezes=427 excavated=5,704 lost=0 hangMaxSteps=191 hangCount=1,698
PERF  sim=5.11ms step=6.65ms fps(avg/worst)=34/17 colliders=2,406/8,000 capHits=0
TUNING  maxSupportGapDegrees=170 supportThawsPerStep=1500 maxSupportChecksPerStep=8000 collapseReserve=200 maxActive=4100 stillSeconds=1 sleepThreshold=0.01 linearDrag=0.05 angularDrag=1.2 thawMoveFraction=0.1

==== collapse snapshot 22:16:43  (t=44.6s) ====
STATE  resting=2,345,334 active=1,599 held=0 hopper=5,704 lost=0
COLLAPSE  queue=3,478 live=1,599/4100 supportThaws/step=0 freezes/step=1 motionEvents/step=182 supportChecks/step=0 budgetBlocked=True
CUMULATIVE  thaws=4,173 freezes=2,574 hangMaxSteps=1359 hangCount=3,549
PERF  sim=5.48ms step=7.33ms fps(avg/worst)=33/17 colliders=3,528/8,000

==== collapse snapshot 22:17:36  (t=97.2s) ====
STATE  resting=2,345,333 active=1,600 held=0 hopper=5,704 lost=0
COLLAPSE  queue=3,135 live=1,600/4100 supportThaws/step=1 freezes/step=0 motionEvents/step=27 supportChecks/step=7 budgetBlocked=True
CUMULATIVE  thaws=6,059 freezes=4,459 hangMaxSteps=3890 hangCount=5,380
PERF  sim=4.78ms step=6.36ms fps(avg/worst)=30/15 colliders=1,709/8,000

==== collapse snapshot 22:18:30  (t=151.1s) ====
STATE  resting=2,345,337 active=1,596 held=0 hopper=5,704 lost=0
COLLAPSE  queue=2,832 live=1,596/4100 supportThaws/step=0 freezes/step=4 motionEvents/step=13 supportChecks/step=0 budgetBlocked=True
CUMULATIVE  thaws=7,372 freezes=5,776 hangMaxSteps=6617 hangCount=6,630
PERF  sim=5.74ms step=7.08ms fps(avg/worst)=27/13 colliders=1,460/8,000
```
(queue 3,931 → 2,832 over 126 s; live pinned ~1,600; budgetBlocked every snapshot;
hangMaxSteps 191 → 6,617 = a ball floating unsupported ~132 s.)


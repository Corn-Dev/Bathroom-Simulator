# Collapse stall bug (milestone-3 regression) — for the strong Kiro

Milestone 3 fixed the serial-collapse and pinned-pool problems and USUALLY works
great (natural collapse, drains in seconds). But collapse now intermittently
**deadlocks**: it runs for a bit, then the active set stops freezing, the queue
stops draining, and it sits forever with a few thousand frozen-looking balls and
the queue stuck. User saw "zero real moving balls, queue stopped going down, just
stuck." P-telemetry below pins the cause.

## The deadlock (from telemetry, 1M FCC pit, undermine tunnel)

Stuck state (three consecutive snapshots, ~unchanged over seconds):
```
STATE   resting=981,208 active=2,913 held=0 hopper=15,879 lost=0
COLLAPSE queue=1,611 regions=23 live=2,913/4000 collapseLimit=2,913
         supportThaws/step=0 freeFall/step=0 freezes/step=0 motionEvents/step=0
         supportChecks/step=0 budgetBlocked=True adaptiveBlocked=True timeBudgetHit=False
PERF    sim=10.97ms simEma=9.26ms step=16.04ms  (targetSimulateMs=8)  fps 38
```

### Root cause: the adaptive cap and the freeze rule deadlock each other
1. **`simEma` (9.26ms) > `targetSimulateMs` (8)** → the adaptive cap engages:
   `adaptiveBlocked=True`, and `collapseLimit` has collapsed to exactly the current
   `live` (2,913). No new collapse thaws allowed.
2. **The 2,913 active balls never freeze** (`freezes/step=0`, `motionEvents/step=0`).
   They are near-still but never satisfy the freeze rule (speed < `freezeSpeed=0.06`
   for `freezeSeconds=0.3` with drift < `freezeMaxDriftFraction`), because in a
   packed pile constant micro-jitter from neighbors keeps resetting the stillness
   timer. So they never demote and never free a slot.
3. Net: **can't thaw more (sim over target), can't free existing (won't freeze) →
   `live` stuck at 2,913, `queue` stuck at 1,611 indefinitely.** A hard stall.

The ~2,913 idle rigidbodies alone cost ~9–11ms of `Physics.Simulate` (broadphase /
island bookkeeping for packed static-touching bodies), which is what pushes
`simEma` over the 8ms target and arms the adaptive block. So the two safety
mechanisms designed in milestone 3 are fighting:
- the adaptive cap says "too expensive, stop thawing,"
- the freeze rule can't retire the expensive balls that caused it.

### Why it's intermittent
An earlier run drained fine (snapshots 23:24:xx): there the balls DID freeze
(`freezes/step=3`, `live` fell 2,895 → 631 → 102 → 1, queue hit 0). Whether the
pile happens to settle under the freeze threshold before the adaptive cap latches
decides pass vs. stall. So it's a race, not a fixed-input bug.

### Also visible: hang metrics climbing while stalled
`hangMaxSteps=717 (14.3s)`, `hangAvg=507ms`, `hangCount=15,589` — balls sitting
queued/unsupported (visibly frozen in air) for many seconds while the stall holds.

## Fix direction (strong Kiro's call — these are your mechanisms)
The core tension: a few thousand near-idle rigidbodies are too expensive to keep,
but the freeze rule won't retire them. Options to weigh:
- **Make freeze robust to neighbor jitter / adaptive pressure.** When the active
  set is at/над the adaptive limit and balls are slow, force-freeze the slowest /
  longest-still ones (quantize-at-pose as usual) rather than waiting for a clean
  stillness window that never comes in a jittery pile. I.e. freezing should be
  guaranteed to make progress whenever thawing is blocked, so the set can always
  drain.
- **Don't let `collapseLimit` clamp below what can still freeze.** If `adaptiveBlocked`
  AND `freezes/step==0` for N steps, that's the deadlock signature — detect it and
  break it (force a freeze pass, or temporarily raise the sim target).
- **Reduce the per-idle-body cost** so 3k resting-but-active balls don't blow the
  8ms target in the first place (faster demotion to static colliders; the whole
  point of the static shell is that resting balls are cheap — these balls should
  be becoming static, not lingering as rigidbodies).
- Possibly decouple: the adaptive cap should throttle *thaw rate*, but never
  prevent *freezing/demotion*, which must always be able to run.

## Repro
1M FCC (press 4), undermine a long tunnel (U) so a few thousand balls thaw at
once. Press P repeatedly. If `simEma > targetSimulateMs` while `freezes/step`
sits at 0 and `live` stops falling, it's stalled. Raising `targetSimulateMs` (e.g.
to 0 = off) should let it finish — a useful confirmation, not the real fix.

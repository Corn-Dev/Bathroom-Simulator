# Ball Physics Stress Test (throwaway)

Local, no networking. One job: find out **how many real rigidbody balls Unity
can simulate at once**, and what a big collapse costs. This decides whether the
"hybrid pit" plan is viable — where the surface layer is REAL physics balls that
roll/settle/collapse for real, over a cheap static deep mass.

## How to run

1. New empty scene.
2. Create an empty GameObject.
3. Add the **BallPhysicsStressTest** component.
4. Press Play.

Bin, camera, light, ball pool, and UI are all built procedurally.

## Controls

| Input | Action |
|---|---|
| **Space** / "Pour N" | Pour `pourBatch` balls in from above |
| **F** / "Fill to target" | Keep pouring until `fillTarget` balls exist |
| **C** / "Collapse!" | Drop the floor so the whole pile cascades (worst case) |
| **W** / "Wake all" | Force every ball awake (max stress: all simulating) |
| **R** / "Reset" | Clear all balls |
| "Force-sleep: ON/OFF" button | Toggle the force-sleep assist live |
| **L** / "Log now" | Log a snapshot to the console |
| RMB-drag / wheel | Orbit / zoom |

## Settling (making balls stop sooner)

Balls that keep jittering just above the sleep threshold stay AWAKE (expensive)
and can take ~30s to fully settle. Three levers reduce that:

- **Linear/angular drag** (`linearDrag`, `angularDrag`) — rolling/spinning balls
  bleed speed and come to rest instead of coasting. Applied at pool build, so
  change in Inspector then press Play.
- **sleepThreshold** — raise it so balls sleep at a higher speed (give up sooner).
  Also applied at build.
- **Force-sleep assist** (`useForceSleep`, toggle live) — any ball below
  `forceSleepSpeed` for `forceSleepDelay` seconds is explicitly put to sleep.
  This is the big hammer: it guarantees packed balls stop instead of jittering
  forever. Toggle it on/off live to see the difference in AWAKE count and the
  "Settle time" readout.

The **Settle time** readout + the `SETTLED in Xs` console line directly measure
how long a disturbed pile takes to come fully to rest — the thing to minimize.

## What to read (this is the whole point)

- **Total vs AWAKE balls.** Unity sleeps settled rigidbodies automatically. A
  pile at rest costs almost nothing — only AWAKE balls actually simulate. This
  is the key proof of the hybrid idea: once balls settle (deep in the pit) they
  sleep and are nearly free, so only the moving surface layer costs anything.
- **FPS avg / worst.** Worst = the hitch during a cascade.

### Suggested test sequence

1. **Steady-state ceiling:** Press **F** to fill to 3,000. Watch FPS as it fills,
   then let it settle. Once settled, AWAKE should drop toward 0 and FPS recover.
   Note the FPS *while pouring* (that's your "how many can move at once" number)
   and *at rest* (should be high regardless of total).
2. **Push the total:** Fill repeatedly (raise `fillTarget` / `maxBalls`) to find
   where even the *settled* pile drags — that's the hard ceiling on total balls.
3. **Cascade cost (your hill scenario):** Fill a big pile, let it settle, then
   hit **C** to drop the floor. Everything wakes and falls at once — watch AWAKE
   spike and FPS dip. That dip is the worst case: digging the middle out of a
   hill so the whole thing collapses.
4. **Absolute worst:** **W** force-wakes everything at the current total — the
   theoretical max of "every ball simulating at once".

### What the numbers tell us

- If a few hundred AWAKE balls holds 60fps, the hybrid works great: a surface
  layer that thick is plenty, and cascades of that size are fine.
- If it chokes at a few hundred awake, we need to cap how many balls can be
  "active" at once (throttle promotions, spread cascades over time).
- The settled-pile total ceiling tells us how big a FULLY-physical pit could be
  before we must fall back to static data for the deep mass.

## Physics optimization knobs (set in Inspector, then press Play)

These apply at startup, so change them in the Inspector and re-enter Play to A/B.
Each log line records the config (`hz=`, `solver=`, `render=`) so you can compare.

- **instancedRendering** (default ON) — draws all balls via one GPU-instanced
  path instead of a MeshRenderer per ball. This isolates render cost from physics
  cost. Toggle OFF to see how much of the "N balls present" cost was just
  rendering 30k MeshRenderers.
- **physicsHz** (default 40) — physics ticks/sec. 50 is Unity default; dropping to
  40 or 30 is a large CPU saving across all awake balls, at a small accuracy cost.
- **solverIterations / solverVelIterations** (default 4 / 1) — lower = cheaper
  per contact, squishier stacks. 4/1 is usually fine for balls; raise if stacks
  jitter or sink.
- Balls use discrete collision, no interpolation, drag + force-sleep (cheapest
  viable settings).

### How to find the real ceiling cleanly
1. With instancedRendering ON, fill to 30k and let it settle (awake=0). Read FPS.
   Compare to the earlier ~11fps with per-ball renderers — the difference is pure
   render cost we removed.
2. Whatever FPS remains at awake=0 is the true **physics-presence** cost of that
   many rigidbodies. That number sets the hard cap on concurrent rigidbodies and
   therefore how thick the hybrid's active band can be.
3. Lower physicsHz / solver and repeat to see how much headroom tuning buys.

## Prototype simplifications (NOT bugs)

- Discrete collision + no interpolation on the balls (cheapest settings) — this
  is a raw physics-throughput test, not a visual-quality test. Real game would
  tune these up for the few balls that matter.
- Balls are individual rigidbodies with sphere colliders (the honest case).
- No grid, no counting, no networking here — that's the other prototype. This
  isolates physics throughput only.
```

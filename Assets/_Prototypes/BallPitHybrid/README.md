# Hybrid Pit Prototype (the merged one)

Local, single-player, **no networking**. This is the synthesis of everything the
earlier prototypes + research taught us. It is the architecture the steering doc
(`networking-and-pit.md`) now records as the plan.

## The model

Three layers, each doing only what it's good at:

1. **BULK = voxel grid data** (`VoxelPitGrid`) — the authoritative pit. 1 cell =
   1 ball, so the count is always exact. Buried balls are never objects.
2. **SURFACE = instanced render + one heightfield collider** (`VoxelPitRenderer`,
   `VoxelSurfaceCollider`) — only cells near an exposed face are drawn (buried
   culled); one mesh collider for the whole surface, not per-ball colliders.
3. **ACTIVE BAND = real rigidbody balls** (`ActiveBallManager`) — the ONLY real
   physics balls. Surface cells **promote** to real balls when disturbed (grab,
   throw, dig) and **demote** back into the grid once they come to rest —
   removed from physics entirely, not left sleeping.

**Hard cap** on concurrent rigidbodies (`maxActiveBalls`, default 1200). A big
dig/collapse promotes balls up to the cap; any overflow is removed as data and
resolved by the cheap grid settler. So the physics budget is bounded no matter
how large the cascade — this is the guardrail that makes "dig the base of a
hill" safe.

## How to run

1. New empty scene.
2. Create an empty GameObject.
3. Add the **HybridPitController** component.
4. Press Play. (Everything builds procedurally.)

## Controls

| Input | Action |
|---|---|
| **Left click** on pit | Grab the clicked ball into your hand (promotes to real physics, animates to hand) |
| **Right click** (no drag) | Throw the held ball — it rolls/bounces for real, then demotes to the pit when it rests |
| **Right-drag** | Orbit camera |
| **Wheel** | Zoom |
| **B** / "Dig" | Bulk-dig a crater under the cursor (promotes real balls up to the cap; overflow collapses as data) |
| **[ ]** | Bulk-dig size |
| **1-5** | Pit size presets (10k / 20k / 40k / 75k / 164k) |
| **R** | Reset |
| **L** | Log a snapshot |

## What to watch

- **ACTIVE balls** readout — the real rigidbody count. It should rise when you
  dig and fall back toward 0 as balls settle and demote. It must never exceed
  the cap (the guardrail).
- **FPS** during a big dig vs at rest. The whole point: digging stays smooth
  because only the active band is real physics, and it demotes back to cheap data
  once settled.
- **Collapse feel** — balls should roll into craters for real (not teleport),
  because the moving balls are genuinely physical.
- **Count stays exact** — remaining + active balls = the balls that exist.

## This verifies the plan

- Real ball-pit feel (rolling/settling/throwing) via the active band.
- Scales to 100k+ cells because the bulk is data, not bodies.
- The cap + demotion keep the rigidbody count bounded, which the research showed
  is the hard requirement (thousands of coexisting rigidbodies is what kills fps,
  not motion).

## Collapse is REAL PHYSICS — one unified rule (the big rewrite)

There is NO data-level settler anymore (no teleporting cells). Everything the
player sees move is a real physics ball. One rule drives it all:

> When a grid cell loses support (the cell below it was removed), it PROMOTES to
> a real physics ball and falls/rolls for real. When a physics ball comes to
> rest, it DEMOTES back into the static mesh. That's the whole system.

- Grab, dig, a thrown ball landing in a hole, balls spilled outside — all go
  through the same path: cell removed → unsupported neighbors promote → they
  fall as physics → they rest → they remesh.
- The collapse FRONT propagates naturally: a promoted cell empties, which
  unsupports the cell above/beside it, which promotes next, etc. — carried by
  real falling balls, never by teleporting data.
- `supportPromotesPerFrame` bounds how many cells promote per frame; the active
  cap bounds total concurrent rigidbodies. Overflow stays queued and promotes as
  balls settle and free up budget (worst case: the top briefly waits — "frozen"
  only in a pathological full-pit collapse).

### Fast settling (so balls don't roll forever)
High linear/angular drag + a generous rest threshold + short demote delay, so a
falling ball rolls a little and settles quickly into the mesh instead of
bouncing around for a minute. Tunables: `demoteSpeed`, `demoteDelay`, and the
drag on the active balls.

## The whole play area is one grid (model)

The grid now spans the **entire play area**, not just the pit: a central PIT
region (`pitX/pitY/pitZ`) that starts full, surrounded by an empty FLOOR margin
(`floorMargin`) where dumped balls accumulate. The pit sits in the middle like an
in-ground pool; the floor around it is the rest of the grid at ground level.

**Demote rule: a ball that comes to rest ANYWHERE in the grid becomes a cell** at
the top of its column — pit or floor, no distinction. So if the player sucks
30,000 balls out and dumps them on the floor, they settle into cheap grid cells
instead of staying 30,000 live rigidbodies. Only MOVING balls are ever real
physics; the moment anything rests, it's data. This fixes both the lag concern
and the earlier "balls teleport back into the hole" / "stuck active forever"
bugs (which came from the old pit-only, height-gated demote).

## Seamless promote/demote (remesh + physics handoff)

- Grab and bulk-dig force an **immediate** surface rebuild (`forceRebuild`), and
  demotes trigger an immediate rebuild too — so the grid ball appears the SAME
  frame the physics ball leaves (and vice versa). No flicker/gap.
- The ~40ms full-pit rebuild throttle (`rebuildInterval`) now applies ONLY to
  continuous data-settling cascades, which is what actually caused the stutter —
  discrete grab/dig/demote events always rebuild immediately for a clean handoff.

## Accurate click selection + highlight

- A **yellow highlight sphere** is drawn around whatever ball the cursor is
  pointing at, every frame, so you see exactly what you'll grab.
- Selection uses a true **ray-vs-sphere** test against the visible surface balls
  (nearest hit along the ray), not column-mapping — so the highlighted/grabbed
  ball is the one actually under the cursor. In the real game this becomes
  range-limited to the player; ignored for now.

## Known limitations / still rough (NOT the functional bugs above)

- **Top layer looks flat.** The heightfield collider uses the max neighbor
  height (plateaus), and a fresh pit starts perfectly level — variation comes
  from digging. Making the resting surface bumpier is a cosmetic follow-up.
- The full-pit rebuild is still O(all cells); a region-only rebuild is the next
  perf optimization if the throttle isn't enough.

## Known prototype simplifications (NOT bugs)

- Reuses the `BallPitProto` namespace classes from `../BallPitVoxel` (grid,
  settler, renderer, surface collider, profiler) — same assembly, no copies.
- Demotion places a resting ball at the top of its column; mid-cascade this can
  briefly look slightly off until the next surface rebuild. Fine for a prototype.
- No networking, no save, no "sell to container" yet — this isolates the hybrid
  physics/rendering model.
- PhysX broadphase is still default SAP here; the steering doc notes testing MBP
  for the real flat pit as a further win.

# Static Collider Experiment

Decides whether the proposed "exact-pose + static-shell" ball-pit architecture is
viable, by testing its two load-bearing claims with the confounds that polluted
our earlier measurements controlled for.

## The two claims under test

1. **Static SphereColliders are cheap at rest.** A resting ball becomes a static
   `SphereCollider` with NO Rigidbody. Statics don't join PhysX simulation
   islands and carry no solver/sleep bookkeeping, so tens of thousands of them
   should cost far less than the ~30k *sleeping rigidbodies* that gave us 11 fps
   earlier. **This is the number that makes or breaks the architecture.**
2. **A thin active layer over the static shell performs well.** 1-2k real
   rigidbody balls falling onto the static mass should settle into small islands
   at good framerate.

## Why our earlier number was suspect

The "30k asleep = 11 fps" result had confounds:
- GPU instancing "not helping" may have been the **SRP Batcher** overriding it —
  i.e. the cost was rendering, not physics.
- A **fixed-timestep death spiral** (frame > fixed step → extra physics steps per
  frame) can look like per-body cost.
- **Editor overhead** on a 30k-object hierarchy.
This experiment removes all three: rendering fully toggleable (instanced draw,
no per-object MeshRenderers), a fixed-steps-per-frame readout, and it must be run
in a build.

## How to run

1. New empty scene, empty GameObject, add **StaticColliderTest**.
2. **Build a Development Build** (not the editor) and run it. Editor overhead is
   itself a confound on this many objects.

## Controls

| Input | Action |
|---|---|
| **S** | Add `staticBatch` (10k) static SphereColliders — the resting shell |
| **D** | Drop `activeBatch` (1k) rigidbody balls onto the shell |
| **V** | Toggle rendering (physics keeps running) — isolate physics cost |
| **T** | Log a broadphase intent (SAP/ABP/MBP). Unity can't switch broadphase at runtime — set it in Project Settings > Physics > Broadphase Type and relaunch to compare |
| **X** | Clear everything |
| **L** | Log snapshot |

## The test sequence

1. **Static cost curve:** press **S** repeatedly (10k → 20k → … → 60k). After each,
   let it sit and read FPS. Then press **V** to turn rendering OFF and read FPS
   again. The render-off number is the PURE physics cost of that many static
   colliders at rest. **Compare to the old 11 fps @ 30k sleeping rigidbodies.**
2. **Active over static:** with a big static shell present, press **D** to drop
   1-2k rigidbodies onto it. Watch FPS during the fall and after they settle, and
   watch `awake` drop as they sleep. Confirm the active layer is cheap over the
   static mass.
3. **Fixed-steps readout:** if it ever reads >1, a timestep spiral is inflating
   the apparent cost — note it.
4. **Broadphase:** set SAP vs ABP vs MBP in Project Settings and relaunch to see
   which handles the wide flat field best.

## How to read it

- If **60k static colliders hold a high FPS (render off)** and **1-2k actives
  settle smoothly on top**, the architecture is validated — resting balls as
  static colliders is cheap, and the physics ceiling is far above our old 1,200
  estimate. Proceed to build the real system.
- If static colliders are *also* expensive at scale, the static-shell idea is out
  and we rethink (CPU granular solver, or capped visible-collider shell only).

## What this does NOT test (deliberately out of scope)

- The exact-pose data store / spatial hash / chunk paging.
- Promote/demote (freeze on sleep, thaw on disturbance).
- Networking / save.
These come next IF this experiment validates the static-collider foundation.
This file isolates the single most load-bearing assumption first.

# Ball Pit Voxel Prototype (throwaway)

Local, single-player, **no networking**. Exists to feel out the voxel pit:
how it looks, the promote/hold/throw grab loop, and whether digging + collapse
(settling) behaves. Backs `.kiro/steering/networking-and-pit.md`.

## How to run

1. In Unity, create a new empty scene.
2. Create an empty GameObject (GameObject > Create Empty).
3. Add the **BallPitProtoController** component to it.
4. Press **Play**.

Everything is built procedurally: the voxel pit (default 25 × 16 × 25 = 10,000
balls), camera, light, pit walls, ground, instanced rendering, a generated
**surface collider**, a pool of real physics balls, and the UI. Nothing else to
place.

## How it works (the three layers)

- **Visible balls** = GPU-instanced visuals, no colliders. Only cells within
  `shellDepth` layers of an exposed face are drawn; the deep buried interior is
  culled (the "don't render buried blocks" trick). Balls are packed like a real
  settled pit — hex-close-packing offsets so each ball nestles into the valley
  below, compressed vertical layer spacing so layers interlock, overlapping
  `ballScale` so neighbors touch, and per-ball jitter for a natural surface. The
  `shellDepth` back layer plugs the front layer's gaps so you can't see through
  to empty space. Dark walls/background mean any residual gap reads as "pit
  depth", never bright emptiness.
- **The surface** = one generated mesh collider that follows the column heights.
  Regenerated when the grid changes. Promoted balls roll on THIS.
- **Promoted balls** = real pooled physics spheres. Only the few you grab/scatter
  are ever real.

## Controls

| Input | Action |
|---|---|
| **Left click** on the pit | Grab: 1 ball goes into your hand, and `balls-per-grab − 1` adjacent balls pop out and scatter |
| **Right click** (no drag) | Throw the held ball forward |
| **Right-drag** | Orbit the camera |
| **Mouse wheel** | Zoom |
| **B** | Bulk-dig `balls-per-grab` balls under the cursor (auto-scatter — stress test) |
| On-screen `-` `+` `x10` | Change balls-per-grab (affects both left-click adjacency and B) |
| **R** | Reset / refill the pit |

Grab a ball (LMB), watch it fly to your hand, then throw it (RMB click). Balls
roll and bounce on the pit surface and the ground.

## What to watch

- **Draw calls** stay tiny for 10k balls (instancing works).
- **FPS** under big bulk digs — crank bulk size with `x10` and hit **B** in the
  middle to trigger large collapses + scatter.
- **Collapse** — holes should slump; the surface collider updates so balls roll
  into craters. Watch for floating balls, gaps that never fill, or the surface
  mesh recook stuttering on huge digs.
- **Promote seam** — the grabbed ball should appear exactly where the surface
  ball was (no pop).

## Known prototype simplifications (NOT bugs)

- The pit interior is solid collision — balls rest on and roll across the
  surface, they don't tunnel into the mass. Deliberate ball-pit behavior.
- Collapse of BURIED cells is still data-only (instant in data); the rolling you
  see is the promoted physics balls on the surface. Deep slump isn't physically
  animated because buried balls aren't real objects — by design.
- Outer-wall balls (against the opaque pit walls) are still drawn even though
  you can't really see them — culling those needs wall-awareness, skipped for
  now. The big win (not drawing the buried interior) is in.
- Grab maps the surface hit point to the nearest column — simple, not final.
- Thrown/scattered balls don't re-enter the grid; they just come to rest.
- No networking, no save.

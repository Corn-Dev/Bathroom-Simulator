# Render fill-cost finding (for the strong Kiro — pit renderer optimization, not a bug)

NOT a regression. The collapse stall fix and NetPit protocol 3 are proven innocent
(`sim`/`step` ≈ 0.5 ms with `active=0`; fps does not track draw count). This is an
inherent characteristic of the ball shader, surfaced now because VSync was turned
off this session (uncapping fps makes the real GPU cost visible) and because the
test camera sits close to / inside the pit.

## Measured (1M FCC, settled, active=0)

| Camera | drawn | fps | SetPass/Batches |
|---|---|---|---|
| far | 28,860 | 110–150 | lower |
| closer | 24,954 | 26–41 | **higher** |
| super far | 279,396 | 31–47 | higher |

Key point: the CLOSE view draws FEWER balls (24,954) yet runs SLOWER (26 fps) than
the far view that draws MORE (28,860 → 150 fps). So fps is driven by **camera
proximity**, inversely, and is **independent of the drawn count**. SetPass/Batches
rise close-up and at the super-far extreme, dip in the mid zoom-out.

## Diagnosis: fragment fill-rate + extra passes, not draw count

- Close-up, each ball covers a huge number of screen pixels, so the per-fragment
  cost of `BallPitInstancedBall.shader`'s ForwardLit pass runs for millions of
  fragments: main-light + soft shadow sampling (`_SHADOWS_SOFT`), SSAO
  (`GetScreenSpaceAmbientOcclusion`), SH ambient, spec. Classic overdraw/fill
  bound.
- The SetPass/Batch rise close-up is the ShadowCaster pass + the occlusion
  depth pre-pass (prev-frame visible set) re-rendering the same instanced meshes,
  not new unique draws (resting balls are still only 2 indirect draws).
- Super-far ticks up because 279k tiny instances load the cull/vertex/args side
  even though each ball is a few fragments.

## Optimization directions (strong Kiro's call)
- Shadows: `ballsReceiveShadows` + the ShadowCaster pass are a big per-fragment
  cost that scales with screen coverage. Consider cheaper/disabled ball shadows,
  or a coarse shadow proxy, especially for deep/close balls.
- SSAO per-fragment: gate or cheapen `_SCREEN_SPACE_OCCLUSION` for balls.
- Overdraw: a depth prepass for the resting balls so the expensive ForwardLit
  fragment only runs on the front-most layer (packed spheres overdraw heavily).
- LOD: push more balls to the far (cheaper) LOD sooner; simplify the far fragment.
- These are GPU/shader optimizations; none affect simulation, networking, or
  correctness.

## Repro / live toggles already in the controller
- `O` toggles occlusion culling (tests the occlusion-pass cost).
- Inspector `ballsCastShadows` (default off) / `ballsReceiveShadows` (default on):
  unticking receive-shadows should raise close-up fps if shadows are the cost.
- Dev-only settings added this session: `Application.runInBackground = true` and
  `QualitySettings.vSyncCount = 0` (the latter is why the honest fps is now
  visible; a shipped game can re-expose a VSync/quality option).

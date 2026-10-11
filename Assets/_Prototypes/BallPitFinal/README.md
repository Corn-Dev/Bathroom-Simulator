# Ball Pit Final — GPU-driven rewrite (integrated)

This is the reviewed rewrite from the second model, integrated into the project.
It replaces the earlier CPU chunked-renderer version. Architecture unchanged in
spirit (exact-pose records, static-collider resting shell, promote/freeze
physics, exact ledger) but every subsystem is stronger.

## Files (all in `Assets/_Prototypes/BallPitFinal/`)

- `BallStore.cs` — source of truth. STABLE ids (no swap-remove); balls that leave
  play become `Stored`/`Lost`. Per-state-per-color ledger, exact by construction.
- `SpatialGrid.cs` — bounded uniform grid, intrusive linked lists in lazily
  allocated 8³ chunks. No Dictionary, no per-frame allocations. (Replaces the old
  `SpatialHash`.)
- `ActivePhysics.cs` — the only live Rigidbodies. Own prev/curr interpolation
  (pooled bodies never streak). Reads `rb.position`.
- `ColliderShell.cs` — static SphereColliders near activity, pooled, toggled via
  `Collider.enabled`, lazy expiry.
- `PitSimulation.cs` — ALL rules: FCC overlap-free fill, thaw/freeze, support-loss
  collapse (frictionless angular-gap stability test), impacts, grab/throw/dig,
  grid-DDA raycast, ledger/index verification. Hooks: `OnThawed/OnFrozen/OnLost/
  OnRestingChanged`, `Deposit`.
- `GpuBallRenderer.cs` — GPU-driven. Persistent id-indexed instance buffer
  (scatter-upload of dirty ids only), two-phase occlusion (previous frame's
  visible set → eye-depth target → 8×8 tile maxima → frustum+occlusion cull →
  append near/far LOD lists → `DrawMeshInstancedIndirect`). Async stat readback.
- `BallMeshes.cs` — true icosphere (per-pixel sphere normals in the shader).
- `BallPitFinalController.cs` — thin orchestrator: input, camera, hover, HUD.
  Physics in `SimulationMode.Script`.
- `Resources/BallPitCull.compute`, `Resources/BallPitInstancedBall.shader`,
  `Resources/BallPitOccluder.shader` — MUST stay in a `Resources` folder
  (loaded by name at runtime).

## How to run

New empty scene → one empty GameObject → add `BallPitFinalController` → Play.
It builds its own camera, light, ground, walls, pools, and renderer. Remove any
other camera from the scene. Default pit = 100×100×100 = 1,000,000 balls.

Controls: LMB grab, RMB throw (or drag orbit), B dig, wheel zoom, O toggle
occlusion, F toggle flipY (occlusion safety valve), R rebuild, L log+verify.

## Project settings to check (from the handoff)

1. Graphics API DX11/DX12/Vulkan (compute + StructuredBuffer in vertex stage,
   shader model 4.5).
2. Player > Active Input Handling: Input Manager or Both (uses legacy Input).
3. Optional: URP Renderer > Add Renderer Feature > SSAO (the ball shader already
   supports `_SCREEN_SPACE_OCCLUSION`). Needs Depth Texture on the URP asset.
4. Physics > Broadphase Type: try Automatic Box Pruning; keep whichever profiles
   faster.
5. Balls render in the Game camera only, not the Scene view — intended.

## Known first-run risks (uncompiled handoff — fix minimally if they appear)

- `BallPitInstancedBall.shader`: if `_LightDirection` is reported redefined in the
  ShadowCaster pass, delete the local `float3 _LightDirection;` line. (URP 14's
  Shadows.hlsl does not declare it, so it should be fine as-is.)
- If balls vanish only when occlusion is ON, press **F** (flipY). If F fixes it,
  set `flipOcclusionY = true` default and note it.
- Verified present in URP 14.0.9: `GetScreenSpaceAmbientOcclusion`,
  `GetNormalizedScreenSpaceUV`, `PackNormalOctQuadEncode`, `PackFloat2To888`,
  `NormalizeNormalPerPixel`, and `Lighting.hlsl` includes `AmbientOcclusion.hlsl`.

## Verify (report numbers)

1. Culling correctness: press O; the image must not change, only the drawn count.
2. Gap bug: grab ~30 (LMB) and dig (B) ~10 holes; look into holes — no missing
   balls, no see-through specks.
3. Collapse: dig deep near a wall and in the middle; balls roll in, settle, active
   count returns to ~0; no snap, no floating.
4. Ledger: press L; `check=OK` required.
5. Report drawn near/far, FPS avg/worst, active/colliders/capHits/queue, and
   Profiler ms for FixedUpdate / LateUpdate / Physics.Simulate at: whole pit from
   outside, close to the surface, inside a crater.

## Not in scope yet
NGO replication, saves, containers/sorting, baked pour-and-settle fill, Burst.
Hooks exist (`OnThawed/OnFrozen/OnLost/Deposit`, stable ids).

---
inclusion: manual
---

# Networking & the Pit — Technical Design

Living technical record for how we network a HUGE pit of balls without melting
the connection, while keeping the illusion seamless: from the player's point of
view they are simply grabbing, excavating, and sorting real balls. The
optimization must be invisible. Decisions marked **[DECIDED]** are firm;
**[OPEN]** needs a call before it drives code; **[ASSUMPTION]** is a working
default we can revisit.

> Companion to `product.md` and `game-design.md`. Those two reference this file
> for "why the pit is data, not thousands of objects." This is that doc.

> **Game structure (2026-10):** the game is a BALL-PIT FACTORY; one venue, one pit
> location, the pit reconfigured in place into successive "shipment" pits toward a
> ~1M finale — see `docs/direction/GAME-DIRECTION.md` and game-design.md §1b. The pit
> tech below is STRUCTURE-AGNOSTIC and unchanged by this: shipment switches ride the
> same `PitWorldBuilt`/`PitWorldDisposed` events, one live session, no host restart.
> (An earlier "4–5-pit linear door campaign" framing is superseded; the tech reuse is
> identical either way.)

> **STATUS (2026-10): Phase A WORKING in-editor (host single-player).** First-person
> spawn, WASD/look/jump/sprint, drop into pit, stand on pile, wall-climb + mantle
> (incl. while carrying), grab/drop/throw with crosshair tint + highlight — all pass.
> `check=OK`. Empty Game-Adapter slot on the bootstrap is CORRECT (the installer
> SetAdapter at -1000 feeds it). Validator 4 errors = unbuilt sizes (only large_1
> built) — expected. NOT yet tested: multiplayer (no 2nd machine) and standalone
> builds. Test findings triaged:
> - EDITOR-ONLY (ignore): balls turn black after editor focus loss/regain — GPU
>   instance buffer context loss; should not happen in a build (confirm in a build).
> - BY DESIGN (not bugs): (a) balls aren't pushed when standing ON TOP of the pile
>   — resting balls are static colliders; `PlayerConfig.playersDisturbPile` is a seam
>   (default off, unimplemented, A36) for optional footstep-scatter later. (b) The
>   pit/wall GAP — the pit's REAL collision walls are invisible proxies built from
>   the fill geometry (hug the ball mass); authored art walls are decorative. Author
>   walls to the cyan interior gizmo and tighten venueBounds (Fit Venue Bounds To
>   Pit) so balls don't rest in empty floor. Venue authoring is pit-dimensions-FIRST,
>   art-second.
> - REAL, LATER TUNING: walking on the pile feels sticky/snagged (motor catching on
>   0.5m sphere colliders) — a PlayerConfig/motor-feel pass.
> - MEDIUM (carry-mantle stall) did NOT reproduce this test.

> **Networking still unverified for the Steam transport only.** Replication + lobby
> are proven locally (UTP, 90fps both instances; host-path session test). A VM
> CANNOT test this (no real GPU → the GPU-driven renderer returns garbage and
> crashes). Needs a 2nd real machine (+ 2nd Steam account) or a friend. The Steam
> transport is the lowest-risk piece (the Phish foundation already shipped on it).
>
> **Release-readiness item (later):** `GpuBallRenderer` throws (black screen) when
> compute/GPU is unsupported — add a graceful "GPU not supported" check before ship.

> **BUILD STATUS (Deliverable D, written by trace; NOT compiled or run): additive NetPit
> seams for an in-session pit rebuild.** `NetPit.cs` gained `BeginRebuild()`,
> `RebindAuthority(PitAuthority)` (host), `ResetClientWorldForRebuild()` (client; ends by
> re-sending Hello), a per-link `forceFullNextReady`, and a `rebuilding` flag guarding six
> host entry points. Wire format and `Protocol = 3` are unchanged; with no rebuild every
> guard is a no-op. They exist because `A` is bound once at `StartHost` and the client
> never nulls `CW`, so a second pit in one live session was otherwise impossible without a
> NetPit restart. `BallPitFinalController.Campaign.cs` adds `RebuildActivePit` etc. The
> two-instance host+client advance, late join and resume are UNVERIFIED until the owner
> play-tests them. See `docs/handoff-D/` (HANDOFF-D §2, §7).

---

## The core problem

The pit is visually full of balls — think thousands. The naive implementation
(one networked, physics-simulated GameObject per ball) is not viable, for two
independent reasons:

1. **Physics cost.** Thousands of live Rigidbodies all colliding in a packed
   volume is one of the worst cases for a physics engine. Even single-player,
   packed Rigidbody piles tank the frame rate.
2. **Network cost.** This is the harder wall. Unity Netcode for GameObjects
   (NGO) does NOT scale to thousands — or even hundreds — of moving networked
   objects. Community testing consistently shows NetworkTransform struggling at
   ~100 moving objects, and a few hundred networked movers pushing clients below
   30 fps. The server's reliable-data budget to each client is roughly on the
   order of ~1 MB/s. (Sources below.)

So the design rule is: **the pit is data, not objects. Only the handful of balls
a player is physically touching are ever real.** **[DECIDED]**

The hard constraint on top of that rule: **the player must never feel the
optimization.** The ball they grab is the exact ball that was sitting in that
spot, that color. Digging leaves real holes where they dug. The pit drains
unevenly, shaped by where players work. If any of that feels faked, the model
has failed.

---

## Prototype findings (measured — these supersede earlier assumptions)

We built two throwaway prototypes under `Assets/_Prototypes/`
(`BallPitVoxel` and `BallPhysicsStress`) and measured on a dev machine
(single-player, no networking, no gameplay). Numbers are machine-relative but
the *shape* of the results is what matters. **[DECIDED]** based on these.

### Rendering a huge pit is solved by surface culling
- Drawing one GPU-instanced sphere per filled cell for the whole volume: a 164k
  pit ran ~10 fps — but the cost was **rendering all the buried balls**, not the
  voxel logic, physics, or networking.
- Culling to only cells near an exposed face ("don't render buried blocks")
  dropped drawn balls from 164k to ~18k and took 164k to ~38 fps; a 40k pit hit
  ~60 fps. Diagonal-aware culling + overlapping ball scale removes see-through.
- **Takeaway:** rendering scales fine to 160k+ cells with surface culling. The
  grid/data model itself is cheap.

### Real physics balls are viable BUT bounded by two separate limits
The big pivot: grid-only "fake" collapse felt bad (balls teleport between cells,
nothing rolls). A ball pit's whole appeal is that the balls are physical. So we
tested how many REAL rigidbody balls Unity can handle. Results:

- **Awake (moving) count is the live-cost budget.** ~90 awake ≈ 65 fps;
  ~3,000 awake ≈ 40 fps; ~100,000 awake ≈ 2 fps. Cost scales with *motion*.
- **Settled balls sleeping is NOT cheap enough at scale.** Measured with the
  awake count genuinely at 0 (profiler overhead ruled out): **30,000 balls fully
  asleep still ran ~11 fps**, and ~50,000 present ran ~0–1 fps. There is a real
  per-rigidbody cost (collider in the broadphase, memory, engine tracking) that
  Unity pays even when nothing moves. So "let settled balls sleep as rigidbodies"
  does NOT scale — a pit-sized count of sleeping rigidbodies is still too much.
- **The ceiling on PRESENT rigidbodies (awake or asleep) is well below 30k** on
  the test machine — comfortable likely in the low thousands. This is the hard
  wall, and it is independent of motion.
- Force-sleep tuning (drag + a force-sleep assist) still matters for the *active*
  set because packed balls jitter above the sleep threshold and wake-propagate,
  taking ~30s to settle otherwise — but sleeping is a transient state on the way
  to **demotion**, not a resting place (see below).

### BREAKTHROUGH: static colliders, not sleeping rigidbodies, for resting balls
An external architecture review flagged that the "30k asleep = 11 fps" number was
measuring the wrong thing: a *sleeping rigidbody* still joins PhysX islands and
carries solver/sleep bookkeeping, so one touch can wake a 30k-ball island, and
the per-body cost persists. A **static collider (SphereCollider with NO
Rigidbody)** does none of that — it never joins an island, carries no solver
state, and is visually/physically identical to a frozen ball at the same pose.

We built `Assets/_Prototypes/StaticColliderTest` to test this directly, with the
earlier confounds controlled (instanced rendering, fixed-steps-per-frame readout).
Measured **in the editor (pessimistic)**:
- **60,000 static SphereColliders at rest: ~50 fps** (vs ~11 fps for 30k sleeping
  rigidbodies). Static colliders scale cheaply — the make-or-break result.
- **60k statics + ~3,700 awake rigidbody balls: ~21 fps**, but with
  `fixedSteps/frame = 2-3` (the fixed-timestep spiral inflating the number) and
  still in-editor. The static mass behind the actives barely affected their cost —
  because statics aren't in the actives' islands. Thesis confirmed.
- Caveat: the static *rendering* cost (60k instanced balls) wasn't cleanly
  isolated (a build issue blocked the render-off comparison), but rendering is
  already solved by surface culling — the real game never draws 60k balls, only
  the exposed shell (~a few thousand). So that decline won't exist in production.

**[DECIDED] This supersedes the "sleeping is too expensive" conclusion above for
the chosen architecture.** Resting balls are **static colliders at their exact
rest pose**, not sleeping rigidbodies. This is cheap at pit scale.

### Resulting model (updates the layers below)
- The deep, buried mass **cannot** be thousands of real rigidbodies — it must be
  **pure data (the grid)**, as the layers section already says.
- Only a bounded **active working set** near the surface / near a disturbance is
  real rigidbody balls (so collapse ROLLS for real, fixing the feel). They
  promote from the grid when disturbed and demote back to data when they settle.
- **Hard-cap the simultaneously-awake balls** and **spread big cascades over
  frames** (the "dig the base of the hill, it all collapses" case). The cap is
  the primary guardrail; find its value by feel, but keep it well under the
  ~3k-awake ≈ 40 fps floor — likely a few hundred.
- [OPEN] The ceiling on how many *settled* rigidbodies can exist at once before
  the at-rest cost bites (somewhere below 50k on the test machine). This decides
  how deep the "real but sleeping" band can be before cells MUST be data only.
  Needs one more targeted test (settle N, read at-rest fps, find the knee).

> Net: rendering is solved; physics-real collapse is viable for a bounded active
> set over a data deep-mass; the open number is the max concurrent rigidbodies
> (awake AND present), which drives the cap and the active-band depth.

---

## CHOSEN ARCHITECTURE: exact-pose records + static-collider shell [DECIDED]

The grid-of-colors model (1 cell = a ball drawn at the cell center) caused a
visible **snap/teleport** when a resting physics ball demoted into a cell,
because the lattice *supplies the position* and sphere rest-packing (~64%
random) never lands on a lattice. The fix (from the architecture review, then
validated by the static-collider experiment) is to stop letting the grid own
positions.

**Every ball always has one real, continuous position that every tier shares.**
The grid becomes a pure spatial index (buckets for neighbor queries, chunks for
paging/render/save/net) and NEVER supplies a position. Promote/demote change
*which system owns a ball*, not *where it is* — so the handoff is seamless, the
same reason engine rigidbody sleeping looks seamless.

### Per-ball record
`id, color, state, exact position` (skip rotation for solid spheres; a
compressed 32-bit quaternion only if balls have markings). ~1.1 MB for 164k at
16-bit-per-axis — memory is a non-issue.

### Ball states
| State | Physics | Rendering |
|---|---|---|
| Active | pooled Rigidbody | dynamic instance buffer |
| Resting (exposed) | **static SphereCollider at the exact pose, no Rigidbody** | per-chunk instance buffer |
| Buried | none | culled |
| Held | attached to player | player-side |
| Stored (container/hopper) | none — a count, no position | fill visual |

### The two handoffs (both seamless)
- **Freeze (active → resting):** trigger on PhysX's own **sleep** (not a speed
  timer — that was our "too eager" snap bug), freeze bottom-up (only a ball with
  no active support below it), **quantize the pose once on the host** (16-bit
  chunk-relative, sub-mm) and use that exact value for collider + render + save +
  net so every peer agrees bit-for-bit. Swap in one step: rigidbody → pool,
  enable static collider, move instance to the static buffer. The ball does not
  move → no snap.
- **Thaw (resting → active):** a resting ball thaws when the ball it rests on is
  removed/moves (grab, dig, vacuum, or an active ball shifting > a few % of a
  radius), or a fast mover's swept path hits it. Thaw at the exact stored pose,
  zero velocity, remove the static collider same step. Collapse then propagates
  at exactly the speed physics moves things.

### Why static colliders (the validated core)
Statics don't join PhysX islands (a touch never wakes a 30k-ball island), carry
no solver/sleep bookkeeping, and are indistinguishable from a frozen ball.
Collider count scales with **exposed surface**, not volume (granular slumping to
the angle of repose keeps the exposed area ~ the pit footprint). Add a collider
when a ball becomes exposed; remove lazily. If the shell is still too big, give
colliders only to resting balls within a couple diameters of active balls/tools.
**Validated:** 60k static colliders ran ~50 fps in-editor (vs 11 fps for 30k
sleeping rigidbodies); active cost was independent of the static mass behind it.

### Spatial index
One uniform hash, cells ~1 ball diameter, for neighbor/exposure/support queries.
Group into ~2-4 m **chunks** that own render buffers, dirty flags, versions, and
save/network pages. Out-of-pit floor piles are not a special case — just chunks.
A sparse chunk map makes empty floor free.

### Counting = a ledger, not the grid
Each ball has exactly one state; every transition goes through one host function
that updates per-color / per-zone counts. Exact counting is a ledger property,
independent of positions — this is what lets us drop the lattice. Add a
dev-build recount-from-records check.

### Supporting decisions
- **Bake the initial fill** offline (pour, settle, freeze) and ship as a binary
  asset per pit — a jittered lattice isn't physically consistent and pops when
  deep balls thaw. Clients get the same baked fill (simplifies networking).
- **Install Burst + Collections + Mathematics (NOT Entities)** for the data work
  (exposure tests, neighbor queries, instance packing). Coexists with
  GameObjects; no DOTS migration.
- **Angular drag / rolling resistance** on active balls — PhysX spheres have no
  rolling resistance, so floor balls roll forever and never sleep otherwise.
- **Guard the fixed-timestep spiral** (cap `Time.maximumDeltaTime` and/or the
  active count) — the experiment showed 2-3 physics steps/frame inflating cost
  under load.

### Networking (NGO, host-authoritative)
No `NetworkObject` per ball. One manager using `CustomMessagingManager` +
`FastBufferWriter`: reliable batched events (Thaw/Freeze/Remove/Spawn/Hold) at a
few bytes each, plus unreliable 15-30 Hz snapshots of *active* balls under a
per-client byte budget (priority: fast/near/on-screen). Clients interpolate,
never simulate. Freeze waits for sleep, so the client already shows the rest pose
when Freeze arrives → clients don't snap either. Join/resync via per-chunk diffs
+ versions/checksums. Players send intents; host resolves.

### Save
Store quantized records as a compressed `byte[]` base64 string (NOT a raw
JsonUtility numeric array). Active balls saved with velocity, thawed on load.

### Build order (from the review)
1. Re-measure in a dev build — DONE: statics validated cheap.
2. Stress 20-50k static colliders + 1-2k actives — DONE: passed.
3. Exact-pose store + spatial index + static shell — DONE.
4. Freeze-on-sleep + thaw / collapse rules — DONE.
5. Bake the initial fill — TODO (next; currently an FCC lattice + jitter).
6. Netcode streams, quantize at freeze — TODO.
7. CPU Burst granular solver — only if the active budget ever bottlenecks (not
   needed so far).

---

## ✅ CURRENT STATUS — the pit is BUILT and VALIDATED [DECIDED]

> **History note (no longer applies):** much of `BallPitFinal` and the delivered
> `MultiplayerKit`/players were produced by an external "strong Kiro" on a separate
> machine that handed over code for this repo to integrate. **That arrangement is
> OVER (2026-10).** Work is now done directly in this repo by various Kiro sessions
> (+ sometimes browser Claude). A fresh session OWNS the code — read it, change it,
> extend it; you are not just an integrator relaying to someone else. The milestone
> notes below are kept as an accurate PAST-TENSE record of how the code got here.

The working implementation lives in **`Assets/_Prototypes/BallPitFinal/`**. This
is the real foundation; the other `_Prototypes` folders (`BallPitVoxel`,
`BallPitHybrid`, `BallPhysicsStress`, `StaticColliderTest`) are earlier
throwaway experiments kept only for reference.

### Milestone 2 landed (integrated, pending in-Unity test)
The other Kiro's milestone-2 drop (handoff `Assets/reball/`, now integrated and
the staging folder deleted) added the networking layer + a collapse stress tool,
and split the old monolithic `PitSimulation` into three:
- **`PitSettings`** — all tunables (now incl. tools, fill type, ball layer, sleep
  threshold, collapse reserve).
- **`PitWorld`** — state shared by host AND client. The ONLY state writers are
  `ToResting` / `ToLive` / `ToGone`; everything (ledger, both grids, colliders,
  renderer) stays consistent through them. Clients replay host events through the
  same three functions, so every peer's ledger is exact.
- **`PitAuthority`** — host-only rules (thaw/freeze, support-loss collapse via a
  min-heap `SupportQueue`, impacts, tools, budget). Supports up to 4 players.
- New: **`Quantizer`** (Q21 poses, snapped once at fill/freeze so every peer
  agrees bit-for-bit; Q16 for moving-ball snapshots), **`NetPit`** (NGO
  host-auth replication via `CustomMessagingManager` — header/sync/events reliable,
  active snapshots unreliable, intents + view from clients; no per-ball
  NetworkObject), **`PitReplica`** (client-side interpolated live balls),
  **`RandomFill`** (poured ~0.58 loose packing, worker-thread, cached to
  `StreamingAssets/BallPitFills` — walls slump instead of FCC-terracing),
  **`CollapseBench`** (K-key repeatable load generator + CSV).
- New extraction primitive **`Excavate`** (V vacuum / U undermine → balls to a
  per-player hopper as data) and **`Dump`** (G → pour the hopper back as real
  physics). Replaces the removed B-scoop.
- `BallStore` gained a `Hopper` state. Controller has modes Offline / Host (H) /
  Client (J), keeps presets 1-5, and T toggles FCC/Random fill.
- Both shader fixes are the other Kiro's own canonical merge (its compute tile-math
  rewrite + the `CommonMaterial.hlsl` include).

Setup needed before testing: **Layer 8 must be free** (balls use it; env support
queries exclude it) — else set `settings.ballLayer`. **Build gotcha:** everything
is created in code (no scene material asset references URP's Lit), so a build
strips `Universal Render Pipeline/Lit` and the controller's material creation
threw in `Start()` → cameraless grey screen in the client build (editor was fine
because the editor loads all shaders). Fix: add `Universal Render Pipeline/Lit` to
Project Settings > Graphics > Always Included Shaders. The controller now also
builds the camera first and falls back to a built-in shader with a clear error if
Lit is missing, so a stripped shader can never silently grey the whole build again.
(The three BallPit shaders load via `Resources.Load`, so they already ship.) Status: integrated and
reviewed for cross-file API consistency; NOT yet compiled/run in Unity. Test plan
is in `Assets/reball/PROMPT2.md`'s §4 (now deleted with the folder — captured in
the hand-back report).

**Measured: 1,000,000 balls at ~125 fps** with real physics (grab/throw/collapse),
exact per-color counts, GPU occlusion culling, and no demote snap. Pit-size
presets (keys 1-5) go up past 2M. The rendering + physics scaling problem is
SOLVED.

### What's in BallPitFinal (the current architecture)
- `BallStore` — stable ids, per-state/per-color ledger (exact counts).
- `SpatialGrid` — allocation-free linked-list uniform grid (index only).
- `PitSimulation` — all rules: FCC overlap-free fill, thaw/freeze, frictionless
  angular-gap support test for collapse, impacts, grab/throw/dig, grid-DDA
  raycast. Physics runs in `SimulationMode.Script`.
- `ActivePhysics` — the only live Rigidbodies (bounded pool); own interpolation.
- `ColliderShell` — static SphereColliders near activity only, pooled.
- `GpuBallRenderer` + `Resources/BallPitCull.compute` + two shaders — GPU-driven
  render: persistent id-indexed instance buffer (scatter-upload dirty ids only),
  two-phase occlusion (prev-frame visible set → eye-depth → 8×8 tile maxima →
  frustum+occlusion cull → near/far LOD append → `DrawMeshInstancedIndirect`).
- `BallPitFinalController` — thin orchestrator (input/camera/hover/HUD).

### Gameplay hooks already in place (for the next phase)
`PitSimulation.Deposit(id)` (ball leaves play → `Stored`), and events
`OnThawed`, `OnFrozen(id,pose)`, `OnLost`, `OnRestingChanged`. Ball ids are
stable, so these are exactly what a save layer and a network layer key on.

### Milestone 3 integrated (collapse redesign — pending in-Unity test)
The other Kiro's milestone-3 drop (handoff `Assets/reball (1)/`, now integrated,
staging deleted) answers the collapse report. Changed: `ActivePhysics`,
`SupportQueue`, `PitAuthority`, `PitSettings`, `PitWorld`, `GpuBallRenderer`,
`PitReplica`, `NetPit`, `BallPitFinalController`; new `CollapseTelemetry` (the P
key). Fixes:
- **Finding 1b (active set pinned at 1,600):** removed the separate pool size —
  the rigidbody pool now grows lazily to `maxActive` (default 4000, prewarm 2048),
  re-read each step. No more `Min(maxActive, poolSize)` clamp.
- **Never-draining active set:** old freeze rule ("moved <2mm over stillSeconds")
  never fired in a jittering deep pile. New rule: speed < `freezeSpeed` for
  `freezeSeconds` with low drift, or PhysX island asleep; plus settle-damping
  (`settleDrag`/`settleAngularDrag`) so slow balls stop creeping.
- **Finding 1 (serial collapse):** `SupportQueue` is now a REGIONAL scheduler —
  vertical columns (`regionSizeDiameters`, 8 default), each lowest-first, drained
  round-robin so every stretch of a long tunnel collapses concurrently. Plus a
  **free-fall cascade**: a ball losing support with an open drop below is flagged
  falling, excluded as a support, and its dependents re-tested the same step, so a
  roof over a cavity drops together. Budget is now **concurrent rigidbodies only**
  with an **adaptive cap** (`targetSimulateMs`, self-calibrating per hardware).
- **Finding 2 (moving balls interactable):** `PitAuthority.IsInteractable` /
  `GatherInteractable`; `TryGrab` accepts Active balls (hands the live body to the
  carrier via `SetCarried`, no freeze first), `Excavate` takes moving balls, hover
  ray-tests the live set (`PitWorld.RaycastSpheres`) on host and client.
- NetPit → protocol 2 (header carries `maxActive`, not pool size). Auto-log no
  longer runs the O(N) ledger recount every 2s (was a ~10ms hitch at 2.35M); L
  still does the full check.
- Re-applied this repo's pipeline-agnostic shader fix on top (URP/Lit vs built-in
  Standard), since the other Kiro's controller still used bare `Shader.Find`.
> **COLLAPSE STALL — FIX APPLIED (2026-10, pending in-Unity re-test).** The
> intermittent deadlock (adaptive cap blocks thaws while the freeze rule can't
> retire near-idle rigidbodies, so `live` can neither grow nor shrink) is fixed by
> the strong Kiro's Part A: the freeze/settle rules now use a SMOOTHED velocity
> (`speedSmoothingSeconds`, EMA) so solver jitter cancels out, plus a STALL BREAKER
> (`PressureFreeze`) that force-freezes the slowest balls bottom-up whenever thawing
> is blocked and almost nothing is freezing — so demotion can always make progress.
> New tunables in `PitSettings`: `speedSmoothingSeconds`, `stallBreakSteps`,
> `stallMinFreezesPerStep`, `pressureFreezeSpeed`, `pressureFreezePerStep`. P
> telemetry gains `pressureFreezes/step`, `stallSteps`, `totalPressureFreezes`.
> `NetPit` is now protocol 3 (client `BP.Hello` handshake → header re-send, so
> joining no longer depends on callback ordering). Re-test: same repro, confirm
> `freezes/step > 0` and the queue drains; pressure-freezing should be rare.
> (The original write-up's staging folder has been deleted; this summary is the
> record. The collapse stall is FIXED and integrated.)

> **Lobby layer (MultiplayerKit) — NOW INTEGRATED** (this block is kept as history;
> see the top-of-file STATUS for current truth). The lobby/session package (~116
> files, 4 asmdefs, host save/resume, friends list + public browser + password +
> kick + mid-game join) was delivered, APPROVED, and is in `Assets/MultiplayerKit/`
> + `Assets/Game/BallPitSession/`. The owner is wiring its UI prefabs. It was
> integrated in phases: Phase 1 (stall fix) DONE; Phase 2 (code integration) DONE;
> Phase 3 = build scenes/prefabs in-editor (HANDOFF §7) + two-account test.
>
> **Phase 2 done (2026-10):** `Assets/MultiplayerKit/` (104 files, 4 asmdefs) and
> `Assets/Game/BallPitSession/` (12 adapter files) copied in; the 4 LobbyDelivery
> pit files swapped into `_Prototypes/BallPitFinal/` (controller + `.Session.cs`,
> PitAuthority + `.Restore.cs`), with our `runInBackground`/`vSyncCount` dev
> settings re-applied on top. The old lobby cluster (SteamLobby, MainMenuUI,
> NgoSessionCoordinator, MultiplayerContracts, the SessionState asmdef, etc. — 18
> files) deleted; kept movement/camera/voice/save/spawn + `SteamManager`. asmdef
> names already matched (`com.rlabrecque.steamworks.net`, `SteamNetworkingSockets
> Transport for Netcode for GameObjects`) — no placeholder edits, no fallback
> needed. URP asset now assigned in the project (pipeline discrepancy resolved).
> Phase 3 walkthrough for the user is `PHASE3_UNITY_SETUP.md` at the project root.

**✅ MOSTLY VALIDATED in Unity (2026-10) — see the stall bug above:** collapse looks natural and performs.
Random fill reads like a real poured pit (walls slump); FCC still available. Holds
**60+ fps even under heavy vacuum/collapse** on millions of balls — the user
couldn't make it meaningfully drop with normal tool use. Collapse propagates
along a whole tunnel concurrently (regional scheduler + free-fall cascade), no
longer the serial minutes-long crawl. The three milestone-3 findings
(pinned active set, never-draining pool, serial collapse) are resolved.
Remaining polish: Random fill has a **slow initial generation** the first time for
a given pit (tens of seconds; then cached to StreamingAssets) — a known tradeoff,
candidate for a future bake/speedup. Set inspector `maxColliders>=16000`,
`linearDrag=0.05`, `angularDrag=1.2`, `sleepThreshold=0.02`, `maxActive=4000`.

> **BOTTOM LINE: the pit is proven.** Rendering, physics feel, exact counting,
> natural collapse, AND host-authoritative networking all work at millions of
> balls. The single biggest technical risk of the whole game is retired. What's
> left is gameplay (containers/sorting/money/upgrades), the Steam-lobby rewrite to
> feed NetPit, saves, and the real player tools — all built ON this proven base.

### ✅ Networking validated (local UTP, 2026-10)
`NetPit` host-authoritative replication WORKS: editor-host + Development-Build-client
over local UnityTransport (127.0.0.1), **1,000,000-ball pit at ~90 fps on BOTH
instances**, client reached `synced, ledger matches host`, and host grab/throw
replicated to the client cleanly (client saw the ball move and the throw). This is
the big unknown de-risked. Still to test: client→host intents under load, a big
collapse over the wire, join-mid-game, and eventually the real Steam-lobby
transport (local test used UTP, no Steam). Known collapse throttle (Finding 1/1b in
the collapse report) is orthogonal and still pending the other Kiro's queue/pool
redesign.

> **Render-pipeline discrepancy found during this test:** URP 14.0.9 is in
> `manifest.json`, but there is NO `UniversalRenderPipelineAsset` assigned in
> Graphics/Quality settings — so the project is actually running the **Built-in**
> pipeline. That's why `Universal Render Pipeline/Lit` doesn't appear in the
> Always-Included-Shaders picker. The ball renderer's own shaders (loaded via
> `Resources.Load`) render regardless, and the controller now picks URP/Lit vs
> built-in `Standard` based on `GraphicsSettings.currentRenderPipeline`, so the
> prototype renders correctly either way. BUT the GpuBallRenderer shaders were
> authored for URP (the `Shadows.hlsl`/`LerpWhiteTo` fix is URP-specific), so the
> project is in a half-configured state. CLEANUP ITEM: either create+assign a URP
> asset (Create > Rendering > URP Asset, then set it in Graphics + Quality) to
> match the stated intent, or formally decide to stay Built-in. Not blocking;
> bundle with the eventual Phish->final rename pass.

### Remaining perf risks (per the user)
1. **Networking under load** — the active-set replication proved out at idle/light
   interaction; still need a big collapse + client intents over the wire, and the
   real Steam transport. Plan is in the "Networking" section above.
2. **Bigger tools** (vacuums, drones, mass extraction) that promote/disturb far
   more balls at once — bounded by `maxActive` + `supportThawsPerStep` /
   `impactThawsPerStep`; a huge tool must spread its work or it hits the cap.
   `PitSimulation.AddToolSource` is the hook.

### Why 30k rigidbodies was the wrong test (research follow-up)
Research into PhysX internals + DOTS + GPU physics (4 parallel investigations)
clarified that the "30k asleep = ~11 fps" result was measuring an architecture
**nobody should ship**, not a limit of the plan. Sleeping removes only the
*solver* cost; it does NOT remove: (a) 30k collider AABBs sitting in the
broadphase — and a wide flat pit is the pathological worst case for Unity's
default Sweep-and-Prune broadphase; (b) 30k GameObject/Transform/component
overhead every frame; (c) PhysX sleep is island-based, and a packed pit is a few
giant islands that wake together. So the conclusion is NOT "physics is too slow"
— it is **never have thousands of real rigidbodies coexist at all.** No engine
(PhysX, DOTS, or GPU) is happy holding tens of thousands of real rigidbodies.

### The resolved architecture (all research tracks converge here) [DECIDED]
1. **The bulk is NEVER real rigidbodies.** Deep/buried balls = voxel grid data +
   GPU-instanced rendering + ONE cheap surface/heightfield collider. This is the
   industry-standard answer for "thousands of physical objects": don't make the
   bulk physical. The `BallPitVoxel` prototype already implements this.
2. **Only a bounded ACTIVE BAND is real physics** — the balls near where players
   dig/disturb. A few hundred to ~1–2k rigidbodies at once (across up to 4 player
   dig sites) is comfortably within PhysX's good range.
3. **Settled balls DEMOTE OUT of physics entirely** — remove the rigidbody +
   collider and fold the ball back into the grid data. NOT "sleep as a
   rigidbody": sleeping still burdens the broadphase/GameObject count. This
   corrects the earlier "let settled balls sleep" assumption, which the 30k test
   disproved. (When removing a body, cache/restore velocity — SetActive(false)
   drops it.)
4. **Hard-cap concurrent active rigidbodies** and spread big cascades over frames.
   This is the one guardrail that makes "dig the base of the hill, it all
   collapses" safe.

### Concrete PhysX wins to apply/test (no engine change needed)
- **Broadphase: A/B test MBP/ABP vs default SAP** on the real flat dense pit —
  SAP degrades badly when many colliders overlap on one axis (a wide pit). MBP
  needs defined world bounds and can be worse when many bodies sleep, so measure.
- **Trim the Layer Collision Matrix** so balls only test the pairs that matter.
- **Keep `Physics.autoSyncTransforms` OFF; batch queries + call SyncTransforms
  once/frame** — matters a lot since we raycast the pit to pick grab targets.
- **Keep Discrete collision, low solver iterations, aggressive demotion,** and a
  larger fixed timestep if the feel allows.
- Replace the settled region with the single surface collider (already done in
  the voxel prototype) rather than many sphere colliders.

### Engine-change options (deferred, NOT needed to hit the goal)
- **DOTS / Unity Physics (+ optional Havok backend):** ~5–10× more bodies, but
  (a) can't coexist with Netcode for Entities so we'd keep NGO and hand-roll an
  ECS↔NGO bridge; (b) stateless Unity Physics has poor dense-pile stability
  (Havok fixes it but is non-deterministic — acceptable for host-authoritative,
  but still a big migration); (c) the project has ZERO DOTS footprint today, so
  this is weeks of work. It raises the active-band ceiling; it does NOT change
  the hybrid architecture. Revisit only if the active band must be far thicker.
- **GPU / compute PBD physics:** proven for simulating the mass (shipped VRChat
  ball-pit precedent), but GPU→CPU readback, the grab bridge, and
  non-deterministic/hard-to-network state make it a research project. Viable for
  a cosmetic mass, poor for gameplay-authoritative balls. Deferred.

> DECISION: Stay on PhysX + NGO. Make the hybrid real (voxel bulk + bounded
> active-band rigidbodies that demote to data on settle + awake cap). The engine
> swaps are escape hatches, not the plan.

---

## The guiding analogy: 7 Days to Die voxel terrain

7DTD doesn't store a GameObject for every block. The world is **voxel data** — a
grid of "what's in each cell." The terrain you see is a **mesh generated from
that data**. When you dig, the block you hit was never a separate object; it was
a voxel. The instant you break it, the game spawns a **real, pickup-able item at
that exact spot**, updates the voxel data, and regenerates the mesh. The seam is
invisible because the real object appears exactly where the data said the block
was.

We use the same architecture for the pit. The one deliberate difference is
**structural behavior** (see below): 7DTD terrain is stubborn and holds up
overhangs; a ball pit slumps and settles. That difference makes our version
*cheaper*, not harder.

---

## Three layers

From the player's POV there is just "a pit full of balls." Under the hood:

### 1. The grid — the source of truth (voxel-equivalent)
The pit's contents are a **3D grid of cells**. Each cell is empty or holds a
ball of a given color.
- This is the authoritative data. Win condition, save file, and the surface
  mesh are all derived from it.
- No cell is a GameObject. A pit of 10,000 cells costs the same per-frame as a
  pit of 10 — the grid is just data.
- Because the grid is deterministic, every client generates the identical
  surface and agrees on which ball is where. **[DECIDED]** the pit is grid data,
  not a loose count. (A loose per-color count was considered and **rejected**:
  it can't place surface balls deterministically, so grabbing could never be
  seamless.)
- [OPEN] Grid resolution (cell size). This is a direct cost/feel dial: finer =
  better-looking holes, more cells to store/settle/sync; coarser = cheaper,
  chunkier digging. Treat as a tunable knob; pin down in the spike.
- [OPEN] Exact grid representation (dense 3D array vs. per-column height stacks).
  Per-column stacks are likely cheaper for a pit shape and make "surface = top
  of each column" trivial. Decide in the spike.

### 2. The surface shell — generated from the grid, render-only
What the player sees as "a pit full of balls" is a **faithful render of the
current surface**, regenerated from the grid.
- Real positions, real colors — it reflects actual grid data, so it's convincing
  up close, NOT a decorative blob.
- Render-only: no Rigidbody, no collider that each ball owns individually, no
  NetworkObject per ball. One instanced draw path (or a generated mesh) for the
  whole visible surface.
- When the grid under it changes (a dig, a settle), the affected patch of
  surface regenerates — exactly like 7DTD remeshing a chunk after a dig.
- **[ASSUMPTION]** GPU-instanced ball meshes for the surface (reads as distinct
  grabbable balls, one draw call). Deforming mesh or particles are fallbacks.
  Pick in the pit-visual spike; the choice does not change the networking model.
- The pit needs a single collider surface (or coarse collision from the grid) so
  players and dropped balls rest on top of the pit — but that is pit-level
  collision, not per-ball.

### 3. The active balls — real, pooled, few (the "dropped item")
Only balls a player is physically interacting with become real objects.
- Grab/vacuum a surface ball → **promote** it: pull a pooled ball GameObject,
  spawn it **at that ball's exact grid position and color**, mark that cell
  empty. From the player's POV the ball they were looking at is now in their
  hand. Seamless.
- Only these in-flight balls (carried, dropped, settling into a container) are
  live, networked objects — realistically a handful to low tens across 4
  players, comfortably inside NGO's budget.
- Sold/returned/settled → **demote**: ball returns to the pool and folds back
  into grid/container data.
- **[DECIDED]** Object pooling for active balls; never Instantiate/Destroy per
  grab.

### Carried-ball behavior **[DECIDED]**
Active balls **stick to the carrier** (attached to a hold point, following the
player, like the owner-authoritative player movement model) and use a **simple
drop** (a short tween/arc into the container or onto the pit), NOT full rolling
Rigidbody physics. This keeps even the active balls cheap and predictable.
Exception: the dump "burst" below, which is intentionally physical for juice.

---

## Structural behavior: settling, not structural integrity

This is the key simplification and the deliberate difference from 7DTD.

- 7DTD is **stubborn**: dirt/stone hold up overhangs, and the roof only collapses
  after you hollow out a lot underneath. That overhang-stability calc is one of
  the most expensive things their engine does.
- A ball pit is the **opposite**: balls roll and settle. We never have to prove a
  structure is stable — we just let anything unsupported fall. **Balls fall with
  a much lower threshold than 7DTD dirt**, and the cheap direction (always fall)
  is simpler than the expensive one (prove it can stand). **[DECIDED]** no
  rigid structural integrity; use a settling rule instead.

**How collapse actually works (revised by prototype findings).** We first tried
a pure data settle (cells teleport between positions, surface redraws). It
**felt bad** — nothing rolls, so it read as teleporting, not slumping. The
prototype proved real rigidbody balls are affordable for a bounded set, so:
- When a region loses support, the affected surface cells **promote into real
  physics balls** that actually roll/tumble into the hole, then **demote back to
  grid data once they settle** (sleep). The deep untouched mass stays pure data.
- This gives genuine ball-pit slumping (real rolling) for the part the player
  sees, while keeping the buried mass free. It is the same promote/demote idea
  as grabbing, applied to collapse.
- **Guardrail:** cap how many balls are awake at once and spread a big cascade
  over several frames (the "dig the base of the hill" case), staying under the
  measured awake-cost floor. See Prototype findings.
- [OPEN] Settle aggressiveness (angle of repose) and the awake cap value — both
  tunable by feel.

Result: 7DTD-style excavation with *real* rolling collapse on the surface,
without paying physics for the buried mass.

---

## What is actually networked

Keep the networked surface area tiny. **[DECIDED]** direction; field-level
details [OPEN] until we build it.

| State | How it's networked | Notes |
|---|---|---|
| Pit grid | Server-owned; synced as **region/patch updates** | A dig or settle sends "here's the new state of this box of cells," not per-ball messages. See risk below. |
| Secondary piles (dumped) | Same grid+patch model as the pit | A dumped pile is just another small grid. |
| Money pot | Server-owned `NetworkVariable<int>` | One value, changes on sell. |
| Purchased upgrades | Server-owned list / `NetworkList` | Changes rarely. |
| Container progress (count per color) | Server-owned, per container | Rarely-changing counts. |
| Active (promoted) balls | A real `NetworkObject` each, pooled | Only the handful in-flight. The ONLY per-ball network traffic. |

Everything that scales with "pit size" lives in the grid and is synced as bulk
region patches, so **network cost is independent of how full the pit is.**
**[DECIDED]**

### Authority model (fits the existing foundation)
- The existing foundation is **host/server-authoritative for shared state** with
  **owner-authoritative movement** for players (`NetworkMovementSync`: owner
  drives their own Rigidbody; server owns impulses via RPC).
- The pit grid, piles, money, upgrades, container progress are
  **server-authoritative**: clients request actions (dig, vacuum, sell) via
  `ServerRpc`; the server validates, mutates the grid, computes settling, and
  broadcasts the resulting patches. Prevents a client inventing money or balls.
  **[ASSUMPTION]**
- [OPEN] Carried-ball authority: the carrying client owns the active ball's
  transform (responsive, matches the player-movement split) while the server
  owns the sell/reject/settle decision. Leaning yes — mirror player movement.
- **Server grid result is truth; client tumble is a flourish.** Clients may play
  a satisfying tumble animation as balls slump into a hole, but it reconciles to
  the server's authoritative grid patch — same philosophy as responsive player
  movement over server-owned outcomes. Prevents popping/desync.

---

## Walking the core loop through this model

1. **Grab from pit** → client sends `RequestDigServerRpc(cell)`. Server reads the
   cell's color, empties it, runs a local settle pass, spawns one pooled active
   ball at that position/color, assigns it to the player, broadcasts the patch.
   The hole is real and visible to everyone.
2. **Carry** → active ball sticks to the carrier (owner-auth transform). Only
   this ball costs transform bandwidth.
3. **Drop in container** → client sends `RequestSellServerRpc(ballId, containerColor)`.
   - Match → money pot += value, container count++, ball demoted (despawned).
     Gone — not returned to the grid.
   - Mismatch → ball returns to the pit: server adds it back to the grid surface
     (or the nearest open cell) and despawns the active ball, with a reject cue.
     - [OPEN] (from `game-design.md`) instant return vs. physical eject. Grid
       model supports either; returning it to a surface cell is the simple,
       low-bandwidth default. **[ASSUMPTION]**
4. **Win check** → server watches total remaining cells across the pit == 0.

The pit never holds thousands of networked objects at any step.

---

## Scaling the loop: vacuum, sections, and dump piles

The whole game is scaling from "1 ball, manual" to "vacuum a section and dump a
pile to sort." That late-game case is handled by the SAME model:

- **Vacuum a section** = a **bulk grid edit**: empty a region of cells at once,
  run the settle pass so the pit slumps into the crater. The removed balls go
  into the player's carry buffer (counts by color) or into a new dumped pile.
- **A dumped pile** = **another small grid** (its own cells + surface) spawned at
  the dump location. Players drain it with the exact same dig loop, sourcing from
  the pile's grid instead of the pit's. Lasting piles players walk around and
  sort over time are first-class, not a special case.
- **The dump "burst" (juice)** = when a vacuum dumps, spawn a short-lived,
  **capped** cluster of pooled real balls (~20-40, tunable) that scatter and
  settle with real physics for a second or two, then fold into the new pile's
  grid once they come to rest. You get the satisfying scatter without keeping
  hundreds of live balls around. Bigger dumps get a bigger but still-capped
  burst. **[ASSUMPTION]**

Design rule for the whole upgrade tree: **an upgrade may increase the RATE and
BULK of grid edits; it must never require hundreds of persistent live networked
balls.** Vacuums/drones/sorters are grid-to-grid transfer operations with
visuals, not clouds of real balls. **[ASSUMPTION]**

---

## Persistence hook (ties into save/resume)

`game-design.md` requires save/resume. The grid serializes directly:
- The pit grid (compressed — big empty/solid regions compress well; run-length
  or per-column stacks are compact).
- Any dumped piles (their grids + positions).
- Money pot, purchased upgrades, per-container sold counts.
- Active in-flight balls do NOT need saving — on load, rebuild the surface from
  the grid; anything mid-carry can be forgiven or returned to the grid.
  **[ASSUMPTION]**

> **Gap flagged:** the current `SaveManager` / `PlayerSaveData` is a *per-player*
> save (currency, achievements, stats) in that player's Steam Cloud. The ball-pit
> session is **shared team state** and needs a **host-owned session save**
> (`game-design.md` default: host owns it). We'll need either a second save
> payload (e.g. `SessionSaveData`, grid + money + upgrades + containers) written
> by the host, or to extend SaveManager to carry a session blob alongside the
> player blob. Design task for the persistence pass — noted so it isn't missed.

---

## Primary risk to de-risk with a spike

**Settling-cascade cost under a big late-game vacuum.** A single grab is tiny.
But vacuuming a large section can trigger a cascade of cells slumping into the
crater, changing many cells, which the server must compute AND broadcast. This
is where the networking/physics cost actually concentrates.

Mitigations to prototype:
- Sync settles as **region/patch updates** (a box of new cell state), never
  per-ball messages.
- **Cap collapse per tick** so a huge vacuum settles over a short, bandwidth-
  bounded window instead of one giant spike (also looks good — a progressive
  slump).
- Run the settle pass only on cells adjacent to changes (dirty-region, not whole
  grid).
- Tune grid resolution — the single biggest lever on cost.

This specifically needs a prototype; it's the thing most likely to bite us and
exactly what the technical spike should answer before we commit gameplay code.

---

## Open questions parking lot (technical)

- Grid resolution (cell size) and representation (dense 3D array vs. per-column
  stacks).
- Settle aggressiveness / angle of repose (steep brief walls vs. fast slump).
- Surface rendering technique (instanced meshes vs. deforming mesh vs. particles)
  — visual spike.
- Carried/active ball authority split (lean: carrier owns transform, server owns
  outcome).
- Max concurrent active balls + dump-burst cap before we throttle.
- Late-join / reconnect: rebuild from the grid + current NetworkVariable state
  (should be clean — little live state to reconcile).
- Pit collision: single surface collider vs. coarse grid-derived collision for
  players/dropped balls resting on top.

---

## Research sources

Networking scale limits (why thousands/hundreds of networked movers is a no-go):
- [Netcode: how many network objects is too many](https://discussions.unity.com/t/how-many-network-objects-in-a-scene-is-too-many/870805/4)
  — hundreds of moving NetworkTransforms blow the per-client reliable-data budget
  (~1 MB/s); suggests spawning only what's in view.
- [Networked variable/object limit advice](https://forum.unity.com/threads/networked-variable-object-limit-or-advice-on-performance.1226427/)
  — ~100 networked projectiles already dropped a client below 30 fps.
- [Syncing high amount of NetworkObjects](https://discussions.unity.com/t/syncing-high-amount-of-networkobjects-bad-idea/1616061)
  — recommends combining many items into one NetworkObject + compressed data
  rather than one-per-item (the spirit of our grid patches).
- [NGO physics (NetworkRigidbody / server-authoritative)](https://docs.unity3d.com/Packages/com.unity.netcode.gameobjects@2.12/manual/advanced-topics/physics.html)

Physics scale limits & the "data not objects" pattern:
- [Thousands of Rigidbodies — maintain data in arrays, not live bodies](https://forum.unity.com/threads/thousands-of-rigidbodies-in-scene-performance-improvement.535367/)
- [10,000s of GameObjects → GPU instancing, one object representing many](https://answers.unity.com/questions/1145980/how-to-improve-performance-with-10000s-of-gameobje.html)
- [Object pooling instead of instantiate/destroy](https://answers.unity.com/questions/893325/optimize-hundreds-of-objects.html)
- [Roblox ball pit perf: strip unneeded interaction (CanTouch/CanQuery) off balls](https://devforum.roblox.com/t/good-performance-ball-pit/1976116)

> Content from the above was rephrased/summarized for compliance with licensing
> restrictions; follow the links for the originals.

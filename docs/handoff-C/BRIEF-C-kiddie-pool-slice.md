# Deliverable C — Kiddie-Pool Vertical Slice (the deposit-feel pass)

**Status:** authoritative spec. Builds on Deliverable B bite (a), already on `main`
(core sort/sell/money/upgrade/win loop, hands-only). Source of truth for design is
`.kiro/steering/game-design.md` — esp. §2 (containers: visible fill + movie-magic +
physical sell-on-cross), §2b (kiddie-pool tutorial), §1b/§1c (campaign), §9b (tiny
pits). Read those first; the [DECIDED] items there are binding.

## 0. Why this exists

Right now a sold ball instantly VANISHES (it becomes a `Stored` ledger count with no
visual). For a *sorting* game that feels awful and kills the core satisfaction. This
deliverable makes the loop FEEL good and legible by making deposits physical and
visible, and proves it in one close-to-real area: the kiddie-pool tutorial. It is the
first vertical slice — systems built as FINAL game code, with rough placeholder art the
owner replaces. Get the FEEL right; that's the whole point.

## 1. Working rules (same as bite a)

- **No Unity compiler on the agent machine.** Verify by cross-file signature tracing
  against the real sources + `Library/PackageCache` (Unity 2022.3.14f1, URP 14.0.9, NGO
  1.7.1, Steamworks.NET). A missing Unity compile is NOT a blocker and NOT a review
  finding — the OWNER compiles in their editor. Write code traced to compile; list any
  NGO-codegen constructs that reading can't fully verify as owner-compile-check items in
  the handoff. The owner does the play test.
- Final game code: tunables serialized or ScriptableObjects; authoring components get
  gizmos + validators; placeholder visuals (rough meshes/particles) sit over the systems
  so the owner re-skins per area. Code never generates SHIPPED visible art, but MAY
  create cheap placeholder/instanced visuals that the owner replaces.
- Keep source files < ~400 lines; split with partial classes. Absolute project root:
  `c:\Users\cohen\Desktop\Game Dev\Active_Projects\Bathroom Simulator`.
- Namespace `BallPitGameplay`, Assembly-CSharp. Extend bite (a)'s code additively; copy
  a file before editing and list every edit in the handoff.

## 2. Hard boundaries (unchanged from bite a)

MultiplayerKit: no edits, public API only. `NetPit.cs` / `PitReplica.cs`: unchanged —
use bite (a)'s gameplay channel (`GameplayNet`). Collapse sim: behavior unchanged, new
behavior only via additive hooks default-off. `GpuBallRenderer`: the existing additive
`SetPalette` may be used; if a container-fill visual reuses GPU instancing, do it in a
SEPARATE renderer/component, do NOT change `GpuBallRenderer`'s pit path/shaders/cull.
Everything in `Assets/Game/BallPitGameplay/` is yours to extend.

## 3. Build these

### 3.1 Physical deposit: sell-on-cross, settle-after (game-design §2)
Today `DepositDriver` calls `authority.Deposit(id)` the instant a ball's centre crosses
the intake box — the ball vanishes. Change the FEEL, not the economy timing:
- **Keep sell-on-cross:** money/credit + the sale event still fire the moment the ball's
  centre crosses the intake box (responsive). Attribution/feed/win unchanged.
- **But the ball must physically ARRIVE and settle in the container, visibly, as its
  colour's fill** — not teleport away. Approach (choose and document): when a correct
  ball crosses the intake, credit it immediately, then let the real thrown ball continue
  its arc INTO the container volume and either (a) settle briefly as a real active ball
  then convert to the cheap fill (§3.2), or (b) hand off its position/velocity to the
  fill visual so it visually drops in. The player sees the ball they threw land in the
  container. The real-ball cost stays bounded (reuse the pit's active cap; a handful in
  flight at once — this is hands-only, one thrower).
- Wrong colour: unchanged (returns to pit). Strays: unchanged.
- This is the "real where attention is, movie-magic between" rule (§2): the thrown ball
  is real into the container; the accumulated mass is cheap fill.

### 3.2 Visible container fill (0–100% colored mass) (game-design §2)
Replace the placeholder `DefaultFillView` approach with a real, cheap, per-container
fill visual driven by the replicated ledger fraction (`readModel.SoldFraction(color)`,
exact on every peer — host and clients agree with no extra traffic):
- A container shows a **rising mass of its own colour**, 0% (empty) → 100% (all of that
  colour sold). CHEAP: a GPU-instanced clump of coloured spheres whose count/height
  tracks the fraction, OR a shaped/scaled coloured mesh that reads as "balls". MUST
  scale to a container holding 100k+ sold balls — NOT real rigidbodies. A container may
  show a representative number of balls (e.g. cap the instanced count and pack density
  to read as "full"), not one-per-sold-ball.
- **Per-area re-skinnable:** the fill is a component + data; the owner's themed vessel
  (gumball machine, bin, clear tube, hopper) is art placed around/over it. The fill
  component exposes where the "fill volume" is (a local box/transform) so art frames it.
  Keep `IContainerFillView` as the seam; add the new instanced/mass view as an
  implementation. The owner swaps views per area.
- Colour comes from the active palette (reuse `PaletteService` / the renderer palette so
  the fill matches the balls' colour exactly, incl. colour-blind display per peer —
  client-local, matches bite a).
- Drive it every peer (the fill is cosmetic + ledger-derived). The existing
  `GameplaySession` per-frame driver (now guarded) is the hook; extend it to the new view.
- **Fix the DefaultFillView regression properly:** DefaultFillView already defaults pivot
  to null + no-ops on root (just patched). Keep that; the new mass view is the real one.

### 3.3 Sale juice (game-design §2)
A small, data-driven feedback on each sale: a plonk/pop SFX hook + an optional particle
pop at the intake, + the money counter ticking. Hooks/events only (owner supplies final
SFX/VFX assets); ship a rough placeholder so the feel is testable. Reuse the existing
container `onSold`/`Sold` events — do NOT add a parallel path.

### 3.4 Upgrade station (replaces the Tab shop) (game-design §2)
The Tab/hotkey shop is retired in favour of an in-world **UpgradeStation**:
- An authored component placed in the world. The player walks up and INTERACTS (reuse
  A's `IInteractable` + `PlayerInteractor` aim/interact, Kind = Machine or a new
  Terminal kind) to open the shop screen; walking away / closing dismisses it.
- The shop UI content is bite (a)'s `ShopPanel` logic, now opened by the station instead
  of Tab. Keep the data providers + buy intent (`GameplaySession.RequestBuy`) exactly.
  Remove the Tab-opens-shop input path (or leave it dev-only behind a flag; document).
- Claims the cursor via the existing seam while open (same as bite a).
- The station is generic (any area can place one, it's that tier's store). The KIDDIE
  POOL may place NO station (tutorial has no upgrades) — the component just isn't placed.

### 3.5 Remove the color-counter HUD (game-design §2)
The on-screen per-colour ball counter in `GameplayHud` was test scaffolding — remove it
(progress is read from scenery: the shrinking pit + filling containers). Keep money
on-screen for now. Keep the sale/purchase feed (small). Do NOT remove the whole HUD —
just the colour-count clutter. If trivial, leave a dev-only toggle to show it, documented.

### 3.6 Throw bug fix (carry > 1)
Bug: with carry capacity 2 (Second Hand), throwing the FIRST ball makes it collide with
the still-held/pocketed SECOND ball and get knocked sideways. In `PlayerInteractor`'s
`ArmsTool` (ReleaseSmart / promote-from-hopper), the next ball is being promoted to the
hand at/overlapping the just-thrown ball's launch position, so they interpenetrate. Fix
the ordering/spacing: fully release + clear the thrown ball's collision before promoting
the next, and/or spawn the promoted ball offset so it can't collide with the outgoing
one. Throw of ball A must go cleanly along the aim ray regardless of a pocketed ball B.
(Carry-2 isn't used in the kiddie pool, but this is a real throwing-feel bug — fix it.)

### 3.7 Kiddie-pool authoring support (game-design §2b, §9b)
The owner authors the actual pool/containers in-editor, but provide what makes that
clean:
- Confirm/support a tiny, ABOVE-GROUND pit (the pit box sits on the floor, not sunk):
  verify `PitSite` + venue authoring supports a small box placed at floor level with the
  rim ~at the ball surface, and document the exact steps (anchor, profile, venueBounds,
  rimHeight, Fit Venue Bounds To Pit) for a ~200-ball kiddie pool.
- A simple default placeholder container prefab (visual mesh + intake box + Interactable
  collider on the right layer + the new fill view) via the `Create Default Gameplay
  Assets` menu or a new menu item, so the owner can drop 3–4 and immediately see the loop
  with fill. Rough art; owner replaces.
- Keep it hands-only, one-at-a-time (no forced upgrades); the station is optional here.

## 4. Deliverables

Code under `Assets/Game/BallPitGameplay/` (+ listed additive edits). Docs under
`docs/handoff-C/`: `DECISIONS-C.md` (decisions + owner questions at top) and
`HANDOFF-C.md` (every new/modified file with per-file edit lists; the owner compile-check
list incl. any NGO-codegen; scene/prefab setup incl. the kiddie-pool authoring steps, the
placeholder container prefab, the UpgradeStation, the fill view; and a manual host+client
test checklist). Also add short LAYERS and CONTAINER-3-PARTS sections to HANDOFF-C (the
two doc gaps found in bite a: Player/PlayerBody for the player, Interactable for
containers, Layer 8 balls; and container = model / intake box / Interactable collider /
fill view).

## 5. Verification

Trace end to end (no compile here): throw correct ball → sell-on-cross credits money →
ball physically arrives + joins the visible fill → fill fraction matches ledger on host
AND client; wrong colour returns; carry-2 throw of the first ball goes straight (bug
fixed); UpgradeStation opens the shop on interact and buys work host+client; the colour-
counter HUD is gone but money + feed remain; the kiddie-pool authoring steps are
documented and the placeholder container prefab works. Confirm boundaries hold (NetPit,
PitReplica, MultiplayerKit untouched; GpuBallRenderer pit path untouched; collapse
unchanged).

## 6. Scope guard (do NOT build)

No campaign scaffolding (room-to-room doors, pit switch, carry-forward, per-tier shop
closing, pre-baked fills, campaign save) — that's a later deliverable; just don't design
against it. No Phase 2–4 tools (rake/broom/dustpan/vacuum/etc.) — but the deposit/fill
system must not assume "thrown by hand" is the only delivery (a future vacuum/conveyor
delivers into the same intake + fill). The kiddie pool is hands-only, one-at-a-time.

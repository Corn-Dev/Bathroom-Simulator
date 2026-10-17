# HANDOFF-C — Kiddie-Pool Vertical Slice (deposit feel + visible fill)

Everything the OWNER needs to compile, wire, and play-test Deliverable C in Unity. There is
NO Unity compiler/runtime on the agent machine — all code was verified by cross-file
signature tracing against the real worktree sources + `Library/PackageCache` (Unity
2022.3.14f1, URP 14.0.9, NGO 1.7.1). The owner does the compile and the host+client play-test.

Branch: `feat/kiddie-pool-slice`. Spec: `BRIEF-C-kiddie-pool-slice.md`. Decisions + owner
questions: `DECISIONS-C.md`. Deep design: `.agents/DESIGN-C.md`.

---

## 0. First step — let Unity import (generate `.meta` files)

The agent did NOT author any `.meta` files. On opening the project, let Unity import the new
scripts and the new placeholder prefab so it generates their `.meta` (GUID) files and
compiles. New ASSETS that need a `.meta` on first import:

- Scripts: `SettlingDeposits.cs`, `MassFillView.cs`, `InstancedBallFillView.cs`,
  `ContainerSaleFx.cs`, `UI/UpgradeStation.cs` (and `IInteractableActivate`, which lives
  inside the already-tracked `PlayerInteractor.cs`, so no new file there).
- Prefab (created by the `Create Placeholder Container` menu, not committed): its `.meta` is
  generated when you run the menu item in-editor.

Edited files already have `.meta`.

---

## 1. New and modified files (per-FEAT edit lists)

### FEAT-001 — deposit feel + visible fill (commit `93132d9`)

NEW:
- `Assets/Game/BallPitGameplay/Containers/SettlingDeposits.cs` — host-only plain C# helper.
  Holds `(id, dwellEndTime)` + a `HashSet<int>` for `Owns(id)`. `ctor(PitAuthority, float
  dwellSeconds = 0.6f)`; `Register(int id, float now)`; `Tick(float now)` — guards
  `authority.Active.IsActive(id)` (drops a non-active id WITHOUT a second `Deposit`), else
  demotes via `authority.Deposit(id)` when `now >= dwellEndTime` OR
  `authority.Active.SmoothedSpeedOf(id) < 0.15`; `Flush()` demotes every remaining active id;
  `Owns(id)`.
- `Assets/Game/BallPitGameplay/Containers/MassFillView.cs` — `MonoBehaviour, IContainerFillView`.
  `SetFill(float)` lerps a dedicated child's local Y scale/position inside a fill-volume box
  (null/self fillVolume = no-op — never drives the root). `SetColor(Color)` recolors via a
  `MaterialPropertyBlock` (`_BaseColor` + `_Color`). Serialized: `fillVolume` (Transform),
  `fillRenderer` (MeshRenderer), `emptyScaleY/fullScaleY/emptyPosY/fullPosY`. THE DEFAULT fill.
- `Assets/Game/BallPitGameplay/Containers/InstancedBallFillView.cs` — optional
  `IContainerFillView`, ships UNASSIGNED. Own `Graphics.DrawMeshInstanced` (own mesh/material/
  MPB), never references `GpuBallRenderer`. `SetFill`/`SetColor`.
- `Assets/Game/BallPitGameplay/Containers/ContainerSaleFx.cs` — optional, `[RequireComponent(
  BallContainer)]`. Subscribes the existing `Sold` event; plays a serialized `AudioClip` +
  optional `ParticleSystem` at `IntakeWorldCenter`. No new event/path.

EDITED:
- `Assets/Game/BallPitGameplay/Containers/DepositDriver.cs` — in `Sell` DELETED the first-line
  `authority.Deposit(id)`; KEPT `attribution.Consume(id, pos, …)` → `core.AddSale` →
  `container.NotifySold(pos)` byte-for-byte; appended `settling.Register(id, Time.fixedTime)`.
  Constructs `SettlingDeposits` in the ctor. Phase-1 loop skips `settling.Owns(id)`.
  `AfterPhysicsStep` calls `settling.Tick(Time.fixedTime)` and KEEPS `CheckWin()` as the LAST
  call. `Detach` wraps `settling.Flush()` in try/catch and does NOT re-check win.
- `Assets/Game/BallPitGameplay/Colors/PaletteApplier.cs` — ADDED private `Color[] displayCache`
  assigned at the END of `Apply()`, and public `Color DisplayColor(int index)` (null/range-safe
  default). `Awake` also calls `gameplaySession.SetPaletteApplier(this)`.
- `Assets/Game/BallPitGameplay/Economy/GameplaySession.cs` — ADDED `public void
  SetPaletteApplier(PaletteApplier)`. `UpdateContainerFills` keeps `c.SetFill(SoldFraction(...))`
  unchanged and additionally pushes `mv.SetColor(paletteApplier.DisplayColor(c.ColorIndex))` to
  a lazily-resolved-once-per-container `MassFillView` cache (`Dictionary<BallContainer,
  MassFillView>`, cleared on TearDown; a null entry means "no mass view", no per-frame
  `GetComponent`).

### FEAT-002 — UpgradeStation, counter removal, carry-2 fix (commit `acc0a45`)

NEW:
- `Assets/Game/BallPitGameplay/UI/UpgradeStation.cs` — `MonoBehaviour, IInteractable,
  IInteractableActivate`. `Kind => Terminal`; `CanTarget => true`. Serialized `ShopPanel
  shopPanel` (auto-`FindObjectOfType` in Awake if unset), `float closeRadius` (default 4).
  `Activate(who) => shopPanel.SetOpen(true)`. `Update` measures `PlayerAvatar.Local` distance
  and calls `shopPanel.SetOpen(false)` beyond `closeRadius` (proximity auto-close; never the
  frozen interactor aim target). `OnDrawGizmosSelected` draws the radius.

EDITED:
- `Assets/Game/BallPitGameplay/Player/PlayerInteractor.cs` — ADDED `interface
  IInteractableActivate { void Activate(PlayerInteractor who); }`. In `Update`, after the
  pressed-edges are computed and BEFORE `tool.Tick`, if `primary && target.Interactable is
  IInteractableActivate act` → `act.Activate(this)` and set `primary = false` for that frame's
  `tool.Tick` (opening the shop never drops/throws a held ball). ADDED to `ArmsTool`: pure
  static `PromoteOffset(Vector3 hand, Vector3 aimDir, float diameter)` = `hand -
  normalize(aimDir) * 1.1 * diameter` (non-finite/zero aim returns hand) and private static
  `BallDiameter(BallPitFinalController)`. `ReleaseSmart` passes the offset point to
  `PromoteFromHopper` at BOTH call sites. `PitAuthority` UNCHANGED.
- `Assets/Game/BallPitGameplay/UI/ShopPanel.cs` — ADDED serialized `bool devToggleKey` (default
  false): the direct `Input.GetKeyDown(Tab)` OPEN toggle is now gated behind it (shipped flow
  opens only via `SetOpen(true)` from the station). ADDED a direct `Input.GetKeyDown(Escape)`
  read in `Update` that, while open, calls `SetOpen(false)` (runs under the cursor claim). KEPT
  the `!releasedLocal` force-close net and `SetOpen` claim/release; buys → `session.RequestBuy`
  unchanged.
- `Assets/Game/BallPitGameplay/UI/GameplayHud.cs` — the per-color counter rebuild (the
  `InPlay(c)`/`SoldFraction(c)` loop into `colorsText`) is now gated behind an ADDED serialized
  `bool showColorCountsDev` (default false). KEPT `moneyText`, `carryText`, `feedText`/
  `RefreshFeed`. NO `IGameplayReadModel` member removed.

### FEAT-003 — kiddie-pool authoring + docs (this feature)

EDITED:
- `Assets/Game/BallPitGameplay/Editor/GameplayAssetMenu.cs` — ADDED two editor menu items:
  - `Tools/Ball Pit/Create Placeholder Container` — creates (never overwrites; pings+selects
    if present) `Assets/Game/BallPitGameplay/Containers/Placeholders/PlaceholderContainer.prefab`
    assembling the CONTAINER-3-PARTS (vessel-body cube with its own BoxCollider as the
    Interactable collider; a child intake-box isTrigger marker sized to the BallContainer
    intake defaults; a child FillVolume cube) plus `BallContainer` (id `placeholder`, color 0,
    intake centre `(0,1,0)` size `(1,0.5,1)`) and `MassFillView` (fillVolume + fillRenderer =
    the FillVolume cube; empty/full scale 0.001→0.9, pos 0.05→0.5). Uses `PrefabUtility.
    SaveAsPrefabAsset`.
  - `Tools/Ball Pit/Create Kiddie Profile` — appends a `kiddie` `PitSizeProfile`
    (`10×2×10 = 200` balls, `ballDiameter = 0.5`, players 1–4) to the VenueConfig at
    `Assets/Game/BallPitGameplay/Resources/BallPitVenueConfig.asset` (or the loaded Resources
    config), via `SerializedObject` on the private `profiles` list. Never overwrites an existing
    `kiddie` key (pings instead); logs how to create the VenueConfig if none exists. The size is
    DATA — no code constant hardcodes a kiddie size.

NEW (docs):
- `docs/handoff-C/DECISIONS-C.md`, `docs/handoff-C/HANDOFF-C.md` (this file).

---

## 2. LAYERS

The gameplay physics relies on three logical layers (the exact index numbers are the owner's
Project Settings → Tags and Layers; the names below are the convention bite (a) uses, validated
by `PlayerLayers.Validate` / `VenueValidator`):

- **Player** (`PlayerLayers.Player`) — the player capsule/avatar. The pusher proxy
  (`HostPlayerPhysics`) is created on this layer. Excluded from the motor's own collision mask.
- **PlayerBody** (`PlayerLayers.PlayerBody`) — the player's body collider(s); also excluded
  from the motor mask so players don't fight their own body.
- **Balls — Layer 8 by default** (`PitSettings.ballLayer`, default 8). The sim's active/live
  ball colliders and the client live-ball proxies live here. The pit's invisible collision
  proxies (`PitSiteEnvironment`) must NOT be on the ball layer or the Player layer (validated).
- **Interactable** — the layer(s) you put containers / UpgradeStations on. `PlayerInteractor`
  only aims at colliders whose layer is in its serialized `interactableMask` (default 0 = none,
  so you MUST set it). This is NOT the ball layer and NOT the Player layer. Put the placeholder
  container's root and the UpgradeStation's collider on this layer and add it to every player
  prefab's `PlayerInteractor.interactableMask`.

OWNER ACTION: ensure an "Interactable" layer exists and is included in each
`PlayerInteractor.interactableMask`, and that the placeholder container prefab root +
UpgradeStation collider are on it. Without this the player's aim ray hits nothing to sell into
or to open the shop.

---

## 3. CONTAINER-3-PARTS

Every container is three parts + two components (what `Create Placeholder Container` assembles,
and what the owner's themed art must preserve):

1. **Visual mesh (vessel body)** — the themed art (gumball machine, bin, tube, hopper). In the
   placeholder it's a primitive cube. Its collider doubles as part 3.
2. **Intake box (the "trigger" rectangle)** — an oriented local-space box, authored via
   `BallContainer.intakeCenterLocal` / `intakeSizeLocal`. It is NOT a physics trigger: the host's
   `DepositDriver` tests active-ball CENTRES against it each step (`BallContainer.TryIntake`), so
   pushing tools can't feed it. A ball whose centre crosses it sells (right color) or returns to
   the pit (wrong color). The placeholder adds an `isTrigger` BoxCollider here purely as an
   authoring marker; `BallContainerGizmo` draws the real tested box.
3. **Interactable collider** — a collider on the Interactable layer (see LAYERS) so the player's
   aim ray can target the container (HUD shows its color/fill). In the placeholder this is the
   vessel body's own BoxCollider.

Plus:
- **`BallContainer` component** — identity (`id`, `colorIndex`, optional `site`), the intake box
  fields, the `Sold`/`Rejected` C# events + `onSold`/`onRejected` UnityEvents, and `SetFill`.
- **Fill view** — a component implementing `IContainerFillView` (default `MassFillView`). It must
  drive a DEDICATED CHILD mesh (the FillVolume), NEVER the container root — driving the root is
  what flattened containers and sank them under the deck. `MassFillView` guards this (a null/self
  fillVolume is a no-op).

Validate any container with `Tools/Ball Pit/Validate Containers` (`BallContainerValidator`):
every active color needs a container, intake above the pit floor, no intake overlapping the pit
interior.

---

## 4. Kiddie-pool authoring steps (tiny, above-ground, ~200 balls)

The kiddie pool is the SAME pit system, tiny — not a special mode. Above-ground just means the
anchor sits at floor level and the visible pool wall is low. Steps:

1. **Seed the profile.** Run `Tools/Ball Pit/Create Kiddie Profile`. This appends a `kiddie`
   `PitSizeProfile` (`10×2×10 = 200` balls, 0.5 m) to your VenueConfig
   (`Assets/Game/BallPitGameplay/Resources/BallPitVenueConfig.asset`). If you have no VenueConfig
   yet, run `Tools/Ball Pit/Create Default Venue Config` first, then re-run.
2. **Place a PitSite** in the gameplay scene for the kiddie area. Set its `siteId` (e.g.
   `kiddie`, matching `^[a-z0-9_.]{1,32}$`) and `profileKey = kiddie`.
3. **Anchor at FLOOR level, UNROTATED.** Assign the site's `anchor` to a transform sitting ON the
   room floor (not sunk). Its world rotation must be identity (validated — only its position is
   used). This makes the pit above-ground: the invisible proxy floor and walls sit on the floor.
4. **rimHeight tunes the INVISIBLE proxy, not the look.** Set `rimHeight` to roughly your visible
   pool-wall height. The proxy walls always rise to `max(fill WallTop, floor + rimHeight)` — this
   is the "walls forced too high" you saw: those are the invisible collision proxies, never
   visible geometry. Model the VISIBLE pool wall as independent art that stops right at the ball
   surface (the top of the ball mass). Low rim is fine; a `rimHeight` of 0 just warns that the
   proxy stops at the fill's WallTop.
5. **Fit the venue bounds.** With the PitSite selected, use the context menu (gear/⋮ on the
   PitSite component) → `Fit Venue Bounds To Pit` (`VenueGizmos.cs`). It sets `venueBounds` to the
   predicted proxy box grown 2 m sideways and 10 m above the rim (throw headroom). Widen further
   if you want more room to walk/throw around the pool.
6. **Confirm the cyan interior gizmo.** The PitSite draws a cyan box = the exact axis-aligned ball
   interior. Dress your round/rubber kiddie-pool art over this square box (§9b: the art can be any
   shape; the box is only where balls live). Keep art colliders OUT of the interior.
7. **Validate.** Run `Tools/Ball Pit/Validate Venue` (`VenueValidator`) and
   `Tools/Ball Pit/Validate Containers`. Expect zero errors: anchor unrotated, `venueBounds`
   encloses the predicted proxy box, every active color has a container, no intake overlaps the
   interior.

At 200 balls the fill generates instantly (the slow generation only bites at hundreds of
thousands — §9b), so NO pre-baked fill is needed for the kiddie pool.

### Placeholder containers + fill view

1. Run `Tools/Ball Pit/Create Placeholder Container`. It writes
   `Assets/Game/BallPitGameplay/Containers/Placeholders/PlaceholderContainer.prefab`.
2. Drop 3–4 (one per active color) into the kiddie area, beside/above the pool (NOT inside the
   interior). Set each instance's `BallContainer.colorIndex` and a unique `id`.
3. Put each container's root on your Interactable layer (see LAYERS).
4. The MassFillView is pre-wired to its FillVolume child. Give the FillVolume a GPU-instancing /
   URP-lit placeholder material so the `_BaseColor` recolor from `GameplaySession` reads. Reframe
   the FillVolume empty/full scale+pos to your real vessel interior when you re-skin.
5. (Optional) add `ContainerSaleFx` to a container and assign a placeholder plonk clip + a pop
   particle for sale juice.

### UpgradeStation (NOT in the kiddie pool)

The kiddie pool has no upgrades, so place NO station there. For later areas: place an
`UpgradeStation` on an authored object with a collider on the Interactable layer, assign its
`ShopPanel` (or leave it to auto-find), set `closeRadius`. The player aims + presses primary to
open (no ball dropped); Escape or walking past `closeRadius` closes.

---

## 5. OWNER-COMPILE / play-test checklist (reading cannot verify)

Compile items (trace-verified; confirm in your editor):

- **Interactor primary-edge consumption ordering** (FEAT-002): in `PlayerInteractor.Update`,
  `Activate` fires and `primary` is suppressed into `tool.Tick` ONLY for an
  `IInteractableActivate` target. Aim at the UpgradeStation holding a ball, press primary → the
  shop opens and the ball is STILL HELD (not dropped/thrown). Ball/None/Container paths see
  `primary` unchanged.
- **MassFillView drives only its child mesh** (FEAT-001/003): the FillVolume child scales/moves;
  the container root, intake box, and art DO NOT move or flatten. This is the regression the owner
  saw ("containers flattened / moved under the deck") — confirm it does not recur on the
  placeholder prefab AND on any re-skinned container.
- **Kiddie profile loads + validates** (FEAT-003): after `Create Kiddie Profile`, a PitSite with
  `profileKey = kiddie` passes `VenueValidator` (anchor unrotated; `venueBounds` encloses the
  predicted proxy box) and the cyan interior gizmo reads 10×10 footprint, 2 high.
- **NGO note — C adds NO new networked codegen.** Deliverable C adds NO new `NetworkBehaviour`,
  `NetworkVariable`, or RPC. The only networked type remains bite (a)'s `GameplayNet` (money /
  upgrades / win on its own channel). The fill fraction is derived from the already-replicated
  `PitWorld.Store` ledger — zero new traffic. The deferred demote still ends in `Stored` via the
  SAME `Events?.Gone(..Stored..)` NetPit already handles, just later in time. `NetPit.cs`,
  `PitReplica.cs`, and everything under `MultiplayerKit/` are UNTOUCHED; `GpuBallRenderer`'s pit
  path/shaders/cull are UNTOUCHED (the fill views use their own `MeshRenderer` /
  `Graphics.DrawMeshInstanced`). No batchmode compile was or could be run on the agent machine.

Play-test (host AND client):

1. **Sell-on-cross, settle-after:** throw a correct-color ball → money/feed credit the instant
   its centre crosses the intake; the SAME ball keeps falling, lands, settles, THEN joins the
   fill (does not vanish on cross).
2. **Visible fill:** the container's colored mass rises as that color sells; the fraction matches
   the ledger on host AND client; the fill color matches the balls, including with colorblind
   display toggled (client-local).
3. **Sale juice:** a plonk/pop fires per sale (if `ContainerSaleFx` + assets are wired).
4. **Wrong color / strays:** wrong-color ball returns to the pit; strays return — unchanged.
5. **Win latch:** the win fires only after the LAST ball finishes settling (not on the final
   cross) — all balls `Stored`.
6. **UpgradeStation:** aim + primary opens the shop (no ball dropped); Escape or walking past
   `closeRadius` closes; buys route through `RequestBuy` and apply on host AND client.
   (`ShopPanel.devToggleKey` can open it with Tab for station-less testing.)
7. **HUD:** the per-color counter is GONE by default; money + carry + feed remain.
   (`GameplayHud.showColorCountsDev` re-shows the old counter for debugging.)
8. **Carry-2 throw:** with Second Hand (carry 2), throw the first of two carried balls → it flies
   straight along the aim (no sideways knock); the second ball promotes behind the hand.
9. **Kiddie pool:** author per §4; confirm 200 balls generate instantly, the visible pool wall
   sits at the ball surface, and the loop (grab → throw → sort → sell → watch the container fill)
   plays hands-only, one ball at a time, with no VenueValidator errors.

Tuning knobs: `SettlingDeposits.DefaultDwellSeconds` / stillness threshold (settle feel),
`MassFillView` empty/full scale+pos (fill framing), `UpgradeStation.closeRadius`, the carry-2
`PromoteOffset` 1.1-diameter factor.

---

## 6. Boundaries held (all three FEATs)

No edits to `NetPit.cs` / `PitReplica.cs` / anything under `MultiplayerKit/` (public API only);
collapse sim behavior unchanged (only pre-existing additive host APIs — `Deposit`, `ReturnToPit`,
`PocketHeld`, `PromoteFromHopper` — are called); `GpuBallRenderer` pit path/shaders/cull
untouched; no new NGO codegen; `DefaultFillView.cs` left as the inert fallback. Edited bite-(a)
files were extended additively and are listed per-file above.

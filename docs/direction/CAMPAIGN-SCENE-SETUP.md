# Campaign Scene Setup — step-by-step (greybox → scalable)

Audience: the owner (hands-on in the Unity editor) and any future AI session with NO
prior context. Goal: turn the current scene (a single pit that auto-spawns balls on
play) into a **scalable campaign** where a `CampaignDirector` walks an ordered list of
PIT PREFABS — build pit → drain it → transition → build the next → win on the last —
with everything else (containers, spawns, upgrade station, warehouse) as permanent
scene furniture. Works the same offline and online (co-op is first-class).

This doc has TWO parts:
- **Part A — what the OWNER builds in the editor** (greybox with primitives now; swap in
  art later the same way). Follow these in order.
- **Part B — what CODE must be built** (the `CampaignDirector` etc.). This is a separate
  implementation deliverable (run as a workflow); Part A's steps reference the components
  it will create. Do Part B's code first (or in parallel) — Part A step 7 needs it.

---

## 0. Core mental model (read first — everything follows from this)

- **A PIT PREFAB is ONLY the pit:** its box dimensions + the art that IS the pit (walls,
  floor, rim). Nothing else. No colors, no containers, no spawns, no scenery. It's the
  vessel the balls live in, plus that vessel's look.
- **Colors are decided by CAMPAIGN LOGIC** (per pit in campaign; player-chosen in
  freeplay), NOT stored in the pit prefab.
- **Everything that is NOT the pit is PERMANENT SCENE FURNITURE**, placed once and shared
  by every pit: the warehouse shell, the color containers, the player spawn points, the
  upgrade station, and the system objects (controller, spawner, director, GameplaySession).
- **One pit is visible at a time.** The `CampaignDirector` INSTANTIATES the current pit
  prefab at runtime and DESTROYS it before building the next — no pits sit in the scene.
- **The pit the balls spawn into is the active pit.** Draining it (selling every ball)
  triggers the transition to the next.
- **Online = first-class.** The director is host-authoritative; the active-pit index is
  replicated to clients so everyone builds the same pit.

A "pit size preset" and "a freeplay pit choice" are the SAME thing: a pit prefab + the
color count chosen by logic. Campaign = an authored ordered list of (prefab, colors).
Freeplay (later) = the player picks a prefab + colors.

---

## PART A — OWNER EDITOR STEPS

> Use primitives (cubes/planes) for all art now. Everywhere this says "art," a textured
> model/3D asset swaps in later the same way (replace the mesh, keep the structure).

### Step 1 — Separate the pit from everything else in your current scene
Your scene today has a pit sitting in it with art, spawn points, etc. tangled together.
Untangle into three buckets:
1. **PIT-only objects:** the `PitSite` component's object + the pit's own art (the pool
   walls/floor/rim meshes). These will become a PREFAB (Step 3).
2. **PERMANENT FURNITURE:** the 4 containers, the spawn points, the upgrade station, the
   warehouse floor/walls. These STAY in the scene, NOT in any pit prefab.
3. **SYSTEM objects:** `BallPitFinalController`, `PlayerSpawner`, `GameplaySession`,
   `BallPitKitInstaller`, etc. These STAY in the scene.
- **Move the spawn points OUT of the pit's `SiteRoot`** and into a top-level empty named
  `VenueSpawns` (they belong to the venue, not the pit — the pit gets destroyed/rebuilt).
- **Move the containers OUT of the pit's hierarchy** too, under a top-level `Containers`
  empty.

### Step 2 — Build the permanent WAREHOUSE greybox (furniture)
Create a top-level empty `Warehouse` and under it, with primitives:
- A floor plane (~28 × 20 m — see `docs/direction/LAYOUT-AND-DEPOSIT.md`).
- Four wall cubes forming the warehouse shell (tall; this is the room the pit sits in).
- A big cube marking the **garage/loading door** on one wall (non-functional landmark now).
- Leave the center open for the pit, and keep a wide walkable band around where the pit
  will be. Do NOT put a pit here — the pit is instantiated at runtime into this space.

### Step 3 — Make PIT PREFABS (one per campaign pit)
For EACH pit you want (start with 2: a small one and a large one), build a prefab that
contains ONLY the pit:
1. Create an empty GameObject, e.g. `Pit_Small`. Add a `PitSite` component to it.
2. On the `PitSite`: assign/author its **anchor** (an empty child at the pit floor
   center, rotation 0,0,0), set its **profileKey** to a size profile (e.g. a 20×12×2
   "large" profile; for the small one a smaller profile — if the profile doesn't exist,
   see Step 3a), give it a unique **siteId** (e.g. `pit_small`, `pit_large`).
3. Add the pit's ART as children: wall/floor/rim cubes shaped to the pit box. (Use the
   `PitSite` cyan gizmo + "Fit Venue Bounds To Pit" context menu to size venueBounds.)
   The pit can be ABOVE-GROUND (box on the floor, e.g. the kiddie pool / shipping
   container) or SUNK (box recessed into the floor, e.g. the big inside pit) — that's
   just where you place the art/anchor relative to the warehouse floor.
4. Drag this GameObject into `Assets/Game/BallPitGameplay/Pits/` to make it a PREFAB.
   Delete it from the scene (the director instantiates it at runtime).
5. Repeat for each pit. You now have `Pit_Small.prefab`, `Pit_Large.prefab`, etc. Adding
   a new pit later = make another prefab and add it to the campaign list (Step 6). THIS
   is what makes the scene scalable.

> **Step 3a — if you need a pit SIZE that has no profile:** pit sizes are data in the
> VenueConfig asset (`Assets/Game/BallPitGameplay/Resources/BallPitVenueConfig.asset`).
> Add a profile entry (key, pitX/pitY/pitZ, ballDiameter 0.5). There's a menu
> `Tools > Ball Pit > Create Kiddie Profile` for the 10×2×10 tutorial size. For others,
> add entries in the VenueConfig inspector. (A nicer authoring flow is possible later;
> for now, profiles are VenueConfig entries.)

### Step 4 — Place the CONTAINERS (permanent)
Under the `Containers` empty, place your color containers (you already have 4) spread on
an L of two sides of where the pit will be, ≥4 m apart, with a ~6 m "sort band" of open
floor between the pit area and the containers (room to make color piles). Each container:
- Set `colorIndex` (0,1,2,3). This is the ONLY color field you set by hand.
- Size its intake box reasonably (not 5×5 m — try ~2.5 m wide; forgiving but not grabbing
  stray balls). Put its body collider on the Interactable layer.
- Do NOT parent containers to any pit. They are permanent.
> NOTE: container DISPLAY color should derive from `colorIndex` + the palette (code item,
> Part B). For greybox, eyeball the colors; don't hand-match permanently.

### Step 5 — Place the UPGRADE STATION and SPAWN POINTS (permanent)
- Drop one `UpgradeStation` object on an open side of the warehouse (approach room, not
  blocking a container). Single station, affordability-gated (no per-tier splitting).
- Under `VenueSpawns`, place the player spawn points where players START. (For now, place
  them in the warehouse near the pit. The later "start outside at the interview, move
  inside after the garage door" is a follow-up — see Part B notes; for the first playable
  campaign just spawn in the warehouse.)

### Step 6 — Author the CAMPAIGN LIST (data) — AFTER Part B exists
Once Part B's code exists, you'll have a `CampaignDefinition` asset. Create one at
`Assets/Game/BallPitGameplay/Campaign/MainCampaign.asset` and fill its ordered list:
- Entry 0: { pitPrefab = `Pit_Small`, colorCount = 3 }
- Entry 1: { pitPrefab = `Pit_Large`, colorCount = 4 }
- (add more entries later to extend the campaign — this is the scalability payoff)

### Step 7 — Wire the CAMPAIGN DIRECTOR (one object) — AFTER Part B exists
- Add one `CampaignDirector` object to the scene. Assign its `CampaignDefinition`
  (`MainCampaign.asset`) and references it needs (the controller, GameplaySession — Part
  B's handoff lists the exact fields).
- It replaces the current "auto-build one pit on play" behavior: on start it builds entry
  0's pit; when that pit is drained it transitions and builds entry 1; etc.; the final
  entry's drain fires the real win.

### Step 8 — Test the flow
- Host/offline: press play. You should spawn in the warehouse, the FIRST (small) pit
  builds, you sort+sell its balls into the containers; when the last ball sells, a
  transition plays (a stub fade/text for now) and the SECOND (large) pit builds in the
  same spot; draining it shows the win.
- Then test host + one client: both see the same active pit, money/upgrades shared, the
  client follows pit switches.
- Add a third pit prefab + a third campaign entry and confirm it just works (scalability
  check).

---

## PART B — CODE TO BUILD (separate implementation deliverable)

Host-authoritative, works offline + networked. New code under
`Assets/Game/BallPitGameplay/Campaign/`:

- **`CampaignDefinition` (ScriptableObject):** an ordered list of entries, each =
  `{ pitPrefab (the Pit_*.prefab), colorCount, (optional later: availableTools) }`.
- **`CampaignDirector` (host-authoritative MonoBehaviour / NetworkBehaviour):**
  - Holds the current entry index (a replicated NetworkVariable so clients agree).
  - On session start / OnSessionLive: instantiate entry[0]'s pit prefab, drive the
    controller to build that pit with entry[0]'s colorCount. (Replaces the adapter's
    single `SelectAndActivate`-then-build with a campaign-driven build. Reuse the
    existing `PitWorldBuilt`/`PitWorldDisposed` + `DisposePit`/`BuildRoutine` path — do
    NOT restart NetPit; it's one live session.)
  - Listen for the DRAIN of the current pit. `DepositDriver` already detects
    `StateTotal(Stored) == Count`. Instead of that firing the FINAL win, route it to the
    director: if more entries remain → advance; if it was the last → real win.
  - On advance: play a pluggable TRANSITION (see below), destroy the current pit prefab +
    DisposePit, increment the index (replicated), instantiate entry[i+1]'s prefab, build
    it. The transition covers the dispose→build hitch.
  - Inject each entry's **colorCount** into the pit settings at build (colors come from
    the director, not the pit prefab).
  - Persist the campaign index in the save (alongside the money/upgrades GameplaySession
    already persists) so a resume returns to the right pit.
- **A campaign-aware `IPitSiteSelector`** (or equivalent): the active pit = campaign
  index, NOT the lobby size. Because this is not derived from lobby settings, it MUST
  replicate the active index/siteId to clients (the adapter's `SiteSelector` seam +
  GameplayNet-style channel; see `networking-and-pit.md` + the adapter's
  `BallPitGameAdapter.SiteSelector`).
- **`IPitTransition` (pluggable):** a stub implementation = screen fade + "Next shipment…"
  text now. Later implementations (garage door opens, a crate/shipping-container droned
  in, a cutscene) drop in WITHOUT changing the director. The director just calls
  `yield return transition.Play()` between dispose and build.
- **Gate the FIRST build behind a trigger (later):** instead of auto-building on load,
  the first pit builds when the player crosses a start/garage-door trigger. For the first
  playable version this can just auto-build entry 0; the trigger-gating + the
  "spawn outside at the interview → move spawns inside after the door" is a follow-up.

### Hard constraints for Part B (from the engine)
- One live session; do NOT restart NetPit between pits — reuse
  `PitWorldDisposed`→`PitWorldBuilt` (the engine already supports rebuilding the world in
  place; `DisposePit()` + `BuildRoutine` exist).
- Only ONE pit's balls exist at a time (instantiate/destroy; never two pits of balls).
- Money/upgrades persist across pits (GameplaySession already does this per world
  lifetime — the director must NOT reset them between pits).
- Works offline (no NGO) AND networked (host + clients) — co-op is first-class; the index
  replication is required, not optional.
- Final-pit win still goes through the existing results/win path (game-design §6).

### What Part B does NOT do (scope guard)
- No freeplay UI yet (but the CampaignDefinition/colorCount split is designed so freeplay
  can reuse pit prefabs + pick colors later).
- No real cutscene art (transition is a stub behind `IPitTransition`).
- No pre-baked fills, no PitPreset authoring UI (profiles stay VenueConfig entries for now).
- No outside-interview area yet (spawn in the warehouse for v1; interview is a follow-up).

---

## Open items / confirm during build
- Exact `CampaignDirector` ↔ `BallPitGameAdapter` integration point (the adapter currently
  owns `SiteSelector` + calls `SelectAndActivate` in `OnSessionLive`; the director likely
  installs a campaign selector there and drives rebuilds). Part B's coder confirms the
  cleanest hook against the real adapter code.
- Container display-color self-config (derive from colorIndex + palette) — small code item,
  fold into Part B or a quick follow-up.
- Transition content (fade now; droned-in crate / garage door later).

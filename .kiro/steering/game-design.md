# Game Design — Drain the Ball Pit (living record)

Single source of truth for gameplay direction. Future sessions start here.
Companion docs: `achievements.md` (the Steam achievement set) and
`networking-and-pit.md` (the pit tech, networking, venue, and current build status).

| Mark | Meaning |
|---|---|
| [DECIDED] | the owner confirmed it |
| [PROPOSED] | working default; cheap to change, revisit after playtests |
| [OPEN] | needs a call |

**Owner's standing guidance:** keep it simple and intuitive; reuse tools instead of
inventing one per job; get the basic loop in first, then iterate from playtests;
don't over-design later phases ahead of time.

## 0. Where we are + what's next (2026-10) — READ FIRST

**How work happens:** various Kiro sessions in THIS repo (+ occasionally browser
Claude). No external "strong Kiro" anymore — this repo owns the work. These steering
docs are the shared memory; keep them current.

**Done & integrated:**
- **Pit tech** — millions of balls, real collapse physics, GPU occlusion culling,
  exact per-color counts, host-authoritative replication (`NetPit` protocol 3).
  `Assets/_Prototypes/BallPitFinal/`.
- **Lobby/menu kit** (`Assets/MultiplayerKit/` + `Assets/Game/BallPitSession/`) —
  integrated; owner wiring the UI prefabs. Main-menu "Join Friends" temporarily opens
  the Steam friends overlay until an in-app browser screen is built.
- **Players + venue (Deliverable A)** (`Assets/Game/BallPitGameplay/`) — first-person
  movement, climb/mantle, stand on pile, grab/drop/throw. WORKS in-editor host
  single-player. NOT yet tested in multiplayer or a build.

**The big design decision this session: the game is a CAMPAIGN** (see §1, §1b, §1c).
A fixed sequence of ~4–5 pits (tiny kids' pool tutorial → huge), one live session,
pits are rooms connected by doors, upgrades/money carry forward, ship pre-baked
fills. Custom/freeplay is the secondary mode. This supersedes any older "pick a size,
drain it" framing.

**NEXT BUILD = Deliverable B: the gameplay loop** (containers, sort, sell, money,
upgrades, win). NOT built yet. A brief exists at `docs/handoff-B/BRIEF-B.md` BUT it
was written for generic "drain a pit" and **must be revised to the campaign framing
before building** (room-to-room doors + lock, carry-forward economy, per-tier shops,
pre-baked fill loading, campaign save). Suggested approach: build B in two bites —
(a) the core loop in the existing `large_1` pit (playable + fun first), then (b) the
campaign scaffolding.

**Networking:** replication + lobby proven locally (UTP). The Steam-transport swap is
the only unverified piece — needs a 2nd real machine (a VM has no GPU and can't run
the renderer). Not blocking; lowest-risk piece.

## 1. The game [DECIDED]

- Co-op 1–4, first person, Steam. Final art is the owner's; code provides systems,
  hooks, placeholder UI.
- The venue is an authored building with in-ground pits of colored balls. Balls are
  0.5 m (right size, per owner). The pit need not be life-size, only the balls.
- **Primary mode = CAMPAIGN (a single fixed playthrough).** Players progress through
  ~4–5 pits in one authored sequence, each bigger/harder than the last, carrying their
  upgrades and money forward the whole way. Pit #1 is a tiny kids' plastic pool that
  doubles as the tutorial. The final pit needs full automation.
- **Secondary mode = CUSTOM / FREEPLAY** for people who just want to sort: the existing
  pick-a-size-and-settings sandbox. Cheap to keep (it's today's behavior). Campaign is
  the focus.
- **Per-pit goal:** drain the pit — get balls out, sort by color, deliver each color to
  its container. A correct ball sells for 1 coin into a shared pot; upgrades bought from
  the pot; the pit is complete when every ball is sold, which opens the door onward.
- **Feel:** walking into each new pit = "oh boy, that'll take a while"; clearing =
  satisfying, almost ASMR; progression like a clicker across the whole campaign, but
  nothing overpowered and no purchase ends a pit minutes after buying it.

## 1b. Campaign structure [DECIDED]

- **One gameplay scene, pits as connected rooms.** All ~4–5 pit areas are authored in
  one scene as rooms linked by doors/corridors. Only the ACTIVE pit's balls exist
  (its `PitSite` built); inactive sites are dormant geometry with ZERO ball/sim cost
  (their balls were never instantiated — not "unloaded", simply not created yet). This
  keeps the whole campaign in ONE live session: no scene load, and crucially NO NetPit
  host-restart / adopt-clients networking. Site-switching rides the existing
  `PitWorldBuilt`/`PitWorldDisposed` events.
  - If per-area lighting ever becomes too constrained in one scene, additive scene
    loading of each room is a documented fallback, but start single-scene.
- **Progression = walk through a door to the next pit.** On draining a pit the door to
  the next area opens; once ALL players pass the trigger, the door **recloses and locks**
  (no backtracking). The finished pit's SHOP also closes — each pit's shop is that
  tier's store; the next area has a better one. This makes the economy a clean sequence
  of self-contained tiers (far easier to tune) and matches "once you've mastered a pit
  you don't want its old shop".
- **No forced breather room.** Players aren't funneled anywhere between pits; the door
  simply opens and they go when ready.
- **Difficulty ramps on TWO axes per pit, both fixed/authored:** ball count (pit size,
  tiny pool → millions) AND color count. Colors range **3–4 up to ~6** (not 2–8; 6 is
  about the sorting-complexity sweet spot — final count is a tuning call). More colors =
  more sorting/gates to manage = genuinely harder.
- **Campaign is FIXED** (authored per pit: size, colors, shop/tool availability). The
  only per-player variability is the colorblind DISPLAY setting (how colors are shown,
  not how many). Custom mode is where players choose settings.
- **Carry-forward:** upgrades + money persist across pits within the one live session.
  The campaign save records which pit you're on + carried economy + the current pit's
  in-progress state (extends the existing world-save via a campaign-position wrapper).

## 1c. Loading / fill performance for the campaign [DECIDED approach]

- **Ship PRE-BAKED fills for every campaign pit.** Fills are deterministic from
  seed+size+settings; campaign pits are fixed and few (~5), so the DEV bakes each pit's
  fill once and ships the `.bin` files in the build. The player's "load" becomes a file
  read (seconds), NOT the multi-minute pour-and-settle generation. This is the main fix
  for the slow-RandomFill problem — it only bites when generating on the player's
  machine, which the campaign avoids. (Baked files are multi-MB each; tens of MB total
  in the build — fine for Steam. Re-bake if a pit's dimensions/seed change.)
- **Background-prepare the next pit while playing the current one.** Fill GENERATION
  (or load) runs on a worker thread during play (hidden). The INSTANTIATION step (GPU
  buffers, grid, collider pool) must run on the main thread and hitches briefly — hide
  it under the door/corridor transition beat. So: small-first (tutorial pool loads
  fast) + prebake-the-rest-while-playing + a transition that covers the instantiation.
- **Not blocking, queued optimization:** speed up RandomFill GENERATION itself
  (currently a sequential pour; parallelizable with Burst/jobs) — matters for
  custom/freeplay (arbitrary sizes still generate) and dev-side baking, but the
  campaign ships baked so it's an optimization, not a blocker.

## 2. Structure and rules [DECIDED]

- Two areas: **in the pit** (get balls out, up to ground level) and **out of the
  pit** (move toward containers → sort → deliver into the matching container).
- **Four tool phases:** by hand → better hand tools → small helpers → full
  automation. Tools scale terrible→powerful in small steps.
- **Containers:** one per color, owner-placed; accept balls only through the top
  opening; fill = % of that color sold (no capacity). Wrong color → back to the pit
  (drops from above at a random spot). Out-of-bounds returns strays the same way.
- **Visible fill is a core satisfaction, not optional polish [DECIDED 2026-10].** The
  player MUST see the balls leave the pit and accumulate in the container — a sold ball
  silently vanishing feels awful and undercuts the whole sorting payoff (a core pillar).
  Each container shows a **0–100% mass of its color** rising as that color sells. The
  container ART is per-area/themed (gumball machine, bin, clear tube, hopper…), but
  under every theme it's the same thing: "this color, this fraction full," driven by the
  replicated ledger. The accumulated mass is CHEAP (instanced/faked colored balls or a
  shaped fill, NOT thousands of real rigidbodies — a container may hold 100k+ sold
  balls). This is the container analogue of the pit's own data-not-objects trick.
- **"Movie magic" between endpoints [DECIDED 2026-10].** Balls are REAL (physics) only
  where the player's attention is — entering a tool/intake and arriving/settling in the
  container. In between (traveling a vacuum tube, a conveyor, a chute) the visual may be
  pure fakery (instanced movers, a stream, or nothing). This extends the pit's "real
  where it matters, data where it doesn't" rule to delivery, and bounds the real-ball
  cost of every future tool.
- **Deposit is PHYSICAL; sell-on-cross, settle-after [DECIDED 2026-10].** Players must
  physically move balls into a container (throw by hand now; tools later). NO hold-to-
  deposit-from-inventory shortcut — the physicality is the point. Mechanic: money/credit
  fires the moment the ball's CENTRE crosses the intake box (responsive, instant feel),
  and the SAME ball then continues physically into the container and visibly joins the
  fill (so you see it land/settle). So: sell on cross + settle visually after, with a bit
  of sale juice (plonk/pop). Revisit only if playtest shows throwing-to-sell is
  frustrating.
- **Upgrades: an in-world UPGRADE STATION, not a menu key [DECIDED 2026-10].** The player
  walks up to a physical upgrade place in each area and interacts; no Tab/hotkey shop.
  This is the per-tier shop of §1b made diegetic (each area's station is that tier's
  store; it closes when the area is finished). The placeholder Tab shop is retired into
  this station screen.
- **Progress is read from SCENERY, not a HUD counter [DECIDED 2026-10].** The on-screen
  per-colour ball counter was test scaffolding and is removed. Players gauge progress
  from the world: the pit visibly shrinking and the containers visibly filling (the two
  rules above). Diegetic container labels/signage are allowed if a tutorial playtest
  shows players are lost; the HUD counter does not come back. (Money display is still
  on-screen for now — showing money diegetically is an open question, parked.)

## 2b. The kiddie pool = the tutorial [DECIDED 2026-10]

- Pit #1 (§1b) is the tiny rubber kids' pool and doubles as the tutorial. It is
  **hands-only, one ball at a time** (no Second Hand on this level — possibly no upgrades
  at all here). It teaches the pure loop: grab → throw → sort by colour → watch the
  container fill. Built with the SAME pit system, tiny (§9b), above-ground (its box sits
  ON the floor, not sunk). It is the first vertical slice built close-to-real (rough art
  OK) to lock the FEEL of the loop before the rest of the campaign is authored.
- Pushing tools (broom, blower) only move balls on the floor; never feed a container.
- Helpers (drones, bots) make piles; only upgrades let them deliver (first "own color
  only", then delivery into that color's container).
- **Limits live on tools, not containers.** The vacuum especially stays modest.
- **Upgrades:** shared, bought from the team pot; anyone buys, no host privilege, no
  player-count scaling.
- Owner places every gameplay object (sites, spawns, containers, stations, build
  zones). Code never assumes a fixed layout/hierarchy.
- Moving balls are grabbable like resting ones; players walk the pile, climb pit
  walls, mantle out, so nobody gets stuck.

## 3. Tools by phase [PROPOSED unless marked]

| Phase | In the pit | Move | Sort | Deliver |
|---|---|---|---|---|
| 1 Hand | hands (grab/carry/throw); Second Hand (carry 2); Strong Arm (throw higher) | carry; broom; Push Broom | rake pulls colors into piles; Wide Rake | throw (Wider Funnels); **dustpan** [DECIDED]: scoop a sorted pile, tip in; Bigger Dustpan |
| 2 Better hand | ball bag; **elevator** [DECIDED]; shop-vac (small, slow) | cart; leaf blower | color comb (rake that catches one color) | dustpan; dump cart (tips over a container rim) |
| 3 Small helpers | elevator loader (auto-cycles); drones (pile at station; upgrade: own color only) | ramps (gravity chutes) | **color gates** on ramps; sorter bots | dustpan + dump cart; vacuum top level (one color → straight into that container); drone delivery [DECIDED: only after color-only] |
| 4 Automation | continuous lift | conveyors (free placement in build zones) | **player-assembled** [DECIDED]: belts through color gates separate every color | belts end at a container's top opening |

- **Elevator** [DECIDED]: buying opens a hole in the pit wall; a platform runs from
  the pile height to ground and tips its load onto the floor; players can ride it.
- **No finale tool** [DECIDED]: the end is the floor appearing and last balls picked
  by hand.
- **Cut** [DECIDED]: Drain finale, shape-sorter table, container loaders/lifters,
  Climbing Gloves, all-in-one optical sorter (gates replace it), claw crane,
  excavator, rope hoist (elevator replaces), laundry basket (ball bag replaces),
  scoop shovel.
- Phase 2–4 details are provisional; revisit after playtesting Phase 1.

## 4. Economy and pacing [PROPOSED]

- 1 coin/ball, per-color multipliers in config (all 1), combined sell cap ~×2.
- Overlap rule: a new tool's first level beats the previous tool's best by ~1.5–2×;
  each level +15–30%; level prices grow ~1.6–2.2×.
- Early prices equal on every pit (first tools ~20–40 min of hand work); late prices
  climb steeply; pit size decides how far down the tree a game goes.
- The bottleneck moves between steps so purchases alternate. More colors = more
  gates/sorting to buy (color count is a difficulty knob).
- Economy simulator (editor tool, later) to tune prices.

## 5. Colors [DECIDED direction]

- Range **~3–4 up to ~6** colors (not 2–8). In CAMPAIGN the count is fixed per pit
  (ramps difficulty: early pits 3–4, later up to ~6). In CUSTOM it's a host setting.
  6 is roughly the sorting-complexity sweet spot; exact per-pit counts are a tuning
  call. The code still supports a wider range; this is the design intent.
- Palette presets scale to the count (Classic, Pastel, Candy, Neon, Ocean, Sunset);
  visual only, can change mid-game. Later: curated custom colors with a "too similar"
  warning.
- Colorblind (per-player setting): Okabe–Ito safe palette (8 colors); later optional
  per-color ball patterns + matching HUD/container icons.

## 6. Win and after [DECIDED]

- Last ball sold → results panel (time, balls sold, money, upgrades, per-player
  stats); closeable; free roam continues; save marked completed.

## 7. Fill look [OPEN — leaning Random-only]

- FCC (lattice) vs Random pour. Owner leans toward **removing FCC** — it looks too
  clean/crystalline; Random reads like a real pit (better aw-factor). Random is
  ~58% density so it fills TALLER than FCC (~74%) for the same count — author venue
  walls/rim to the Random fill height. If dropped, FCC may stay as a dev-only instant
  fill. Decision pending a final look pass.

## 8. Venue authoring (how the pit sits in a level) [DECIDED model]

- The pit's REAL collision walls are invisible proxies the sim builds from the fill
  geometry (they hug the ball mass). Authored "walls" are decorative only. So: author
  pit-dimensions-FIRST — set anchor + profile, then model the pool to the cyan
  interior gizmo the PitSite draws; run Fit Venue Bounds To Pit.
- Everything cosmetic/spatial is author-controlled and NOT constrained: the building,
  deck size, lighting, spawn-point placement (anywhere, not just the rim),
  out-of-bounds volumes (place only where wanted), and the walkable area
  (`venueBounds` can cover the whole building floor; turn `respawnOutsidePlayBounds`
  off for roaming). Constraints: the pit interior is an axis-aligned box, the anchor
  unrotated, venueBounds must contain the ball mass within a grid-memory limit.
- Likely direction: ship ONE pit size (best-looking), one authored venue — simpler
  than 5 size-specific venues. Multi-size stays supported in code if wanted.

## 9. Campaign (CHOSEN — see §1b for the structure)

- The campaign is now the primary mode (§1b/§1c). The venue system was built
  campaign-ready and this uses it directly:
  - Multiple pit sites in ONE scene, one active — becomes the pit sequence (rooms).
  - Swappable `IPitSiteSelector` — becomes "campaign progress" (which room you're in)
    instead of "lobby size choice". NOTE: a selector not derived from lobby settings
    must replicate the active siteId to clients (B's gameplay channel), per the A notes.
  - Site recorded in the save — becomes campaign position.
  - `PitWorldBuilt`/`PitWorldDisposed` events — drive the room-to-room switch.
- **Because it's one live session, NO NetPit host restart is needed** (that was only
  required for a mid-session scene/area change across sessions). Door-to-door switching
  stays within the running session.

## 9b. Pit shape + tiny pits [DECIDED facts about the tech]

- **The simulated pit interior MUST be an axis-aligned rectangular box** (flat floor,
  four vertical walls at WallMinX/MaxX/MinZ/MaxZ). This is baked deep into the
  fill/grid/collision/collapse math; non-box ball volumes (round, L-shaped, sloped
  walls) are NOT supported and would be a major rewrite. What IS free: (a) the visible
  art/rim/room around the pit can be ANY shape — the box is just where balls live;
  dress a round-looking kiddie pool with art that masks the square corners; (b) box
  PROPORTIONS vary freely per pit (long trough, deep well, wide shallow), giving
  plenty of visual variety across the campaign without leaving "rectangular". If a
  truly non-box ball volume is ever wanted, flag it as a major feature, don't assume.
- **The kiddie-pool tutorial (~200 balls, manual-only) uses the SAME pit system,
  tiny — no special fallback.** At ~200 balls the fill generates instantly (the slow-
  generation problem only appears at hundreds of thousands), and keeping one system
  means the tutorial teaches the exact grab/carry/sort/sell/`Deposit` loop the real
  pits use. A separate "just rigidbodies" tutorial pool was considered and rejected
  (second ball system, wouldn't share the sorting/selling plumbing). No pre-bake needed
  for a pit this small.

## 10. Tech constraints for gameplay work (verified in pit code)

- **Extraction primitive:** every in-pit tool reuses Excavate→holder, then Dump as
  physics; real cave-in physics refills the hole.
- **Pushing tools need a thaw sweep** (wake resting balls in the tool volume, capped
  per step): PhysX doesn't collide moving tools with static resting balls.
- **Return-to-pit is one step:** Demote → `ToGone(Hopper)` → Promote → `ToLive`.
  Never leave a ball in Hopper across a step outside a player hopper (NetPit join sync
  only enumerates player hoppers 0–3).
- **Holders beyond players** (carts, elevator, drones, belts) need a small additive
  change to NetPit's sync enumeration (wire format unchanged).
- **Lost balls:** `PitAuthority.Lose` marks `Lost` (would block the win) — the game
  must return them to the pit via a hook instead.
- **Container fill on clients** comes from the replicated ledger (no extra traffic);
  deposit detection is host-side (active ball positions vs intake boxes).
- **Gameplay state** (money, upgrades, win) lives on its own NGO channel; NetPit
  untouched. Purchases via ServerRpc, same validation for everyone.
- **Belts** (Phase 4) move balls as data (deterministic, ~no bandwidth).
- **Physics budget:** ~4k moving balls at once — tools pour/drop in short bursts.
- **Late-join sync for huge pits** needs a compact "sold" format (one bit/ball) before
  release.

## 11. Open questions [OPEN]

- Fill look (section 7) — leaning Random-only (FCC too crystalline). Final call pending
  a look pass. (Campaign ships baked Random fills regardless — §1c.)
- The campaign pit sequence: how many pits (~4–5), and each one's size + color count +
  which tool tier its shop unlocks. The authored difficulty curve. Needs design + the
  economy simulator to tune.
- Exact Phase 2–4 tool set (revisit after Phase 1 playtest).
- Prices across the campaign tiers (one continuous curve; playtests + simulator).
- Player size relative to the 0.5 m balls (visual call now that A is in).
- Known Phase-A polish: sticky pile-walking (motor feel), optional footstep ball-
  scatter (`PlayerConfig.playersDisturbPile` seam, off), MEDIUM host carry-mantle
  stall, balls-go-black on editor focus loss (likely editor-only).
- Queued optimization: speed up RandomFill GENERATION for custom mode + dev baking.
- **CLIENT frame drops IN THE PIT, not on the deck (reported 2026-10, multi-machine
  playtest) [TO DIAGNOSE].** Host was smooth, two clients hitched when standing in the
  pile. Prime suspect is CLIENT-ONLY near-pile cost, NOT the network transport (transport
  would hitch regardless of position): `ClientPitColliders` (`Assets/Game/BallPitGameplay/
  Player/`) rebuilds the local player's collider SHELL + kinematic live-ball PROXIES every
  fixed step only when the player is near balls — exactly "in the pit, not on the deck".
  Second suspect: in-pile RENDER cost (the camera sees far more exposed balls up close
  inside the pile). DIAGNOSE FIRST (don't tune blind): a client reproduces with the
  Console open — look for `[ClientPitColliders] the client collider shell is full (…)`;
  and a Unity Profiler frame while hitching — is the spike in Physics or in Camera/render?
  LEVERS (all serialized on the PlayerSpawner's `PlayerPitPhysicsConfig`, inspector-only,
  reversible): `liveProxyBudget`, `liveProxyRange`, `clientShellKeepSeconds`,
  `clientShellMaxColliders`, `colliderReleasePerStep`. Deeper fix if config isn't enough:
  throttle/spread the client shell+proxy maintenance across frames. Not a networking bug.

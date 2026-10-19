# Game Design — Drain the Ball Pit (living record)

Single source of truth for gameplay direction. Future sessions start here.
Companion docs: `product.md` (pitch), `achievements.md` (the Steam achievement set),
`networking-and-pit.md` (the pit tech, networking, venue, current build status), and
`docs/direction/GAME-DIRECTION.md` (the market/direction reasoning behind §1).

| Mark | Meaning |
|---|---|
| [DECIDED] | the owner confirmed it |
| [PROPOSED] | working default; cheap to change, revisit after playtests |
| [OPEN] | needs a call |

**Owner's standing guidance:** keep it simple and intuitive; reuse tools instead of
inventing one per job; get the basic loop in first, then iterate from playtests;
don't over-design later phases ahead of time. This is a SOLO dev's FIRST released
game — bias hard toward "ship a small, cohesive, polished thing," not maximal design.

---

## 0. STATUS — what is TRUE right now (2026-10) — READ FIRST

**How work happens:** various Kiro sessions in THIS repo (+ occasionally browser
Claude). No external integrator, no "strong Kiro." NO Unity compiler/runtime on the
agent machine — agents verify by tracing; the OWNER compiles + play-tests in their
editor (see `product.md`). These steering docs are the shared memory; keep them
current.

**BUILT & on `main` (works in-editor host; multiplayer lightly tested):**
- **Pit tech** (`Assets/_Prototypes/BallPitFinal/`) — millions of balls, real collapse
  physics, GPU occlusion culling, exact per-color counts, host-authoritative
  replication (`NetPit` protocol 3). Measured 1,000,000 balls ~125fps solo; ~90fps
  host+client over local UTP. The single biggest technical risk is retired.
- **Lobby/menu kit** (`Assets/MultiplayerKit/` + `Assets/Game/BallPitSession/`) —
  integrated; owner wiring UI prefabs. "Join Friends" opens the Steam overlay for now.
- **Players + venue — Deliverable A** (`Assets/Game/BallPitGameplay/Player`, `Venue`) —
  first-person move, climb/mantle, stand on pile, grab/drop/throw.
- **Core loop — Deliverable B bite (a)** (`BallPitGameplay/Economy`, `Containers`,
  `Tools`, `Colors`, `UI`) — containers, sell/reject/return, shared money, data-driven
  upgrade catalog + ServerRpc purchase, save participant, hands tool + hotbar seam,
  palette + colorblind, placeholder HUD/shop/results. Hands-only.
- **Deposit feel + visible fill — Deliverable C** — physical sell-on-cross + settle,
  `MassFillView` visible container fill, sale juice, in-world UpgradeStation (replaces
  Tab), color-counter HUD removed, carry-2 throw fix, kiddie-pool authoring menus.
  (Owner compiling/play-testing; see `docs/handoff-C/`.)

**THE DIRECTION (revised 2026-10 — supersedes the earlier "4–5-pit linear door
campaign"):** the game is set in a **BALL-PIT FACTORY**; the players are its workers.
One venue, ONE pit location; the pit is **reconfigured in place into successive
themed, escalating pits** ("shipments") toward a huge finale (~1,000,000 balls, perf
permitting). Hands → factory tools → automation, as a continuous upgrade run. One
ending. See §1/§1b. This is essentially the old campaign's escalation + tool arc,
minus the expensive multi-room authoring.

**NEXT BUILD:** continue Deliverable B/C follow-ups and then the **shipment wrapper**
(§1b) — the dispose→rebuild-in-place pit switch behind a transition. The old
`docs/handoff-B/BRIEF-B.md` is superseded; `docs/handoff-B/BRIEF-B-bite-a.md` and
`docs/handoff-C/` are current.

**Current open bugs / TODOs / risks live in §12 (consolidated).** Check it before
assuming something works.

---

## 0b. STANDING ENGINEERING PRINCIPLES [DECIDED 2026-10 — apply to all gameplay work]

These exist because the game was drifting toward a HAND-ASSEMBLED, hardcoded scene
(e.g. a lone large pit with manually-placed GameObject walls; containers whose color
was hand-typed to match a ball color). That does not scale to a campaign OR freeplay.
Build systems, not one-off scenes.

- **DATA-DRIVEN, not hardcoded. Build PARAMETERS + reusable PRESETS; author SEQUENCES
  from them.** A pit is fundamentally just **the ball-volume box dimensions + colors**;
  the walls/deck/room around it are ART assets placed in the scene (that part is fine
  to place by hand). Capture a pit as a reusable **PitPreset** (size + color count +
  container layout + which tools are available), as a prefab/ScriptableObject. The
  CAMPAIGN is then "an authored ordered list of presets"; FREEPLAY is "the player picks
  a preset / tweaks the parameters." Same system, two front-ends. Do NOT bake campaign
  assumptions into gameplay systems.
- **DESIGN EVERY SYSTEM TO WORK IN BOTH CAMPAIGN AND FREEPLAY.** Freeplay is a
  first-class lens, not an afterthought. Before building anything, ask "does this work
  if the player chose the parameters instead of the campaign authoring them?" Freeplay
  lets players pick pit size, color count, which colors (incl. future hard modifiers
  like near-identical shades — a freeplay-only idea), and a tool-start setting (start
  with none / a tier / all). Campaign presets should be loadable by freeplay too (and
  freeplay may later offer MORE than the campaign ships — later scope).
- **CONTAINERS SELF-CONFIGURE from their color.** A container's `colorIndex` is the
  only thing authored; its DISPLAY color (and label) must DERIVE from the active
  palette automatically — never hand-matched. Hand-typing a container's color to match
  its ball color is a bug waiting to happen (a palette change silently breaks it). This
  is a concrete near-term cleanup (today they're hand-colored in the scene).
- **The first concrete payoff of this is the PitPreset system** — likely a more
  foundational next build than placing more hand-made pits, because the campaign,
  freeplay, and the shipment switch all sit on top of it. (Flagged in §12.)

---

## 1. The game [DECIDED]

- Co-op 1–4, first person, Steam, ~$5. Final art is the owner's; code provides
  systems, authoring components, hooks, placeholder UI.
- **Fiction = a ball-pit FACTORY; the players are the (understaffed) workers.** This
  framing is load-bearing: it justifies the mechanics. A factory worker naturally has
  a **broom, an excavator, a shop-vac, a dump cart, conveyors** — so the tool tree
  reads as real factory equipment, not a random grab-bag. Lean on this when designing
  anything: "would a ball-pit-factory worker plausibly use this?"
- The venue is one authored factory building. Balls are 0.5 m (per owner). The pit
  need not be life-size, only the balls.
- **Primary mode = ONE FACTORY, a run of escalating "shipment" pits.** One pit
  location. You drain the current pit (get balls out, sort by color, deliver each color
  into its machine); when it's clear, the **next shipment** arrives and the pit is
  reconfigured in place into a bigger / differently-themed pit. Upgrades + money
  persist across the whole run (one live session). ~4–5 pits is roughly the target
  count, but the exact number is **[OPEN]** — a tuning call once we know more.
  The LAST pit is the finale (aiming for ~1,000,000 balls, gated on the client-in-pit
  perf work in §12). A results/win screen fires **only at the true finale**, not
  between shipments.
- **Secondary mode = CUSTOM / FREEPLAY** for people who just want to sort: the existing
  pick-a-size-and-settings sandbox. Cheap to keep (today's behavior). The main run is
  the focus.
- **Per-pit goal:** drain the pit — every ball sold. Clearing a pit does NOT show a
  results screen; it triggers the **next shipment** (transition/cutscene, §1b).
- **Feel:** a new shipment arriving = "oh boy, that'll take a while"; clearing =
  satisfying, almost ASMR; progression like a clicker across the whole run; nothing
  overpowered, no purchase ends a pit minutes after buying it.
- **The toy-box soul is first-class [DECIDED].** In a no-objective playtest, friends
  invented their own fun — digging holes, throwing balls at each other for the
  knockback, chucking balls onto the deck. That emergent physics-sandbox + co-op
  messing-around IS the appeal. Every structural decision must AMPLIFY freeform play,
  never gate it ("do X before you may mess around" is forbidden). Keep throw knockback,
  cannonballing, ball fights — they're free comedy and the word-of-mouth engine.

## 1b. Structure: one factory, shipment pits [DECIDED shape; counts OPEN]

- **One gameplay scene, ONE active pit location.** The venue has the kiddie-pool
  tutorial pit and then the main pit location. Only the ACTIVE pit's balls exist (its
  `PitSite` built); nothing else carries ball/sim cost. Pit switching rides the
  existing `PitWorldBuilt`/`PitWorldDisposed` events — no scene load, no NetPit
  host-restart.
- **Progression = the next shipment, NOT doors between rooms.** On draining a pit, the
  current pit is torn down (`PitWorldDisposed`) and the next shipment's pit is built in
  the SAME location (`PitWorldBuilt`) — new size, new color count, new theme. You do
  NOT physically move the ball pit through the world (that would be a GPU nightmare and
  is pointless); the pit is data rebuilt in place. **A transition/cutscene beat hides
  the dispose→rebuild hitch** ("SHIPMENT 2 ARRIVING", lights change, a door closes).
  The venue DRESSING around the pit changes per shipment (see theming below).
- **[KEY INTEGRATION RISK]** "rebuild the pit into the next shipment mid-session, over
  the network, with carried economy" is the one genuinely unproven piece (same risk the
  old campaign had as its mid-session room switch). The building blocks exist (Activate,
  PitSiteEnvironment build/teardown, the world events, `IPitSiteSelector`); doing it
  twice in one live session with clients + carried money is the thing to prove. The
  transition/cutscene is also what covers the instantiation hitch (§1c). A client
  selector (not derived from lobby settings) MUST replicate the active siteId/shipment
  index to clients over B's gameplay channel.
- **Themed pits = venue dressing, NOT a different pit shape.** The pit interior is
  always an axis-aligned box (§9b hard constraint); the THEME changes the art/room
  around it freely. Examples on the table: kiddie pool → a shipping container →
  … → finale. A "pit in the sky" theme (deck floor partly missing so stray balls fall
  off and return via the out-of-bounds system) is just authored art + the existing
  return-to-pit rule — zero new pit tech.
  - **Shipping-container pit [PROPOSED early beat].** A shipping container is a
    rectangular box → a PERFECT pit shape. Author it as a top/front-opening rectangular
    pit. CAVEAT [DECIDED tech fact]: the dramatic "doors swing open and the pile
    avalanches out onto the floor" is NOT something the collapse sim does for free —
    that's a dynamic breach of the box wall, which the box-interior math does not model.
    It can be done as a SCRIPTED/COSMETIC reveal (doors open, a canned burst of cheap
    balls tumbles out as the real pit becomes active), but NOT as the live pile
    genuinely pouring through a doorway. Don't assume a live breach.
- **Difficulty ramps on TWO authored axes per shipment:** ball count (size, tiny pool →
  ~1M finale) AND color count (**3–4 → up to ~6**; 6 is the sorting-complexity sweet
  spot). More colors = more sorting to manage = genuinely harder.
- **The run is FIXED/authored** (each shipment's size, colors, available tools). The
  only per-player variability is the colorblind DISPLAY setting. Custom mode is where
  players choose settings.
- **Carry-forward:** upgrades + money persist across shipments within the one live
  session. The save records the current shipment index + carried economy + the current
  pit's in-progress state.
- **ONE persistent in-world upgrade station** (§2), NOT a per-tier shop that closes.
  It offers the whole tool tree, gated by what the team can afford as the run deepens.
- **No seeded dailies / no score cadence [DECIDED].** No "pit of the day", no
  leaderboard in scope. The run + the upgrade tree + the toy-box are the game.
- **No mid-progression results screen [DECIDED].** A results screen only makes sense at
  the true finale (every ball of the last pit sold). Between shipments it's just the
  next shipment arriving.

## 1c. Loading / fill performance [DECIDED approach]

- **Ship PRE-BAKED fills for every authored shipment pit.** Fills are deterministic
  from seed+size+settings; shipments are fixed and few, so the DEV bakes each one's
  fill once and ships the `.bin` files. The player's "load" is a file read (seconds),
  not multi-minute pour-and-settle generation. (Baked files are multi-MB each; tens of
  MB total — fine for Steam. Re-bake if a shipment's dimensions/seed change.)
- **Background-prepare the next shipment while playing the current pit.** Fill
  generation/load runs on a worker thread during play (hidden). The INSTANTIATION step
  (GPU buffers, grid, collider pool) runs on the main thread and hitches briefly — hide
  it under the shipment transition/cutscene beat (§1b). Small-first (tutorial loads
  fast) + prebake-the-rest-while-playing + a transition that covers instantiation.
- **Queued optimization (not blocking):** speed up RandomFill GENERATION itself
  (sequential pour; parallelizable with Burst/jobs) — matters for custom/freeplay
  (arbitrary sizes still generate) and dev-side baking; the run ships baked.

## 2. Structure and rules [DECIDED]

- Two areas: **in the pit** (get balls out, up to ground level) and **out of the pit**
  (move toward machines → sort → deliver into the matching one).
- **Containers ("machines"):** one per color, owner-placed; accept balls only through
  the top opening; fill = % of that color sold (no capacity). Wrong color → back to the
  pit (drops from above at a random spot). Out-of-bounds returns strays the same way.
  A container = 3 independent parts: the visible ART model, the intake BOX (a geometry
  test, not a physics trigger — the sell zone), and an Interactable-layer collider (so
  the aim ray targets it). See `docs/handoff-C/HANDOFF-C.md` for the authoring detail.
- **Visible fill is a CORE satisfaction, not optional polish [DECIDED].** The player
  MUST see balls leave the pit and accumulate in the container — a sold ball silently
  vanishing feels awful and undercuts the sorting payoff. Each container shows a 0–100%
  MASS of its color rising as that color sells (`MassFillView`, built in Deliverable C).
  Container art is per-theme; underneath it's always "this color, this fraction full,"
  driven by the replicated ledger. The mass is CHEAP (instanced/faked balls or a shaped
  fill, NOT thousands of rigidbodies — a container may hold 100k+ sold balls). The
  container analogue of the pit's own data-not-objects trick.
- **"Movie magic" between endpoints [DECIDED].** Balls are REAL (physics) only where
  the player's attention is — entering a tool/intake and arriving/settling in the
  container. In between (a vacuum tube, a conveyor, a chute) the visual may be pure
  fakery (instanced movers, a stream, or nothing). Extends the pit's "real where it
  matters, data where it doesn't" rule to delivery, and bounds the real-ball cost of
  every future tool. (Example: a vacuum — balls go real as they're sucked in and again
  as they exit into the container; the travel between is movie magic.)
- **Deposit is PHYSICAL; sell-on-cross, settle-after [DECIDED].** Players physically
  move balls into a container (throw by hand now; tools later). NO hold-to-deposit-
  from-inventory shortcut — the physicality is the point. Money/credit fires the moment
  the ball's CENTRE crosses the intake box (responsive), and the SAME ball continues
  physically into the container and visibly joins the fill. Sell on cross + settle
  visually after, with sale juice (plonk/pop). Revisit only if throwing-to-sell plays
  frustratingly.
- **Upgrades: ONE in-world UPGRADE STATION, not a menu key [DECIDED].** The player
  walks up to the station and interacts; no Tab/hotkey shop. It's a single persistent
  station offering the whole tool tree, gated by affordability — NOT a per-tier shop
  that closes. (Built in Deliverable C; the placeholder Tab shop is retired behind a
  dev flag.)
- **Progress is read from SCENERY, not a HUD counter [DECIDED].** The on-screen
  per-color ball counter was test scaffolding and is removed. Players gauge progress
  from the world: the pit visibly shrinking and the containers visibly filling.
  Diegetic container labels/signage are allowed if a tutorial playtest shows players
  are lost; the HUD counter does not come back. (Money display stays on-screen for now;
  showing money diegetically is parked, §12.)
- **Pushing tools (broom, blower) only move balls on the floor; never feed a container.**
- **Helpers (drones, bots) make piles; only upgrades let them deliver** (first "own
  color only", then delivery into that color's container).
- **Limits live on tools, not containers.** The vacuum especially stays modest.
- **Upgrades are shared,** bought from the team pot; anyone buys, no host privilege, no
  player-count scaling.
- **Owner places every gameplay object** (sites, spawns, containers, stations, build
  zones). Code never assumes a fixed layout/hierarchy.
- **Moving balls are grabbable like resting ones;** players walk the pile, climb walls,
  mantle out, so nobody gets stuck.

## 2c. Container visual direction: PNEUMATIC TUBES (creature skin later) [DECIDED 2026-10]

The container's LOOK/FEEL (the thing the owner was unhappy with as plain tanks) is
decided. Plain tanks/bins/silos are OUT. All of this is an ART + VFX skin over the
SAME existing system (a colored intake box that sells on center-cross + a fill driver,
`MassFillView` / `BallContainer`) — picking this costs nothing architecturally.

- **v1 = PNEUMATIC TUBES.** Each color has a floor-level intake MOUTH that SLURPS balls
  up with a satisfying whoomp and rockets them through CLEAR OVERHEAD PIPES across the
  factory ceiling to that color's collection point. The deposit MOMENT (suck-up +
  balls flying through transparent pipes overhead) is the juice; a tank/belly shows the
  0→100% fill. Horizontal/floor-level intake = no upward throw, forgiving (§2
  sell-on-cross). The travel through the pipe is "movie magic" (§2) — instanced/faked
  movers, not real physics.
- **LIKELY LATER: the HUNGRY CREATURE skin (B).** A strong candidate to layer ON the
  tube system — the collection point is a goofy color-creature that gets fatter/happier
  (0% hungry → 100% stuffed), FED by the pneumatic tube. Creature = satisfying
  *container*; slurp-feed = satisfying *action*. "All creatures stuffed = you win" is a
  great ending image. Not v1-committed; revisit after the tube system feels good.
- **WRONG-COLOR RETURN via pipe [DECIDED].** A wrong-color ball that enters a mouth is
  not stored — it travels a RETURN pipe back OVER the pit area and drops back into the
  pit (reuses the existing wrong-color return-to-pit rule, §10, just routed visibly
  through a pipe instead of dropping from nowhere). Diegetic + readable: "wrong color
  → it comes back."
- **PLAYER SUCTION toy-box feature [DECIDED — fun, keep gentle].** If a player gets too
  close to a tube mouth they can be SUCKED into the pipe and dropped back out (over the
  pit / at a pipe exit) — co-op comedy, same spirit as throw-knockback. MUST be tuned
  GENTLE: a suction pull only in a small zone right at the mouth, never a strong
  always-on vacuum that traps players or ruins normal depositing. A toy, not a hazard;
  tune so it's funny, not frustrating. (Implementation: a small suction trigger at the
  mouth that, above a proximity threshold, carries the player through and respawns them
  at the exit — like the ball return path but for a player body.)
- Container art is still per-area/themed (§1b) — the tube/creature designs can vary per
  shipment theme; the system underneath is unchanged.

## 2b. The kiddie pool = the tutorial [DECIDED]

- The first pit is the tiny rubber kids' pool and doubles as the tutorial. **Hands-only,
  one ball at a time** (no carry upgrade here — possibly no upgrades at all on this
  pit). It teaches the pure loop: grab → throw → sort by color → watch the container
  fill. Built with the SAME pit system, tiny (§9b), ABOVE-GROUND (its box sits on the
  floor, not sunk). It is the first vertical slice built close-to-real (rough art OK) to
  lock the FEEL before the rest of the run is authored. Likely second beat: a shipping-
  container pit (§1b).

## 3. Tools by tier — framed as FACTORY equipment [PROPOSED unless marked]

Primitive-first: build and tune the simple hand tools BEFORE the later tiers.
Automation IS in scope (it's the end-game power fantasy for the huge pits and the
~1M finale plausibly needs it) but is the LAST-scoped tier — don't build it early.

| Tier | In the pit | Move | Sort | Deliver |
|---|---|---|---|---|
| 1 Hands | hands (grab/carry/throw); Second Hand (carry 2); Strong Arm (throw higher) | carry; broom; Push Broom | rake pulls colors into piles; Wide Rake | throw (Wider Funnels); **dustpan** [DECIDED]: scoop a sorted pile, tip in; Bigger Dustpan |
| 2 Hand tools | ball bag; **elevator** [DECIDED]; **excavator** [DECIDED 2026-10]; shop-vac (small, slow) | cart; leaf blower | color comb (rake that catches one color) | dustpan; dump cart (tips over a container rim) |
| 3 Helpers | elevator loader (auto-cycles); drones (pile at station; upgrade: own color only) | ramps (gravity chutes) | **color gates** on ramps; sorter bots | dustpan + dump cart; vacuum top level (one color → straight into its container); drone delivery [DECIDED: only after color-only] |
| 4 Automation (LAST-scoped, in v1) | continuous lift | conveyors (free placement in build zones) | **player-assembled** belts through color gates separate every color | belts end at a container's top opening |

- **Excavator** [DECIDED 2026-10]: a factory-appropriate in-pit extraction tool; reuses
  the pit's Excavate→holder→Dump primitive (§10). Fits the factory fiction well — add
  it to the tree (building it is a later deliverable).
- **Elevator** [DECIDED]: buying opens a hole in the pit wall; a platform runs from the
  pile height to ground and tips its load onto the floor; players can ride it.
- **No separate finale tool** [DECIDED]: the end is the floor appearing and the last
  balls picked by hand — regardless of how much automation is running.
- **Cut** [DECIDED]: Drain finale, shape-sorter table, container loaders/lifters,
  Climbing Gloves, all-in-one optical sorter (gates replace it), claw crane, rope hoist
  (elevator replaces), laundry basket (ball bag replaces), scoop shovel. (NOTE: the
  earlier "excavator — cut" is REVERSED as of 2026-10; the excavator is now IN, Tier 2.)
- Tier 2–4 details are provisional; revisit after playtesting Tier 1.

## 4. Economy and pacing [PROPOSED]

- 1 coin/ball, per-color multipliers in config (all 1), combined sell cap ~×2.
- Overlap rule: a new tool's first level beats the previous tool's best by ~1.5–2×;
  each level +15–30%; level prices grow ~1.6–2.2×.
- Early prices tuned to the first pits (first tools ~20–40 min of hand work); late
  prices climb steeply; a shipment's size decides how far down the tree that stretch of
  the run goes.
- The bottleneck moves between steps so purchases alternate. More colors = more
  gates/sorting to buy (color count is a difficulty knob).
- Economy simulator (editor tool, later) to tune prices across the run's tiers.

## 5. Colors [DECIDED direction]

- Range **~3–4 up to ~6** colors (not 2–8). In the MAIN RUN the count is fixed per
  authored shipment (ramps difficulty: early shipments 3–4, later up to ~6). In CUSTOM
  it's a host setting. 6 is roughly the sorting-complexity sweet spot; exact per-
  shipment counts are a tuning call. The code supports a wider range; this is intent.
- Palette presets scale to the count (Classic, Pastel, Candy, Neon, Ocean, Sunset);
  visual only, can change mid-game. Later: curated custom colors with a "too similar"
  warning.
- Colorblind (per-player setting): Okabe–Ito safe palette; later optional per-color
  ball patterns + matching container icons. (Client-local; built in Deliverable B/C.)

## 6. Win and after [DECIDED]

- Final pit's last ball sold → results panel (time, balls sold, money, upgrades,
  per-player stats + a few fun awards); closeable; free roam continues; save marked
  completed. This fires ONLY at the finale, never between shipments (§1b).

## 7. Fill look [OPEN — leaning Random-only]

- FCC (lattice) vs Random pour. Owner leans toward **removing FCC** — too
  clean/crystalline; Random reads like a real pit (better aw-factor). Random is ~58%
  density so it fills TALLER than FCC (~74%) for the same count — author venue walls/rim
  to the Random fill height. If dropped, FCC may stay a dev-only instant fill. Decision
  pending a final look pass. (The run ships baked Random fills regardless — §1c.)

## 8. Venue authoring (how the pit sits in a level) [DECIDED model]

- The pit's REAL collision walls are invisible proxies the sim builds from the fill
  geometry (they hug the ball mass). Authored "walls" are decorative only. So: author
  pit-dimensions-FIRST — set anchor + profile, then model the pool/room to the cyan
  interior gizmo the PitSite draws; run Fit Venue Bounds To Pit. Lower `rimHeight` so
  the art rim sits at the ball surface (fixes "walls look too tall" — it's unfit art,
  not the sim forcing a height; see §12).
- The pit can be ABOVE-GROUND (box on the floor) or sunk — the sim doesn't care. The
  kiddie pool is above-ground.
- Everything cosmetic/spatial is author-controlled and NOT constrained: the building,
  deck size, lighting, spawn-point placement (anywhere), out-of-bounds volumes (place
  only where wanted), the walkable area (`venueBounds` can cover the whole floor; turn
  `respawnOutsidePlayBounds` off for roaming). Constraints: the pit interior is an
  axis-aligned box, the anchor unrotated, venueBounds contains the ball mass within a
  grid-memory limit.
- One factory venue; the pit is reconfigured per shipment (§1b), so it's one venue with
  several authored pit configs, not several venues. Run `Tools/Ball Pit/Validate Venue`
  after authoring — it warns (doesn't force) about unfit walls, intrusions, etc.

## 9. Structure reuse of the venue/site system (CHOSEN — see §1b)

- The shipment run reuses the venue/site system directly:
  - One scene, one active pit — becomes the shipment sequence (rebuilt in place).
  - Swappable `IPitSiteSelector` — becomes "which shipment index" instead of "lobby
    size choice." A selector not derived from lobby settings MUST replicate the active
    siteId/shipment index to clients (B's gameplay channel), per the A notes.
  - Site/shipment recorded in the save — becomes the run position.
  - `PitWorldBuilt`/`PitWorldDisposed` — drive the shipment switch.
- **One live session, NO NetPit host restart** (that was only for a mid-session
  scene/area change across sessions). The shipment switch stays within the running
  session. (Proving this round-trip twice-in-a-session is the §1b key integration risk.)

## 9b. Pit shape + tiny pits [DECIDED facts about the tech]

- **The simulated pit interior MUST be an axis-aligned rectangular box** (flat floor,
  four vertical walls). Baked deep into the fill/grid/collision/collapse math; non-box
  ball volumes (round, L-shaped, sloped/curved floors) are NOT supported and would be a
  major rewrite. What IS free: (a) the visible art/rim/room around the pit can be ANY
  shape — dress a round pool or a shipping container with art that masks the square box;
  (b) box PROPORTIONS vary freely per shipment (long trough, deep well, wide shallow).
- **Curved-look nuance [DECIDED 2026-10]:** balls DO physically settle on real scene
  colliders, but FILL GENERATION + the COLLAPSE/support scheduler assume a FLAT-FLOORED
  box. So: flat floor + curved WALLS/art = FREE (covers rinks, round pools, containers —
  make the art curve around a rectangular ball footprint). A visibly CURVED FLOOR the
  player digs down to is the only true-rewrite case. Default to box-with-curved-art.
- **The kiddie-pool tutorial (~200 balls) uses the SAME pit system, tiny — no special
  fallback.** At ~200 balls the fill generates instantly; one system means the tutorial
  teaches the exact grab/carry/sort/sell/Deposit loop the real pits use. No pre-bake
  needed this small.

## 10. Tech constraints for gameplay work (verified in pit code)

- **Extraction primitive:** every in-pit tool (incl. the excavator) reuses
  Excavate→holder, then Dump as physics; real cave-in physics refills the hole.
- **Pushing tools need a thaw sweep** (wake resting balls in the tool volume, capped
  per step): PhysX doesn't collide moving tools with static resting balls.
- **Return-to-pit is one step:** Demote → ToGone(Hopper) → Promote → ToLive. Never
  leave a ball in Hopper across a step outside a player hopper (NetPit join sync only
  enumerates player hoppers 0–3). (Built in Deliverable B/C.)
- **Holders beyond players** (carts, elevator, drones, belts) need a small additive
  change to NetPit's sync enumeration (wire format unchanged).
- **Lost balls:** `PitAuthority.Lose` marks `Lost` (would block the win) — the game
  returns strays to the pit via the additive `LossHook` instead (built in Deliverable C).
- **Container fill on clients** comes from the replicated ledger (no extra traffic);
  deposit detection is host-side (active ball positions vs intake boxes).
- **Gameplay state** (money, upgrades, win) lives on its own NGO channel (`GameplayNet`);
  NetPit untouched. Purchases via ServerRpc, same validation for everyone, buyer name
  resolved host-side from the sender id.
- **Belts** (Tier 4) move balls as data (deterministic, ~no bandwidth).
- **Physics budget:** ~4k moving balls at once — tools pour/drop in short bursts.
- **Late-join sync for huge pits** needs a compact "sold" format (one bit/ball) before
  release.
- **Shipment rebuild** rides `PitWorldDisposed`→`PitWorldBuilt` in one session; the
  transition/cutscene hides the instantiation hitch (§1b/§1c). Proving it twice-per-
  session over the network with carried economy is the key open integration (§1b, §12).

## 11. Pillars (quick reference)

- **Satisfying bulk progress** — the pit visibly shrinks; draining feels good (ASMR-ish).
- **Co-op coordination + toy-box** — divide labor (extract/sort/haul) OR just mess
  around; both are valid, neither is gated.
- **Clicker-like progression across the run** — each upgrade changes HOW you play;
  nothing overpowered; no purchase trivializes a pit.
- **Session persistence** — a run is long; money/upgrades/pit state saveable/resumable.
- **Factory fiction makes the tools make sense** (§1).

## 12. Open questions / TODOs / known bugs [consolidated — check before assuming]

**Design [OPEN]:**
- How many shipments (~4–5?), and each one's size + color count + which tools its tier
  unlocks. The authored difficulty curve + prices across the run. Needs the economy
  simulator. The finale target is ~1M balls (gated on the client perf item below).
- Name (working title "Drain the Ball Pit", not final) + exact per-shipment themes
  (kiddie pool → shipping container → … → finale; "pit in the sky" is a candidate).
- Fill look (§7) — leaning Random-only; final call pending a look pass.
- Exact Tier 2–4 tool set (revisit after Tier 1 playtest).
- Player size relative to the 0.5 m balls (visual call).
- Deliverable C owner-questions (fill style, settle dwell time, kiddie dims, dev
  toggles, colorblind) — see `docs/handoff-C/DECISIONS-C.md`.
- Money shown on-screen for now; whether to make it diegetic later is parked.
- **Container FILL fantasy across the run [OPEN].** Decided: one PERSISTENT container
  per color that fills toward 100% by the finale (no shipment/truck "ship-out" — owner
  disliked that). Open: how the fill is SCALED so every pit visibly moves it rather
  than the ~1M finale dwarfing all earlier pits (fill by "fraction of each pit's balls
  of this color delivered," i.e. per-pit chunks, NOT linear ball count). Visual = the
  pneumatic-tube system (§2c). Tune the scaling when the economy/pacing is tuned.

**Foundational engineering TODOs (from the §0b principles) [NEAR-TERM]:**
- **PitPreset system** — make a pit a data-driven, loadable preset (box dims + colors +
  container layout + tool availability), used by BOTH campaign (ordered list) and
  freeplay (player picks/tweaks). Replaces the current hand-assembled "large pit +
  manually-placed GameObject walls" scene. Likely the most foundational next build.
- **Containers self-configure color** from `colorIndex` + the active palette (stop
  hand-coloring them in the scene). Small, near-term.
- **Freeplay parameterization** — expose pit size / color count / colors / tool-start
  setting; load campaign presets. (Later scope, but design toward it now per §0b.)

**Key integration risk [UNPROVEN]:**
- **Shipment rebuild mid-session over the network** with carried economy + client
  siteId replication + transition cover (§1b). The one piece that isn't de-risked.

**Known bugs / perf [TO DIAGNOSE / TUNE]:**
- **CLIENT frame drops IN THE PIT, not on the deck** (multi-machine playtest, host
  smooth). Prime suspect: client-only near-pile cost — `ClientPitColliders`
  (`BallPitGameplay/Player/`) rebuilds the local collider SHELL + kinematic live-ball
  PROXIES every fixed step near balls (exactly "in the pit"). Second suspect: in-pile
  RENDER cost (camera sees more exposed balls up close). DIAGNOSE FIRST: a client with
  the Console open — look for `[ClientPitColliders] the client collider shell is full
  (…)`; and a Profiler frame while hitching — Physics or Camera/render? LEVERS
  (serialized on PlayerSpawner's `PlayerPitPhysicsConfig`, inspector-only, reversible):
  `liveProxyBudget`, `liveProxyRange`, `clientShellKeepSeconds`,
  `clientShellMaxColliders`, `colliderReleasePerStep`. Deeper fix: throttle/spread the
  client shell+proxy maintenance across frames. NOT a networking-transport bug.
- **Phase-A polish:** sticky pile-walking (motor feel); optional footstep ball-scatter
  (`PlayerConfig.playersDisturbPile` seam, off); MEDIUM host carry-mantle stall;
  balls-go-black on editor focus loss (likely editor-only, confirm in a build).
- **"Walls look too tall"** = authored art not yet Fit to the fill (+ `rimHeight`), NOT
  the sim forcing a height (§8). Documentation/authoring item, not a bug.

**Unverified / release items:**
- Steam-transport swap (vs local UTP) — needs a 2nd real machine (a VM has no GPU).
  Lowest-risk piece; the Phish foundation already shipped on it.
- `GpuBallRenderer` graceful "GPU not supported" check before ship.
- URP-vs-Built-in pipeline half-config (see `networking-and-pit.md`) — bundle with the
  eventual Phish→final project rename.

**Queued optimization (not blocking):**
- Speed up RandomFill GENERATION (Burst/jobs) for custom mode + dev baking.

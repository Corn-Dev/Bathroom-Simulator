# Game Design — Drain the Ball Pit

This is the living gameplay design record. Decisions marked **[DECIDED]** are
firm; **[OPEN]** needs a call before it drives code; **[ASSUMPTION]** is a
working default we can revisit cheaply.

> **Status:** the pit tech is **built and validated** (`Assets/_Prototypes/BallPitFinal/`,
> 1,000,000 balls at ~125 fps, real physics feel, exact counts, GPU occlusion).
> The design below drove that build and still holds. Remaining work is the
> gameplay layer (sorting/money/upgrades) plus the two risk areas: networking and
> bigger tools. The stronger Kiro on the other computer leads implementation;
> this repo's Kiro applies/relays. See networking-and-pit steering for the
> architecture, gameplay hooks (`Deposit`, `OnThawed/OnFrozen`, `AddToolSource`,
> stable ids), and the "✅ CURRENT STATUS" block.

## The pit
- One large pit, visually full of colored balls. **[DECIDED]**
- Balls come out one at a time per player initially. **[DECIDED]**
- Win condition: pit fully drained. **[DECIDED]**
- Every ball owns one continuous position (exact pose); the spatial grid is an
  index only, and per-color "fullness" is tracked as an exact ledger/count. This
  is what makes counting exact AND avoids snapping — see networking-and-pit for
  why a count-only or lattice model was rejected. **[DECIDED]**
- Resting balls use static colliders (no Rigidbody) near activity; only balls in
  the active band are true rigidbodies. **[DECIDED]**
- [OPEN] Does the pit have a fixed starting count (e.g. a defined level) or is it
  tuned per difficulty? Default [ASSUMPTION]: a single fixed starting count we
  tune by feel. (Prototype ships 5 size presets, keys 1–5: 40k / 144k / 384k /
  1M / ~2.35M, for tuning by feel.)

## Balls & colors
- Start with 4 colors. Tunable — adding/removing colors should be data-driven
  (a ScriptableObject or config list), not hardcoded. **[DECIDED]**
- [ASSUMPTION] Each color is equally common at start. Weighting can come later.
- [OPEN] Do balls have any properties beyond color (size, value, "special"
  balls)? Default: color only for v1.

## Sorting & selling
- A player carries a ball to a container and drops it in. **[DECIDED]**
- Correct color container = sell = money to shared pot. **[DECIDED]**
- Wrong color container = ball returns to the pit. **[DECIDED]**
  - [OPEN] Returns instantly to pit count, or physically ejected back into the
    pit so it must be re-grabbed? Default [ASSUMPTION]: returns to pit count with
    a small visual/audio "reject" cue (simpler, networking-friendly).
- [OPEN] Is there a sell value per color (rarer colors worth more) or flat value?
  Default: flat value per ball in v1.

## Two separable jobs (important for co-op depth)
Extraction (getting balls OUT of the pit) and sorting (getting them INTO the
right container) are **distinct steps for the player at first**. This separation
is the backbone of co-op division of labor and the upgrade tree. **[DECIDED]**
- Early game: a player does both (grab from pit, walk to container, drop).
- Later game: upgrades can automate or decouple these steps (sorters, drones).

## Money (shared pot)
- Shared team pot funds shared upgrades. **[DECIDED]**
- [OPEN] Are any upgrades per-player (e.g. personal carry capacity) vs.
  pit-wide/shared? Likely a mix. Needs a call when we design the upgrade tree.

## Upgrades (first-tier brainstorm — NOT final)
Design goal: each upgrade changes HOW you play, not just a stat.
- **Carry capacity** — hold more than one ball at a time (per-player?).
- **Vacuum** — suck balls out of the pit at range / in bulk (extraction speed).
- **Drones** — autonomous helpers that haul or extract.
- **Sorters** — automate the sorting step (balls fed in get routed to the right
  container), decoupling extraction from sorting.
- [ASSUMPTION] Upgrades are data-driven (ScriptableObject definitions) with:
  id, display name, description, cost, prerequisite(s), effect hook. This keeps
  the tree editable without code changes.
- [OPEN] Upgrade progression shape: linear tiers, branching tree, or flat shop?

## Persistence (session save/resume)
- A full playthrough is long; most groups won't finish in one sitting.
  Sessions MUST be saveable and resumable. **[DECIDED]**
- See networking-and-pit and persistence notes for what state must be saved:
  pit remaining count (per color), money pot, purchased upgrades, container
  contents/progress.
- [OPEN] Who owns the save — the host (one save per host), or is it tied to a
  lobby/party? Default [ASSUMPTION]: host-owned save, loaded when host starts a
  session (reuses existing SaveManager + Steam Cloud).

## Open questions parking lot
- Fail states / soft-fail? (Is there any way to "lose", or only win/quit?)
- Timers or score for replayability?
- Cosmetics / progression that persists across playthroughs vs. per-session?
- Controller support expectations?

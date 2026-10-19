# STEERING ALIGNMENT — proposed edits to match GAME-DIRECTION.md

This doc proposes the edits needed to bring the steering files in line with the
chosen direction in `GAME-DIRECTION.md` (one refilling venue + upgrade-driven
"clear" loop + seeded score; the fixed 4–5-pit linear door campaign is CUT from
v1 and demoted to a post-launch idea; the Phase-4 automation tier is post-launch).

**Nothing here is applied yet.** The steering docs are shared memory across
sessions — the owner approves these before any file is touched. Each item gives the
file, the exact current text, the proposed replacement, and a one-line rationale.
`[DECIDED] CONFLICT` flags where the new direction contradicts an owner-confirmed
decision and needs an explicit call.

**Core principle for the rewrite:** the campaign isn't *deleted* as an idea — the
escalation (bigger size + more colors) it provided survives as **per-refill scaling
inside one venue**. So most edits reframe "sequence of authored rooms connected by
locked doors" → "one venue whose pit refills bigger/more-colorful, with persistent
upgrades," and move automation (Phase 4) + multi-room to a POST-LAUNCH section.

Legend: ✏️ = wording change · ⚠️ = `[DECIDED]` conflict needing owner sign-off ·
➕ = add · 🗑️ = remove/retire-to-post-launch.

---

## A. `.kiro/steering/product.md`

### A1 ✏️ Elevator pitch — campaign → refilling venue
**Old:**
> Players work through a **campaign** of ever-bigger pits of colored balls. In each
> pit you drain it by pulling balls out, sorting them by color, and delivering each
> color into its matching container. … Upgrades and money carry forward from pit to
> pit. Win = drain the final pit.

**New:**
> Players drain a giant pit of colored balls in a single authored venue: pull balls
> out, sort them by color, and deliver each color into its matching machine. A
> correct delivery "sells" the ball for 1 coin into a shared team pot; wrong color
> goes back to the pit. The pot funds shared upgrades (a tool tree: by hand → better
> hand tools → helpers). Draining the main pit the first time is the completion arc
> (results screen); afterward the pit refills — optionally bigger and with more
> colors — so the upgrade tree and co-op play keep paying off. Optional seeded
> "pit of the day/week" gives a shared challenge + drain-time score.

**Rationale:** The pitch is the first thing a new session reads; it must describe
the refilling-venue loop, not a linear campaign with a final pit.

### A2 ✏️ Modes block — rewrite Campaign/Custom
**Old:**
> - **Campaign (primary):** one fixed authored playthrough, ~4–5 pits in sequence,
>   tiny kids' pool (tutorial) → huge. Upgrades + money carry forward. One live
>   session, pits are rooms connected by doors (see game-design.md §1b).
> - **Custom / Freeplay (secondary):** the pick-a-size-and-settings sandbox …

**New:**
> - **Main loop (primary):** one authored venue; a kiddie-pool tutorial pit then a
>   main pit you drain. First clear = a results screen; the pit then refills
>   (authored size/color scaling) and upgrades + money persist in the one live
>   session. An optional seeded "pit of the day/week" adds a shared drain-time score.
> - **Custom / Freeplay (secondary):** the pick-a-size-and-settings sandbox for
>   people who just want to sort. It's the existing behavior, kept cheaply.

**Rationale:** Modes must match §2.3 of the direction; removes the ~4–5-pit/door
framing while keeping escalation as refill scaling.

### A3 ✏️ Pillars — "across the campaign" → "across the venue/run"
**Old:**
> - **Clicker-like progression across the campaign.** …
> - **Session persistence.** A full campaign is long; it must be saveable/resumable.

**New:**
> - **Clicker-like progression across a run.** Each upgrade changes HOW you play;
>   nothing is overpowered; no purchase ends a pit minutes after buying it.
> - **Session persistence.** Runs are long; progress (money, upgrades, pit state)
>   must be saveable/resumable.

**Rationale:** "campaign" is no longer the framing; progression now spans a
persistent run in one venue.

### A4 ➕ Out of scope — add the v1 cut line
**Old (list):**
> - Enemies/combat, procedural level generation, competitive/PvP.
> - The old Phish fishing content and the Bathroom Simulator concept (both retired).

**New (append two bullets):**
> - **[v1] Multi-room linear door campaign (5 authored pit rooms).** Escalation ships
>   as refill scaling in one venue; a true multi-venue campaign is a post-launch idea.
> - **[v1] Full automation tier (conveyors/belts, drone delivery, player-built
>   factory).** Post-launch; v1 is hands + simple tools + first helpers.

**Rationale:** Makes the solo-scope cut line explicit at the product level so future
sessions don't rebuild the campaign/automation.

### A5 ✏️ Internal-doc pointer line
**Old:**
> The FULL living game design is `game-design.md` (campaign structure, tool phases, …)

**New:**
> The FULL living game design is `game-design.md` (venue + refill loop, tool phases, …)

**Rationale:** Keep the cross-reference accurate after the structure change.

---

## B. `.kiro/steering/game-design.md`

> This file carries the most OLD framing (it's where the campaign was decided). The
> big items below are **`[DECIDED]` conflicts** — the owner originally confirmed the
> campaign — so they need an explicit "yes, supersede it" from the owner. The
> direction doc exists precisely because the owner reopened this.

### B1 ⚠️ §0 "Where we are" — the headline campaign decision
**Old:**
> **The big design decision this session: the game is a CAMPAIGN** (see §1, §1b, §1c).
> A fixed sequence of ~4–5 pits (tiny kids' pool tutorial → huge), one live session,
> pits are rooms connected by doors, upgrades/money carry forward, ship pre-baked
> fills. Custom/freeplay is the secondary mode. This supersedes any older "pick a
> size, drain it" framing.

**New:**
> **The big design decision (revised): the game is ONE VENUE + a refilling "clear"
> loop** (see §1, §1b, §1c). A kiddie-pool tutorial pit then a main pit in one
> authored venue; draining it the first time is the completion arc, then the pit
> refills with authored size/color scaling; upgrades + money persist; one live
> session; ship pre-baked/seeded fills. An optional seeded "pit of the day/week"
> adds a shared drain-time score. Custom/freeplay is the secondary mode. This
> supersedes BOTH the older "pick a size, drain it" framing AND the interim
> "4–5-pit linear door campaign."

**Rationale + CONFLICT:** This reverses the `[DECIDED]` campaign call. Owner must
confirm the supersede. Everything downstream (B1b…B9) depends on this.

### B2 ✏️ §0 "NEXT BUILD = Deliverable B" note
**Old:**
> … **must be revised to the campaign framing before building** (room-to-room doors
> + lock, carry-forward economy, per-tier shops, pre-baked fill loading, campaign
> save). Suggested approach: build B in two bites — (a) the core loop in the existing
> `large_1` pit … then (b) the campaign scaffolding.

**New:**
> … **must be revised to the one-venue refill framing before building** (carry-
> forward economy, single in-world upgrade station, pre-baked/seeded fill loading,
> run save with pit-refill state). Suggested approach: build B in two bites — (a) the
> core loop in the existing `large_1` pit … then (b) the refill + optional seeded-pit
> wrapper (NOT campaign room/door/lock/per-tier-shop scaffolding).

**Rationale:** B is loop-level and still correct; only its wrapper changes from
campaign scaffolding to refill scaffolding. Matches direction §5.

### B3b ✏️ §1 "Feel" bullet — "across the whole campaign"
§1's **Feel** bullet (line 69) sits just after the mode/goal bullets B3 edits and
was NOT inside B3's quoted old-text, so its "campaign" string survived.

**Old:**
> - **Feel:** walking into each new pit = "oh boy, that'll take a while"; clearing =
>   satisfying, almost ASMR; progression like a clicker across the whole campaign,
>   but nothing overpowered and no purchase ends a pit minutes after buying it.

**New:**
> - **Feel:** walking up to the pit = "oh boy, that'll take a while"; clearing =
>   satisfying, almost ASMR; progression like a clicker across the whole run, but
>   nothing overpowered and no purchase ends a pit minutes after buying it.

**Rationale:** Removes the last multi-pit framing in §1 ("across the whole campaign"
→ "across the whole run"). The plural "walking into each new pit" is softened to
"walking up to the pit" to drop the multi-pit implication — but note refills DO grow
the pit each time, so "each new pit" was arguably defensible; flagging for the owner
in case they prefer to keep the plural.

### B3 ⚠️ §1 "The game" — primary/secondary mode bullets
**Old:**
> - **Primary mode = CAMPAIGN (a single fixed playthrough).** Players progress
>   through ~4–5 pits in one authored sequence, each bigger/harder than the last,
>   carrying their upgrades and money forward the whole way. Pit #1 is a tiny kids'
>   plastic pool that doubles as the tutorial. The final pit needs full automation.
> - **Secondary mode = CUSTOM / FREEPLAY** … Campaign is the focus.
> - **Per-pit goal:** … the pit is complete when every ball is sold, which opens the
>   door onward.

**New:**
> - **Primary mode = ONE VENUE, a refilling clear loop.** A kiddie-pool tutorial pit
>   then a main pit in one authored venue. Draining the main pit the first time is
>   the completion arc; it then refills with authored size/color scaling, and
>   upgrades + money persist. The top tool tier is simple helpers (no mandatory full
>   automation to finish).
> - **Secondary mode = CUSTOM / FREEPLAY** … The main loop is the focus.
> - **Pit goal:** drain the pit — get balls out, sort by color, deliver each color to
>   its machine. A correct ball sells for 1 coin into a shared pot; the pit is
>   complete when every ball is sold (→ results screen, then the pit refills).

**Rationale + CONFLICT:** Removes the campaign + "final pit needs full automation"
`[DECIDED]` content; replaces the door-open completion with results+refill.
"Final pit needs full automation" conflicts with the post-launch-automation cut —
owner must confirm v1 is finishable with hands + simple tools + first helpers.

### B4 ⚠️ §1b "Campaign structure" — the heart of the old framing
This whole subsection is written around multi-room doors/locks/per-tier shops. It
should be **replaced** with a "One venue + refill loop" subsection.

**Old (representative lines):**
> - **One gameplay scene, pits as connected rooms.** … ~4–5 pit areas … rooms linked
>   by doors/corridors. …
> - **Progression = walk through a door to the next pit.** On draining a pit the door
>   to the next area opens; once ALL players pass … the door **recloses and locks**
>   … The finished pit's SHOP also closes …
> - **Difficulty ramps on TWO axes per pit … ball count … AND color count.** …
> - **Campaign is FIXED** (authored per pit: size, colors, shop/tool availability). …
> - **Carry-forward:** upgrades + money persist across pits …

**New (replacement §1b "One venue + refill loop"):**
> - **One gameplay scene, one active pit.** The venue has the kiddie-pool tutorial
>   pit and one main pit area in a single scene. Only the ACTIVE pit's balls exist
>   (its `PitSite` built); the tutorial pit is dormant geometry after onboarding
>   (ZERO ball/sim cost). Site/refill switching rides the existing `PitWorldBuilt`/
>   `PitWorldDisposed` events — no scene load, no NetPit host-restart.
> - **Progression = refill, not doors.** Draining the main pit → results screen →
>   the pit refills in place (optionally bigger / more colors). No room-to-room
>   doors, no door lock, no per-tier shops that close. There is ONE persistent
>   in-world upgrade station.
> - **Difficulty ramps on TWO authored axes per refill: ball count (size) AND color
>   count (3–4 → up to ~6).** Same knobs as before, now applied across refills
>   instead of across rooms.
> - **The venue is FIXED/authored; refill scaling is authored data** (a small table
>   of size+color steps). The only per-player variability is the colorblind DISPLAY
>   setting. Custom mode is where players choose settings freely.
> - **Carry-forward:** upgrades + money persist across refills within the one live
>   session; the save records the current refill step + carried economy + the current
>   pit's in-progress state.
> - **Optional seeded "pit of the day/week":** a fixed size+colors+seed shared that
>   period, with a drain-time leaderboard (leaderboard may be a fast-follow). Cheap
>   because fills are already seed-deterministic and bakeable (see §1c + networking).

**Also drop (explicit, not accidental):** the current §1b sub-bullet
> - If per-area lighting ever becomes too constrained in one scene, additive scene
>   loading of each room is a documented fallback, but start single-scene.
is removed by this wholesale replacement — there are no rooms anymore, so the
per-room additive-scene-loading fallback no longer applies. If per-pit lighting
isolation is ever wanted for the single venue, treat it as a post-launch concern.

**Rationale + CONFLICT:** This is the central campaign `[DECIDED]` reversal. Keeps
the two difficulty axes and carry-forward (still true); drops doors/locks/per-tier
shops. Needs owner sign-off.

### B5 ✏️ §1c "Loading / fill performance" — campaign pits → refills + seeds
**Old:**
> - **Ship PRE-BAKED fills for every campaign pit.** … campaign pits are fixed and
>   few (~5), so the DEV bakes each pit's fill once …
> - **Background-prepare the next pit while playing the current one.** …

**New:**
> - **Ship PRE-BAKED fills for the authored refill steps + any fixed seeded pit.**
>   The main pit's refill steps are few and authored, so the DEV bakes each step's
>   fill once and ships the `.bin` files. The seeded "pit of the day/week" is
>   seed-deterministic; bake common ones or generate on a worker thread. The player's
>   "load" is a file read, not multi-minute generation.
> - **Background-prepare the next refill while playing the current pit.** Generation/
>   load runs on a worker thread during play; the instantiation hitch is hidden under
>   the results screen / refill transition beat.

**Rationale:** The prebake insight is still 100% valid; it just applies to refill
steps + seeded pits instead of 5 campaign rooms.

### B6 ⚠️ §2 "Structure and rules" — upgrade station line (already mostly aligned)
Mostly compatible (sell-on-cross, visible fill, in-world station, scenery-not-HUD
are all kept — they match DECISIONS-C and the direction). One line to adjust:

**Old (in the in-world upgrade station bullet):**
> This is the per-tier shop of §1b made diegetic (each area's station is that tier's
> store; it closes when the area is finished). …

**New:**
> This is a single persistent in-world station (NOT a per-tier shop that closes); it
> offers the whole tool tree, gated by what the team can afford. …

**Rationale:** Removes the per-tier/closing-shop assumption tied to rooms; the rest
of §2 stays as-is.

### B7 ✏️ §2b "Kiddie pool = tutorial" — reword "campaign" reference
**Old:**
> … It is the first vertical slice built close-to-real … before the rest of the
> campaign is authored.

**New:**
> … It is the first vertical slice built close-to-real … before the main pit + refill
> scaling are authored.

**Rationale:** Keep this section (it's great and matches DECISIONS-C); only the
trailing "campaign" word changes. Pit #1 "doubles as tutorial" stays true.

### B8 ⚠️ §3 Tools table + "Phase 4 Automation" row
**Old (table row + surrounding):**
> | 4 Automation | continuous lift | conveyors (free placement in build zones) |
> **player-assembled** … belts through color gates … | belts end at a container's
> top opening |

**New:**
> Mark the **Phase 4 Automation row as POST-LAUNCH** (not v1). v1 ships Phases 1–2 in
> full and optionally the FIRST Phase-3 helper (per §3 that's **drones that pile at a
> station — no delivery yet**); conveyors/belts/player-built factory and drone
> *delivery* are a post-launch tier. Keep the row in the doc under a "POST-LAUNCH /
> stretch" heading so the design isn't lost.

**Rationale + CONFLICT:** Several Phase-4 items are `[DECIDED]` (elevator, player-
assembled belts, "no finale tool"). The elevator (Phase 2) and "no finale tool" stay
in v1; the belt/conveyor automation moves to post-launch. Owner must confirm
automation is out of v1 (direction §2.6 cut line).

### B9 ✏️ §9 "Campaign (CHOSEN)" — retitle + reframe
**Old:**
> ## 9. Campaign (CHOSEN — see §1b for the structure)
> - The campaign is now the primary mode (§1b/§1c). The venue system was built
>   campaign-ready and this uses it directly: … becomes "campaign progress" … Site
>   recorded in the save — becomes campaign position. …

**New:**
> ## 9. One venue + refill loop (CHOSEN — see §1b)
> - The refilling-venue loop is the primary mode (§1b/§1c). The venue/site system is
>   reused directly: the `IPitSiteSelector` becomes "which refill step / seeded pit"
>   (still must replicate the active siteId to clients per the A notes); the active
>   site + refill step are recorded in the save; `PitWorldBuilt`/`PitWorldDisposed`
>   drive the refill switch. Because it's one live session, NO NetPit host restart is
>   needed.

**Rationale:** The technical reuse is identical; only the vocabulary ("campaign
position" → "refill step / seeded pit") changes.

### B10 ✏️ §11 "Open questions" — update the campaign line
**Old:**
> - The campaign pit sequence: how many pits (~4–5), and each one's size + color
>   count + which tool tier its shop unlocks. The authored difficulty curve. …

**New:**
> - The refill curve: how many refill steps, each step's size + color count, and the
>   single-station tool pricing across the run. Plus the seeded-pit cadence (daily/
>   weekly/random) and whether a drain-time leaderboard ships in v1 or as a
>   fast-follow. Needs design + the economy simulator to tune.

**Rationale:** The open question is still real; it's now about refill steps + seeds,
not a 5-room sequence.

### B10b ✏️ §11 "Open questions" — second `campaign` reference (prices line)
§11 has a distinct line (separate from the one B10 edits) that still says "campaign":

**Old:**
> - Prices across the campaign tiers (one continuous curve; playtests + simulator).

**New:**
> - Prices across the run's refill tiers (one continuous curve; playtests + simulator).

**Rationale:** Same economy-curve question, reworded off the multi-pit framing so no
live "campaign" string survives in §11.

### B11 ✏️ §4 Economy — "on every pit" wording
**Old:**
> - Early prices equal on every pit (first tools ~20–40 min of hand work); … pit size
>   decides how far down the tree a game goes.

**New:**
> - Early prices are tuned to the first pit (first tools ~20–40 min of hand work); …
>   refill size decides how far down the tree a run goes.

**Rationale:** Minor reword from multi-pit to single-venue-with-refills.

### B12 ✏️ §7 "Fill look" — one-word `campaign` touch-up
§7 ends with a parenthetical that still says "campaign"; the body of §7 (FCC vs
Random) needs NO change, only this trailing word:

**Old:**
> (Campaign ships baked Random fills regardless — §1c.)

**New:**
> (The main loop ships baked Random fills regardless — §1c.)

**Rationale:** Trivial reword so "campaign" doesn't survive in §7; the fill-look
decision itself is structure-agnostic and untouched.

### B13 ✏️ §5 Colors — "In CAMPAIGN the count is fixed per pit"
§5's body (3–6 colors, palette presets, colorblind) is structure-agnostic, but one
sentence (line 216) still carries the retired campaign/multi-pit framing:

**Old:**
> In CAMPAIGN the count is fixed per pit (ramps difficulty: early pits 3–4, later up
> to ~6). In CUSTOM it's a host setting.

**New:**
> In the MAIN LOOP the count is fixed per authored refill step (ramps difficulty:
> early steps 3–4, later up to ~6). In CUSTOM it's a host setting.

**Rationale:** Same color-count-is-a-difficulty-knob design, reworded from "per pit
in a campaign" to "per refill step" so no live "campaign" string survives in §5.

### B14 ✏️ §9b Pit shape — "visual variety across the campaign"
§9b's body (the box-interior tech: axis-aligned rectangular pit, free proportions,
tiny kiddie-pool) is genuinely unchanged — the tech is structure-agnostic. Only one
trailing phrase (line 277) carries the retired framing:

**Old:**
> giving plenty of visual variety across the campaign without leaving "rectangular".

**New:**
> giving plenty of visual variety across refill steps without leaving "rectangular".

**Rationale:** One-word-class touch-up ("across the campaign" → "across refill
steps"); the box-interior constraint and the per-pit proportion freedom it describes
are untouched.

> **NOTE on §6 (Win and after), §8 (Venue authoring), §10 (Tech constraints):** these
> are all structure-agnostic and need NO change. §6's results panel + "free roam
> continues + save marked completed" fits the first-clear arc perfectly (the refill is
> the "keep playing"). §8's "ship ONE pit size / one authored venue" already LEANS the
> way the direction recommends — the direction just confirms it.
>
> §5 (Colors), §7 (Fill look), and §9b (Pit shape / tiny pits) are **substantively**
> structure-agnostic too, but each carried a stray live "campaign" string and so get a
> one-phrase touch-up above: §5 in B13, §7 in B12, §9b in B14. Their design bodies
> (color range/presets, FCC-vs-Random, the box-interior pit constraint) are unchanged.

---

## C. `.kiro/steering/achievements.md`

> This list is in good shape and largely structure-agnostic. Only the pit-COMPLETION
> group assumes "one per pit profile in a sequence." Light edits.

### C1 ✏️ "Completion (one per pit profile + meta)" group
**Old:**
> 23 Kiddie Pool Champion — smallest — 45% · 24 Party Pooper — 2nd size — 30% ·
> 25 Warehouse Clearance — 3rd — 18% · 26 The Big Drain — biggest — 10% · 27
> Drainmaster — every size — 6%

**New (reframe "sizes in a campaign" → "refill steps"):**
> 23 Kiddie Pool Champion — clear the tutorial pit — 45% · 24 First Drain — clear the
> main pit the first time — 30% · 25 Bigger Pit — clear a scaled-up refill — 18% ·
> 26 The Big Drain — clear the largest authored refill — 10% · 27 Drainmaster — clear
> every authored refill step (+ a seeded pit) — 6%

**Rationale:** Maps the completion tier onto refill steps instead of a 5-pit
sequence. Numbers/intent unchanged.

### C2 ✏️ Header phase tags + "pit-size achievements" note
**Old (header line, ~line 6):**
> Pit-size achievements map to pit profile keys, so they follow whatever sizes ship.

**New (header line):**
> Pit-size / completion achievements map to the authored refill steps' pit profile
> keys, so they follow whatever sizes ship.

**Also append a v1 scope note after the existing phase-tags sentence** ("Phase tags:
C = Phase 2 tools, D = Phase 3, E = Phase 4; if a tagged tool was cut in
game-design.md, drop its achievement."):
> **v1 scope note:** Phase-4 (E) automation is POST-LAUNCH, so E-tagged achievements
> (48 Some Assembly Required, 49 Factory Floor, 54 Handcrafted, 68 Lights-Out Factory,
> and E parts of 50/65–67) are POST-LAUNCH too — keep them designed but don't gate v1
> on them. Pit-completion achievements map to authored REFILL STEPS (+ the seeded
> pit), not a room sequence.

**Rationale:** Folds the "refill step" vocabulary into the stale header line (review
Finding 5) and flags which achievements wait for the post-launch automation tier
(direction §2.6). Keeps the list otherwise intact.

### C3 ➕ (optional) seeded-pit achievements
**Add (optional, if the seeded pit ships in v1):**
> ➕ Daily Dipper — clear a seeded pit of the day/week — ~20% ·
> ➕ Pit Record — beat your own best drain time on a seeded pit — ~15%

**Rationale:** The new seeded-score layer deserves a couple of cheap achievements;
optional, drop if the leaderboard is a fast-follow.

> **Everything else in achievements.md is already aligned** — the skill/co-op/secret
> tiers (Dodgeball, Cannonball, Bucket Brigade, Catch, etc.) directly reward the
> toy-box the direction protects, and the lifetime/pit-moment tiers are
> structure-agnostic. No other edits needed.

---

## D. `.kiro/steering/networking-and-pit.md`

> This file is TECH, not structure, and is almost entirely unaffected — the pit, the
> net protocol, the fill/bake pipeline all work identically whether pits are rooms or
> refills. Two tiny wording touch-ups only; no technical change.

### D1 ✏️ Bake-the-fill note — "per pit" → "per refill step + seeded pit"
This file contains NO "campaign" / "campaign pit" string anywhere (grep: 0 matches),
so there is nothing worded as "each campaign pit" to change. The real line is in the
"Supporting decisions" of the CHOSEN ARCHITECTURE block (~lines 227–229):

**Old:**
> - **Bake the initial fill** offline (pour, settle, freeze) and ship as a binary
>   asset per pit — a jittered lattice isn't physically consistent and pops when
>   deep balls thaw.

**New:**
> - **Bake the initial fill** offline (pour, settle, freeze) and ship as a binary
>   asset per authored refill step + any fixed seeded pit — a jittered lattice isn't
>   physically consistent and pops when deep balls thaw.

**Rationale:** The bake pipeline is unchanged — it bakes a fill per size+colors+seed;
only the vocabulary (how many / why) shifts from campaign rooms to refill steps +
seeded pits. No code/protocol impact.

### D2 ➕ Add a one-line pointer to the new direction
**Add near the top STATUS block:**
> Game structure is the one-venue refill loop — see `docs/direction/GAME-DIRECTION.md`
> and game-design.md §1b. The pit tech below is structure-agnostic and unchanged.

**Rationale:** So a tech-focused session lands on the current framing. The frame-drop
`[TO DIAGNOSE]` item, protocol notes, bake pipeline, and all measurements stay as-is.

> **No `[DECIDED]` conflicts in this file.** The NetPit protocol, static-collider
> architecture, "bake the fill," and the "one live session, no host restart" point
> all REINFORCE the refill loop (refills ride `PitWorldBuilt`/`Disposed`, same as the
> campaign plan did).

---

## E. Summary of `[DECIDED]` conflicts needing an explicit owner call

These are the places the new direction reverses an owner-confirmed `[DECIDED]`
item. The direction doc was commissioned to reopen exactly these, but they should be
signed off explicitly before editing the steering files:

1. **B1 / B3 / B4 — "the game is a CAMPAIGN of ~4–5 linear door-connected pits"**
   → reversed to "one venue + refilling clear loop." (The biggest one.)
2. **B3 — "the final pit needs full automation"** → reversed to "v1 is finishable
   with hands + simple tools + first helpers; automation is post-launch."
3. **B8 — Phase-4 automation (`[DECIDED]` player-assembled belts, conveyors)**
   → moved to post-launch. (NOTE: the `[DECIDED]` **elevator** (Phase 2) and **"no
   finale tool"** are KEPT in v1 — only the belt/conveyor/drone-delivery automation
   moves out.)

Everything else is reframing/terminology that aligns cleanly with existing
`[DECIDED]` loop decisions (DECISIONS-C: sell-on-cross, visible fill, in-world
station, scenery-not-HUD, kiddie-pool tutorial) — all of which the direction KEEPS.

---

## Responses to the design review (revision pass 2)

Second review verdict was CHANGES_REQUESTED (3 MEDIUM, 3 NITs). The three MEDIUM
findings were all the same class of miss: live "campaign" strings in game-design.md
(§5, §9b, and the §1 "Feel" bullet) that the alignment doc did not edit while
asserting none survived — two of them even listed on the "needs NO change" note. All
three are now fixed with exact old→new B-items, and the no-change note is corrected.

- **Finding 1 [MEDIUM] — §5 Colors keeps a live "In CAMPAIGN the count is fixed per
  pit" string, yet was on the "needs NO change" list.** ADDRESSED. Added **B13** with
  the exact edit (§5 line 216 → "In the MAIN LOOP the count is fixed per authored
  refill step (… early steps 3–4, later up to ~6). In CUSTOM it's a host setting.")
  and removed §5 from the no-change NOTE (moved it into the "substantively unchanged,
  one-phrase touch-up" line).
- **Finding 2 [MEDIUM] — §9b keeps "visual variety across the campaign", yet was on
  the "needs NO change" list.** ADDRESSED. Added **B14** editing just the trailing
  phrase (§9b line 277 → "giving plenty of visual variety across refill steps without
  leaving 'rectangular'") and removed §9b from the no-change NOTE (its box-interior
  tech body is genuinely unchanged — flagged as such).
- **Finding 3 [MEDIUM] — §1 "Feel" bullet keeps "progression like a clicker across
  the whole campaign"; not inside B3's quoted old-text.** ADDRESSED. Added **B3b**
  editing the Feel bullet (line 69 → "across the whole run"), and softened "walking
  into each new pit" → "walking up to the pit" while flagging to the owner that the
  plural was arguably defensible (refills do grow the pit).
- **Finding 4 [NIT, consistency OK] — "no purchase ends a pit minutes after buying
  it" kept verbatim in both the direction (§0/§2.2) and A3's pillar rewrite.**
  VERIFIED consistent; no change needed, recorded to confirm the spot-check.
- **Finding 5 [NIT, optional] — achievements.md header "Pit-size achievements map to
  pit profile keys" survives, slightly stale.** ADDRESSED (folded in). **C2** now
  edits the header line → "Pit-size / completion achievements map to the authored
  refill steps' pit profile keys, so they follow whatever sizes ship," in addition to
  the appended v1-scope note.
- **Finding 6 [NIT, verify] — "color comb" (direction §2.6, Phase 2) consistent with
  game-design §3.** VERIFIED consistent (color comb is in the Phase-2 Sort cell of
  §3); no change, recorded to confirm the cross-check.

No change was needed to GAME-DIRECTION.md this pass — all three MEDIUM findings and
both substantive NITs were edits inside STEERING-ALIGNMENT.md (the alignment doc's
coverage of game-design.md), and Findings 4/6 were consistency confirmations.

### History — responses to the first review (CHANGES_REQUESTED, 1 MEDIUM, 5 NITs)

- **F1 [MEDIUM] — D1 cited "campaign pit" text that doesn't exist in
  `networking-and-pit.md`.** ADDRESSED. **D1** states the file has zero "campaign"
  strings and quotes the REAL line ("ship as a binary asset **per pit**", Supporting
  decisions) → "per authored refill step + any fixed seeded pit."
- **F2 [NIT] — missed the second §11 `campaign` reference.** ADDRESSED via **B10b**
  ("Prices across the run's refill tiers (…)").
- **F3 [NIT] — §7 Fill look still contained "campaign".** ADDRESSED via **B12**.
- **F4 [NIT] — B4 dropped the additive-scene-loading-per-room fallback bullet.**
  ADDRESSED with an explicit "Also drop" note in **B4**.
- **F5 [NIT] — confirm "color comb" consistency.** VERIFIED (same as this pass's F6).
- **F6 [NIT, optional] — name the "first Phase-3 helper."** ADDRESSED: "drones that
  pile at a station, no delivery" in GAME-DIRECTION §2.6 and alignment **B8**.

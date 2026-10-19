# GAME DIRECTION — Drain the Ball Pit

A decisive direction recommendation for a solo-dev, first-released, ~$5 co-op
Steam game. This doc is the "why/what" of the game loop and structure. It keeps
the FIXED pillars (ball-pit tech, sort-by-color, co-op, first-person, shared
money + upgrades, Steam, ~$5) and makes ONE opinionated call on everything that
was on the table — the overall loop and structure.

> Companion: `STEERING-ALIGNMENT.md` lists the exact edits to bring the steering
> docs in line with this direction. Nothing in the steering files is changed until
> the owner approves those edits.

---

## 0. TL;DR (read this first)

- **Keep:** the ball pit as the star, grab/throw/dig physics, sort-by-color,
  shared money → shared tool-tree upgrades, co-op 1–4, first-person, Steam, ~$5.
- **Change:** drop the **fixed 4–5-pit linear "walk through doors" campaign** as
  the primary structure. It's too much bespoke content for a solo dev's first
  ship, it fights the toy-box soul, and it doesn't answer "why keep playing."
- **Recommendation:** ship **ONE hand-authored venue (a "ball pit arcade"/indoor
  playground) with a few pits in it, and make the spine a repeatable "clear the
  pit" loop driven by a persistent upgrade tree + a daily/seedable challenge
  score.** One big pit you actually drain for the first-run arc; then it refills
  (optionally bigger / more colors) so the upgrade tree and co-op messing-around
  keep paying off. The hook is the **tool-tree dopamine + visible bulk progress +
  co-op sandbox**, not a story you finish once.
- **Scope line:** one venue, one pit system (already built), ~3 tool tiers (not
  4), 3→6 colors, visible container fill, an in-world upgrade station, a results/
  score screen, Steam Cloud save. Everything else (multi-room campaign, drones/
  conveyor automation tier, bespoke per-room art) is explicitly POST-LAUNCH.

---

## 1. Market grounding (light research — honestly hedged)

What's **grounded** (observable on Steam today):

- The "satisfying chore / cleanup / tidy" sim is a proven, sticky niche.
  **PowerWash Simulator** (Steam "Very Positive") is the anchor example: no fail
  state, no combat, slow visible transformation of a dirty thing into a clean
  thing, drop-in co-op, and a long tail of word-of-mouth clips. Its loop is
  literally "point at mess, mess goes away, number ticks up." Source: Steam store
  page, PowerWash Simulator (reviews "Very Positive"). <https://store.steampowered.com/app/1290000/PowerWash_Simulator/>
- **Unpacking** (Steam "Very Positive") shows the same genre's zen appeal at tiny
  scope — a small team shipped a focused "take things out, place them" loop that
  sold well on vibe and polish, not content volume. Source: Steam store page.
  <https://store.steampowered.com/app/1135690/Unpacking/>
- **Viscera Cleanup Detail** proved years earlier that "clean up a physics mess in
  first person, with friends" is intrinsically funny and shareable even when the
  'game' is just tidying. (Longstanding "Very Positive" cleanup-sim cult hit.)
- The "sorting / find-and-extract / slow-start-then-upgrade" lineage the owner
  cited (the "find the needle in the haystack" / idle-upgrade sorters) consistently
  pairs a **simple satisfying manual action** with a **visible upgrade curve** that
  slowly automates the grind. The compulsion is the upgrade tree, not a narrative.

What's an **educated bet** (NOT guaranteed — do not oversell):

- A ball pit is a *novel, screenshot-first* skin on a proven loop. Novelty +
  "oddly satisfying" + co-op is the exact profile that catches short-form video.
  That CAN drive wishlists cheaply, but virality is never a plan — treat it as
  upside, not revenue.
- ~$5 with co-op is a known-friendly price for impulse + "buy it for the group."
  It is a reasonable bet, not a promise. First-game sales are usually modest; the
  realistic goal is **finish, ship, learn, and let word-of-mouth do what it does.**

What research does NOT tell us: that THIS game will sell. It won't be claimed here.
The genre is viable and the tech pillar is genuinely uncommon; that's the honest
case. Execution and polish decide the rest.

### The two lessons that matter most for scope

1. **These games retain on LOOP + POLISH, not on CONTENT VOLUME.** PowerWash and
   Unpacking win on feel and finish, not on map count. A solo dev's leverage is
   making ONE pit feel incredible, not building five.
2. **The shareable moment is emergent and physical.** The clips people post are
   mess + physics + friends, not scripted beats. This is exactly the toy-box the
   playtest already produced for free. Protect it.

---

## 2. PRIMARY RECOMMENDATION — "Ball Pit Arcade": one venue, endless-ish clear loop, upgrade-driven

### 2.1 The core fantasy

You and up to three friends are the (gleefully understaffed) crew of a giant
indoor ball-pit arcade/playground after hours. The pit is a glorious mess of
colored balls. Your job is to **drain it** — dive in, dig, haul, throw, and sort
every color into its machine — and every ball you sort pays into a shared pot that
buys **better and sillier tools** until you're practically automating the place.
It is equal parts *satisfying cleanup* and *physics playground with friends.*

This fantasy is deliberately NOT a story. It's a **place you return to** to run the
loop better than last time, the way you return to a clean-up sim or a round-based
co-op game.

### 2.2 Moment-to-moment loop (unchanged core, this is the proven fun)

1. **Dig / grab** balls out of the pit (the star tech — real collapse, holes you
   dug stay dug).
2. **Haul / throw** them toward the color machines (throwing has knockback — keep
   it, it's free comedy and co-op chaos).
3. **Sort** by color; a correct ball **sold on cross** into its machine (per
   DECISIONS-C) pays 1 coin to the **shared pot**; wrong color bounces back to the
   pit.
4. **Watch progress in the world** — the pit visibly shrinks, each machine's
   colored mass visibly fills 0→100% (per DECISIONS-C: visible fill is core, not
   polish).
5. **Walk to the in-world upgrade station** and spend the pot on the next tool —
   each upgrade changes *how* you play (hands → rake/broom/dustpan → ball bag/
   elevator/shop-vac → the first helpers).
6. The bottleneck moves, so the next purchase feels obviously worth it. Repeat.

### 2.3 Structure — the opinionated call

**One authored venue. One pit system (already built). A repeatable "clear" loop
wrapped in persistent progression and an optional score.** Concretely:

- **First Run = the arc.** A fresh save starts with a tutorial-scale pit (the
  kiddie pool, §2b of game-design — keep it, it's perfect onboarding) and then the
  main pit. Draining the main pit the first time IS the "I beat it" moment — it
  gives the one-and-done player a clean finish and a results screen (time, balls
  sorted, money, per-player stats; per game-design §6).
- **After the first clear, the pit REFILLS** — same venue, no new rooms, no new
  art required. Each refill can scale **two authored knobs we already support:
  size and color count** (3→6). This reuses the EXACT pit tech and the baked/seeded
  fill system; a "bigger / more colorful" refill is a data change, not a new level.
- **Upgrades + money PERSIST** across refills in the save. The tool tree is the
  spine. You don't rebuild a campaign; you deepen one venue.
- **Optional score / challenge layer for retention:** a **seeded "pit of the day/
  week"** (fixed size + colors + seed, shared by everyone that period) with a
  **drain-time leaderboard**, plus a **run results score**. This is cheap (the fill
  is already seed-deterministic and bakeable — networking-and-pit "bake the fill")
  and it's the piece that turns "I cleaned it once" into "let's beat our time" and
  "the group wants another round." Leaderboards can even be a fast-follow if time
  is tight; the seeded-refill itself already gives replay.

Why this shape: it **maximizes reuse of the one thing that's done** (the pit) and
**minimizes bespoke content** (one venue, no door-locked rooms, no five sets of
art). It gives a finish for the completionist AND a reason to relaunch for the
group. It's a structure a solo dev can actually land in months.

### 2.4 The HOOK — the dev's #3 doubt, answered directly

Three stacked compulsions, each independently proven in the genre:

- **Upgrade dopamine (the engine).** The tool tree is a clicker-style curve where
  every purchase visibly changes play. "One more upgrade" is the oldest reliable
  hook there is, and it's already the design's backbone. This is the primary
  reason a solo player keeps going.
- **Visible bulk progress (the satisfaction).** The pit shrinking and the machines
  filling is the PowerWash "watch the dirt vanish" payoff. It's the reason sorting
  *feels* good instead of feeling like data entry — and it's exactly what the owner
  said is currently missing (balls silently vanishing "feels awful").
- **Co-op sandbox (the reason to recommend it).** The emergent physics play —
  digging, ball fights, chucking balls on the deck — is the shareable, "come play
  this with me" layer. It's what makes it a *party* purchase at $5, and it's the
  word-of-mouth engine. We AMPLIFY it (see §2.5), never gate it behind objectives.

The honest version: the *compulsion* is the upgrade curve, the *satisfaction* is
visible progress, and the *virality* is co-op physics. The seeded score is the
light meta that gives the loop a destination without bespoke content.

### 2.5 Co-op and the toy-box are first-class (the critical constraint)

- **No rigid objective gating.** There is always a pit and always money to earn;
  there is never a "you must do X before you may mess around" wall. You can spend a
  whole session just throwing balls and still be earning if any of it lands sorted.
- **Division of labor is rewarded, not required.** Diggers, haulers, sorters, and
  the person on the upgrade station naturally emerge (game-design pillar) — but one
  person doing all of it still works.
- **Keep the physics comedy.** Throw knockback (hit a friend, it shoves them),
  ball fights, cannonballing into the pit, chucking balls onto the deck — these
  stay. Several map directly to cheap, clip-worthy achievements already in
  `achievements.md` (Dodgeball, Cannonball, Bucket Brigade, Catch!). That list is
  already aligned to this direction and barely needs changing (see alignment doc).
- **"Sandbox toggle" is basically free:** the pit refills and the money/upgrades
  already exist, so a "just mess around, no pressure" session IS the default feel.
  The score layer is opt-in.

### 2.6 SCOPE — what a solo dev ships in months

**IN (v1.0):**

- The proven pit (done) + players/venue (done) + lobby/save kit (done/integrating).
- **One authored venue** with the kiddie-pool tutorial pit + one main pit area.
  (Author ONE good-looking venue — game-design §8 already leans this way.)
- The **core loop**: containers, sell-on-cross deposit, visible container fill,
  shared money, results screen (all per DECISIONS-C + game-design §2/§6).
- **In-world upgrade station** (DECISIONS-C) with **Tiers 1–3 of the tool tree**:
  Phase 1 (hands, Second Hand, Strong Arm, broom, rake, dustpan) + Phase 2 (ball
  bag, elevator, small shop-vac, cart, color comb) + the **first** Phase-3 helper
  (drones that pile at a station, no delivery — per game-design §3) only if time
  allows. Stop there for v1.
- **Refilling pit** with authored size/color scaling + a **seeded daily/weekly pit**
  for replay. Leaderboard optional / fast-follow.
- Co-op 1–4 with the physics toy-box intact.
- A sensible chunk of the existing achievement list (it's already written).
- Colorblind display support (already in; DECISIONS-C Q6).

**CUT LINE — deliberately OUT of v1 (post-launch if the game earns it):**

- The **multi-room, door-locked, 4–5-pit linear campaign.** This is the biggest
  cut and the direct answer to doubt (a): it's the content sink. The "sequence of
  ever-bigger pits" survives as *refill scaling in one venue*, not as five
  authored rooms.
- **Full automation tier (Phase 4: conveyors/belts, drone delivery, player-built
  factory).** Huge scope, and it actively *removes* the hands-on toy-box that is
  the soul. Ship the game where you mostly play with your hands and simple tools;
  add automation later as a "prestige"/post-game tier if players want it.
- Bespoke per-area theming/art for five rooms, per-tier shops that each close,
  branching economy tiers.
- Anything in `achievements.md` tagged **E** (automation) and most **D** (helpers)
  — keep as post-launch stretch, not v1 gates.

This cut keeps the *identity* (drain a pit, sort, upgrade, mess around with
friends) at full strength while removing the parts that balloon a solo timeline.

---

## 3. Alternatives considered (and why they lost)

### Alt A — Keep the fixed 4–5-pit linear campaign (the current steering direction)

- **Pros:** Clear authored difficulty ramp; a definite "ending"; the tool tiers map
  cleanly onto rooms; already written down.
- **Cons:** Biggest content cost for a solo dev (five venues/looks, door+lock
  logic, per-tier shops, five baked fills, campaign-position save). A fixed linear
  path is also the *weakest* fit for a co-op physics sandbox — once you've passed a
  door you can't go back and just play. And it still doesn't answer "why keep
  playing after you finish."
- **Why it lost:** directly collides with all three of the owner's stated doubts.
  The good parts (escalating size + colors, tool progression) are fully preserved
  in the recommendation as *refill scaling*, at a fraction of the build cost.

### Alt B — Pure endless / score-attack sandbox, no "clear" arc

- **Pros:** Minimal structure to build; maximum toy-box; trivial to scope.
- **Cons:** No sense of completion for the one-and-done player; the upgrade tree
  has no natural "you did it" beat; harder to make a store page *promise* something
  concrete. Pure endless can feel aimless to players who want a finish.
- **Why it lost:** the recommendation is 90% this, but adds a **first-clear arc +
  results screen** so there's a real "I beat it" moment, which costs almost nothing
  and meaningfully helps marketing and single-player satisfaction. Endless is the
  *mode*, not the whole game.

### Alt C — Contract / job board ("accept a pit job: this size, these colors, this payout")

- **Pros:** PowerWash-style job list is familiar and gives bite-sized sessions and
  a natural variety knob; could be a nice fast-follow.
- **Cons:** Needs a job-board UI/meta, payout balancing, and job authoring/variety
  to not feel samey — more systems than a solo v1 wants, and it risks turning the
  freeform pit into a checklist (soul risk).
- **Why it lost:** the **seeded daily/weekly pit** gives ~80% of the "fresh
  defined challenge" benefit with almost none of the authoring/UI cost, because the
  fill is already seed-deterministic. Promote the contract board to a post-launch
  idea if the daily proves players want structured goals.

---

## 4. The dev's three doubts — answered head-on

- **(a) "The campaign is too much content for a small game."** Correct, and the
  recommendation removes it. You build **one** venue and **one** pit system (you
  already have the pit). Escalation becomes refill *data* (size + color count), not
  five authored rooms. This is the single biggest scope saving.
- **(b) "A fixed linear campaign feels wrong for a co-op sorting game."** Also
  correct. Co-op cleanup/physics games thrive on *repeatable, drop-in, replayable*
  structure (PowerWash free-play + challenges, Viscera jobs), not a one-way corridor
  you finish once. The recommendation is repeatable-with-persistent-progression,
  which fits the genre and the toy-box far better.
- **(c) "I can't picture the hook."** The hook is the **upgrade tree (keep-going)
  + visible bulk progress (satisfaction) + co-op physics sandbox (share-with-
  friends)**, with a light **seeded-score** destination. None of these depend on a
  campaign; all three are proven in this genre. See §2.4.

---

## 5. What this means for the next build (non-binding note)

The already-planned "Deliverable B = the gameplay loop" (containers, sort, sell,
money, upgrades, win/results) is **still exactly the right next build** — it is
loop-level and structure-agnostic. The only change is that B should be built
against **one refilling venue + a results screen**, NOT against campaign room/door/
lock/per-tier-shop scaffolding. Build the core loop in the existing `large_1` pit
(as game-design already suggests for bite "a"), then add the **refill + seeded
pit** wrapper instead of the campaign wrapper for bite "b". The loop-level
decisions already locked in DECISIONS-C (physical sell-on-cross, visible fill,
in-world upgrade station, scenery-not-HUD progress, kiddie-pool tutorial) all carry
over unchanged.

---

## 6. Open questions this direction creates (for the owner)

1. **First-clear framing:** does draining the main pit the first time end with a
   results screen + "pit refilled, keep going / new seeded pit available," or do you
   want a harder "you've mastered it" capstone? (Recommend the former — cheap,
   friendly.)
2. **Refill scaling:** how many refill steps and how far up in size/colors before it
   caps? (A tuning call; needs the economy simulator later.)
3. **Seeded pit cadence:** daily, weekly, or just a "random seed" button for v1?
   (Recommend weekly + a free "random seed" button; leaderboard as fast-follow.)
4. **Automation tier:** confirm Phase-4 full automation is POST-LAUNCH, not v1.
5. **Name/venue theme:** "Drain the Ball Pit" still fits; the venue theme (arcade/
   playground/after-hours) is art direction, your call.

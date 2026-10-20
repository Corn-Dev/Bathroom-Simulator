# LAYOUT & DEPOSIT FEEL — Drain the Ball Pit

A decisive layout + deposit-feel design for the factory venue. Feeds a greybox
build next. Opinionated on purpose; tuning numbers are rough and flagged.

> Scope note: this is the first MAIN-RUN factory pit (game-design §1/§1b) — NOT
> the ~200-ball kiddie-pool tutorial (§2b), which sits in the same venue but is
> tiny. One authored venue, ONE rectangular pit reconfigured in place per
> shipment. The numbers below are sized to the **reference first MAIN pit**
> (~20×12×2 m); later shipments scale the SAME room (more in §2 and the open
> questions). GAME-DIRECTION.md's older "refilling arcade / seeded-daily" framing
> is superseded by the factory/shipment steering — layout reasoning here follows
> the steering.
>
> **SCALE CAVEAT (added after the first scene build):** everything in this doc is sized
> to a ~4,000-ball REFERENCE pit. It is NOT the existing `large` profile (80×60×80 balls =
> 384,000 balls, ~40×35 m footprint, ~32 m deep) or the ~1M finale (~100×100 m wide and
> ~12 m deep if authored shallow). Profile pitX/Y/Z are ball counts, not meters — see the
> sizing table in `CAMPAIGN-SCENE-SETUP.md` Step 2. The relative guidance (sunk pit,
> floor-level mouths, ≥4 m spacing, sort band, forgiving deposit) still holds; the absolute
> deck size (28×20 m) must be recomputed per pit.
>
> **Ball count for that box (consistent set used throughout):** 20×12×2 = 480 m³;
> at ~0.58 random packing a 0.5 m ball occupies 0.0654 m³, so 480 × 0.58 / 0.0654
> ≈ **~4,250 balls** — call it **~4,000 balls** for round math. (The earlier draft
> said ~2,000; that under-counted the same box ~2×. All floor math below uses
> ~4,000.)

---

## (a) The constraints we are designing against

Hard facts (do not violate — from game-design §9b, §2, and networking-and-pit):

- **The pit interior is an axis-aligned box.** Flat floor, four straight walls.
  Art/rim/room around it can be any shape, but the ball volume is a rectangle.
- **The pit's real collision walls are invisible proxies** that hug the ball
  mass; authored walls are decorative. Author pit-FIRST, room-second (§8).
- **Balls are 0.5 m.** A player is ~1.8 m (≈3.5 balls tall). A player's shoulder
  width is ~0.6 m — roughly one ball + a bit.
- **Selling = a ball's CENTER crosses the container's intake BOX** (a pure
  geometry test in `BallContainer.TryIntake`, not a physics trigger). The intake
  box has an author-set local center + size and a runtime `IntakeScale` (the
  "funnel" upgrade hook). The ball then keeps falling physically and settles into
  the visible `MassFillView` fill. Wrong color → returns to the pit from above.
- **Only ~4k balls can be live rigidbodies at once**; the rest are static
  colliders or data. Piles of balls on the deck are real static colliders where
  players are, data where they aren't — a big floor pile is cheap.
- **Out-of-bounds / stray balls return to the pit** from above at a random spot.
- **The loop rhythm the owner wants:** EXTRACT from pit → pile on the
  floor/deck → SEPARATE by color into heaps → DELIVER each heap into its
  matching container. The satisfying part is extract + sort; **depositing must be
  EASY and forgiving — the challenge is SORTING and VOLUME, never AIMING.**

---

## (b) THE RECOMMENDATION

### One-sentence version
Sink the pit into a large flat deck; ring the pit with **floor-level, front-open
"chute-mouth" containers spaced wide apart along TWO adjacent sides**, so players
drag/sweep a sorted heap straight into a mouth at floor height — no throwing up,
no rim to clip — leaving one long open side and the far end clear as sorting
space and future conveyor lanes. Keep wrong-color mouths far apart (≥4 m) and set
the sort band back behind a low berm so stray wrong-color balls don't drift into a
mouth and get yanked back to the pit.

### Deck: SUNK, not raised

Recommend the pit is **sunk into the deck floor** (its rim flush with the deck,
or nearly). Reasons, grounded in this loop:

1. **Extraction wants a flat exit, not a lip.** The core verb is dragging/sweeping
   balls OUT of the pit onto the floor. A sunk pit with a flush rim means balls
   come up and over onto a continuous flat surface — a broom/rake/hands push them
   out with no 0.5 m+ curb to lift them over. A RAISED pit (render's stairs +
   railings + tall rim) puts a wall between the pit and the sorting floor:
   everything you extract has to clear that wall, which is exactly the "lift balls
   up over a lip" frustration we're banning at the containers — don't reintroduce
   it at the pit edge.
2. **Climb-out is already solved for sunk.** Players wall-climb + mantle out of
   the pit (Deliverable A, working). A sunk pit uses that. Raised decks add
   stairs/railings — more collision geometry for players AND balls to snag on
   (see §c).
3. **Stray balls roll back IN on a flat deck, which is fine** — out-of-bounds
   return handles true strays, and a gentle grade (below) keeps the working floor
   from draining back into the pit.

The ONE concession to "raised": give the deck a **shallow lip/berm (~0.3 m, half
a ball) a couple meters back from the pit edge** so sorted heaps don't trickle
back into the pit. That is a low, climbable curb, not a wall — players step over
it, balls at rest don't roll over it.

### Floor AREA math (why the deck must be big)

Two tuning figures drive the deck size — both flagged as greybox-confirm knobs:

- **Per-ball floor footprint:** a settled 0.5 m ball loosely covers **~0.35–0.45
  m² per ball per layer** (loose single-layer coverage on the deck; use ~0.4 m²).
- **Heap depth:** swept heaps pile to the angle of repose. Assume **~1.5 m / ~3
  balls deep average** per heap. This is the load-bearing assumption — halve or
  double it and the floor area swings 2× (see open questions). Note ~1.5 m peaks
  near the player's ~1.8 m eye-line, so a heap is see-/reach-over-able but not by
  much; if greybox shows heaps feel too tall, cap sweep height lower and the deck
  grows accordingly.

A ~4,000-ball first MAIN pit, if you dumped ALL of it onto the floor at once:
4,000 ÷ 3 deep × ~0.4 m² ≈ **~535 m² if every ball were on the floor at once** —
larger than the whole deck below, which is exactly why you never do that.

You NEVER have all of it on the floor — that is the key. The loop is a PIPELINE:
extract a batch, sort it, deliver it, extract the next. A realistic working buffer
is **one-to-two colors' worth staged at a time, plus slop** — call it **~15–25% of
the pit on the floor at peak** (~600–1,000 balls). At ~3 deep × ~0.4 m² that is
**~80–135 m² of active heaps.** Add walking lanes and container frontage and the
**working deck wants ~150–200 m² of open floor** beyond the pit footprint (pit is
~240 m²). Round to a **deck roughly 28 m × 20 m** (≈560 m² total, pit included)
for the first pit — comfortable at a ~20% buffer, tight if players let heaps run
to ~25%+, which is itself a healthy "haul it, don't hoard it" pressure.

> This is ALSO the headroom later shipments need. A bigger shipment pit is
> deeper/longer in the SAME room (game-design §1b), but the floor pipeline stays
> ~a fraction-on-the-floor-at-once — so the deck sized for comfortable sorting of
> the first pit holds up. If a late shipment's single color exceeds the floor
> buffer, that is a TOOLING answer (carts/conveyors buffer balls off-floor), not
> a bigger-room answer. Author the room once, to this size.

### Containers: floor-level, front-open "chute mouths," spaced WIDE

This is the heart of "deposit is not a skill-test." Each container is authored as:

- **A low, front-opening MOUTH at floor level**, not a top-opening bin you throw
  up into. Picture a wide hopper/skip whose intake face sits ON the deck: the
  player sweeps or dumps a heap horizontally INTO the mouth, the way you'd sweep
  dirt into a dustpan. No upward throw, no lip to clip. *(Authoring note: the
  intake is an ORIENTED authored box — `BallContainer.TryIntake` tests the ball
  center in the container's local space, so a front/floor mouth is just an authored
  `intakeCenterLocal` + the transform's rotation + `intakeSizeLocal`. NO code
  change — the component's `(0,1,0)` "top-opening" default is only a default.)*
- **A generous intake box.** `intakeSizeLocal` authored WIDE and tall at the
  mouth — think **~2.5 m wide × ~1.2 m tall × ~1.0 m deep** for the mouth opening
  on the first pit's containers. The sale fires when a ball's CENTER crosses that
  box, so a ball merely ROLLED into the mouth sells (right color); it does not need
  to be thrown or placed. Wide-and-forgiving by authoring, and the `IntakeScale` /
  "funnel" upgrade only ever makes it MORE forgiving.
- **The wide floor mouth cuts BOTH ways — guard the stray wrong-color case.**
  `DepositDriver` tests EVERY live ball's center against EVERY mouth each host step
  with no grace period: a wrong-color ball whose center crosses a mouth is instantly
  `Reject`ed and returned to the pit from above. A wide floor-level mouth is exactly
  the geometry a loose wrong-color ball rolling across the sort band can wander
  into — the player sees it silently yanked away ("where did my ball go"). This is
  the one real deposit frustration and the layout must defend against it, not the
  intake code. Defenses, all layout/authoring (no code change): the **≥4 m mouth
  spacing** (§placement) keeps colors far apart; the **~0.3 m berm + set-back sort
  band** keep strays from drifting mouth-ward; the **apron rises INTO the mouth**
  so only a deliberately-swept heap reaches the intake plane, not a ball idling on
  the flat. If greybox still shows stray mis-sells, the cheapest code-side follow-up
  (OUT OF SCOPE here, logged as an open question) is a small "wrong color bounced!"
  signal or a brief approach threshold — but try the layout defenses first.
- **A back-stop + down-chute behind the mouth** so a ball that crosses the intake
  can't bounce back OUT onto the deck: past the intake plane the geometry funnels
  down into the container body (where `MassFillView` shows the colored fill
  rising). "Movie magic" (§2) covers the travel; what the player sees is heap goes
  in, never comes back, fill rises.
- **A slight ramp/apron on the deck up to each mouth (rise ~0.2–0.3 m over ~1 m)**
  so sweeping a heap toward the mouth naturally feeds it, and loose balls near the
  mouth tend to roll IN rather than linger at the base.

**Placement & spacing:** containers ring **two ADJACENT sides** of the pit (an
"L"), not clustered on one side like the render. Spacing between adjacent mouths
is **≥ 4 m edge-to-edge.** Rationale:

- **(a) Mis-deposit safety.** Intake boxes are wide (~2.5 m) and sell on center-
  cross. If two different-color mouths are close, a heap being swept toward BLUE
  can clip RED's intake and mis-sell → ball returns to pit → frustration. A ≥4 m
  gap (≥1.5 m of clear deck between the 2.5 m boxes) means you can only be aimed
  at one mouth at a time. Color labels/signage over each mouth (diegetic, §2)
  reinforce it.
- **(b) Co-op crowding.** Up to 4 players. Spreading 4–6 containers along an L of
  the pit (two ~20 m and ~12 m sides give ~32 m of frontage) lets four people
  each work a different color/mouth without body-blocking each other. One wall of
  tightly-packed bins forces everyone into the same corner.
- **(c) Conveyor room.** The long side OPPOSITE the container L, and the open far
  end, stay clear — that is the **build-zone for future conveyors/drones** (§e).
  Conveyors terminate at a container mouth from the open side; keeping containers
  on the L leaves a straight shot across the deck for belts.

### Where piles happen

**The open deck BETWEEN the pit edge and the container L** is the sorting floor.
Players extract onto this band, rake/sweep same-color balls into heaps there,
then push each heap the last few meters into its mouth. Keep this band **≥ 5–6 m
deep** all along the container sides so a heap-in-progress plus a player plus a
walking lane coexist.

### Piles = EMERGENT, not a formal mechanic

**Recommend piles are purely emergent.** The game does NOT recognize "a pile" as
an object. A heap is just balls at rest on the floor (static colliders / data,
which the pit tech already handles as off-pit chunks — networking-and-pit:
"out-of-pit floor piles are not a special case — just chunks"). Players make
heaps by pushing balls together; tools (rake, broom, later color comb) push them;
the ONLY thing the game formally recognizes is the intake-box cross at a
container. This is simplest to build, reuses existing tech with zero new systems,
and keeps the toy-box free (you can heap balls anywhere for any reason, gated by
nothing). A formal pile object would add detection, ownership, and
network-sync cost for no gameplay the emergent version lacks.

### Top-down layout (rough meters, first pit)

```
        far end: OPEN sorting + conveyor build-zone (keep clear)
   ┌──────────────────────────────────────────────────────────┐  z
   │   · · · · · · · · · · · · · · · · · · · · · · · · · · · ·  │  ↑
   │   ·            open deck / future belts            ·       │  │
   │  [B1]                                                      │  │
   │  blue   ┌───────────────────────────────────────┐         │
   │  mouth  │                                        │  ~6 m   │
   │  [B2]   │           BALL PIT (sunk)              │  sort   │  28 m
   │  green  │        ~20 m × 12 m × 2 m deep         │  band   │  (x)
   │  mouth  │        ~4,000 balls, flush rim         │  →[     │
   │  [B3]   │                                        │  mouths │
   │  red    │                                        │         │
   │  mouth  └───────────────────────────────────────┘         │
   │  [B4]                                                      │
   │  yellow   ← ~4 m gaps between mouths →                     │
   │   ·    open deck (spawn, upgrade station this side)  ·     │
   └──────────────────────────────────────────────────────────┘
        containers ring TWO adjacent sides (an L): left (long) + bottom (short)
                 deck ~28 m (x, vertical) × ~20 m (z, horizontal), ≈560 m²
                             (→ not to scale in ASCII)
```

- Containers sit on the LEFT (long) + BOTTOM (short) sides = the L.
- ~6 m sort band between pit and container mouths on the container sides.
- TOP and the open interior deck = clear sorting + conveyor build-zone.
- **UpgradeStation + player spawns** on an open side away from the container L so
  nobody spawns inside a heap or blocks a mouth.
- Shallow ~0.3 m berm ~2 m back from the pit edge keeps heaps from draining in.

---

## (c) Anti-frustration checklist — "nothing gets stuck"

**Balls getting stuck:**

- **Gaps behind/beside containers.** Containers sit FLUSH to the berm/wall with no
  walk-behind gap, OR with a gap too narrow (<0.5 m) for a ball to enter. No
  0.5–1 m dead pockets where a ball wedges forever. (Any true escapee is caught by
  out-of-bounds return.)
- **Sharp inside corners of the sort band.** The two pit corners on the container
  L are where swept heaps pile against two walls and jam. Mitigation: author a
  **45° chamfer/fillet in the art** at those two corners so balls deflect toward
  the nearest mouth instead of packing a corner. (Art-only; the pit box is
  untouched.)
- **Under the container mouth apron.** The apron ramp meets the floor flush (no
  undercut lip) so balls can't roll UNDER the mouth and vanish.
- **At the pit rim.** Flush/berm rim means balls that don't make it to a mouth
  roll back toward the pit and are re-extractable — never a limbo zone.
- **No mouth ever CLOGS with the wrong color** — a wrong-color ball that crosses a
  mouth is bounced straight back to the pit (not stored), so a mouth never fills or
  jams with the wrong color. (This is NOT consequence-free, though: see the
  stray-wrong-color caveat in §b — the real risk is a loose wrong-color ball
  wandering into a mouth and being silently returned, which the ≥4 m spacing + berm
  + into-mouth apron are there to prevent.)

**Players getting stuck:**

- **No stairs/railings** (a reason to prefer sunk + wall-climb over the render's
  raised deck). Railings are the #1 thing a 1.8 m capsule snags on; we don't add
  them. Climb-out is the existing mantle.
- **Container edges.** Mouths are low and front-facing; players walk UP to a mouth,
  not around a tall bin. Keep ≥1.5 m clear deck in front of every mouth so a player
  delivering a heap isn't pinned between the mouth and the berm.
- **Pit entrapment.** The whole pit perimeter is climbable (mantle works on all
  four straight walls); no "fell in a corner, can't get out" because the box has no
  concave trap. Berm is ~0.3 m — steppable, not a trap.
- **Co-op body-block.** ≥4 m mouth spacing + ≥6 m sort band + wide deck give four
  1.8 m players room to pass. Walking lanes are REAL (≥1 m clear) between heaps and
  the container frontage.
- **Spawn safety.** Spawns on an open side, never in the sort band or a mouth apron.

---

## (d) Alternatives considered

**Alt 1 — Containers clustered on ONE side (the reference render).**
Pros: compact, readable "wall of machines," matches the render the owner liked.
Cons: 4 players crowd one corner; adjacent wide intake boxes invite mis-deposits;
no clear straight run for conveyors; the far sides of the pit have no purpose.
**Lost** because it fights co-op and future tooling; we keep the render's *look*
(nice machines, signage) but spread them onto an L.

**Alt 2 — RAISED pit deck (stairs, railings, tall rim — the render's framing).**
Pros: dramatic silhouette; clear separation of pit and deck; "arena" feel.
Cons: puts a wall between extraction and the sort floor (lift every ball over the
rim — the exact frustration we ban at containers); stairs/railings are player- and
ball-snag hazards; more bespoke collision art. **Lost** on the core verb: extraction
must flow onto a flat floor, not over a curb.

**Alt 3 — Top-opening bins with a throw-to-deposit (render's container shape).**
Pros: visually obvious "drop it in the top"; matches a real skip/hopper.
Cons: requires throwing balls UPWARD over a rim where they clip the lip and bounce
back — turns deposit into an aim skill-test, which the owner explicitly rejected.
**Lost.** Kept the hopper *art* but moved the functional intake to a wide FRONT
mouth at floor level so the sale is a horizontal sweep-in, not an upward throw.

**Alt 4 — "Colored trucks drive off with each load" (owner's musing, msg 5).**
Pros: strong factory fiction; a satisfying "load leaves."
Cons: a truck that DRIVES is movement/pathing + a departing-vehicle cutscene for
every color — more systems, and it hides the fill payoff (the owner also noted
filling a truck might be *less* satisfying than watching a container fill). **Lost**
for v1; the stationary container with a visibly rising `MassFillView` keeps the
fill satisfaction. A truck can be the ART SKIN of a container later (parked truck,
mouth = its open back) with zero mechanic change.

---

## (e) Where future tooling fits the SAME room

The layout must not need a redesign when tools arrive (game-design §3).

- **Brooms / rakes / push-brooms:** operate on the flat sort band pushing heaps to
  mouths — the band IS sized for this. No change.
- **Excavator (Tier 2, now in):** extracts from the pit onto the sort band, same as
  hands but faster — the flush rim + flat band already support bulk dumping.
- **Carts / dump cart:** need deck lanes wide enough to drive and a mouth they can
  tip into. The ≥6 m band + ≥1.5 m mouth frontage covers it; the cart tips over the
  mouth apron.
- **Shop-vac:** sucks from pit/floor, exits into a container — "movie magic" travel,
  endpoints are the pit surface and a mouth. No layout need beyond standing room.
- **Conveyors (Tier 4, free-placed, take floor):** the **open far end + the long
  side opposite the container L** are the declared build-zone. A belt runs from a
  pit-side pickup across open deck, through color gates, and terminates AT a
  container mouth from the open approach. Because mouths are on an L and the
  opposite side is clear, belts have straight, unobstructed runs. Reserve this
  zone in the greybox even though belts come last.
- **Drones (Tier 3):** pile at a station on open deck, later deliver into a mouth —
  the open deck and mouth frontage already accommodate an aerial approach.

Design rule carried forward: **everything delivers into the SAME wide floor-level
mouth.** Hands, rake, cart, vac, belt, drone all aim at one forgiving intake box —
so no tool ever needs its own bespoke deposit geometry.

---

## Open questions for the owner

1. **Pit truly flush or slightly sunk below deck?** Recommend flush rim + a ~0.3 m
   berm set back ~2 m. Confirm the berm height feels steppable, not trippy, in
   greybox.
2. **Container count on the L for the first pit:** 4 colors → 4 mouths fit the
   bottom side alone; 6 colors (late shipments) use the full L. Confirm the first
   pit is 3–4 colors (game-design §5) so the bottom side suffices early.
3. **Mouth intake size (~2.5×1.2×1.0 m) and the ≥4 m spacing** — these are rough;
   they want a greybox pass with a player actually sweeping a heap in.
4. **Berm vs no berm** — is a small curb worth it, or does out-of-bounds return +
   the sort band being set back handle drain-back on its own? (Cheap to try both.)
5. **One room sized for the biggest shipment, confirmed?** This doc sizes the deck
   (~28×20 m) to be comfortable for the first pit and relies on the pipeline
   staying fraction-on-floor for bigger shipments. Confirm later shipments won't
   demand a physically bigger room (vs. deeper pit + more tooling).
6. **Upgrade station + spawn side** — recommend the open side opposite the long
   container run; confirm against where the owner wants players to enter the venue.
7. **Heap depth (the load-bearing floor-math knob).** The ~28×20 m deck assumes
   swept heaps average **~1.5 m / ~3 balls deep** at ~15–25% of the pit on the floor
   at peak. Confirm in greybox: real angle of repose, how tall a heap a broom/rake
   actually makes, and whether ~1.5 m heaps feel too tall to see/reach over (if so,
   cap sweep height lower and the deck must grow — this single number swings the
   footprint ~2×).
8. **Stray wrong-color mis-sell — is the layout defense enough?** The wide floor
   mouth + no-grace-period reject means a loose wrong-color ball rolling into a mouth
   is silently returned to the pit. The ≥4 m spacing + berm + into-mouth apron are
   the first-line (no-code) defense. Confirm in greybox whether that's sufficient, or
   whether the cheap code follow-up (a "wrong color, bounced!" signal or a brief
   approach threshold in `DepositDriver`) is worth a later deliverable. Out of scope
   for this layout doc.

---

## Review responses (revision 1 — addressing `layout-deposit-review.md`)

- **F1 [HIGH] ball count vs volume — FIXED.** Picked ONE consistent set: kept the
  20×12×2 m box and corrected the count to **~4,000 balls** (derived in the scope
  note from the doc's own 0.58 density). Reran the whole floor-math chain (all-on-
  floor ≈ ~535 m², working buffer ~600–1,000 balls, ~80–135 m² active heaps). Added
  an explicit line that this is the first MAIN pit, NOT the ~200-ball kiddie-pool
  tutorial.
- **F2 [HIGH] wrong-color stray mis-sell — FIXED.** Added a dedicated caveat in §b
  (the no-grace-period `Reject`, the stray-into-mouth risk, and the layout defenses)
  and reworded §c from "impossible" to "no mouth CLOGS with the wrong color" while
  flagging the real stray frustration. Added open question 8 routing the code-side
  follow-up out of scope.
- **F3 [MEDIUM] footprint units error — FIXED.** Replaced the `0.5×0.5/0.58` step
  with a directly-stated **~0.35–0.45 m² per ball per settled layer**, no spurious
  volumetric division.
- **F4 [MEDIUM] heap-depth assumption — FIXED.** Stated ~1.5 m / ~3-ball-deep as an
  explicit load-bearing tuning knob, noted it peaks near the ~1.8 m player, and added
  open question 7 (greybox confirms repose + sweep height; swings footprint ~2×).
- **F5 [NIT] top-opening default — FIXED.** Added the authoring note that the intake
  is an oriented authored box; a front/floor mouth is just authored center+rotation+
  size on the existing `BallContainer`, no code change; `(0,1,0)` is only a default.
- **F6 [NIT] dangling volume figure — FIXED.** The `0.0654 m³` figure is now USED (in
  the F1 ball-count derivation) rather than computed-and-abandoned in the floor math.
- **F7 [NIT] GAME-DIRECTION.md contradiction — ACKNOWLEDGED, not actioned here.** The
  review itself scopes the banner fix OUT of this doc; this doc already states it
  follows the steering and that GAME-DIRECTION's older framing is superseded. Logged
  for a separate steering-hygiene pass (recommend a one-line "superseded by steering"
  banner atop GAME-DIRECTION.md).
- **F8 [NIT] diagram vs prose dimensions — FIXED.** Diagram caption now reads 28 m ×
  20 m (≈560 m²) to match the prose; corrected the in-diagram ball count to ~4,000.

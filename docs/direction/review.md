# Design Review — GAME-DIRECTION.md + STEERING-ALIGNMENT.md

Reviewed fresh against the brief and cross-checked against the steering files they
touch: `.kiro/steering/product.md`, `.kiro/steering/game-design.md`,
`.kiro/steering/networking-and-pit.md`, `.kiro/steering/achievements.md`, and
`docs/handoff-C/DECISIONS-C.md`. No build/test run (docs-only task).

## Verdict

CHANGES_REQUESTED — 1 MEDIUM finding. Everything else is strong; the one blocker is
a single surviving live "campaign" string (the §1c section header) that
STEERING-ALIGNMENT.md leaves unedited while implying full coverage. This is the exact
class of miss the doc's own revision history caught twice before, so it should be
closed the same way for consistency.

The two docs are, overall, coherent, honestly scoped, faithful to the fixed pillars,
and protective of the toy-box soul. All six judgment points below pass except for the
alignment-coverage gap in point 6.

---

## Findings

### 1. [MEDIUM] STEERING-ALIGNMENT.md misses the §1c section header — a surviving live "campaign" string

`game-design.md` line 102 is the header:

```
## 1c. Loading / fill performance for the campaign [DECIDED approach]
```

Item **B5** edits §1c but its quoted old-text begins at the first bullet ("Ship
PRE-BAKED fills for **every campaign pit**…"), not the header. After B5 is applied as
written, the section TITLE still reads "for the campaign." This directly contradicts
the doc's repeated assertion (B12/B13/B14 rationale, the §5/§7/§9b no-change NOTE, and
the revision-pass summaries) that "no live 'campaign' string survives." It is the same
failure mode the earlier reviews flagged (headers/bullets outside the quoted old-text):
B4 correctly handled the §1b header by replacing the whole subsection, and B9 explicitly
retitled the §9 header, but §1c's header was left behind.

CONCRETE FIX — add a one-line header edit to B5 (or a new B5-header item):

> **Old:** `## 1c. Loading / fill performance for the campaign [DECIDED approach]`
> **New:** `## 1c. Loading / fill performance for the main loop [DECIDED approach]`
> **Rationale:** The §1c body is reframed to refill steps + seeded pits; its header must
> drop "for the campaign" so no live "campaign" string survives in §1c.

Also correct the "no live 'campaign' string survives" assertions to acknowledge the
header was included once this edit lands.

### 2. [NIT] B3b flags the "each new pit" plural as "arguably defensible" but still edits it — leaves a soft open question in an edit doc

B3b rewrites "walking into **each new pit**" → "walking up to **the pit**," then notes
the plural "was arguably defensible (refills do grow the pit)." An alignment doc is meant
to hand the owner a decided old→new pair; leaving a parenthetical "maybe keep the plural"
inside the proposed replacement is a small ambiguity for whoever applies it.

CONCRETE FIX — keep the edit as the recommendation but move the hedge out of the
replacement text into the §E conflict/flag list (e.g. "B3b: recommend singular 'the pit';
owner may keep the plural since refills grow it"), so the proposed New text is unambiguous.

### 3. [NIT] achievements.md `inclusion: manual` and networking-and-pit.md `inclusion: manual` front-matter not noted

Both files carry `---\ninclusion: manual\n---` front-matter (so they are NOT auto-loaded
into every session, unlike `product.md`/`game-design.md`). The alignment doc treats all
four steering files as equally "shared memory read by a fresh session," which is slightly
off for the two manual-inclusion files. Not a correctness problem for the edits themselves,
but D2's rationale ("so a tech-focused session lands on the current framing") overstates
reach, since networking-and-pit.md is manual-inclusion.

CONCRETE FIX — add a one-line note in §C and §D that these two files are manual-inclusion,
so the owner knows the cross-pointer (D2) matters most where it is linked FROM
(product.md / game-design.md), which the edits already cover.

---

## Judgment-point assessment (brief's 6 checks)

**1. ONE decisive recommendation + 2–3 alternatives — PASS.**
GAME-DIRECTION §2 gives a single opinionated call ("Ball Pit Arcade": one venue +
repeatable clear loop + persistent upgrade tree + optional seeded score). Core fantasy
(§2.1), moment-to-moment loop (§2.2), structure (§2.3), and an articulable hook (§2.4:
upgrade dopamine + visible bulk progress + co-op physics, with seeded score as the light
meta) are all present and distinct. §3 gives three alternatives (A: keep the linear
campaign; B: pure endless; C: contract/job board) each with pros, cons, and a "why it
lost." Decisive, not wishy-washy.

**2. Honestly scoped for a solo dev's first ship, months, ~$5 — PASS.**
§1 explicitly hedges the market read ("does NOT tell us THIS game will sell," "virality is
never a plan," "first-game sales are usually modest"). §2.6 has a clear IN list (v1.0) and
an explicit CUT LINE (multi-room campaign, Phase-4 automation, bespoke 5-room art,
E/most-D achievements). The cuts target the real content sinks and nothing in the IN list
reads like years of work. No overselling.

**3. Keeps the fixed pillars + reuses the pit tech as the star — PASS.**
§0 and §2.6 keep ball-pit tech, sort-by-color, co-op 1–4, first-person, shared money →
shared tool-tree upgrades, Steam, ~$5. The recommendation is explicitly built to
"maximize reuse of the one thing that's done (the pit)"; refills are a data change on the
existing baked/seeded fill system (consistent with networking-and-pit "bake the fill").

**4. Preserves the toy-box soul — PASS.**
§2.5 makes co-op and freeform physics first-class: no rigid objective gating, division of
labor rewarded-not-required, throw knockback / ball fights / cannonball / chucking onto
the deck kept, and maps them to existing clip-worthy achievements (Dodgeball, Cannonball,
Bucket Brigade, Catch!). Directly honors the playtest anecdote in the original request
(friends digging holes, throwing balls at each other, chucking onto the deck).

**5. Addresses the dev's three doubts — PASS.**
§4 answers each head-on: (a) content scope → campaign removed, escalation becomes refill
data; (b) linear-vs-other → repeatable-with-persistence fits the co-op cleanup genre far
better than a one-way corridor; (c) the hook → the three stacked compulsions + seeded
score. Matches the owner's message 11/12 framing (smaller scope, finishable, options with
reasoning, inspired by sort/upgrade sorters).

**6. STEERING-ALIGNMENT catches the old framing with exact old→new edits + [DECIDED]
conflict flags — MOSTLY PASS (one gap = Finding 1).**
Coverage is thorough: product.md A1–A5, game-design.md B1–B14, achievements.md C1–C3,
networking-and-pit.md D1–D2, plus an §E summary of the three real [DECIDED] conflicts
(campaign reversal; "final pit needs full automation"; Phase-4 automation → post-launch).
Each item gives file, exact old text, proposed new text, and a one-line rationale; the
three [DECIDED] conflicts are correctly flagged ⚠️ for explicit owner sign-off. The one
defect is the §1c HEADER "campaign" string left unedited by B5 (Finding 1) — a genuine
missed old-framing reference given the doc asserts none survive.

---

## Verified Assumptions (checked against source)

- **product.md elevator pitch / Modes / Pillars / out-of-scope** quoted in A1–A5 match the
  file verbatim (incl. "Win = drain the final pit," the Campaign/Custom modes block,
  "across the campaign" pillar, the internal-doc pointer line).
- **game-design.md old-text** quoted in B1 (§0 "the game is a CAMPAIGN", line 34), B2 (§0
  NEXT BUILD note, lines 42–46), B3 (§1 primary/secondary/per-pit, lines 58–68), B3b (§1
  Feel bullet, line 69 "across the whole campaign"), B4 (§1b header + body, lines 72–100,
  incl. the additive-scene-loading fallback bullet it explicitly drops), B5 (§1c bullets,
  lines 104–112), B6 (§2 per-tier-shop line), B7 (§2b "before the rest of the campaign"),
  B9 (§9 header + body, lines 254–264), B10/B10b (§11 lines 313/317), B11 (§4 "on every
  pit"), B12 (§7 line 312), B13 (§5 lines 216/218), B14 (§9b line 277) — ALL verified
  present at the cited locations.
- **achievements.md** C1 completion group (entries 23–27) and C2 header line ("Pit-size
  achievements map to pit profile keys, so they follow whatever sizes ship.") and the
  phase-tag sentence (C/D/E) — verified present verbatim.
- **networking-and-pit.md** D1 "ship as a binary asset **per pit**" (Supporting decisions,
  lines 227–229) verified present; D2's "near the top STATUS block" target exists.
- **DECISIONS-C** loop decisions that GAME-DIRECTION §2.2/§5 claims to preserve
  (sell-on-cross + settle-after, visible container fill via MassFillView, in-world
  UpgradeStation replacing the Tab shop, per-color counter removed, kiddie-pool tutorial,
  colorblind display) — all verified present and accurately represented.
- **GAME-DIRECTION's reuse claim** (refills ride the existing baked/seeded fill system; no
  NetPit host restart in one live session) is consistent with networking-and-pit.md and
  game-design.md §1b/§9.

## Unverified / Wrong Assumptions

- **WRONG (Finding 1):** STEERING-ALIGNMENT asserts (B12/B13/B14 + §5/§7/§9b NOTE + revision
  summaries) that after its edits "no live 'campaign' string survives" in game-design.md.
  The §1c section HEADER (line 102, "Loading / fill performance **for the campaign**") is NOT
  covered by any item and would survive. Assertion is inaccurate as written.
- **UNVERIFIED (not blocking):** the market/research claims in GAME-DIRECTION §1 (PowerWash
  "Very Positive," Unpacking, Viscera) cite Steam store pages but were not independently
  fetched in this review; they are presented honestly as "grounded vs. educated bet" and
  the doc explicitly refuses to claim THIS game will sell, so the hedging is appropriate
  regardless of exact current review tags.
- **UNVERIFIED (not blocking):** the specific achievement API-name / numbering in C1–C3 maps
  cleanly onto Steam partner config — out of scope for a direction/alignment review; the
  renames are design-level and internally consistent.

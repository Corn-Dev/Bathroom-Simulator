---
inclusion: manual
---

# Steam Achievements — design (companion to game-design.md)

Status: a DESIGN proposal (not built). The achievement framework lands in Phase B+.
All names are placeholders. Pit-size achievements map to pit profile keys, so they
follow whatever sizes ship. Phase tags: C = Phase 2 tools, D = Phase 3, E = Phase 4;
if a tagged tool was cut in game-design.md, drop its achievement.

## Principles
1. Early drip — 5–6 unlocks in the first 30–45 min to teach the loop.
2. Spectacle moments — first cave-in, first floor glimpse, container filling, last ball.
3. Rarity curve — ~90% first sale; 40–60% first pit drained; 10–20% biggest pit;
   1–5% challenge runs; <1% hardest.
4. Natural skill moments (long throws, bank shots, catches, streaks) = clip-worthy.
5. Co-op flavor without locking out solo; most co-op achievements work at 2 players.
6. Grinds with Steam progress bars, capped sanely (top ≈ 3 playthroughs, not 1000 hrs).
7. Challenge runs visible in a Challenges panel; warn before an action breaks one.
8. Secret/hidden achievements for physics silliness.
9. No missables, no luck, no griefing incentives; all earnable in a new save.
10. **Integrity:** if a dev tool is ever used in a save, it unlocks nothing and
    increments no stats (flag from Deliverable A: `DevToolsUsedInSession`).
11. Friendly names (never e.g. "Colorblind").

## Co-op credit
- Completion/challenge: everyone in the session when the pit drains (incl. late
  joiners; optional anti-boost: present ≥10% of save playtime).
- Skill/event: the player who did it.
- Two lifetime counters: **team sales** (every sale in your sessions, incl. automation)
  and **hand sales** (balls you delivered yourself).

## Mastery note
Biggest pits can't be cleared purely by hand, so "clear bare-handed" becomes (a)
automation-restriction challenges (finishable) and (b) hand-delivery grinds. Pit
sizes are never changed to fit an achievement; the economy simulator checks each
challenge is finishable.

## The list (68; trim/add freely, Steam's limit is far higher)
Target = rough global unlock % to tune toward. H = hidden.

### Onboarding (first hour)
1 Dive In — grab first ball — 95% · 2 Cha-Ching — sell first ball — 93% ·
3 Nothing but Net — sell a ball you threw — 80% · 4 Close Enough — ball rejected by
wrong container — 75% · 5 Retail Therapy — buy first upgrade — 75% · 6 Century —
100 team sales — 70%

### Lifetime (Steam stats + progress bars)
7 Ball Handler — 1k team sales — 60% · 8 Pit Professional — 10k — 45% · 9 Ball Baron
— 100k — 30% · 10 Ball Tycoon — 1M — 12% · 11 Ten Million Little Spheres — 10M — 3% ·
12 Hands-On — 10k hand sales — 25% · 13 Workhorse — 100k hand — 4% · 14 Ball Is Life
— 25 hrs — 15%

### Pit moments
15 Avalanche — cave-in moves 1k balls — 70% · 16 Landslide — one cave-in moves 25k —
25% · 17 Rock Bottom — uncover the floor — 55% · 18 Spelunker — touch floor while pit
≥90% full — 15% · 19 Halfway There — drain 50% — 50% · 20 Monochrome — one container
100% while all others <50% — 8% · 21 Last Ball Standing — sell a pit's final ball —
20% · 22 Mountaineer — stand on a ≥2 m pile outside the pit — 20%

### Completion (one per pit profile + meta)
23 Kiddie Pool Champion — smallest — 45% · 24 Party Pooper — 2nd size — 30% ·
25 Warehouse Clearance — 3rd — 18% · 26 The Big Drain — biggest — 10% · 27 Drainmaster
— every size — 6%

### Skill
28 Three-Pointer — sell a 10 m throw — 40% · 29 Half-Court Heave — 25 m — 10% ·
30 Bank Shot — thrown ball bounces then sells — 30% · 31 Swish — 10 thrown sales in a
row — 20% · 32 On Fire — 50 correct sales, no reject — 15% · 33 Catch! — catch a ball
mid-air — 35% · 34 Juggler (H) — catch same ball 3× before it touches anything — 5% ·
35 Quick Hands — 20 hand sales in 10 s — 12% · 36 Color Coordinated — sell into every
color within 10 s — 18%

### Co-op (2p unless noted)
37 Assist — teammate catches your throw & sells in 5 s — 15% · 38 Better Together —
drain with ≥2p — 25% · 39 Pit Crew — every player sells within the same 3 s — 12% ·
40 Bucket Brigade (3p) — one ball through 3 players before selling — 5% · 41 Full Crew
(4p) — drain with 4p — 6% · 42 Dodgeball (H) — hit a teammate with a thrown ball — 30%

### Upgrades & automation
43 Gearing Up — 10 upgrade levels in one save — 50% · 44 Fully Loaded — max carry
capacity — 25% · 45 Clean Sweep (C) — vacuum 10k — 30% · 46 Drone Pilot (D) — first
drone — 35% · 47 Swarm (D) — max drones flying — 10% · 48 Some Assembly Required (E) —
first conveyor — 30% · 49 Factory Floor (E) — 100 conveyor pieces — 8% · 50 Hands Off
(D/E) — 1k balls sold in a minute, untouched — 8% · 51 Completionist — every upgrade
maxed — 4% · 64 Rake It In — rake 1k into single-color piles — 55% · 65 Going Up (C) —
ride elevator with a full platform — 35% · 66 Combed Over (C) — comb 1k out of mixed
heaps — 25% · 67 Gatekeeper (D) — Color Gates kick 10k into lanes — 10% · 68 Lights-Out
Factory (E) — a ball floor→container untouched — 8%

### Challenge runs (save-wide; shown in Challenges panel)
52 Flawless — drain with zero rejects (all players, whole save) — 3% · 53 Ground Crew
(D) — drain without buying drones — 4% · 54 Handcrafted (E) — drain without a conveyor
— 5% · 55 Minimalist — drain with ≤15 upgrade levels — 2% · 56 Speed Drainer — 2nd size
under N hrs (simulator sets N) — 3% · 57 Lone Wolf — biggest pit solo, no one ever
joined — 1.5% · 58 Iron Pit — biggest under Minimalist rules — 0.5%

### Secret (H)
59 Cannonball — jump into the pit falling ≥2 m · 60 Never Gonna Give You Up — hold one
ball 30 min · 61 Hoarder — hold a full load 10 min without selling · 62 Out of Bounds —
throw a ball out of the play area · 63 Just One More — one session 3 hrs straight

## What the game must track (B framework; C/D/E add their own)
- **Steam stats (int, increment-only):** `team_sold`, `hand_sold`, `vacuumed`,
  `playtime_minutes`, `drones_launched`.
- **Host-broadcast gameplay events** (each player evaluates locally, unlocks for self):
  Grab (player, airborne?), Sale (ball, color, method drop/throw/drone/belt, seller,
  throw distance, bounced?, chain of holders, caught?), Reject (player), CaveIn (balls
  moved in a 10 s window, from PitAuthority thaw stats per region), FloorUncovered,
  PlayerOnPitFloor (player, pit fraction), PlayerOnPile (player, pile height outside
  pit), PitDrained (profile key, save flags, players present), Purchase (upgrade,
  level), ToolCounters (C/D).
- **Save-wide flags** (world-save participant, survive resume): `devToolsUsed`,
  `peakPlayers`, `upgradeLevelsBought`, `everBoughtDrones`, `everPlacedConveyor`,
  `rejectsTotal`, `playtime`.
- **Cheap spatial detectors:** coarse resting-ball count per 1 m pit column (a column
  hitting zero = Rock Bottom + pit fraction); coarse height map outside the pit for
  Mountaineer.

## Steam implementation notes (verify vs the project's Steamworks.NET)
- `SteamUserStats.SetAchievement`, `SetStat`/`GetStat`, `IndicateAchievementProgress`
  (progress popup), `StoreStats`. Batch `StoreStats` at milestones + every couple
  minutes (Steam rate-limits).
- Older Steamworks.NET: `RequestCurrentStats` + `UserStatsReceived_t` before any read.
- Partner-site: progress bars, hidden flags, icons, localized names; each achievement
  needs an API name, 2 icons, a description.
- Definitions as ScriptableObjects mapping Steam API name → rule (stat threshold, event
  predicate, or completion + save flags). Adding one needs no code unless it's a new
  event type.
- Kit gap: a "joining breaks this save's Lone Wolf challenge" lobby warning needs a kit
  hook; until then B warns in-game and offers to switch the lobby to invite-only.

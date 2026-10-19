# Product Vision — Drain the Ball Pit

## Elevator pitch
A co-op (1–4 players) first-person physics sorting game on Steam, ~$5. You and your
friends are the (understaffed) workers of a **ball-pit factory**. A pit of colored
balls arrives; you drain it — pull balls out, sort them by color, and deliver each
color into its matching machine. A correct delivery "sells" the ball for 1 coin into
a shared team pot; wrong color goes back to the pit. The pot funds shared upgrades (a
tool tree of real factory gear: by hand → brooms/rakes/excavator/carts → helpers →
conveyors/automation). When a pit is drained, the **next shipment** arrives and the
pit is reconfigured in place into a bigger, differently-themed pit — escalating toward
a huge finale. Upgrades + money persist across the whole run. Win = drain the final
pit. Underneath it all it's a physics TOY-BOX: digging, throwing balls at each other,
messing around with friends is first-class, never gated.

> Working title: "Drain the Ball Pit" (name not final). The in-engine project folder
> is still "Bathroom Simulator" (a retired earlier concept); rename is deferred work.

## Modes
- **Main run (primary):** one authored factory venue, ONE pit location. A kiddie-pool
  tutorial pit, then a run of ~4–5 escalating "shipment" pits (bigger / more colors /
  new theme) reconfigured in place toward a huge finale (~1M balls, perf permitting).
  Upgrades + money persist; one live session; a results/win screen only at the finale.
  See game-design.md §1/§1b. (Count/finale size are open tuning calls.)
- **Custom / Freeplay (secondary):** the pick-a-size-and-settings sandbox for people
  who just want to sort. It's the existing behavior, kept cheaply. The run is the focus.

> The FULL living game design is `game-design.md` (factory/shipment structure, tool
> tiers, economy, colors, win, tech constraints, and a consolidated open-questions/
> bugs list in §12). `achievements.md` is the Steam achievement plan.
> `networking-and-pit.md` is the pit tech + networking + current build status.
> `docs/direction/GAME-DIRECTION.md` is the market/direction reasoning. THIS file is
> just the high-level pitch; those are the detail.

## Pillars
- **Satisfying bulk progress.** The pit visibly shrinks; draining feels good (ASMR-ish).
- **Co-op + physics toy-box.** Divide labor (extract vs. sort vs. haul) OR just mess
  around (dig, ball-fight, chuck balls on the deck) — both valid, neither gated. This
  emergent play is the word-of-mouth engine; protect it.
- **Clicker-like progression across the run.** Each upgrade changes HOW you play;
  nothing is overpowered; no purchase ends a pit minutes after buying it.
- **The factory fiction makes the tools make sense.** Workers naturally use brooms,
  excavators, carts, vacuums, conveyors — the fiction justifies the mechanics.
- **Session persistence.** A run is long; money/upgrades/pit state must be
  saveable/resumable.

## Explicitly out of scope
- Enemies/combat, procedural level generation, competitive/PvP.
- The old Phish fishing content and the Bathroom Simulator concept (both retired).
- **A multi-room, door-linked campaign of separate authored venues.** Escalation ships
  as the shipment-pit sequence in ONE venue (pit reconfigured in place), not several
  rooms — this was the deliberate scope cut for a solo first ship (see
  `docs/direction/GAME-DIRECTION.md`).
- **Seeded dailies / leaderboards.** Not in scope; the run + upgrades + toy-box are the
  game.

## Foundation
Reuses the retained multiplayer foundation: Unity Netcode for GameObjects (NGO
1.7.1) + Steamworks.NET + Steam lobbies + Steam Cloud save. Unity 2022.3.14 LTS,
URP 14.0.9.

> **Steam app [DECIDED]:** reuse the existing (unreleased, no store page) **Phish**
> Steam app — rename it in Steamworks, don't register a new AppID (the AppID is just
> a number; the foundation is wired to it; a new app costs the $100 fee + Steam-key
> cooldown; safe because Phish was never released). Final project/folder rename off
> "Bathroom Simulator" is separate deferred work.

## How work happens now (2026-10)
- Development is done by **various Kiro sessions in this repo**, plus occasionally
  browser Claude (to save credits). There is NO LONGER an external "strong Kiro" on a
  separate machine — ignore any older note implying one; this repo's Kiro owns the
  work directly.
- The steering docs (this + game-design + achievements + networking-and-pit) are the
  SHARED MEMORY across sessions. Keep them current; they are how a fresh session
  learns the project.
- **No Unity compiler or runtime on the agent machine [IMPORTANT].** Kiro sessions
  (and any workflow agents they spawn) CANNOT run a Unity batchmode compile, enter
  play mode, or run the game here — no Unity editor is installed on the dev box the
  agent runs on. An agent's "verification" is careful cross-file signature tracing
  against the real sources in the repo + the packages in `Library/PackageCache`. The
  final compile and the actual play/multiplayer test are ALWAYS done by the OWNER in
  their Unity editor. So: agents must write code that is traced to compile, and must
  hand back (in a HANDOFF doc) the exact in-editor steps + a manual test checklist for
  the owner — never claim a Unity compile or a runtime behavior was verified by the
  agent. This replaces the retired external-Kiro assumption that "another Kiro
  integrates and compile-fixes"; there is no such integrator now, only the owner's
  editor. If a brief or review implies the agent itself compiles/runs Unity, that
  wording is stale — fix it.
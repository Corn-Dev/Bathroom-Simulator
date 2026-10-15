# Product Vision — Drain the Ball Pit

## Elevator pitch
A co-op (1–4 players) first-person physics sorting game on Steam. Players work
through a **campaign** of ever-bigger pits of colored balls. In each pit you drain
it by pulling balls out, sorting them by color, and delivering each color into its
matching container. A correct delivery "sells" the ball for 1 coin into a shared
team pot; wrong color goes back to the pit. The pot funds shared upgrades (a tool
tree: by hand → better hand tools → helpers → full automation). Upgrades and money
carry forward from pit to pit. Win = drain the final pit.

> Working title: "Drain the Ball Pit" (name not final). The in-engine project folder
> is still "Bathroom Simulator" (a retired earlier concept); rename is deferred work.

## Modes
- **Campaign (primary):** one fixed authored playthrough, ~4–5 pits in sequence,
  tiny kids' pool (tutorial) → huge. Upgrades + money carry forward. One live
  session, pits are rooms connected by doors (see game-design.md §1b).
- **Custom / Freeplay (secondary):** the pick-a-size-and-settings sandbox for people
  who just want to sort. It's the existing behavior, kept cheaply. Campaign is the
  focus.

> The FULL living game design is `game-design.md` (campaign structure, tool phases,
> economy, colors, win, tech constraints). `achievements.md` is the Steam
> achievement plan. `networking-and-pit.md` is the pit tech + networking + current
> build status. THIS file is just the high-level pitch; those three are the detail.

## Pillars
- **Satisfying bulk progress.** The pit visibly shrinks; draining feels good (ASMR-ish).
- **Co-op coordination.** Dividing labor (extract vs. sort vs. haul) beats everyone
  doing the same thing.
- **Clicker-like progression across the campaign.** Each upgrade changes HOW you play;
  nothing is overpowered; no purchase ends a pit minutes after buying it.
- **Session persistence.** A full campaign is long; it must be saveable/resumable.

## Explicitly out of scope
- Enemies/combat, procedural level generation, competitive/PvP.
- The old Phish fishing content and the Bathroom Simulator concept (both retired).

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
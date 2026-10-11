# Product Vision — Drain the Ball Pit

## Elevator pitch
A co-op (up to 4 players) physics sorting game. Players spawn around a HUGE,
completely full pit of colored balls. The goal is to drain the pit by pulling
balls out one at a time and sorting them into color-matched containers. Correctly
"selling" a ball (dropping it in the right container) earns money for the shared
team pot, which funds upgrades that help clear the pit faster. Wrong color in the
wrong container → the ball goes back into the pit.

> Working title: "Drain the Ball Pit" (name not final).

## Core loop
1. Grab a ball from the pit (one at a time to start).
2. Carry it to a container.
3. Drop it in the matching color container to sell it → earn money (shared pot).
4. Wrong container → ball returns to the pit (penalty: lost time/effort).
5. Spend the shared pot on upgrades that speed up extraction and sorting.
6. Repeat until the pit is drained (win condition).

## Pillars
- **Satisfying bulk progress.** The pit visibly shrinks. Draining should feel good.
- **Co-op coordination.** Four players dividing labor (extract vs. sort vs. haul)
  should beat four players doing the same thing.
- **Meaningful upgrades.** Each purchase changes how you play, not just a number.
- **Session persistence.** A full playthrough is long; players must be able to
  save and resume (see tech/persistence steering).

## Target scope (early)
- 4 ball colors to start (tunable — colors are cheap to add/remove).
- 1 shared pit, 4 containers (one per color).
- Shared money pot.
- A first tier of upgrades (see design steering).
- Save/resume of a session's progress.

## Explicitly out of scope for now
- Enemies/combat.
- Procedural level generation.
- Competitive/PvP modes.
- The old Phish fishing content and the Bathroom Simulator concept (both retired).

## Status
Reusing the retained multiplayer foundation (Unity Netcode for GameObjects +
Steamworks.NET + Steam lobbies + save system).

The single biggest technical risk — "can we even render/simulate a huge pit?" —
is **SOLVED**. The pit tech is built and validated: `Assets/_Prototypes/BallPitFinal/`
runs **1,000,000 balls at ~125 fps** with real physics feel, exact per-color
counts, GPU occlusion culling, and seamless physics↔static handoff (no snapping).
See the networking-and-pit steering for the architecture and gameplay hooks.

We are now moving from pit-tech into the **gameplay layer** (containers, sorting,
money loop, upgrades) and the two remaining risks: **networking** (host-authoritative
NGO over Steam) and **bigger tools** (vacuum/drones/sorters that touch many balls).
Those two are the only things likely to cause lag going forward.

These steering files are the living design record; update them as decisions firm up.

> **Steam app / project rename [DECIDED]:** reuse the existing (unreleased, no
> public store page) **Phish** Steam app — rename it in Steamworks rather than
> registering a new AppID. Rationale: the AppID is just a number (store name, art,
> description are editable), the foundation (Steamworks.NET init, lobbies, Steam
> Cloud saves) is already wired to it, and a new app costs the $100 Direct fee +
> the Steam-key request cooldown the user flagged. Safe to reuse precisely because
> Phish was never released (no reviews/ownership baggage). The in-engine project
> folder is still named "Bathroom Simulator"; the eventual code/folder rename to
> the final title is separate deferred work.

> **Who drives implementation:** a stronger Kiro instance on a separate computer
> (no repo access) leads the hard implementation work and did substantially better
> on the pit rewrite. This repo's Kiro primarily **applies the changes that Kiro
> hands over and relays information back**. Other Kiro instances on this computer
> should assume the same split: prefer integrating/validating the other Kiro's work
> over re-architecting from scratch.

---
inclusion: always
---

# Episode Log Steering

Criteria, rules, and lifecycle for the episode log at `/content/logs/episode-current.md`.

## Purpose

The episode log is a content-filtered dev log scoped to the current video episode being built toward. It captures dev activity that has entertainment value for a gaming or general audience — not a technical record, but a story bank.

## The Entertainment Filter

Before logging something here, ask: would a gamer or general viewer find this interesting if it showed up in a YouTube video?

**Log it:**
- A feature that looks cool or satisfying on screen (e.g. fish actually biting, players seeing each other move)
- A bug that was funny, weird, or visually broken (e.g. player sliding across the map, fish spawning inside the dock)
- A dev struggle that took longer than expected — especially if there's a "finally got it working" moment
- A decision that changed the direction of something (e.g. "switched from X to Y because Z")
- A moment of visible progress — something that didn't exist yesterday and now does
- Anything with a clear before/after that could be shown on screen

**Skip it:**
- Purely internal refactors with no visible effect (e.g. "extracted interface from SteamLobby")
- Config changes, build setup, dependency updates with no gameplay impact
- Minor fixes that aren't funny or interesting (e.g. "fixed a null ref in the lobby list")
- Anything that would require 2 minutes of context to explain why it matters

**Borderline cases — use judgment:**
- A technical decision that shaped something the player will experience → probably yes
- A refactor that unblocked a visible feature → log the feature, not the refactor
- A frustrating bug that was invisible to the player → only if the struggle itself is the story

## Rules

1. Always read `.kiro/skills/commit/refs/episode-log-format.md` before writing an entry — use that format exactly.
2. New entries go at the TOP of the file, below the header comment.
3. Write conversationally, not technically. Imagine explaining it to a friend who plays games but doesn't code.
4. Each entry must include: what happened, visual potential (high/medium/low), and a content angle tag.
5. Never delete or edit existing entries — append only.
6. Never carry entries over to a new episode — each episode starts fresh.

## Content Angle Tags

Use one of these per entry:
- `feature-showcase` — new thing that exists and looks good
- `funny-bug` — something broke in an entertaining way
- `dev-struggle` — took longer than it should, relatable pain
- `decision-moment` — a fork in the road, why you went one way
- `satisfying-progress` — before/after, something clicking into place
- `player-experience` — directly affects how the game feels to play

## Episode Lifecycle

1. **Open** — episode-current.md is active, entries accumulate
2. **Close** — triggered when you approve a long form script based on this episode's content
   - On close: rename/move to `/content/logs/archive/episode-[N].md` using sequential episode numbers
   - Immediately create a fresh `/content/logs/episode-current.md` with the standard header
   - New episode starts with no carryover from the previous one
3. **Archive** — closed episodes live in `/content/logs/archive/` and can be referenced by the Reference Analyzer

When closing an episode, ask the user to confirm the episode number before archiving.

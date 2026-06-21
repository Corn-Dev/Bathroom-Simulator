---
inclusion: always
---

# Episode Log Steering

Criteria, rules, and lifecycle for the episode log at `/content/logs/episode-current.md`.

## Purpose

The episode log is a content-filtered dev log scoped to the current video episode being built toward. It captures dev activity that has entertainment value for a gaming or general audience — not a technical record, but a story bank.

## The Entertainment Filter

Before logging something here, apply this test in order:

**The one-sentence test (apply first — this is the gate):**
Can you explain why this matters in one sentence without using any technical terms? If you can't, don't log it. "The player movement finally feels good" passes. "Switched from server-authoritative to owner-authoritative NetworkTransform" does not. If the explanation requires the viewer to understand how game engines or networking work, it fails this test.

**The viewer test (apply second):**
Would a gamer or general viewer find this interesting if it showed up in a YouTube video — without any setup or context? If they'd need 30 seconds of explanation before they could care, skip it.

**Log it:**
- Something the player can feel or see that didn't exist before (e.g. movement feels responsive, fish actually bite, players appear in each other's games)
- A bug that was funny, weird, or visually broken (e.g. player sliding across the map, fish spawning inside the dock)
- A dev struggle with a payoff — especially if there's a "finally got it working" moment that's relatable without technical context
- A decision that changed the direction of something, explained in plain terms (e.g. "decided each player gets their own plot of land")
- A moment of visible progress — something that didn't exist yesterday and now does, and you can show it on screen

**Skip it:**
- Anything where the interesting part is the implementation, not the result
- Internal refactors, architecture changes, or system rewrites — even big ones — unless the player-facing result is immediately obvious and showable
- Config changes, build setup, dependency updates
- Fixes that are invisible to someone playing the game
- Anything where the draft entry contains words like: authoritative, sync, refactor, interface, component, architecture, layer, system, pipeline

**Borderline cases:**
- A technical change that made something feel better → log the feel, not the change ("movement finally doesn't feel floaty" not "rewrote the physics integration")
- A big rewrite that has no visible result yet → wait until there's something to show
- A frustrating bug that was invisible to the player → only if the struggle itself is funny or relatable without technical context

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

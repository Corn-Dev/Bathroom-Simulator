---
inclusion: always
---

# Changelog Steering

Criteria and rules for the master changelog at `/content/logs/changelog.md`.

## Purpose

The master changelog is a standard professional dev log. It tracks the project's technical history — what changed, when, and why. It is multipurpose: good engineering practice regardless of video content, and a source of raw material for content workflows.

Think: what would you put in a GitHub changelog.

## What Belongs Here

Log it if it's any of:
- A new feature or system added
- A bug fixed (include what the bug was, not just "fixed bug")
- A refactor with meaningful scope (not just renaming a variable)
- A notable technical decision — especially if alternatives were considered
- A dependency added or removed
- A breaking change to an existing system
- A performance improvement with measurable or observable effect
- Infrastructure / tooling changes (build, test setup, CI, etc.)

## What Does NOT Belong Here

- Typo fixes, comment cleanup, minor formatting
- Work-in-progress commits with no meaningful change
- Changes that will be immediately reverted
- Meta/project management notes (those go in episode log if anywhere)

## Rules

1. Always read `.kiro/skills/commit/refs/changelog-format.md` before writing an entry — use that format exactly.
2. New entries go at the TOP of the file, below the header comment.
3. Group related changes under one entry if they were part of the same logical unit of work.
4. Be specific. "Fixed lobby join crash" is better than "Fixed bug". "Refactored SteamLobby to use IMatchDiscovery interface" is better than "Refactored SteamLobby".
5. Use past tense.
6. Never delete or edit existing entries — append only.

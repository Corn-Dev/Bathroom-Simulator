---
name: daily-focus
description: Manage daily work focus and session planning. Use when the user says things like "start session", "what am I working on", "what's the plan", "wrap up", "end of day", "done for today", or "what should I do next".
---

## How This Works

This skill manages daily planning using two files:
- `.kiro/notes/today.md` — the current focus and daily log
- `.kiro/steering/roadmap.md` — the full feature backlog (source of truth for what needs doing)

## Start of Session

When the user starts a session:

1. Read `.kiro/notes/today.md`
2. Read `.kiro/steering/roadmap.md` for broader context of what's pending
3. Summarize what they should be focusing on today based on what was planned
4. Mention any blockers from last session
5. Ask if the plan still makes sense or if priorities shifted

## End of Day / Wrap Up

When the user wraps up:

1. Read `.kiro/steering/roadmap.md` to understand the full backlog
2. Ask what they accomplished today (even if it was research/planning — that counts)
3. Based on what's next in the roadmap + what the user is motivated toward, suggest 2-3 features/areas for tomorrow
4. Break those features into **concrete, actionable tasks** sized for one session (3-4 hours):
   - Bad: "Work on fishing polish"
   - Good: "Model Bluegill in Blender using vertex colors (2 hrs), import to Unity and test as catch visual (30 min), add size display to catch popup text (30 min)"
5. Ask the user if the plan feels right or needs adjustment
6. Update `.kiro/notes/today.md` with:
   - Today's date in the End of Day Log
   - What was completed (even "researched fishing games for inspiration" is valid)
   - Tomorrow's planned tasks (the concrete ones agreed upon)
   - Any blockers/notes
7. Update "What I'm Working On Now" to reflect tomorrow's tasks

## Breaking Features Into Tasks

When suggesting tomorrow's work, reference the roadmap checkboxes and break unchecked items into steps the user can actually sit down and DO:

- If it's a code task: describe what to implement specifically
- If it's an art task: describe what to model/draw and what "done" looks like
- If it's a design task: describe what decision needs to be made
- Estimate rough time per task so the day doesn't get overloaded
- A good day has 2-3 tasks that feel completable, not 10 aspirational items

## File Format for today.md

```markdown
# Today's Focus

## Current Goal
[High-level goal for the current work block]

## What I'm Working On Now
[2-3 specific, concrete tasks in priority order with rough time estimates]

## Up Next (after current tasks)
[What comes after, from the roadmap]

## Blockers / Open Questions
[Anything stuck or undecided]

---

## End of Day Log

### [YYYY-MM-DD]
**Completed:**
- [what got done — be honest, research/planning counts]

**Tomorrow:**
- [concrete tasks with time estimates]

**Notes:**
- [anything to remember]
```

## Notes

- If `.kiro/notes/today.md` doesn't exist, create it
- Keep log entries chronological (newest at top)
- Don't judge unproductive days — some days are research, planning, or rest
- The roadmap is the source of truth for WHAT to do; this skill helps with WHEN and HOW MUCH per day

---
inclusion: manual
---

# Reference Analyzer Skill

## Description

```yaml
name: reference-analyzer
description: >
  Invoke this skill when the user wants to analyze completed video performance
  data to improve future video guidance. Triggered by things like "analyze
  my videos", "update insights", "run the reference analyzer", or "improve
  the guidance". This is a periodic meta-level tool, not part of every video
  creation run.
triggers:
  - "analyze my videos"
  - "update insights"
  - "run the reference analyzer"
  - "improve the guidance"
  - "reference analyzer"
  - "analyze performance"
```

---

## Purpose

The Reference Analyzer is a periodic, standalone skill. It takes completed video data points and outcome data, analyzes patterns across videos, and outputs opinionated insight files that improve the long form and short form workflows over time.

It does NOT run as part of every video creation. It is a meta-level tool for leveling up the system.

---

## Status: TODO

This skill is stubbed and ready for implementation. The core video creation workflows (long form, short form) run on hardcoded guidance until this is built out.

### What needs to be designed

1. **Video data point template** — the format for files in `/content/video-data/long-form/` and `/content/video-data/short-form/`. Needs to capture:
   - Observational data (fill while watching): hook type + duration, text overlay usage, pacing/cut style, audio type, transition style (long form), energy management (long form)
   - Retrospective data (fill after watching): did the throughline hold, single wow moment and when, thumbnail + title relationship, how it ended/CTA
   - Outcome data: views, likes, comments, gut rating (1–5), notable comment patterns

2. **Analyzer workflow** — how Kiro reads the data files, what patterns it looks for, how it decides what's worth adding to the insight banks vs filtering out

3. **Output format** — how the insight banks in `long-form-insights.md` and `short-form-insights.md` are structured and written

### Design principles (already decided)

- The analyzer outputs ONLY things it thinks Kiro can use to have success — it filters bad patterns rather than cataloguing everything
- It never touches core guidance files (`long-form-core.md`, `short-form-core.md`) — those are stable and author-controlled
- Insight files are owned by the analyzer — don't edit them manually
- The analyzer should be opinionated: "hooks that open on a broken visual outperform hooks that open on talking head" not "here are all the hook types observed"

### Insight banks to populate (in long-form-insights.md and short-form-insights.md)

**Long form:**
- Hook bank — categorized hook types with good outcomes attached
- Arc/throughline bank — narrative structures that worked
- Transition bank — how topic shifts were handled in high performing videos
- Pacing/energy patterns — where engaging moments vs breathing room landed
- Thumbnail/title bank — combinations that worked and why

**Short form:**
- Hook bank — hook types with good outcomes
- Payoff timing patterns — when the wow moment landed in high vs low performers
- Audio/visual style patterns — what combinations worked

---

## When This Gets Built

Implement this skill when there are at least 3–5 completed videos with data points filled in. Before that, there isn't enough signal to generate useful insights.

To kick off implementation: invoke this skill and say "let's design the video data template."

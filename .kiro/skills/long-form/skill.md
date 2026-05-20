---
inclusion: manual
---

# Long Form Video Skill

## Description

```yaml
name: long-form
description: >
  Invoke this skill when the user wants to create a YouTube long form video,
  devlog, or episode. Triggered by things like "let's make a video", "start
  a devlog", "write a script", "make an episode", "outline the next video",
  or "long form". Runs the full outline → script pipeline.
triggers:
  - "make a video"
  - "let's make a video"
  - "start a devlog"
  - "write a script"
  - "make an episode"
  - "outline the next video"
  - "long form"
  - "youtube video"
  - "devlog"
```

---

## Inputs

Before starting, identify what you're working with:
1. **Episode log** — read `/content/logs/episode-current.md` (primary source)
2. **Direct idea** — user gives you a concept or topic directly
3. **Both** — use the episode log as raw material, shaped by the user's concept

If the episode log is empty or sparse, tell the user and ask if they want to proceed with a direct idea instead.

---

## Reference Files — Read These Before Starting

Always read both before generating anything:
- `.kiro/skills/long-form/refs/long-form-core.md` — core guidance, always applies
- `.kiro/skills/long-form/refs/long-form-insights.md` — performance insights, apply if populated

---

## Workflow

### Step 1 — Idea / Angle

Propose a central angle for the video. One sentence: what is this video about and why would someone watch it?

Show it to the user. Get confirmation or adjustment before moving on.

### Step 2 — Outline

Generate a full outline. Structure:
- **Hook** — first 30 seconds, what earns the viewer's attention
- **Sections** — 3–6 sections, each with a title and 2–3 bullet points of what's covered
- **Throughline note** — one sentence describing the thread that connects all sections
- **Ending** — how it closes, what the CTA or tease is

Save the outline to `/content/outlines/[slug]-outline.md` where slug is a short kebab-case name for the episode (e.g. `ep-01-multiplayer-outline.md`).

Show the outline to the user. Wait for approval or edits. Do not proceed to script until the outline is approved.

### Step 3 — Thumbnail / Title (composed step)

After outline approval, invoke the thumbnail-title skill.
Tell it: "We have an approved outline. Generate thumbnail and title options for this video."
Pass it the outline as context.

Wait for the user to select or approve a direction before proceeding.

### Step 4 — Script

Write the full script based on the approved outline.

Format: two-column markdown table.

| What you say | What's on screen |
|---|---|
| Spoken words, natural conversational tone | Visual description: gameplay clip, screen recording, b-roll, text overlay, etc. |

Rules for the script:
- Write the spoken words as you'd actually say them — not formal, not stiff
- Every row is one beat (a sentence or two max)
- The "on screen" column should be specific enough to know what to record/capture
- Include a `[SECTION: Name]` row as a divider between outline sections
- Include `[HOOK]` at the start and `[OUTRO]` at the end

Save the script to `/content/scripts/[slug]-script.md`.

Show the script to the user. Wait for approval or edits.

### Step 5 — Approval and Archive

When the user approves the script:
1. Move the script to `/content/scripts/archive/` if it's ready for filming
2. Move the outline to `/content/outlines/archive/`
3. Close the episode log — ask the user to confirm the episode number, then move `/content/logs/episode-current.md` to `/content/logs/archive/episode-[N].md`
4. Create a fresh `/content/logs/episode-current.md` with the standard header

---

## Notes

- Never skip the outline approval step and jump to script — the outline is the contract.
- If the user says "just write the script" without an outline, generate a quick outline first and get a fast confirmation before scripting.
- The script is a filming guide, not a teleprompter — it should feel natural to read while recording.

---
inclusion: manual
---

# Short Form Video Skill

## Description

```yaml
name: short-form
description: >
  Invoke this skill when the user wants to create a short form video — a
  Reel, TikTok, or YouTube Short. Triggered by things like "make a short",
  "reel idea", "tiktok", "short form", "quick video", or "short about this".
  Also invoked when the user asks Kiro to scan the changelog for short ideas.
triggers:
  - "make a short"
  - "short form"
  - "reel"
  - "tiktok"
  - "youtube short"
  - "quick video"
  - "short about this"
  - "short ideas"
  - "scan for short ideas"
```

---

## Idea Sourcing — Three Modes

Identify which mode applies before starting:

**Mode A — Direct idea**
User gives you a concept or topic. Use it as-is.

**Mode B — Changelog scan**
User asks for short ideas from recent changes. Read `/content/logs/changelog.md` (recent entries) and `/content/logs/episode-current.md`. Generate a list of 5–8 short video ideas, one sentence each. Present the list and ask the user to pick one or pass. Do not proceed until one is selected.

**Mode C — Specific entry**
User points at a specific changelog or episode log entry. Use that entry as the source material.

---

## Reference Files — Read These Before Starting

Always read both before generating anything:
- `.kiro/skills/short-form/refs/short-form-core.md` — core guidance, always applies
- `.kiro/skills/short-form/refs/short-form-insights.md` — performance insights, apply if populated

---

## Workflow

### Step 1 — Angle

One sentence: what is this short about and what's the hook in the first 2–3 seconds?

Show it to the user. Get confirmation before moving on.

### Step 2 — Outline

Short form outline is lightweight:
- **Hook** (first 2–3 seconds) — the thing that makes someone stop scrolling
- **Middle** (the content) — 2–4 bullet points
- **Payoff / End** — how it lands or cuts

Show the outline. Wait for approval or edits before scripting.

### Step 3 — Script

Write the full script. Same two-column format as long form:

| What you say | What's on screen |
|---|---|
| Spoken words or on-screen text | Visual: gameplay, screen recording, text overlay, etc. |

Rules for short form scripts:
- Total runtime target: under 60 seconds unless content genuinely earns more
- Hook must be in the first 2–3 rows
- Every row should earn its place — cut anything that doesn't move the video forward
- "What's on screen" should be specific enough to know exactly what to record
- Mark `[HOOK]` at the start and `[END]` at the close

Save the script to `/content/scripts/[slug]-short-script.md`.

Show the script. Wait for approval or edits.

### Step 4 — Done

No episode log lifecycle for short form. No archiving step.
When the script is approved, it's ready to film. Optionally move to `/content/scripts/archive/` when filming is complete.

---

## Notes

- Short form has no running episode log — it's lighter weight than long form.
- No thumbnail/title step — not needed for short form platforms.
- If the user says "just write it" — still do a fast one-line angle confirmation before scripting.

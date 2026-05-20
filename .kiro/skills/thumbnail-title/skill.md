---
inclusion: manual
---

# Thumbnail / Title Skill

## Description

```yaml
name: thumbnail-title
description: >
  Invoke this skill when the user wants thumbnail or title ideas for a video.
  Can run standalone ("give me thumbnail ideas for this video") or as a
  composed step inside the long form workflow. Triggered by things like
  "thumbnail ideas", "title ideas", "what should I call this", "thumbnail
  and title", or when the long form skill reaches its thumbnail/title step.
triggers:
  - "thumbnail ideas"
  - "title ideas"
  - "what should I call this"
  - "thumbnail and title"
  - "title for this video"
  - "thumbnail concept"
```

---

## Inputs

This skill works from any of:
- An approved outline (preferred — most context)
- A script summary
- A video concept described in a sentence or two
- A direct user description ("it's a video about getting multiplayer working")

If invoked as part of the long form workflow, the outline is passed as context automatically.

---

## Reference Files — Read Before Starting

Check for insights that apply:
- `.kiro/skills/long-form/refs/long-form-insights.md` — thumbnail/title bank section, if populated

---

## Workflow

### Step 1 — Understand the video

Summarize the video in one sentence based on the input. Confirm with the user if the input is vague.

### Step 2 — Generate options

Produce **3 title options** and **3 thumbnail concepts**.

**Title format:**
- Each title on its own line
- After each title, one sentence explaining the angle it's taking (curiosity, achievement, struggle, etc.)

**Thumbnail concept format:**
- Each concept described in 2–3 sentences: what's in the frame, what text overlay (if any), what emotion or reaction it conveys
- Note the visual style: gameplay screenshot, face reaction, split before/after, etc.

### Step 3 — Present and discuss

Show all options. Ask the user which direction resonates or if they want variations on any of them.

This skill does not save files — it's a generative/discussion tool. If the user wants to save the chosen title/thumbnail concept, they can ask and it will be appended to the relevant outline or script file.

---

## Title Principles

- Specific beats vague. "I Finally Got Multiplayer Working (It Took 2 Weeks)" beats "Multiplayer Update"
- The viewer's question is "why should I watch this?" — the title should answer it
- Numbers and timeframes add credibility ("2 weeks", "3 bugs", "one weird fix")
- Avoid clickbait that the video doesn't deliver on — it hurts retention
- Dev journey framing works well for this audience: struggle, breakthrough, decision

## Thumbnail Principles

- One clear focal point — don't try to show everything
- Face + reaction works when the reaction is genuine and readable at small size
- Gameplay screenshots work when the moment is visually striking
- Text overlay should be 3–5 words max, large enough to read on mobile
- Before/after splits work well for visible progress moments
- Avoid busy backgrounds that compete with the subject

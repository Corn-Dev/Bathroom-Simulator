---
inclusion: manual
---

# Commit Skill

## Description

```yaml
name: commit
description: >
  Invoke this skill when the user wants to commit changes, stage files, or
  says things like "commit this", "let's commit", "make a commit", or
  "commit and log". Handles the full commit workflow: diff review, log
  suggestions, confirmation, and git commit.
triggers:
  - "commit"
  - "commit this"
  - "let's commit"
  - "make a commit"
  - "commit and log"
  - "stage and commit"
```

---

## Workflow

Execute these steps in order. Do not skip steps.

### Step 1 — Check staged changes

Run `git diff --staged` to see what's staged. If nothing is staged, run `git status` and ask the user which files to stage, or if they want to stage all changes.

### Step 2 — Review diff for loggable activity

Read the diff carefully. Apply the logging criteria from:
- `.kiro/steering/changelog-steering.md` — for master changelog
- `.kiro/steering/episode-log-steering.md` — for episode log

For each log, draft any entries you think belong there. Use the formats from:
- `.kiro/skills/commit/refs/changelog-format.md`
- `.kiro/skills/commit/refs/episode-log-format.md`

### Step 3 — Present log suggestions

Show the user your drafted entries. Format:

```
**Changelog entry:**
[drafted entry]

**Episode log entry:**
[drafted entry, or "Nothing from this diff meets the episode log bar."]

Confirm these, edit them, or skip logging entirely — then I'll commit.
```

Wait for the user's response before proceeding.

### Step 4 — Write confirmed log entries

Write any confirmed entries to their respective log files. New entries go at the TOP, below the header comment. Do not modify existing entries.

### Step 5 — Commit

Run the git commit with a message. Derive the commit message from the changelog entry if one was written — use the summary line. Otherwise write a concise conventional commit message based on the diff.

Format: `type(scope): short description`
Examples: `feat(lobby): add player slot UI`, `fix(voice): disable physics on fish catch`

Show the user the commit message before running it and ask for confirmation or edits.

### Step 6 — Done

Confirm the commit was made. Optionally show the commit hash.

---

## Notes

- This skill can also be invoked by a `userTriggered` hook — the workflow is identical either way.
- If the user says "just commit, skip logging" — skip steps 2–4 and go straight to step 5.
- If the user says "auto-log" or "log everything" — write entries without asking for confirmation on each one, but still show what was written after.

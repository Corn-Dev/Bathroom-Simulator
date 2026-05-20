---
inclusion: always
---

# Dev Logger — Triggers

This steering file defines WHEN logging happens. The criteria for what goes in each log lives in `changelog-steering.md` and `episode-log-steering.md`. Read those files when deciding what to log.

## The Two Logs

- **Master Changelog** → `/content/logs/changelog.md`
- **Episode Log** → `/content/logs/episode-current.md`

Every loggable event should be evaluated against both logs independently. Something can go in one, both, or neither.

---

## Trigger 1 — Conversation Monitoring

Watch all conversations for notable dev activity: new features, bug fixes, refactors, technical decisions, interesting problems solved, funny or frustrating moments.

When you spot something loggable:
- Suggest it to the user before logging. Never log without confirmation unless explicitly told to auto-log.
- Say which log(s) it belongs in and why.
- Keep the suggestion brief: "That bug fix seems worth logging — changelog for the fix, episode log for the struggle. Want me to add both?"

Do not suggest logging for every small thing. Use judgment. Routine edits, typo fixes, and minor tweaks are not worth interrupting for.

---

## Trigger 2 — Explicit Logging

When the user says something like "log this", "add this to the log", "note that", or similar:
- Determine which log(s) it belongs in based on each log's criteria.
- Draft the entry and show it to the user for confirmation before writing.
- If it clearly belongs in both, draft both and confirm together.

---

## Trigger 3 — Commit Review

When the commit skill is invoked, before committing:
1. Review the staged diff for loggable activity.
2. Suggest additions to both logs where appropriate.
3. Wait for confirmation, then write confirmed entries.
4. Then proceed with the commit.

This trigger is handled inside the commit skill workflow. The commit skill composes this logger as a step.

---

## Format References

- Changelog entry format → read `.kiro/skills/commit/refs/changelog-format.md`
- Episode log entry format → read `.kiro/skills/commit/refs/episode-log-format.md`

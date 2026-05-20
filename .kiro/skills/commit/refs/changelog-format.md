# Changelog Entry Format

Use this format exactly when writing entries to `/content/logs/changelog.md`.

---

## Format

```
## [vX.Y.Z] — YYYY-MM-DD

### Added
- Short description of what was added and why it matters

### Fixed
- Short description of what was broken and what the fix was

### Changed
- Short description of what changed and what it changed from

### Removed
- Short description of what was removed and why
```

Only include sections that apply. If there are no fixes, omit the Fixed section entirely.

---

## Versioning Convention

Use semantic versioning loosely:
- **Major (X)** — significant milestone: first playable, major system overhaul, shipping a build
- **Minor (Y)** — new feature or system added
- **Patch (Z)** — bug fix, small improvement, or refactor

If you're unsure of the version, look at the most recent entry in the changelog and increment appropriately. If the changelog is empty, start at `v0.1.0`.

---

## Examples

```
## [v0.2.0] — 2025-06-01

### Added
- Steam lobby creation and joining via SteamMatchmaking API
- LobbyPlayerList UI showing up to 8 player slots with names and avatars

### Fixed
- Lobby host disconnect no longer leaves clients in a broken session state — ConnectionHandler now returns all clients to main menu
```

```
## [v0.1.1] — 2025-05-28

### Fixed
- Fixed null reference exception in SteamLobby when joining a lobby before SteamManager was initialized
```

---

## Rules Reminder

- One version block per logical unit of work (can cover multiple files)
- Be specific — name the system, describe the actual change
- Past tense
- New entries go at the TOP of the changelog, below the header comment

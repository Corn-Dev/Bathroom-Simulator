# Episode Log Entry Format

Use this format exactly when writing entries to `/content/logs/episode-current.md`.

---

## Format

```
### [YYYY-MM-DD] — Entry Title

**What happened:** One to three sentences, conversational tone. Explain it like you're telling a friend who plays games but doesn't code. Focus on what it means for the game, not how it was implemented.

**Visual potential:** High / Medium / Low
*(High = looks great on screen or has a clear before/after. Medium = explainable with a quick screen recording. Low = mostly invisible to a viewer.)*

**Content angle:** [tag]

**Notes:** (optional) Any extra context useful for scripting — a funny detail, a quote, a specific moment worth calling out.
```

---

## Content Angle Tags (pick one)

- `feature-showcase` — new thing that exists and looks good on screen
- `funny-bug` — something broke in an entertaining way
- `dev-struggle` — took longer than it should, relatable pain
- `decision-moment` — a fork in the road, why you went one way
- `satisfying-progress` — before/after, something clicking into place
- `player-experience` — directly affects how the game feels to play

---

## Examples

```
### [2025-06-01] — Players Can Finally See Each Other

**What happened:** Spent two days fighting NGO's spawn system and finally got players appearing in each other's games. First time I loaded in and saw my own character walking around from another client's perspective — actually felt like a real multiplayer game for a second.

**Visual potential:** High

**Content angle:** satisfying-progress

**Notes:** The moment it worked I immediately called my friend to test it. That reaction could be good footage if I can recreate it.
```

```
### [2025-05-30] — The Fish That Wouldn't Stop Sliding

**What happened:** Fixed a bug where fish would keep sliding across the dock after being caught instead of stopping. Turned out physics wasn't being disabled on catch. Looked absolutely unhinged.

**Visual potential:** High

**Content angle:** funny-bug

**Notes:** Recorded a clip of it before fixing. Definitely usable.
```

```
### [2025-05-28] — Decided to Go Tycoon-Style for Player Plots

**What happened:** Was going back and forth on how to structure player land ownership. Landed on a Roblox tycoon-style approach — each player gets their own plot that persists and upgrades over time. Changes a lot about how progression will work.

**Visual potential:** Medium

**Content angle:** decision-moment
```

---

## Rules Reminder

- Conversational, not technical
- New entries go at the TOP of the file, below the header comment
- Visual potential and content angle are required — don't skip them
- Never edit existing entries

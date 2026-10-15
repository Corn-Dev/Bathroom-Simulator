# Deliverable B — Bite (a): the core gameplay loop in one pit

**Status:** authoritative spec for the FIRST bite of Deliverable B. Supersedes the
header framing of the old `BRIEF-B.md` (which was written for a generic "drain a pit"
game on a separate no-compiler machine). The old brief is kept alongside this one as a
**detailed design reference** for the loop, containers, economy, and tool seams — read
it for the fine detail of §6.2–§6.8, but obey THIS file where they differ.

Source of truth for design: `.kiro/steering/game-design.md` (esp. §1, §1b, §1c, §2,
§3 Phase-1 row, §4, §5, §6), `.kiro/steering/networking-and-pit.md` (the pit API +
constraints, esp. §10), and `.kiro/steering/product.md`.

---

## 0. What this bite is and is NOT

This bite builds **the core sort/sell/money/upgrade/win loop, playable and fun, in the
single existing `large_1`-style pit** in the standalone dev scene. It does NOT build
the campaign scaffolding (room-to-room doors, pit-to-pit switch, carry-forward across
pits, per-tier shops, pre-baked fills, campaign save). That is **bite (b)**, a separate
follow-up. Build the loop first; prove it is fun; then scaffold the campaign around it.

**Tool scope for this bite: HANDS ONLY** (grab / carry / throw, plus the carry-capacity
and throw-power upgrades). The owner's decision: the full Phase 1–4 tool variety
(rake, broom, dustpan, helpers, automation) is deferred until the campaign layout and
difficulty ramp are designed, because tool choice depends on that ramp. BUT the tool
**system** must be built with the full variety in mind — see §5 (seams). Do not
hardcode "hands" as the only tool; build the hotbar/tool-seam architecture so rakes,
brooms, dustpans, powered tools, and two-stage tools slot in later with no rework.

**This is final shipping game code**, not a throwaway prototype. The owner builds all
visible art (containers, tools, venue, UI skin). Code supplies systems, authoring
components with gizmos + validators, and hooks. Code never generates visible geometry in
the shipped path. Every tunable is a serialized field or a ScriptableObject. Placeholder
UI (uGUI + TMP, unstyled) sits over read-only data providers so the owner's real UI
drops in later.

---

## 1. Working rules (in-repo, real compiler)

- **This repo, real Unity.** Unity 2022.3.14 LTS, URP 14.0.9, NGO 1.7.1, Steamworks.NET.
  A real compiler is available — COMPILE and fix errors here. Ignore any old-brief rule
  about "no compiler", "another Kiro integrates", stub-compiles, or `c:\Users\cscheel`
  paths; those were for a retired external machine.
- **Absolute workspace paths.** Project root:
  `c:\Users\cohen\Desktop\Game Dev\Active_Projects\Bathroom Simulator`.
- **Write to disk early and incrementally.** Keep source files under ~400 lines; split
  with partial classes where needed. Authoring components get gizmos + validation;
  editor-only code lives in `Editor/` folders.
- **Verify by compiling + tracing.** Before signalling done, the project must compile and
  the manual loop (below §6) must be traced end to end. Clean up any temp files.

---

## 2. The loop to build (game-design §1, §2, §6)

In one pit: get balls out → sort by color → deliver each color into its matching
container. Correct color → the ball "sells" for 1 coin into a shared team pot. Wrong
color → returns to the pit (drops from above at a random spot). Out-of-bounds strays
return the same way. The pot funds shared upgrades (bought by any player, no host
privilege, no player-count scaling). The pit is drained — and this bite's win fires —
when every ball is sold.

---

## 3. Architecture: gameplay channel (old brief §6.1; game-design §10)

- **Plain-C# host core** holds the rules (economy, upgrades, containers, deposit
  detection, stats, win). A thin **NGO adapter** carries it on ONE gameplay
  `NetworkObject`, separate from NetPit. NetPit and PitReplica are UNTOUCHED.
- **State:** NetworkVariables for money (`long`), upgrade levels, win state. Late joiners
  get them automatically.
- **Purchases:** `ServerRpc(RequireOwnership = false)`, identical validation for every
  buyer including the host.
- **Events:** batched ClientRpcs for sales, rejects, purchases (with buyer name), win.
- **Must run in three contexts:** a kit session; the standalone dev scene dev-host (H)
  and dev-client (J); and offline with no NGO running (the core runs locally).
- **Player tool state:** a small NetworkBehaviour on A's player prefab with an
  owner-written NetworkVariable. NetPit is NOT used for tool/carry state.
- Decide and document whether the gameplay NetworkObject is scene-placed or host-spawned
  in `OnSessionLive`; if spawned, register it in the kit bootstrap `networkPrefabs`.

---

## 4. Build these for bite (a)

Namespace `BallPitGameplay`, Assembly-CSharp (no asmdef), under
`Assets\Game\BallPitGameplay\`:

- **`Economy\`** — money, upgrade catalog + levels, purchase validation/apply,
  `IBallPitSaveParticipant` save participant, integrity flags.
- **`Containers\`** — `BallContainer` authoring component, top-opening intake box,
  fill-view hook + default view, sell/reject/return events.
- **`Tools\`** — hotbar + tool seam (built for full variety), the HANDS tool
  (carry/throw), per-player tool-state replication. (Rake/broom/dustpan NOT built this
  bite — but the seam must fit them.)
- **`Colors\`** — `PalettePreset` (Classic + Pastel complete; others placeholder),
  colorblind client-local display palette (Okabe–Ito; protan/deutan/tritan).
- **`Progress\`** — session + per-player stats, win detection + results.
- **`UI\`** — placeholder HUD, shop (Tab), results panel. All read from data providers;
  none reach into the kit.
- **`Editor\`** — validators + default-asset menu items.

**Modified copies** (copy first, then edit additively, list every edit in the handoff):
`Assets\Game\BallPitGameplay\Player\` (tool/carry NetworkBehaviour, hotbar input),
`Assets\_Prototypes\BallPitFinal\` (ONLY the additive hooks below). Where A already
delivered a file, A's version wins as the base.

### 4.1 Containers (old brief §6.2–§6.3)
- `BallContainer`: authored, placed anywhere, optional `PitSite` link, color index, a
  **top-opening intake box** (local bounds, NOT a physics trigger), a fill-view hook
  (interface + default pivot move/scale), sell + reject C# events and UnityEvents with
  a world position.
- Fill = `CountOf(Stored, color) / ColorTotal(color)`, from the replicated ledger on
  every peer. Multiple containers may share one color.
- Balls enter **through the top only**. Pushing tools never feed a container (no pushing
  tools this bite, but keep the rule in the design).
- Validator: every active color has a container; intakes are above the floor; no intake
  overlaps the pit interior.

### 4.2 Sell / reject / return (old brief §6.3; game-design §10)
Each host step, AFTER `PitAuthority.Step`, test active ball positions against each
intake box.
- **Right color:** `Deposit(id)` → money += value(color) → raise a sale event with
  attribution (last holder/thrower via a host-side map, method dropped/thrown, throw
  distance).
- **Wrong color:** return to the pit.
- **Return is ONE step** (never leave a ball in Hopper across a step outside a player
  hopper 0–3, per NetPit join-sync): Demote → `ToGone(Hopper)` → Promote at the drop
  point → `ToLive`. Drop point = random XZ inside the pit interior (margin), raised
  above the pile by raycasting down from above the rim, clearance-checked.
- **Strays** (below kill height, or inside A's `OutOfBoundsVolume`) take the same return
  path. Needs an **additive, optional loss hook** on `PitAuthority` because today
  `Lose` marks balls `Lost`, which would block the win (game-design §10).

### 4.3 Economy + upgrades (old brief §6.4; game-design §4)
- Assets: `EconomyConfig` (per-color values, default 1, combined cap ×2);
  `UpgradeDefinition` (id, name, desc, icon, phase/step/line, max level, cost curve
  base×growth^level, prereqs = an upgrade level or "site completed", effects = per-level
  stat modifiers and/or a named hook); `UpgradeCatalog`.
- Levels are **team-shared**, feed every player's stats through A's capability seam.
- Buy allowed when money ≥ cost, prereqs met, below max level → apply + broadcast
  "X bought Y".
- **Default catalog for this bite:** Second Hand (carry 1→2) and Strong Arm (throw
  power), plus Wider Funnels (intake scale) and Sneakers (move speed). Players start
  hands-only. Costs are placeholders; playtests tune. **Leave the catalog data-driven so
  rake/broom/dustpan/etc. entries drop in later with no code change** (effects via named
  hooks are allowed for tools that don't exist yet).
- **Save participant** (`IBallPitSaveParticipant`, pattern = HopperOwnership) stores
  money, levels, stats, completed state, and integrity flags: `devToolsUsed` (from A's
  `DevToolsUsed`), `peakPlayers`, `upgradeLevelsBought`, `rejectsTotal`. These feed the
  later achievements framework.

### 4.4 Hands (old brief §6.5 Hands row only)
- A's grab + throw plus carry capacity. Extra balls go into the player's hopper via a
  new additive `PitAuthority` method that pockets one ball by id; throwing releases the
  held ball then promotes the next from the hopper.
- **Smart release:** while aiming at a container, a ball of its color releases first.
- Upgrades: Second Hand → capacity 2; Strong Arm → throw power.
- Carried stack is replicated through the player NetworkBehaviour so others see it.
- **Hotbar** (keys 1–5 + wheel through A's input reader) is built now with ONE entry
  (hands); the seam must accept more tools, powered tools, and two-stage tools later.

### 4.5 Colors (old brief §6.7; game-design §5)
- `PalettePreset` SO: a color + display name for every count 2–8. Classic + Pastel
  complete; Candy/Neon/Ocean/Sunset placeholder.
- One lobby setting `palette` (Choice of preset keys) in BallPitSettingsMap; not
  world-shaping, editable after start.
- Apply at build and live via the single additive `GpuBallRenderer.SetPalette`
  (the ONLY renderer change allowed). Color names show on containers + HUD.
- Colorblind: per-player PlayerPrefs override from Okabe–Ito (protan/deutan/tritan),
  client-local, never affects other players.

### 4.6 HUD / shop / results (old brief §6.6, §6.8)
- HUD: money; balls-left-per-color with % sold; carry count + current tool; a
  sale/purchase feed.
- Shop (Tab): catalog entries with level/cost/prereqs + Buy (sends intent); claims the
  cursor through A's input seam.
- Win: host detects `StateTotal(Stored) == Count`, broadcasts results (time, balls sold,
  money, upgrades, per-player stats + a few fun awards). Every peer shows a results panel
  with Close and an optional Main Menu button (`SessionController.SaveAndQuit()` host /
  `Leave()` client — verify in kit code). Afterwards free roam continues; save marked
  completed.

---

## 5. Seams for later (design in, DON'T build) — critical given hands-only scope

The owner deferred tool variety, so the tool system's EXTENSIBILITY is the thing that
matters most in this bite. Honor these:
- **Tool seam** supports future powered and two-stage tools (cart, vacuum, dustpan tip),
  not just hands. The hotbar holds N tools.
- **Holders** are not hardcoded as "a hopper belongs to a player" — carts, elevator
  platforms, drones, belts will hold balls later (game-design §10: holders beyond
  players need a small additive NetPit sync-enumeration change — do NOT make it this
  bite, but don't design against it).
- **Upgrade effects** allow named hooks for tools that don't exist yet; the catalog is
  pure data so Phase 2–4 entries need no code.
- **Layout** is author-driven: containers + tools must not assume a static layout (build
  zones come later; game-design §2).
- **Campaign seam (bite b):** the loop must not assume it's the only pit. Keep the win /
  "site completed" state and the economy as things a campaign wrapper can read, carry
  forward, and reset per pit. A campaign `IPitSiteSelector` that picks by progress (not
  lobby size) will need to **replicate the active siteId to clients over this gameplay
  channel** — leave room for that, don't build it. (See `PitSites.cs` /
  `DefaultPitSiteSelector` comments.)
- **Events + flags** carry enough data for the achievements framework (achievements.md
  §"What the game must track").

---

## 6. Boundaries (hard)

1. MultiplayerKit: no edits; public API only.
2. `NetPit.cs` / `PitReplica.cs`: unchanged. Use the new gameplay channel (§3).
3. Collapse simulation: behavior unchanged; new behavior only via additive hooks or
   methods that default to off.
4. `GpuBallRenderer`: one additive `SetPalette` only; shaders + cull path untouched.
5. Everything else, incl. A's code, is yours to extend — report what changed and why.

Verified pit API (game-design §10, old brief §8): `PitAuthority.TryGrab(player,id)`,
`ThrowHeld`, `Excavate(player,center,radius,max)`, `Dump(player,point,max)`,
`Deposit(id)`, `AddToolSource(pos,radius)`, `HeldId[]`, `HandTarget[]`, `Hopper(p)`,
`HopperCount(p)`, `StepIndex`, `Events`, `W` (PitWorld), `Active` (ActivePhysics:
`Count`, `IdAt`, `PosAt`, `VelAt`, `IsActive`). Ledger: `CountOf(state,color)`,
`StateTotal`, `ColorTotal`, `InPlay` — exact on host and clients. A's additions:
`GrabFilter`, `ToolFilter`, `FreezeBlocker`, a collider accessor — read A's copy for
real signatures. Confirm every referenced member exists before relying on it.

---

## 7. Deliverables

- Code under `Assets\Game\BallPitGameplay\` + the listed additive edits.
- `docs\handoff-B\DECISIONS-B.md`: each decision (what / why / how to override), owner
  questions at the top.
- `docs\handoff-B\HANDOFF-B.md`: every new + modified file (per-file edit list for
  copies); scene/prefab setup (gameplay NetworkObject, containers, HUD/shop/results
  canvases, hands viewmodel attach points on the player prefab, default catalog +
  palette assets); kit `networkPrefabs` registration if any; runtime order host +
  client; risky APIs to verify first; anything needed from the owner; and a manual test
  checklist for host + one client:
  - grab balls, carry to capacity, throw one that sells
  - a wrong-color throw returns to the pile
  - a ball thrown out of bounds comes back
  - buy each upgrade from both peers; carry capacity + throw power + funnel + speed apply
  - change the palette mid-game; colorblind display modes
  - late join (money, levels, fill correct)
  - save and resume
  - drain a tiny dev pit to the results panel, then use Main Menu
  - the standalone dev scene still works offline (no NGO)

## 8. Reviewer checklist

- Boundaries hold (kit, NetPit, PitReplica untouched; collapse unchanged; only renderer
  change is `SetPalette`; modified copies differ only as listed).
- Every path traced end to end: throw → intake → sale or return; purchase intent →
  validate → apply → replicate → capabilities; win → results → leave; save → resume;
  late join. No ball left in Hopper across a step except player hoppers 0–3. Host +
  clients use identical purchase validation.
- Tool seam genuinely admits future tools (not hands-hardcoded); catalog is data-driven;
  campaign/holder seams are not designed against.
- Every referenced type/member exists with a matching signature; NGO 1.7.1 usage is
  plausible. Any HIGH finding goes back to the coder.

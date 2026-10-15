> ⚠️ **OUTDATED HEADER — build from `BRIEF-B-bite-a.md`, not this file.**
> `BRIEF-B-bite-a.md` (same folder) is the authoritative, campaign-aware, in-repo spec
> for the first bite of Deliverable B (core loop in one pit, hands-only, tool seam built
> for later variety). THIS file is kept only as a DETAILED DESIGN REFERENCE for the loop
> / containers / economy / tool fine-detail (its §6.2–§6.8); where the two differ, the
> bite-(a) brief wins. The header below is retained for history.
>
> This brief was written by the former external "strong Kiro" for a generic
> "drain a pit" game and a separate-machine, no-compiler workflow. BOTH are now
> wrong:
> 1. **The game is a CAMPAIGN** (fixed sequence of ~4–5 pits, one live session, pits
>    as door-linked rooms, upgrades/money carry forward, per-tier shops that close,
>    pre-baked fills). See `.kiro/steering/game-design.md` §1/§1b/§1c — that is the
>    source of truth, not this brief. B must build the sort/sell/money/upgrade loop
>    AND the campaign scaffolding.
> 2. **Work happens in THIS repo** (various Kiro sessions + browser Claude), with a
>    real Unity compiler available. Ignore the "no compiler / another Kiro integrates
>    / `c:\Users\cscheel\develop` paths / stub-compile" rules below — they were for
>    the old external machine. Use the real project paths and compile here.
> Suggested approach: build B in two bites — (a) the core loop in the existing
> `large_1` pit (get it playable + fun first), then (b) the campaign scaffolding.
> The sections below are still USEFUL as a detailed design reference for the loop
> and Phase-1 tools (containers, sell/reject, money, upgrades, the extraction
> primitives) — just reframe them for the campaign and the in-repo workflow.
> This file should be REVISED into a clean campaign-aware brief before/at the start
> of the B build.

---

# Brief: Deliverable B: the basic loop and Phase 1 tools

## 0. Hard working rules (earlier runs failed when agents broke these)

- **Write to disk early.** Create your output file within your first few tool calls, starting with a heading and an outline.
- **Write incrementally.** Fill files one section or one source file at a time. Never draft more than about 150 lines of a document, or more than one source file, in a single reasoning block before writing it.
- **Keep files small.** Source files stay under about 400 lines; split larger ones (partial classes are fine).
- **If you run low on room, write what you have.**
- **Verify before finishing.** Check that every file you were assigned exists and is non-empty before signaling success. Signal an error if any are missing.
- **Absolute paths only.** Step cwd is `c:\Users\cscheel\develop`.
- **No compiler here.** There is no Unity or C# compiler on this machine. Verification is careful code reading and tracing. Temporary stub compiles are allowed if you delete them afterwards. Another Kiro integrates and compile-fixes for Unity 2022.3.14, URP 14.0.9, NGO 1.7.1 and Steamworks.NET.

## 1. Quality bar and owner guidance

- **This is the shipping Steam game.** The owner builds all art: containers, tools, venue and UI skin. Code supplies systems, authoring components and hooks, and never generates visible geometry in the shipped path.
- **Tunables:** every one is a serialized field or a ScriptableObject.
- **Editor code:** authoring components get gizmos and validation. Editor-only code goes in `Editor` folders.
- **Placeholder UI:** the HUD and shop are deliberately plain (uGUI and TMP, unstyled) and sit over read-only data providers, so the owner's real UI can replace them.
- **Owner guidance:** "get some of the basic tooling and gameplay in, then take it from there."
  - Keep it simple and intuitive.
  - Build the basics solidly.
  - Don't build ahead into later phases beyond the seams in section 7.

## 2. Goal and scope

Build the basic playable loop, following the living design record:

`c:\Users\cscheel\develop\ballpit-handoff\GameplayDelivery\GAME-DESIGN.md`

Read it first. Its [DECIDED] items are binding, and the Phase 1 row of its section 3 is the tool list for this deliverable.

**In scope:**
- containers, with sell, reject and return-to-pit
- money and upgrades, with a placeholder shop and HUD
- the Phase 1 tools: hands carry, rake, broom, dustpan, and their small upgrades
- palette presets and the colorblind display palette
- basic stats and the win flow
- the save-wide integrity flags

**Out of scope, for later deliverables:**
- Phase 2–4 tools
- the achievements framework (only its flags and stats are stored now)
- colorblind ball patterns
- Custom palettes
- the economy simulator

## 3. Inputs

1. **Deliverable A's delivered code is canonical:** `c:\Users\cscheel\develop\ballpit-handoff\GameplayDelivery\Assets\`.
   - Read `HANDOFF-A.md` and `DECISIONS.md` in `c:\Users\cscheel\develop\ballpit-handoff\GameplayDelivery\` for the real names of the seams B builds on:
     - player, capabilities, interactor and tool
     - input reader
     - `PitWorldBuilt` / `PitWorldDisposed`
     - `IPitStepListener`
     - `GetDrawnDynamic`
     - `DevToolsUsed`
     - `GrabFilter`, `ToolFilter`, `FreezeBlocker`
     - `PlayerConfig`
     - `BallVisualConfig`
     - `PitSite` / `IPitSiteSelector`
     - `OutOfBoundsVolume`
   - Use A's actual signatures.
2. **Pit files A didn't copy:** `c:\Users\cscheel\develop\ballpit-handoff\Assets\_Prototypes\BallPitFinal\` (NetPit protocol 3, PitReplica, ActivePhysics, GpuBallRenderer, shaders). Where A delivered a copy, A's copy wins.
3. **Adapter and save code:** A's copies under `GameplayDelivery\Assets\Game\BallPitSession\`. Anything not copied comes from `c:\Users\cscheel\develop\ballpit-handoff\GameplayHandoff\GameplayHandoff\current\Adapter\`, including `IBallPitSaveParticipant` (HopperOwnership shows the pattern) and `BallPitSettingsMap`.
4. **MultiplayerKit, read-only:** `c:\Users\cscheel\develop\ballpit-handoff\LobbyDelivery\Assets\MultiplayerKit\`, plus `LobbyDelivery\HANDOFF.md`.

## 4. Output location

Write only under `c:\Users\cscheel\develop\ballpit-handoff\GameplayDelivery-B\`.

**Design and review:**
- `design\`: small section files
- `.build\`: reviews

**New code** (namespace `BallPitGameplay`, Assembly-CSharp, no asmdef):
- `Assets\Game\BallPitGameplay\Economy\`: money, upgrades, purchase, save participant, integrity flags
- `Assets\Game\BallPitGameplay\Containers\`: containers, intake, fill views, sell/reject/return
- `Assets\Game\BallPitGameplay\Tools\`: hotbar, hands carry, rake, broom, dustpan, tool replication
- `Assets\Game\BallPitGameplay\Colors\`: palette presets, colorblind display palette
- `Assets\Game\BallPitGameplay\Progress\`: stats and the win flow
- `Assets\Game\BallPitGameplay\UI\`: placeholder HUD, shop and results panel
- `Assets\Game\BallPitGameplay\Editor\`: validators and default-asset menu items

**Modified copies:** `Assets\Game\BallPitSession\`, `Assets\Game\BallPitGameplay\Player\` and `Assets\_Prototypes\BallPitFinal\`. Take A's version where A delivered one, otherwise the section 3 source. Copy the file first, then edit it additively, and list every edit in HANDOFF-B.md.

**Docs:** `DECISIONS-B.md` and `HANDOFF-B.md`.

## 5. Boundaries

1. **MultiplayerKit:** no edits. Call its public API only.
2. **NetPit.cs and PitReplica.cs:** unchanged. B uses its own gameplay channel (6.1).
3. **Collapse simulation:** behavior unchanged. New behavior comes only through additive hooks or methods that default to off.
4. **GpuBallRenderer:** one additive `SetPalette` method is allowed. Nothing else changes; the shaders and the cull path are untouched.
5. **Everything else, including A's code, is yours to extend.** Report what changed and why.

## 6. Build these

### 6.1 Gameplay channel and architecture

- **Core and adapter:** the host rules (economy, upgrades, containers, stats, win) are a plain-C# core. A thin NGO adapter carries it on one gameplay NetworkObject.
- **The NetworkObject:** either placed in the gameplay scene or spawned by the host in `OnSessionLive`. Choose one and document why. If it's spawned, it must be registered in the kit bootstrap's `networkPrefabs`.
- **State:** NetworkVariables for money (`long`), upgrade levels and win state. Late joiners get them automatically.
- **Purchases:** a `ServerRpc(RequireOwnership = false)`, with the same validation for every buyer, the host included.
- **Events:** batched ClientRpcs for sales, rejects, purchases (with the buyer's name) and the win.
- **Where it must run:**
  - in a kit session
  - in the standalone dev scene, both the dev H/J host and client and offline with no NGO running (the core runs locally)
- **Player tool state:** a small NetworkBehaviour added to A's player prefab, with an owner-written NetworkVariable. NetPit is not used for this.

### 6.2 Containers

- **`BallContainer`:** an authored component, placed anywhere, with an optional `PitSite` link. Fields:
  - color index
  - a **top-opening intake box**: local bounds, not a physics trigger
  - a fill view hook: an interface, plus a default view that moves or scales a pivot by the fill fraction
  - sell and reject events with a position, as C# events and UnityEvents, for VFX/SFX
- **Fill** = `CountOf(Stored, color) / ColorTotal(color)`, computed on every peer from the replicated ledger. Several containers can share one color.
- **Entry rule:** balls enter **only through the top** [DECIDED]. Pushing tools never feed containers.
- **Wider Funnels** is a global stat that scales the intake boxes and raises an event for the art.
- **Validator:**
  - every active color has a container
  - intakes sit above the floor
  - no intake overlaps the pit interior

### 6.3 Sell, reject and return

- **Each host step,** after `PitAuthority.Step`, test active ball positions against every intake box.
- **Right color:**
  1. `Deposit(id)`.
  2. Money += `value(color)`. EconomyConfig holds the values; per-color multipliers default to 1, and all multipliers combined are capped at ×2.
  3. Raise a sale event with attribution: the last holder or thrower (a host-side map), the method (thrown, dropped or poured), and the throw distance.
- **Wrong color:** return the ball to the pit.
- **Return to the pit is a one-step operation:**
  1. Demote.
  2. `ToGone(Hopper)` with a Gone event.
  3. Promote at the drop point.
  4. `ToLive` with a Spawned event.

  A ball must never stay in Hopper across a step, because NetPit's join sync only enumerates player hoppers. The demote frees a slot, so the promote always succeeds.
- **Drop point:** a random XZ inside the pit interior (with a margin), placed above the pile surface by raycasting down from above the rim, and clearance-checked.
- **Stray balls take the same path:**
  - Balls below the kill height. This needs an additive, optional loss hook on PitAuthority, because today `Lose` marks balls `Lost`, which would block the win.
  - Balls inside A's `OutOfBoundsVolume` volumes, checked host-side against active ball positions.

### 6.4 Economy and upgrades

- **Data assets:**
  - `EconomyConfig`
  - `UpgradeDefinition`, with fields:
    - id, name, description, icon
    - phase, step, line
    - max level
    - cost curve (base × growth^level)
    - prerequisites: an upgrade level, or "site completed"
    - effects: per-level stat modifiers and/or a named hook
  - `UpgradeCatalog`
- **Levels are shared by the team** and feed every player's stats through A's capability seam.
- **Buying:**
  - Allowed when money ≥ cost, prerequisites are met and the upgrade is below max level.
  - On success, apply it and broadcast "X bought Y".
- **Default catalog asset:** cheap unlocks for Rake, Broom and Dustpan, plus Second Hand, Strong Arm, Wide Rake, Push Broom, Bigger Dustpan, Wider Funnels and Sneakers. Players start with hands only. Costs are placeholders; playtests tune them.
- **Save participant** (`IBallPitSaveParticipant`) stores:
  - money
  - levels
  - stats
  - completed state
  - **integrity flags:** `devToolsUsed` (set from A's `DevToolsUsed`), `peakPlayers`, `upgradeLevelsBought`, `rejectsTotal`

  These flags are what the later achievements framework will read.

### 6.5 Phase 1 tools (GAME-DESIGN section 3, Phase 1 row)

- **Hotbar:**
  - Keys 1–5 and the mouse wheel, read through A's input-reader interface.
  - Each tool implements A's tool seam.
  - Placeholder viewmodels have first- and third-person attach points; the owner replaces the meshes.
- **Hands:** A's grab and throw, plus carry capacity.
  - Extra balls go into the player's hopper through a new additive PitAuthority method that pockets one ball by id.
  - Throwing releases the held ball, then promotes the next one from the hopper.
  - **Smart release:** while aiming at a container, a ball of its color goes first.
  - Upgrades: Second Hand raises the capacity to 2; Strong Arm raises throw power.
  - The carried stack is replicated through the player's NetworkBehaviour, so others see it.
- **Rake:** hold to pull the balls in its head volume toward you. It works on the pile surface from the rim and on the floor. Wide Rake widens the head.
- **Broom:** hold to push the balls in front of you along the floor. Push Broom widens it.
- **Dustpan:**
  - Push it into balls to scoop them into your hopper, up to its capacity: 6, or 12 with Bigger Dustpan.
  - Tip it (RMB) to pour the load out as a stream at the lip. The poured balls are Dumped as physics, so tipping it over a container's top opening delivers them.
  - Switching tools is blocked while you carry a load, with a hint.
- **Sneakers:** move speed.
- **Tool physics runs on the host:**
  - Each FixedUpdate, before `authority.Step`, the host reads every player's tool state: tool id, active flag, pose and aim.
  - An additive **thaw sweep** on PitAuthority wakes resting balls inside a capsule or box. It is capped per step and respects the collapse budget. PhysX doesn't make moving kinematic tools collide with static resting balls, so this is needed.
  - Pushing goes through an additive velocity accessor on ActivePhysics.
  - `AddToolSource` keeps colliders around the tool.
  - Clients only render.

### 6.6 Placeholder HUD, shop and results

- **HUD:**
  - money
  - balls left per color, with the percentage sold
  - carry count and current tool
  - a sale and purchase feed
- **Shop (Tab):** catalog entries with level, cost and prerequisites, and a Buy button that sends the intent. It claims the cursor through A's input seam.
- **Results panel:** see 6.8.
- **All views read from data providers.** None of them go inside the kit.

### 6.7 Colors (GAME-DESIGN section 5)

- **`PalettePreset` ScriptableObject:** a name, and a hand-picked color and display name for every count from 2 to 8. Default assets for Classic and Pastel are complete. Candy, Neon, Ocean and Sunset are placeholders.
- **One new lobby setting:** `palette`, a Choice of preset keys in BallPitSettingsMap. It isn't world-shaping and stays editable after start.
- **Applying:** at build, and live through the additive `GpuBallRenderer.SetPalette`. Color names appear on containers and the HUD.
- **Colorblind display palette:** per player, saved in PlayerPrefs, with protan, deutan and tritan modes. It's a client-local palette override built from Okabe–Ito, and it never affects other players.

### 6.8 Stats and the win

- **Stats:** per session and per player (sales, rejects, longest throw, and so on).
- **Win:** the host detects `StateTotal(Stored) == Count` and broadcasts the results: time, balls sold, money earned, upgrades bought, and per-player stats with a few fun awards.
- **Results panel:** every peer shows it. It has a Close button and an optional, removable Main Menu button.
  - The button leaves through the kit's public API: `SessionController.SaveAndQuit()` for the host and `Leave()` for clients. Verify both, and how the game reaches its SessionController, in the kit code.
- **Afterwards:** free roam continues, and the save is marked completed.

## 7. Seams for later deliverables (design them in, don't build them)

- **Holders:** don't hardcode "a hopper belongs to a player" in new code. Carts, elevator platforms, drones and belts will hold balls later.
- **Tools:** the tool seam must support powered and two-stage tools later (cart, vacuum).
- **Upgrade effects:** named hooks are allowed for tools that don't exist yet.
- **Layout:** containers and tools must not assume a static layout, since build zones come later.
- **Events and flags:** sale, reject and purchase events and the integrity flags must carry enough data for the later achievements framework (see `GameplayDelivery\ACHIEVEMENTS.md`, section 5).

## 8. Verified integration facts (from the orchestrator, who wrote the pit code)

- **PitAuthority API:**
  - **Grab and throw:** `TryGrab(player, id)`, `ThrowHeld`.
  - **Moving balls in and out:** `Excavate(player, center, radius, max)`, `Dump(player, point, max)`, `Deposit(id)`.
  - **Colliders:** `AddToolSource(pos, radius)`, cleared every Step.
  - **Players:** `HeldId[]`, `HandTarget[]`, `Hopper(p)`, `HopperCount(p)`.
  - **Stepping and members:** `StepIndex`; `Events` (IPitEvents); `W` (PitWorld); `Active` (ActivePhysics: `Count`, `IdAt`, `PosAt`, `VelAt`, `IsActive`).
  - **A's additions:** `GrabFilter`, `ToolFilter`, `FreezeBlocker` and a collider accessor. Read A's copy.
- **BallStore ledger:** `CountOf(state, color)`, `StateTotal`, `ColorTotal` and `InPlay` are exact on host and clients, because clients apply NetPit events.
- **NetPit:**
  - Its join sync emits Hopper balls only for player hoppers 0–3, via `A.Hopper(p)`. Clients ignore the owner byte in Gone events.
  - It replicates Gone and Spawned, so the return path needs no NetPit change.
- **Renderer palette:** GpuBallRenderer sets `_Palette[16]` once, at construction, on a private material. A's `BallVisualConfig` gives each color index a fixed identity and name.
- **Color setting:** BallPitSettingsMap has `colorCount` as Int 2–8, default 4, world-shaping.
- **Kit:**
  - `SessionController.Leave()` and `SaveAndQuit()` are public.
  - `SessionMenuView.OpenChanged` and `KitOverlay.IsModalOpen` exist.
  - How gameplay code reaches the SessionController is in the kit's runtime and services code. Find it; don't add to the kit.

## 9. Deliverables

- **Code:** everything under the output location.
- **DECISIONS-B.md:** each decision (what, why, how to override), with the owner questions at the top.
- **HANDOFF-B.md:**
  - Every new and modified file, with a per-file edit list for the copies.
  - Scene and prefab setup:
    - the gameplay NetworkObject
    - containers (intake box and fill pivot)
    - the HUD, shop and results canvases
    - tool viewmodels on the player prefab
    - the default catalog and palette assets
  - Kit `networkPrefabs` registration, if any.
  - The runtime order on host and client.
  - Risky API calls to verify first.
  - Anything needed from the owner.
  - A manual test checklist for a host plus one client:
    - rake balls over the rim, then rake color piles
    - broom a pile across the floor
    - scoop with the dustpan and tip it into a container
    - a throw sells
    - a wrong color goes back onto the pile
    - a ball thrown out of bounds comes back
    - buy each upgrade from both peers
    - change the palette mid-game
    - colorblind display modes
    - late join (money, levels and fill are correct)
    - save and resume
    - drain a tiny dev pit to the results panel, then use Main Menu
    - the standalone dev scene still works

## 10. Reviewer checklist

- **Boundaries hold:**
  - MultiplayerKit, NetPit and PitReplica are untouched.
  - Collapse behavior is unchanged.
  - The only renderer change is `SetPalette`.
  - Modified copies differ only as listed.
- **Every runtime path is traced end to end:**
  - throw → intake → sale or return
  - dustpan scoop → tip → intake
  - purchase intent → validate → apply → replicate → capabilities
  - win → results → leave
  - save → resume
  - late join
- **Hoppers:** no ball is left in Hopper across a step, except in player hoppers.
- **Purchases:** host and clients use the same validation.
- **Signatures:** every referenced type and member exists with a matching signature, in A's code, the pit code, the kit, or B's own code.
- **APIs:** NGO 1.7.1 usage is plausible and listed for verification.

Any HIGH finding goes back to the coder.

# Handoff — Deliverable B, bite (a): the core gameplay loop

Implements the sort / sell / money / upgrade / win loop, hands-only, in the single
`large_1`-style pit, per `BRIEF-B-bite-a.md` and `.agents/plan.md`. All code is
Assembly-CSharp (no asmdef), namespace `BallPitGameplay` (gameplay) or `BallPitFinal`
(the two additive pit edits). See `DECISIONS-B.md` for the why.

> **VERIFICATION NOTE (read first).** No Unity editor is installed on this machine
> (`C:\Program Files\Unity\Hub\Editor` is absent; `ProjectVersion.txt` = 2022.3.14f1), so a
> headless/batchmode compile could NOT be run here. Verification was done by careful
> cross-file signature tracing against the real worktree sources and the installed packages
> (`com.unity.netcode.gameobjects@1.7.1`, `textmeshpro@3.0.6`, `ugui@1.0.0`). **An in-editor
> compile is still required of you.** Specifically check:
> - the project compiles with no CS errors after the new files import (watch the Economy /
>   Containers / UI / Tools / Colors folders);
> - NGO codegen accepts `GameplayNet` (its `[ServerRpc(RequireOwnership=false)]`,
>   `[ClientRpc]`, `NetworkVariable<long/bool>`, `NetworkList<int>`, `FixedString64/128Bytes`);
> - `PlayerToolState`'s owner-write NetworkVariables codegen.
> The `NetworkList<int>(null, Everyone, Server)` ctor and the RPC/NetworkVariable surface were
> confirmed against the 1.7.1 package source in `Library/PackageCache`.

---

## New files (all under `Assets/Game/BallPitGameplay/` unless noted)

**Colors/**
- `PalettePreset.cs` — SO: ordered colours + names for counts 2–8; `BuildColors`, `NameOf`.
- `ColorblindDisplay.cs` — client-local Okabe–Ito override (PlayerPrefs); `Apply`, `Mode`.
- `PaletteService.cs` — resolves the active preset by key; `ActiveColors`, `NameOf`.
- `PaletteApplier.cs` — MonoBehaviour: on `PitWorldBuilt` / palette / colour-blind change,
  resolves colours and calls `controller.SetPalette`. Shares its service with `GameplaySession`.

**Economy/**
- `EconomyConfig.cs` — SO: per-colour sell value (default 1, cap ×2); `Value(color)`.
- `UpgradeDefinition.cs` — SO: id/name/desc/icon, phase/step/line, maxLevel, cost curve,
  prereqs, per-level effects (PlayerStat modifier OR a named hook).
- `UpgradeCatalog.cs` — SO: ordered upgrades, `IndexOf`/`Find`, `Validate`.
- `GameplayCore.cs` + `GameplayCore.Upgrades.cs` — plain-C# authority: money, team levels,
  `CanBuy`/`TryBuy`/`AddSale`, integrity counters, `EffectiveStat`/`NamedHookAmount`.
- `GameplayNet.cs` — NGO adapter: replicates money/levels/win; `BuyUpgradeServerRpc`;
  `FeedClientRpc`/`WinClientRpc`; `static Instance`. Also `ResultsData`.
- `IGameplayReadModel.cs` + `GameplayReadModel.cs` — read-only facade for UI + containers.
- `EconomySaveParticipant.cs` — `IBallPitSaveParticipant` (tag `economy`), integrity flags.
- `GameplaySession.cs` — the scene glue: builds the core per world, host-spawns `GameplayNet`,
  owns `DepositDriver`, applies team upgrade levels to every player's `PlayerCapabilities`,
  routes buy intents, raises `WinResults`/`FeedChanged`.

**Containers/**
- `IContainerFillView.cs`, `DefaultFillView.cs` — fill-view hook + a placeholder pivot view.
- `BallContainer.cs` — authored container: colour, top-opening intake box (local, not a
  trigger), `TryIntake`, sell/reject events, `IInteractable` (Kind=Container), funnel scale.
- `ContainerRegistry.cs` — the live set of containers (OnEnable/OnDisable).
- `DepositDriver.cs` — host-only `IPitStepListener`: deposit/reject/return + stray sweep +
  win detection; installs `PitAuthority.LossHook`.
- `DropPointPicker.cs` — a safe return drop point above the pile.
- `BallAttribution.cs` — host-side "who threw this ball" map for sale credit.

**Tools/**
- `IToolCatalog.cs` — the N-wide tool seam.
- `Hotbar.cs` — local hotbar (Hotbar1..5 + wheel); one tool this bite; mirrors slot to
  `PlayerToolState`.
- `PlayerToolState.cs` — per-player NetworkBehaviour: replicated carried count + tool slot.

**UI/**
- `GameplayHud.cs` — money, per-colour balls-left + %, carry count + tool, feed.
- `ShopPanel.cs` + `ShopRow.cs` — Tab shop; Buy sends an intent; claims the cursor.
- `ResultsPanel.cs` — win results; Close → free roam; Main Menu → `SaveAndQuit()`/`Leave()`.

**Editor/** (new only)
- `BallContainerGizmo.cs` — draws the intake box + colour swatch.
- `BallContainerValidator.cs` — `Tools/Ball Pit/Validate Containers`.
- `CatalogValidator.cs` — `Tools/Ball Pit/Validate Upgrade Catalogs`.
- `GameplayAssetMenu.cs` — `Tools/Ball Pit/Create Default Gameplay Assets` (EconomyConfig,
  UpgradeCatalog + the 4 starter upgrades, the 6 palette presets).

**`Assets/_Prototypes/BallPitFinal/`** (new partial)
- `BallPitFinalController.Palette.cs` — `public void SetPalette(Color[])` → `gpu?.SetPalette`.
- `PitAuthority.Gameplay.cs` — additive `PocketHeld`, `PromoteFromHopper`, `ReturnToPit`.

## Modified files (copies backed up in `.agents/pre-edit-backups/`)

- `Assets/_Prototypes/BallPitFinal/GpuBallRenderer.cs` — **+1 method** `SetPalette(Color[])`
  (re-fills `_Palette` with the ctor packing). Nothing else changed. Shaders + cull path
  untouched. *(diff: +14 lines, additive)*
- `Assets/_Prototypes/BallPitFinal/PitAuthority.cs` — **+1 field** `LossHook` and **+1 guard
  line** at the top of `Lose(id)` (`if (LossHook != null && LossHook(id)) return;`). Null
  (default) = today's collapse behavior bit-for-bit. *(diff: +9 lines, additive)*
- `Assets/Game/BallPitGameplay/Player/PlayerInputReader.cs` — additive only:
  `PlayerButton.Hotbar1..5` appended at the END of the enum (existing indices unchanged);
  `IPlayerInput.ToolWheel`; `IPlayerInputSource.ReadWheel()`; `LegacyInputSource` hotbar
  KeyCodes + `ReadWheel()`; reader `ToolWheel` property + wheel sampling; `ButtonCount` 7→12.
- `Assets/Game/BallPitGameplay/Player/PlayerInteractor.cs` — additive to `ArmsTool`: carry
  capacity > 1 (pocket on grab / promote on throw), Strong Arm (already via `ThrowVelocity`,
  now fed `caps.ThrowPower`), smart release toward the aimed container colour, `SyncCarried`
  to `PlayerToolState`. No change to A's grab/throw-with-capacity-1 behavior.
- `Assets/Game/BallPitSession/BallPitSettingsMap.cs` — additive `Palette` key + a `palette`
  Choice (6 preset keys, `editableAfterStart: true`). NOT added to `WorldShapingKeys`.

**NetPit.cs and PitReplica.cs are UNCHANGED. MultiplayerKit is UNCHANGED (public API only).**

## Scene / prefab setup the owner must do

Open the project in Unity (it generates `.meta` for the new files/folders), then:

1. **Default assets.** Run `Tools/Ball Pit/Create Default Gameplay Assets`. It creates, under
   `Assets/Game/BallPitGameplay/`:
   - `Economy/Defaults/EconomyConfig.asset`, `Economy/Defaults/UpgradeCatalog.asset` +
     `Economy/Defaults/Upgrades/*.asset` (Second Hand, Strong Arm, Wider Funnels, Sneakers),
   - `Colors/Presets/{classic,pastel,candy,neon,ocean,sunset}.asset`.
2. **Gameplay NetworkObject prefab.** Create `BallPitGameplayNet.prefab`: an empty GameObject
   with `NetworkObject` + `GameplayNet`. Add it to the **same `NetworkPrefabsList`** assigned
   to `MultiplayerKitBootstrap.networkPrefabs` (the one `Player.prefab` is already in).
3. **GameplaySession.** Add a `GameplaySession` component (one, anywhere in the gameplay
   scene). Assign `economyConfig`, `upgradeCatalog`, and `gameplayNetPrefab`
   (= `BallPitGameplayNet.prefab`'s NetworkObject).
4. **PaletteApplier.** Add a `PaletteApplier` component. Assign its `PaletteService` presets
   (drag the 6 `PalettePreset` assets), set `defaultPaletteKey` (e.g. `classic`), and leave
   `gameplaySession` to auto-find (or assign it).
5. **Containers.** For EACH active colour, place a `BallContainer` (the owner's art + a
   `DefaultFillView` or a custom `IContainerFillView`). Set `colorIndex`, size the top-opening
   intake box (gizmo shown), optionally link a `PitSite`. Put the container's collider on a
   layer included in `PlayerInteractor.interactableMask` so aiming highlights it.
6. **Player prefab.** Add `PlayerToolState` and `Hotbar` to `Player.prefab`'s root (next to
   `PlayerAvatar`/`PlayerInteractor`). (A hands viewmodel attach point is cosmetic — the hold
   point already exists on `PlayerView.HoldPoint`.)
7. **HUD / shop / results canvases.** Place `GameplayHud`, `ShopPanel` (+ a `ShopRow` prefab
   assigned, and a `rowContainer`), and `ResultsPanel` on canvas(es); wire their TMP/Button
   refs. All auto-find `GameplaySession` if left empty.
8. Run `Tools/Ball Pit/Validate Containers` and `Tools/Ball Pit/Validate Upgrade Catalogs`.

## Kit networkPrefabs registration

- `BallPitGameplayNet.prefab`'s `NetworkObject` MUST be in the `NetworkPrefabsList` assigned to
  `MultiplayerKitBootstrap.networkPrefabs` (validated the same way `PlayerSpawner` validates
  `Player.prefab`). Without it, `GameplayNet.Spawn` fails on the host.

## Runtime order (host + client)

- Host build: `BuildRoutine` → `Restore` (money/levels + economy blob) → `NetPit.StartHost` →
  `PitWorldBuilt`. On `PitWorldBuilt`: `GameplaySession` builds the core (bound to
  `adapter.GameState`), registers the economy save participant, adds `DepositDriver` at
  `ColliderSources`, applies restored team levels to players, and spawns `GameplayNet`
  (after the host is up).
- Each FixedUpdate (host/offline): `PlayerMotor` (0) → `PlayerInteraction` (100) →
  `authority.Step` → `ColliderSources` (200) listeners, where `DepositDriver.AfterPhysicsStep`
  runs deposits/returns and the win check.
- Client: `GameplayNet.Instance` is found on spawn; the read model reads replicated
  money/levels/win; buy intents go through `BuyUpgradeServerRpc`.
- Offline (standalone dev scene, no NGO): no `GameplayNet`; the core runs locally and the UI
  reads it directly. (Capability application is a no-op offline — no `PlayerAvatar`s.)

## Risky APIs verified first (for the reviewer)

- **NGO 1.7.1:** `NetworkList<int>(IEnumerable, readPerm, writePerm)` ctor confirmed in the
  package source. `NetworkVariable<long>/<bool>` with `Everyone`/`Server` perms (same pattern
  as `PlayerCapabilities`). `[ServerRpc(RequireOwnership=false)]`, `[ClientRpc]`,
  `FixedString64Bytes`/`FixedString128Bytes`, `NetworkObject.Spawn(bool)/Despawn(bool)`,
  `NetworkVariableWritePermission.Owner` (PlayerToolState).
- **Hopper invariant:** `PocketHeld`/`PromoteFromHopper` only use the player's own hopper
  (0–3); `ReturnToPit` completes Hopper→Active within one call. No ball lingers in
  `State.Hopper` across a step except a player hopper (networking-and-pit §10).
- **Collapse boundary:** `LossHook` default null = unchanged. The one `Lose` edit is a pure
  early-return guard. `DepositDriver` sets `authority.LossHook` on build and clears it on
  `Detach`. Session hosts already set `authority.ToolFilter`; this bite does not touch it.
- **Kit API (public only):** `SessionController.SaveAndQuit()` / `Leave()`,
  `MultiplayerKitBootstrap.Instance.Controller`, `GameplayCursor.Claim/Release`,
  `BallPitKitInstaller.Instance.Adapter`, `adapter.GameState`,
  `adapter.Register/UnregisterSaveParticipant` — all confirmed to exist.

## Manual test checklist (host + one client) — brief §7

Run after the editor compile + the scene/prefab setup above:

- [ ] Grab balls, carry to capacity (buy Second Hand first), throw one that SELLS into the
      matching container (money goes up, feed line, fill view rises).
- [ ] A wrong-colour throw into a container RETURNS to the pile (drops from above, re-settles).
- [ ] A ball thrown out of bounds (below the pit) COMES BACK (via the loss hook); if an
      `OutOfBoundsVolume` is placed, a ball inside it returns too.
- [ ] Buy each upgrade from BOTH peers; confirm Second Hand (carry), Strong Arm (throw power),
      Wider Funnels (wider intake), Sneakers (move speed) all apply to every player.
- [ ] Change the palette mid-game (call `PaletteApplier.SetPaletteKey`); try each colour-blind
      mode (set `ColorblindDisplay.Mode`) — only the local player's colours change.
- [ ] Late join: a joining client sees the right money, levels and fill.
- [ ] Save and resume: money + levels + integrity flags persist.
- [ ] Drain a tiny dev pit to the RESULTS panel; use Main Menu (host saves and quits; a client
      leaves).
- [ ] The standalone dev scene still works OFFLINE (no NGO): the loop runs on the local core.

## Notes

- `.meta` files for the new scripts/folders are generated by Unity on first open (committed
  code is `.cs` only). Commit the metas after opening if you want them tracked.
- The `palette` live-change path and the stray-volume sweep are discussed in `DECISIONS-B.md`
  (owner questions 2 and 5).

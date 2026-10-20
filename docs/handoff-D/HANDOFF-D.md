# HANDOFF-D — Deliverable D: CampaignDirector (owner hand-back)

Read `DECISIONS-D.md` for the why, `DESIGN-D.md` for the design (and its §19 addendum for what tracing the real code
added). Scene model: `docs/direction/CAMPAIGN-SCENE-SETUP.md`.

> **Nothing here was compiled or run.** There is no Unity editor on the agent machine, so no compile, play mode or
> multiplayer session happened (`.kiro/steering/product.md`). Everything below is from tracing the code against the
> real sources. The compile and every play test is yours.

> **THE NETWORKED REBUILD REQUIRES A TWO-MACHINE OR TWO-INSTANCE OWNER PLAY TEST, AND THE AGENT COULD NOT VERIFY IT.**
> Host advance, the client follow, the forced full resync, a late join during pit 2 and the Hello re-send are all
> unproven until you run a host plus at least one client. See §7 tests 4 to 6.

## 1. Summary

A drained pit now advances the run instead of ending it. `CampaignDirector` (one plain scene object) owns the live
pit INSTANCE (a prefab: pit art + a `PitSite`). When a pit drains and more entries remain, the host plays a
transition, destroys the old instance, instantiates the next, and calls the new
`BallPitFinalController.RebuildActivePit`. No scene load, no session detach and no NetPit restart. The host replicates
the entry index through `CampaignNet`; each client follows by disposing its world, swapping its own pit instance,
rebinding its site and asking NetPit for the new header. The last entry's drain reaches the unchanged win/results
path. With no `CampaignDirector` in the scene, everything behaves as before.

Colours come from the campaign entry (`colorCount`), not the lobby and not the prefab. Money and upgrade levels carry
across pits. The campaign index is saved (`campaign` blob) and a host resume rebuilds the saved entry.

## 2. Per-file edits

Every pre-existing file that was edited has a byte-for-byte copy in `docs/handoff-D/pre-edit/<name>.orig` (outside
`Assets/`, so Unity ignores it). Compare with `git diff -U0 -- <file>`.

### 2a. Edited files

| File | Edit (exact seam) | Why | Original |
|---|---|---|---|
| `Assets/_Prototypes/BallPitFinal/NetPit.cs` | ADDITIVE. `Link.forceFullNextReady` (:69); `private bool rebuilding` (:76); `BeginRebuild()` (:129); `RebindAuthority(PitAuthority)` (:138); client `ResetClientWorldForRebuild()` (:544, last statement `SendHello()`). Six one-line guards: `OnClientConnected` `SendHeader` (:173); `OnClientDisconnected` `ThrowHeld` (:185); `OnHello` (:223, `&& !rebuilding`); `OnReady` early return (:228) and `bool full = link.forceFullNextReady \|\| ...; link.forceFullNextReady = false;` (:230); `OnIntent` early return (:452); `HostUpdate` first line `if (rebuilding) return;` (:373). | `A` is bound once at `StartHost` and the client never nulls `CW`, so a mid-session rebuild is otherwise impossible without a NetPit restart (forbidden, game-design §1b/§9). The guards stop host paths reading the disposed world during the async rebuild (G1). `forceFullNextReady` because equal dims+seed+colours give an equal checksum. | `pre-edit/NetPit.cs.orig` |
| `Assets/Game/BallPitSession/BallPitGameAdapter.cs` | Three null-safe director seams: `CampaignDirector.Instance?.EnsureFirstPit(resume ? save.Fill.SiteId : null)` directly before `AttachHost`'s `PitSites.SelectAndActivate` (:167); `AttachHost` new-game `else` branch (now braced) calls `CampaignDirector.Instance?.ApplyEntryColors(settings)` after `BallPitSettingsMap.Apply` (:193); `CampaignDirector.Instance?.EnsureFirstPit(null)` before `AttachClient`'s `SelectAndActivate` (:327). New `public void OnCampaignPitRebuilt(PitSiteEnvironment env)` (:301). | First pit must exist before the site scan; `Apply` is the only `colorCount` writer so the override must follow it, and not on the resume branch; the host's build-time snapshot (`fillAtBuild`, `ActiveEnvironment`, hopper ops) goes stale after a rebuild (G3). | `pre-edit/BallPitGameAdapter.cs.orig` |
| `Assets/Game/BallPitSession/HopperOwnership.cs` | New `public void ForgetParked()` (clears `parkedOwner`, `lastSeenUtc`, `moved`; keeps `slotOwner`). | Parked ids belong to the discarded world; clients stay connected. | `pre-edit/HopperOwnership.cs.orig` |
| `Assets/Game/BallPitSession/PitSlotMirror.cs` | `ops` is no longer `readonly`; new `public void RebindOps(IHopperOps newOps)`. | The mirror's ops were bound to the disposed authority (G3). | `pre-edit/PitSlotMirror.cs.orig` |
| `Assets/Game/BallPitGameplay/Economy/GameplaySession.cs` | `OnPitWorldBuilt` passes `OnPitDrainedHost` to `DepositDriver` instead of `OnWinHost` (:118); new `private void OnPitDrainedHost()` (:288: `dir != null && dir.HasMorePits` -> `dir.OnActivePitDrained()`, else `OnWinHost()`); new field `localState` (:42) and the line at :107 `if (gameState == null && CampaignDirector.Instance != null) gameState = localState ?? (localState = new BallPitSession.BallPitGameState());`. | Re-route the drain without editing `DepositDriver.cs`; keep money and upgrades across rebuilds offline and on the dev host (G5). | `pre-edit/GameplaySession.cs.orig` |

### 2b. New files

| File | Purpose |
|---|---|
| `Assets/_Prototypes/BallPitFinal/BallPitFinalController.Campaign.cs` | New partial of `BallPitFinalController` (namespace `BallPitFinal`): `RebuildActivePit`, private `OnCampaignRebuilt`, `DisposeClientPitForRebuild() => DisposePit()`, `RebindClientSite`. The three existing controller files are untouched. |
| `Assets/Game/BallPitGameplay/Campaign/CampaignDefinition.cs` | `CampaignEntry` (`pitPrefab`, `colorCount`, reserved `availableTools`), `CampaignDefinition` (ScriptableObject, `entries`), `CampaignMath`. |
| `Assets/Game/BallPitGameplay/Campaign/IPitTransition.cs` | `IEnumerator Play(IEnumerator work)`. |
| `Assets/Game/BallPitGameplay/Campaign/FadeTransition.cs` | IMGUI fade stub with "Next shipment…" text. |
| `Assets/Game/BallPitGameplay/Campaign/CampaignNet.cs` | Host-spawned `NetworkBehaviour`, one `NetworkVariable<int> entryIndex`. |
| `Assets/Game/BallPitGameplay/Campaign/CampaignSaveParticipant.cs` | Save participant, tag `campaign`. |
| `Assets/Game/BallPitGameplay/Campaign/CampaignPitSiteSelector.cs` | `IPitSiteSelector` returning the director's live pit. |
| `Assets/Game/BallPitGameplay/Campaign/CampaignDirector.cs` | The orchestrator. |
| `Assets/Game/BallPitGameplay/Campaign/Editor/CampaignEditorTools.cs` | The three `Tools/Ball Pit/...` menus (§5). |

Not touched (byte-identical to base): `PitReplica.cs`, `Assets/MultiplayerKit/**`, `DepositDriver.cs`,
`PitAuthority*.cs`, `PitWorld.cs`, `GpuBallRenderer.cs`, and the three existing `BallPitFinalController*.cs` files.
`NetPit`'s `Protocol` is still 3 and no `Write*`/`Read*` line changed.

## 3. The exact hook

**Install (scene `Awake`).** `CampaignDirector.Awake`: `previousSelector = adapter.SiteSelector;`
`selector = new CampaignPitSiteSelector(this, previousSelector); adapter.SiteSelector = selector;`. Offline and
dev host (`!BallPitFinalController.SessionDriven`) also call `EnsureFirstPit(null)` and
`ApplyEntryColors(controller.settings)` here, because the controller self-builds in `Start`.

**The three adapter lines** (`BallPitGameAdapter.cs`):

```csharp
// AttachHost, directly before PitSites.SelectAndActivate
CampaignDirector.Instance?.EnsureFirstPit(resume ? save.Fill.SiteId : null);
// AttachHost, new-game branch only, right after BallPitSettingsMap.Apply
CampaignDirector.Instance?.ApplyEntryColors(settings);
// AttachClient, directly before PitSites.SelectAndActivate
CampaignDirector.Instance?.EnsureFirstPit(null);
```

**The drain** (`GameplaySession.cs`): `DepositDriver` calls `OnPitDrainedHost`, which calls
`CampaignDirector.Instance.OnActivePitDrained()` when `HasMorePits`, else the unchanged `OnWinHost()`.

**Host advance, in order** (`CampaignDirector`):
1. `OnActivePitDrained` -> `AdvanceRoutine`: first statement `yield return null` (leave the `CheckWin` step-listener stack).
2. `RunWithTransition(HostRebuildWork(...))` -> `transition.Play(work)`: fade in, then run `work`, then fade out.
3. `HostRebuildWork`, with NO `yield` between these steps: `localIndex = next; CampaignNet.Instance.SetIndex(next)`;
   `DestroyLive()` + `SpawnPit(prefab)`; `PitSites.Activate(site, new List<PitSite>{site})`; look up the profile with
   `VenueConfig.TryGetProfile(site.ProfileKey, ...)`; `controller.settings.Clone()`, `colorCount` from the entry,
   `site.TryApplyTo(...)`, `PitWorld.ComputeBounds(s)`; `new PitSiteEnvironment(site)`;
   `controller.RebuildActivePit(s, bounds, env, site, site.OverviewCamera, callback)`.
4. Inside `RebuildActivePit`: `netPit?.BeginRebuild()` -> `BuildRoutine` (its first step is `DisposePit`, which raises
   `PitWorldDisposed`) -> on success `OnCampaignRebuilt`: `netPit.RebindAuthority(authority)` (host), `ToolFilter`
   (session only), `MarkWorldReady()` (raises `PitWorldBuilt`), then the callback.
5. The director's callback (session only) calls `adapter.OnCampaignPitRebuilt(env)` right there, in the same call
   stack as `MarkWorldReady`, so no save tick can pair the new world with the previous pit's FILL.
   `HostRebuildWork` meanwhile waits for the callback (timeout `advanceTimeoutSeconds`).

**Client follow, in order** (`CampaignNet.entryIndex` changes -> `OnReplicatedIndexChanged` -> `ClientFollow`, which
starts with `yield return null`): `ClientWork`:
1. `controller.DisposeClientPitForRebuild()` (raises `PitWorldDisposed`; `world == null`).
2. Destroy the old instance, `SpawnPit(prefab)`, `PitSites.Activate(...)`.
3. `controller.RebindClientSite(env, site, overview, prepareClientSettings)`.
4. `controller.Net.ResetClientWorldForRebuild()` **LAST** (clears `CW`/`HeaderPending`, then `SendHello()`).
5. Wait until `controller.IsPitWorldReady` (or `ClientSyncSeconds` / disconnect); the cover lifts.

The existing `Update` guard (`HeaderPending && world == null`) then starts the client build from the host's header; the
header is authoritative for dimensions and `colorCount`. Both orderings of the host's `RebindAuthority` header and the
client's Hello were walked by trace (FEAT-002 findings, T4).

## 4. Owner compile-check list

Open the project in Unity 2022.3.14f1 and let it compile. Then confirm:

- [ ] The Console has no compile errors. Pay attention to the new files: `CampaignDirector.cs`, `CampaignNet.cs`,
      `CampaignDefinition.cs`, `FadeTransition.cs`, `CampaignSaveParticipant.cs`, `CampaignPitSiteSelector.cs`,
      `IPitTransition.cs`, `Editor/CampaignEditorTools.cs`, `BallPitFinalController.Campaign.cs`.
- [ ] The NGO weaver accepts `CampaignNet` (it has a `NetworkVariable<int>`; no RPCs). No codegen is possible
      without the editor.
- [ ] C# 9 `??` is used in `GameplaySession.cs` line 107 (`localState ?? (localState = new ...)`); the project's
      language version is C# 9 so this should be fine.
- [ ] `public const byte Protocol = 3;` in `NetPit.cs` is unchanged: `git diff -U0 -- Assets/_Prototypes/BallPitFinal/NetPit.cs`
      shows only the hunks in §2a and no `Write*`/`Read*` line.
- [ ] `git diff --stat` does NOT list `PitReplica.cs`, `Assets/MultiplayerKit/**`, `DepositDriver.cs`, `PitAuthority*.cs`,
      `PitWorld.cs`, `GpuBallRenderer.cs` or the three existing controller files.
- [ ] Only one `CampaignDirector` exists in the scene (`[DisallowMultipleComponent]`; it is a singleton via `Instance`).
- [ ] After §6 below: `BallPitCampaignNet.prefab` exists and is in the kit's `NetworkPrefabsList`.
- [ ] Commit the Unity-generated `.meta` files for the new `Campaign/` folder and files.

## 5. Editor menus (new)

| Menu | Result |
|---|---|
| `Tools/Ball Pit/Create Campaign Definition` | Creates `Assets/Game/BallPitGameplay/Campaign/MainCampaign.asset` with two empty entries (`colorCount` 3 and 4, prefabs empty). Never overwrites; pings and selects an existing one. |
| `Tools/Ball Pit/Create Campaign Net Prefab` | Creates `Assets/Game/BallPitGameplay/Campaign/BallPitCampaignNet.prefab` (`NetworkObject` + `CampaignNet`). Never overwrites. |
| `Tools/Ball Pit/Validate Campaign` | Validates the open scene's `CampaignDirector` definition (else every `CampaignDefinition` asset). **Errors:** no definition; null `pitPrefab`; no `PitSite` on the prefab; `PitSite.Validate` errors; `profileKey` not in the `VenueConfig`; a colour `0..maxColors-1` with no `BallContainer`; `campaignNetPrefab` empty or lacking `CampaignNet`. **Warnings:** `colorCount` outside 2..8; duplicate `siteId` across entries; non-identity prefab root rotation or scale; fewer `BallContainer`s than the largest `colorCount`; a `BallContainer.Site` is set; a `PitSite` resident in the scene; `PitSite.Validate` warnings. |

## 6. EDITOR SETUP (matches `docs/direction/CAMPAIGN-SCENE-SETUP.md` Part A steps 6 and 7)

Do Part A steps 1 to 5 first (pit prefabs under `Assets/Game/BallPitGameplay/Pits/`, containers, upgrade station,
spawn points). Then:

**Step 6 — the campaign list.**
1. Run `Tools/Ball Pit/Create Campaign Definition`. It creates `Assets/Game/BallPitGameplay/Campaign/MainCampaign.asset`.
2. Fill the entries: entry 0 `pitPrefab = Pit_Small`, `colorCount = 3`; entry 1 `pitPrefab = Pit_Large`,
   `colorCount = 4`. Add more entries later (use `+` on the list) to extend the campaign. No code change is needed.
3. Run `Tools/Ball Pit/Create Campaign Net Prefab`. Add `BallPitCampaignNet.prefab` to the `NetworkPrefabsList` assigned
   to `MultiplayerKitBootstrap.networkPrefabs`, the same list that holds `BallPitGameplayNet.prefab` and `Player.prefab`.

**Step 7 — the director.**
1. Add ONE empty GameObject (e.g. `CampaignDirector`) to the gameplay scene and add the `CampaignDirector` component.
2. Assign `definition` = `MainCampaign.asset`.
3. Assign `campaignNetPrefab` = `BallPitCampaignNet.prefab` (needed for co-op; offline works with it empty).
4. Leave `transitionOverride` empty (a `FadeTransition` is added automatically) and leave `advanceTimeoutSeconds` at 900.
5. Delete any `PitSite`, `siteroot_*` or pit art still sitting in the scene. The director instantiates pits at runtime.
   Containers, spawn points and the upgrade station stay in the scene, unparented from any pit.
6. Each `Pit_*.prefab` must have: root at identity rotation and scale; authored at its final WORLD position (the
   prefab is instantiated at its own pose, not reparented); a `PitSite` (root or any child) with a UNIQUE `siteId`
   (`^[a-z0-9_.]{1,32}$`), a `profileKey` that exists in the `VenueConfig`, an unrotated `anchor` and `venueBounds`.
7. Containers: set `colorIndex` 0..N-1 for at least the largest `colorCount`; leave the container `Site` field empty.
8. Run `Tools/Ball Pit/Validate Campaign` and fix every Error. Read the Warnings.

Note for offline and dev-host play: the controller's inspector pit size should match one of the entries; if the
dims match no site profile, `PitSiteStandalone.TryPrepare` falls back to the first site with a Warning (acceptable).

## 7. Manual test checklist (owner)

Watch the Console. Healthy lines: `[BallPitNet] header: player N, ... balls, fill ...`, then
`[BallPitNet] synced, ledger matches host`. Bad lines: `[BallPitNet] ledger mismatch after sync`,
`Duplicate save participant tag`, `[CampaignDirector]` or `[BallPitFinal] campaign rebuild failed` errors,
`timed out waiting for the next pit to build`.

1. **Offline (standalone scene, 2 pits).** Press Play with the gameplay scene. Entry 0 builds with its colour count.
   Sell every ball: the fade plays ("Next shipment…"), entry 1 builds in the same spot with its colour count, money
   and upgrade levels are intact, players are re-placed, the old pit art is gone. Drain entry 1: the results panel
   shows. Expect no errors.
2. **Dev host (`H` key harness), alone.** Same as 1 with the harness host running. Expect no errors and the pit to
   advance. A harness `J` client cannot follow an advance (unsupported, D10).
3. **Session host-only.** Host through the kit lobby with nobody joined. Advance once. Check the Console for
   `[CampaignNet]`/`[CampaignDirector]` errors; check HUD money.
4. **Session host + 1 client (two Unity instances or two machines). REQUIRED, NOT AGENT-VERIFIED.** Both in the
   pit; drain pit 1. Watch the client: it fades, `[BallPitNet] header:` appears once with the NEW pit's ball count,
   then `[BallPitNet] synced, ledger matches host`; the same pit art appears on both; no client stuck on
   "waiting for header/build"; money and upgrades the same on both; both players re-placed and able to grab and
   throw; the HUD fill and container fill update on both. Repeat for the last pit (win on both).
5. **Late join during pit 2.** Host advances to pit 2, THEN a client joins. The client should build pit 2 directly
   (`header:` shows pit 2's size) and sync. Also try joining while the host is mid-fade (inside the rebuild
   window): the client must still end on pit 2 with a matching ledger.
6. **Resume mid-campaign.** Host a session, advance to pit 2, save, quit, resume. The host should start on pit 2
   (not entry 0) with the saved colour count; a `[CampaignDirector]` Warning "restored campaign index ... does not
   match" means the reconcile fell back to the siteId-derived index. A harmless "FILL wins" Warning is expected.
7. **Freeplay regression.** Open a scene or session WITHOUT a `CampaignDirector`. Pit selection, colours, the win
   panel and saving must behave exactly as before.
8. **Scalability.** Make a third pit prefab, add a third entry in `MainCampaign.asset`, run `Validate Campaign`, play.
   It should just work with no code change.
9. **Failure cases.** (a) A `profileKey` that is not in the `VenueConfig`: `Validate Campaign` reports an Error, and a
   run logs `which VenueConfig does not define` on the advance. (b) An empty or missing definition: the director logs
   `no campaign definition (or it is empty)` and the scene falls back to its default pit selection. (c) Remove
   `campaignNetPrefab` and host: no `CampaignNet`, clients will not follow (expected, the validator flags it).

**What to watch on every networked advance:** client stuck waiting for the header or build; the HUD fill; money
intact; players re-placed; no errors in either Console. **The agent could not verify any of the networked behaviour;
please report what you see.**

## 8. Known limits

See `DECISIONS-D.md` "Known limits and follow-ups" (no rebuild-failure retry, results panel shows only the last pit,
container fill resets per pit, trigger-gated start is a follow-up, and others).

# Decisions — Deliverable B, bite (a): the core gameplay loop

This records the decisions made while implementing the sort/sell/money/upgrade/win loop
(brief `BRIEF-B-bite-a.md`, plan `.agents/plan.md`). Each decision says what, why, and how
to override. Owner questions are at the TOP.

## Owner questions / things to confirm

1. **Default upgrade catalog + prices are placeholders.** The four starter upgrades
   (Second Hand, Strong Arm, Wider Funnels, Sneakers) and their cost curves are first-pass
   guesses (brief §4.3 says costs are placeholders, tuned by playtest). Confirm the set and
   retune `EconomyConfig` / each `UpgradeDefinition` after a playtest. Costs/growth live on
   the `.asset` files — no code change.
2. **Palette setting wiring.** A `palette` lobby setting exists (visual only, editable
   mid-game). The *default* key applies at build. Changing it LIVE from the kit settings UI
   needs a one-line call to `PaletteApplier.SetPaletteKey(key)` from wherever you surface the
   setting (the adapter's `OnSettingsChanged` is still a no-op — I did NOT edit the adapter
   for this). Confirm whether you want me to wire the live setting→applier in bite (b).
3. **Smart release is 2-deep.** When carrying >1 and aiming at a container, the tool swaps
   the held ball for a matching-colour pocketed ball ONCE (handles the common case). A full
   "always throw the matching colour first across a deep stack" needs the hopper to be
   colour-indexed; deferred. Confirm this is enough for the hands-only bite.
4. **Carry capacity > 1 is a host rule.** Pocketing/promoting happens on the host (slot
   hoppers 0–3). A client predicts nothing beyond NetPit's single held ball; the replicated
   `PlayerToolState.CarriedCount` shows the stack to everyone. Confirm this is acceptable for
   bite (a) (it keeps the NetPit join-sync invariant intact).
5. **Out-of-bounds stray sweep.** Balls that fall below the kill height return via the
   additive `LossHook`. Balls resting inside an `OutOfBoundsVolume` also return, but only when
   at least one volume exists (a cheap guard). Confirm you want the per-step volume sweep (it
   is O(active balls × volumes) and skipped entirely when no volume is placed).

## Architecture decisions

- **D1. Gameplay NetworkObject = host-spawned, SEPARATE from NetPit.** A prefab
  `BallPitGameplayNet.prefab` (NetworkObject + `GameplayNet`) is spawned by `GameplaySession`
  on `PitWorldBuilt` (which fires after `NetPit.StartHost`), and despawned on
  `PitWorldDisposed`. Register it in the kit `NetworkPrefabsList` (same list as `Player.prefab`).
  *Override:* make it scene-placed instead if you prefer — but then the standalone dev scene
  (no kit) must not require it; the offline path already runs without it.
- **D2. Plain-C# `GameplayCore` owns ALL rules; `GameplayNet` is a thin mirror.** The core
  runs identically offline, dev-host and session-host. `GameplayNet` replicates money
  (`NetworkVariable<long>`), upgrade levels (`NetworkList<int>` indexed by catalog order) and
  win (`NetworkVariable<bool>`), and forwards purchases via `ServerRpc(RequireOwnership=false)`.
  Offline there is no `GameplayNet`; gameplay calls the core directly.
- **D3. Money + upgrade levels persist via `adapter.GameState`;** extra integrity flags
  (`devToolsUsed`, `peakPlayers`, `upgradeLevelsBought`, `rejectsTotal`, `siteCompleted`)
  persist via a registered `IBallPitSaveParticipant` (tag `"economy"`). No save wire-format
  change.
- **D4. Deposit detection runs host-only in `DepositDriver.AfterPhysicsStep`** at
  `PitStepOrder.ColliderSources`, testing `authority.Active` ball positions against each
  container's intake box. Clients never run it (authority is null on clients).
- **D5. Carry capacity > 1 uses additive `PitAuthority.PocketHeld` / `PromoteFromHopper`,**
  which only ever use the player's own hopper slot (0–3), so NetPit join-sync stays correct.
- **D6. Return-to-pit is one atomic step** (`PitAuthority.ReturnToPit`): Demote → ToGone(Hopper)
  → re-promote to Active at the drop point, all within one call, so no ball lingers in
  `State.Hopper` across a step. Pool-full fallback: settle it as a resting ball at the drop
  point (never dropped from the ledger).
- **D7. `PitAuthority.LossHook` is additive, host-only, default off.** Consulted at the top of
  `Lose(id)`; returning true means the game returned the ball to the pit, so it is NOT marked
  `Lost`. `DepositDriver` installs it to route strays through `ReturnToPit`. Null = today's
  collapse behavior bit-for-bit.
- **D8. One additive `GpuBallRenderer.SetPalette(Color[])`** re-applies the 16-slot `_Palette`
  array with the ctor's packing; a thin `BallPitFinalController.SetPalette` forwarder. The
  only renderer change; shaders + cull path untouched.
- **D9. Hotbar input is additive to A's input seam.** `PlayerButton` gained `Hotbar1..5`
  (appended, so existing indices are unchanged) and `IPlayerInput` gained `float ToolWheel`.
  The hotbar holds N tools; bite (a) registers ONE (A's `ArmsTool`).
- **D10. Containers implement A's `IInteractable`** (Kind=Container), so the existing
  `PlayerInteractor.Aim` ray already targets them (set the container's layer in the
  interactor's `interactableMask`). The HUD/aiming reuse A's pipeline.

## Deviations from the plan (worth noting)

- **`DepositDriver` is a plain C# `IPitStepListener`, not a MonoBehaviour.** The plan listed
  it as a MonoBehaviour; a plain class lets `GameplaySession` inject the core + attribution
  and own its lifetime cleanly. The step registry accepts any `IPitStepListener`.
- **No separate `UiRefs.cs`.** Each UI component (`GameplayHud`, `ShopPanel`, `ResultsPanel`)
  holds its own serialized TMP/Button refs — simpler than a shared refs object. (The plan
  listed `UiRefs` as optional.)
- **ShopPanel does NOT touch `controller.InputBlocked`.** Claiming the cursor via
  `GameplayCursor.Claim` already makes `PlayerInputReader` free the mouse, suspend aiming AND
  block the dev-harness keys (its installed `InputBlocked` hook returns true while a claim is
  live). Touching `controller.InputBlocked` would clobber the reader's own hook.
- **`RegisterSaveParticipant`/`UnregisterSaveParticipant` already exist on the adapter** (the
  plan's "seam" assumed they might need adding — they did not; no adapter edit was needed).

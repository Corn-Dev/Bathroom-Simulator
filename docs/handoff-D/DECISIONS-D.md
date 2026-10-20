# DECISIONS-D — Deliverable D (CampaignDirector)

Decisions taken while building Deliverable D, with the reason for each. Companion docs:
`DESIGN-D.md` (approved design, rev 4 + the §19 addendum), `HANDOFF-D.md` (what to do in the editor, what
to test). **Nothing here was compiled or run: there is no Unity on the agent machine
(`.kiro/steering/product.md`). Every statement about behaviour is from reading the code, not from running it.**

## Decisions

**D1. Namespace is `BallPitGameplay`, not `BallPitGameplay.Campaign`.** The dispatch brief said "namespace
BallPitGameplay (Campaign sub-folder)"; DESIGN-D §9 wrote `.Campaign`. The brief wins. It also means
`BallPitGameAdapter.cs` (already `using BallPitGameplay;`) and `GameplaySession.cs` (already in that namespace)
need no new `using`. The editor file uses the same namespace and is `internal static`.

**D2. Object model: a plain scene `CampaignDirector` (MonoBehaviour) plus a host-spawned `CampaignNet`.** The
director is NOT a NetworkObject (it exists on every peer, from the scene). Only one thing needs replicating, the
entry index, so `CampaignNet` is a tiny RPC-free `NetworkBehaviour` with one `NetworkVariable<int> entryIndex`
(Everyone read, Server write), modelled on `GameplayNet`. It is spawned ONCE per session by the host, not once per
world like `GameplayNet`, so the index survives pit rebuilds and a late joiner receives it from NGO's spawn sync.

**D3. The hook is the adapter's existing `SiteSelector` seam plus three null-safe one-line calls.** The director
installs a `CampaignPitSiteSelector` in `Awake` (keeping the previous selector as the fallback and restoring it on
teardown only if it is still ours). Three calls were added to `BallPitGameAdapter`:
`CampaignDirector.Instance?.EnsureFirstPit(...)` before `PitSites.SelectAndActivate` in `AttachHost` and in
`AttachClient`, and `CampaignDirector.Instance?.ApplyEntryColors(settings)` in `AttachHost`'s new-game branch.
With no director in the scene each is a no-op (freeplay is unchanged).

**D4. `IPitTransition.Play(IEnumerator work)` instead of `Play()`.** Part B and DESIGN-D §9.5 said a parameterless
`Play()` spanning dispose to build. A parameterless method cannot know when the rebuild finished, so the reveal would
be timer-guessed. Passing the director's rebuild enumerator keeps it ONE coroutine and ONE method, lets any future
transition (garage door, droned-in crate) wrap the same work, and removes the race. `FadeTransition` is the IMGUI
stub (no prefab, no Canvas). The director drives the transition by hand (`RunWithTransition`) so a throwing
transition can never strand the rebuild: on failure the same `work` enumerator is stepped to completion.

**D5. Resume (finding F2) = gated `EnsureFirstPit` keyed by the save's `Fill.SiteId`.** On a host resume,
`AttachHost` passes `save.Fill.SiteId`; `EnsureFirstPit` maps it to an entry with `CampaignDefinition.IndexOfSiteId`
(reads the prefab asset, no instantiate) and instantiates that entry BEFORE the site scan. The selector then returns
that live pit, so the adapter's resume check agrees. The `campaign` save blob (`CampaignSaveParticipant`, tag
`campaign`, version byte + int index) only reconciles: `OnIndexRestored` accepts the saved index if its prefab's
siteId matches the live pit, otherwise logs a Warning and keeps the siteId-derived index. The participant is
registered once per session inside `EnsureFirstPit`, i.e. before `AttachSession`, so it exists when the first
build's `Restore` runs.

**D6. NetPit seam set, and why not Plan B.** The only edits to the frozen `NetPit.cs` are additive:
`BeginRebuild()`, `RebindAuthority(PitAuthority)`, `ResetClientWorldForRebuild()`, `Link.forceFullNextReady`, a
private `rebuilding` flag, and six one-line guards that are no-ops when no rebuild is happening. `Protocol` stays 3
and no `Write*`/`Read*` line or message id changed. Why they are needed: `NetPit` binds `A` once in `StartHost` and
the client never nulls `CW`, so without these a mid-session rebuild is impossible short of `Dispose` + `StartHost`,
which is a NetPit restart and is forbidden by game-design §1b/§9 (one live session). Plan B (tear the session down
and re-attach for every pit) was rejected because it drops every client mid-run, re-runs the lobby handshake, loses
the in-flight economy and defeats the "one live session" rule. DESIGN-D §14's smell rule applies: if these methods
ever grow beyond this, reconsider Plan B.

**D7. Colours via a post-`Apply` override, new-game branch only.** `BallPitSettingsMap.Apply` is the only writer
of `colorCount` on the host build path and overwrites anything set earlier, so `ApplyEntryColors` runs right after
it. The resume branch keeps the saved `colorCount` (`save.Fill.ApplyTo`). Later pits get their colours from the
cloned `PitSettings` inside `HostRebuildWork`.

**D8. Six gaps found by tracing the real code (G1 to G6) are closed with the smallest additive seams.** See
DESIGN-D §19 for the table. In short: G1 NetPit host paths read a disposed world during the async rebuild
(`rebuilding` guards); G2 the client's `Environment`/`activeSite`/`overviewCamera`/`prepareClientSettings` pointed at
the destroyed pit (`RebindClientSite`); G3 the adapter's `fillAtBuild`, `ActiveEnvironment` and `slotMirror` ops went
stale (`OnCampaignPitRebuilt`, `PitSlotMirror.RebindOps`, `HopperOwnership.ForgetParked`); G4 `Destroy` is deferred and
`PitSites.FindAll` would still see the old pit (advance calls `PitSites.Activate` directly); G5 offline and dev-host
`GameplayCore` used per-core money so a new core reset money and upgrades (`GameplaySession.localState`); G6
`DepositDriver.CheckWin` runs inside the step-listener stack (`AdvanceRoutine` starts with `yield return null`).

**D9. `PitSites.Activate`, not `SelectAndActivate`, on advance.** `Destroy` is deferred and `FindAll` uses
`FindObjectsOfType<PitSite>(true)`, so rescanning in the same frame can return the old site or drop one as a duplicate
siteId. The director activates the new site directly. `FindAll` is only used for the first pit, before any swap.

**D10. Offline is the standalone scene; a dev-host client is unsupported.** Offline and the dev host (harness `H`
key) run with `SessionDriven == false`: the director instantiates entry 0 in `Awake` (before the controller's
`Start`). There is no `CampaignNet` on the dev host's throw-away NetworkManager, so a harness `J` dev CLIENT cannot
follow an advance. The supported co-op path is the kit session host/client. The dev host itself advances and rebinds
correctly (by trace).

**D11. Colour count is clamped to 2..8.** `CampaignMath.ClampColorCount`. The campaign deliberately bypasses the
lobby's colour setting; the validator warns for entries outside the range.

## Owner questions (DESIGN-D §16) and the defaults I chose

- **Q1. The NetPit rebind (the one edit to the frozen file).** Chosen: Plan A, the additive seam set in D6
  (`RebindAuthority` / `ResetClientWorldForRebuild` / `forceFullNextReady`, plus `BeginRebuild` and the guards the
  tracing showed were needed). Plan B (in-place re-fill with a bigger controller change) was not built. Please
  confirm the NetPit edit is acceptable; the hunks are listed in HANDOFF-D §2 and the original is in
  `docs/handoff-D/pre-edit/NetPit.cs.orig`.
- **Q2. Resume to mid-campaign.** Chosen: honoured, not just captured. The host builds the saved entry's prefab via
  the siteId-keyed `EnsureFirstPit` (D5) and `CampaignSaveParticipant` reconciles. It is written, but it is not
  traced end to end by any run, so treat resume as an owner test item (HANDOFF-D §7 test 6) rather than a given.
- **Q3. Mid-run rebuild failure UX.** Chosen: logged error, no in-run retry (see Known limits). Say so if you want
  retry or abort-to-menu.

Change any of these and tell me; none is hard to reverse.

## Known limits and follow-ups

- **The networked rebuild is untested.** It needs a two-machine or two-instance owner play test (HANDOFF-D §7).
- **Rebuild failure has no retry UX.** If a rebuild fails or times out, `localIndex` and the replicated index have
  already advanced, the old pit is already gone, and NetPit stays in `rebuilding` (host paths frozen). v1 logs an
  error and stops. The timeout is `CampaignDirector.advanceTimeoutSeconds` (default 900 s).
- **Results panel shows only the last pit's** time, balls and rejects, while money and upgrade levels are run-wide.
- **Container fill resets per pit** (each pit gets a fresh ledger). game-design §12 "container FILL fantasy across the
  run" is still open.
- **Trigger-gated start and the "spawn outside at the interview, move spawns inside after the door" flow** are a
  follow-up. v1 spawns in the warehouse.
- **Pre-existing, not changed here:** `EconomySaveParticipant` is registered in `OnPitWorldBuilt`, after the first
  build's `Restore`, so a resume does not deliver its blob. Left alone on purpose.
- **Harmless resume warning:** `DescribeSnapshotMismatch` can log a "FILL wins" Warning on campaign resumes because
  the lobby's `pitSize`/`colorCount` differ from the entry's.
- **No watchdog covers a client stuck syncing pit 2.** The adapter's watchdog stops after the first sync. The
  director's client wait loop gives up lifting the cover after `BallPitGameAdapter.ClientSyncSeconds` (120 s) but does
  not recover the client.
- **Host fade timing:** the host's fade-in runs before `HostRebuildWork` starts, so `SetIndex` is published after the
  host fades and clients fade after they see it. Total client cover is roughly twice the fade. Cosmetic.
- **`HostRebuildWork` validates the next entry's prefab and `PitSite` before `SetIndex`,** so a bad entry does not
  advance the index.
- **Same-siteId authoring rule:** give every pit prefab a unique `siteId`. Two entries sharing one is legal but the
  validator warns, because a resume then depends on the saved index.
- **`availableTools`** on `CampaignEntry` is reserved and unused in v1.
- **No `.meta` files** were created for the new Campaign folder or files; Unity generates them on import and the owner
  should commit them.
- **Re-enabling a disabled director** does not reinstall the selector (teardown runs from `OnDisable`).

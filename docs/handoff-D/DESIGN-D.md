# DESIGN-D — The CampaignDirector (Deliverable D)

Status: DESIGN ONLY (no production code in this step). Revision 4 (review rounds 1, 2 AND 3
applied — see §18 for the finding-by-finding responses; round 3 = the Hello re-send pin, the
resume-ordering correction, the `GetComponentInChildren<PitSite>(true)` standardization, and
`settings = newSettings` in `RebuildActivePit`). Unity 2022.3.14f1, URP 14.0.9,
NGO 1.7.1, Assembly-CSharp. There is NO Unity compiler/runtime on this machine; every
claim below is cross-file signature tracing against the worktree sources, not a compile.
A missing Unity compile is NOT a defect. Anything that is a genuine in-editor runtime
check (NGO weaver output, domain-reload behavior) is marked
**[owner in-editor check]**; there are no remaining design decisions deferred to
implementation.

Binding spec: `docs/direction/CAMPAIGN-SCENE-SETUP.md` (Part B is the code contract,
Part A the scene model). Steering: game-design §0b/§1/§1b/§9/§10, networking-and-pit.md.

---

## 0. Overview

The campaign walks an ordered list of **pit prefabs**. On session start it builds
entry[0] (the prefab's `PitSite` + the director's chosen `colorCount`); the player drains
that pit (`DepositDriver` detects every ball `Stored`); the director plays a pluggable
transition, tears the pit down, advances a **replicated** index, instantiates the next
prefab, and rebuilds — until the last entry, whose drain fires the existing win/results
path unchanged. Money + upgrades persist automatically because they already live in the
session-lifetime `BallPitGameState`, not per-world.

The design's hard problem is **rebuilding the pit world mid-session without tearing down
the NGO session**, because `NetPit` binds to one `PitAuthority` instance at
`StartHost` and has no public rebind. The trace below shows exactly where that bites and
proposes the one additive `NetPit`/controller seam needed, with the wire format
untouched. This is the §1b "KEY INTEGRATION RISK" and is called out explicitly.

The components are built so **freeplay** reuses them: a pit prefab + a `colorCount` is the
same tuple the director feeds per entry; freeplay later supplies that tuple from player
choices instead of from a `CampaignDefinition`.

---

## 1. What the real code gives us (traced signatures)

All paths under `.worktrees/campaign-director/`.

### 1.1 The adapter — `Assets/Game/BallPitSession/BallPitGameAdapter.cs`
- `public IPitSiteSelector SiteSelector { get; set; }` — settable seam. Constructed in
  the ctor: `SiteSelector = new DefaultPitSiteSelector(BallPitSettingsMap.PitSize);`.
- `public void OnSessionLive(SessionLiveContext context)` — the single per-session entry
  on every peer. It finds the `BallPitFinalController`, validates the `PlayerSpawner`,
  then `AttachHost(context)` or `AttachClient(context)`.
- `AttachHost` calls `PitSites.SelectAndActivate(SiteSelector, context.Settings, …)`,
  computes `PitSettings` via `BallPitSettingsMap.Apply(context.Settings, controller.settings, VenueConfig)`
  (new game) and calls `controller.AttachSession(new PitSessionRequest { … })` **once**.
- `AttachClient` does the same selection (`savedSiteId == null`) and
  `controller.AttachSession` with `IsHost=false`.
- `public VenueConfig VenueConfig { get; }`, `public BallPitGameState GameState => gameState;`
  — `gameState` is re-created `new BallPitGameState()` **once per session** inside
  `AttachHost`, NOT per world build. (This is why money/upgrades survive a rebuild.)
- `OnSessionEnding(SessionEndReason)` → `controller.DetachSession()` + `gameState = new …`.

### 1.2 The controller — `Assets/_Prototypes/BallPitFinal/BallPitFinalController*.cs`
Partial class `BallPitFinal.BallPitFinalController` (MonoBehaviour).
- `public PitSettings settings` (field) — the live settings; `settings.colorCount`
  (int, `[Range(1,16)]`, from `PitSettings.cs`) is the color knob.
- `public static bool SessionDriven` — true under the kit; Start then waits for
  `AttachSession` instead of self-building.
- `public void AttachSession(PitSessionRequest r)` — **guards**:
  `if (sessionStarted || pending != null) { … ignored … }`. So it CANNOT be called a
  second time for the next pit without a `DetachSession` first.
- `public void DetachSession()` — full teardown: `netPit?.Dispose(); netPit = null;
  DisposePit(); mode = Offline; …`. Disposing `netPit` unregisters NGO handlers — i.e.
  **this is a NetPit restart** if followed by another `AttachSession`.
- `private IEnumerator BuildRoutine(PitSettings s, Bounds bounds, bool asAuthority, Action<bool,string> onBuilt)`
  — builds a brand-new `PitWorld` and, as authority, a brand-new `PitAuthority`
  (`authority = new PitAuthority(w);`). **Private.**
- `private void DisposePit()` — `buildEpoch++`, raises `PitWorldDisposed`, tears down
  environment, `authority?.Dispose()`, `world?.Dispose()`. **Private.**
- `OnHostBuilt(r, ok, msg)` → `netPit = NetPit.StartHost(r.Nm, network, authority);` then
  `MarkWorldReady()` (raises `PitWorldBuilt`) then `r.Ready(netPit)`. **NetPit is bound to
  `authority` HERE, once.**
- Events (View partial):
  `public event Action<PitWorld, IPitSite> PitWorldBuilt`,
  `public event Action<PitWorld> PitWorldDisposed`.
- Accessors: `public PitWorld World`, `public PitAuthority Authority`, `public NetPit Net`,
  `public bool IsBuilding`, `public bool IsPitWorldReady`, `public IPitSite ActiveSite`,
  `public Mode CurrentMode`.
- `PitSessionRequest` fields used by the adapter: `Nm, IsHost, Settings, Bounds, Restore,
  Ready, Failed, Environment, PrepareClientSettings, Site, OverviewCamera,
  ClientColliders, ClientMaxColliders, PlayerLayer`.

### 1.3 Site selection — `Assets/Game/BallPitGameplay/Venue/PitSites.cs`
- `interface IPitSiteSelector { PitSite Select(in PitSiteQuery query); }` — the doc on
  the interface states a selector not derived from `Settings` **must replicate the siteId
  to clients itself**.
- `PitSites.SelectAndActivate(IPitSiteSelector selector, IReadOnlyHostSettings settings,
  string savedSiteId, out List<PitSite> sites, out string error)` — the adapter's one
  call. Returns the active `PitSite` and calls `Activate(site, sites)`.
- `PitSites.FindAll()` returns every `PitSite` in loaded scenes (inactive included).
- `DefaultPitSiteSelector` picks the site whose `ProfileKey == settings.GetString(pitSize)`.

**Consequence for the director:** A pit PREFAB carries a `PitSite`. When the director
instantiates `Pit_Small.prefab`, a `PitSite` appears in the scene; when it destroys that
instance and instantiates `Pit_Large.prefab`, a different `PitSite` appears. So "the
active site = the campaign entry's prefab" is naturally expressed as: **only the current
entry's prefab is instantiated, so there is exactly one `PitSite` in the scene at a
time**, and a trivial selector returns it. The replicated piece is the **index** (so the
client instantiates the SAME prefab), after which selection is deterministic.

### 1.4 Drain + win — `Assets/Game/BallPitGameplay/Containers/DepositDriver.cs`
- `DepositDriver(BallPitFinalController controller, GameplayCore core, BallAttribution,
  Func<PitSite> activeSite, Action onWin)` — host-only `IPitStepListener`.
- `CheckWin()`: `if (world.Store.StateTotal(BallStore.State.Stored) != world.Store.Count) return;`
  then `won = true; onWin?.Invoke();` — fires **once**. The `onWin` callback is the
  re-route seam.

### 1.5 Gameplay session glue — `Assets/Game/BallPitGameplay/Economy/GameplaySession.cs`
- Subscribes `controller.PitWorldBuilt += OnPitWorldBuilt` / `PitWorldDisposed += OnPitWorldDisposed`.
- `OnPitWorldBuilt`: builds a **new** `GameplayCore(economyConfig, upgradeCatalog, adapter.GameState)`
  each build, bound to the **same** `adapter.GameState`. Host path constructs
  `depositDriver = new DepositDriver(controller, core, attribution, …, OnWinHost)` and
  `controller.AddStepListener(depositDriver, PitStepOrder.ColliderSources)`. Networked
  host: `SpawnGameplayNet()` (Instantiate + `Spawn(true)`); client: `BindClientNet()`.
- `OnWinHost()`: `core.SiteCompleted = true; … net.HostBroadcastWin(data)` (networked) or
  `WinResults?.Invoke(data)` (offline). `ResultsPanel` listens to `session.WinResults`.
- `OnPitWorldDisposed` → `TearDown()` despawns `GameplayNet`, removes the step listener,
  nulls `core`/`readModel`.

### 1.6 Economy persistence — `GameplayCore.cs`, `EconomySaveParticipant.cs`, `BallPitGameState.cs`
- `GameplayCore.Money` and `LevelOf/SetLevel` read/write **`adapter.GameState`**
  (`state.MoneyPot`, `state.Upgrades`) when `gameState != null` (every networked/dev-host
  path). `GameState` is per-SESSION. **Therefore a world rebuild that creates a fresh
  `GameplayCore` bound to the same `GameState` keeps money + upgrade levels with ZERO extra
  work.** Integrity counters (`SiteCompleted`, etc.) live on `GameplayCore` and ARE
  per-core; see §6 for the `SiteCompleted` reset the director must do between pits.

### 1.7 NetPit — `Assets/_Prototypes/BallPitFinal/NetPit.cs` (WIRE FORMAT FROZEN)
- `public static NetPit StartHost(NetworkManager nm, NetSettings n, PitAuthority authority)`
  — stores `A = authority`, `authority.Events = net`, registers CMM handlers +
  `nm.OnClientConnectedCallback`. Everything downstream reads `A.S`, `A.W`, `A.Store`.
- `SendHeader(Link)` reads `A.S` / `A.W.PlayBounds` / `A.W.FillChecksum`; the client's
  `OnReady` compares checksums and triggers a full resync on mismatch.
- `public void Dispose()` — unregisters handlers; clears `A.Events`.
- **There is no public method to re-point `A` or re-issue headers to already-connected
  clients.** Confirmed by grep: only `StartHost` assigns `A`.

### 1.8 Existing NGO-codegen exemplar — `GameplayNet.cs`
`sealed class GameplayNet : NetworkBehaviour`, host-spawned NetworkObject (not scene),
`NetworkVariable<long> money (Server-write, Everyone-read)`, `NetworkVariable<bool> won`,
`NetworkList<int> levels`, a `[ServerRpc(RequireOwnership=false)]` buy, `[ClientRpc]`
fan-outs, `OnNetworkSpawn/Despawn` wiring. **This is the pattern the replicated campaign
index should copy.**

### 1.9 SaveManager — `Assets/Scripts/Game/Save/SaveManager.cs`
This is the **retired Bathroom-Simulator** Steam-Cloud `PlayerSaveData` saver — it is NOT
the kit's world-save path and is unrelated to money/upgrades. The real persistence used
by the game is the kit **world save**: `BallPitGameAdapter.CaptureWorldSave()` encodes
`BallPitGameState` (money/upgrades) + registered `IBallPitSaveParticipant` blobs
(`EconomySaveParticipant`). **The campaign index must persist in the kit world save, not
in `SaveManager`.** See §7.

---

## 2. Chosen integration hook (exact, against the real adapter/controller)

The cleanest hook is to **own the pit lifecycle from a new component that the adapter
delegates to**, rather than adding campaign branches inside the adapter.

**Object model (DECIDED — resolves the Part A vs §5.4 contradiction).** `CampaignDirector`
is a **plain scene `MonoBehaviour`** (exactly what Part A step 7 tells the owner to drop in
the scene), NOT a `NetworkObject`. It is the policy brain: it instantiates/destroys pit
prefabs, installs the selector, listens for drain, and drives advances. The one piece that
must be *replicated and survive a pit rebuild* — the active entry index — lives on a small
**separate host-spawned registered network prefab `CampaignNet`** (a `sealed
NetworkBehaviour`, modeled on `GameplayNet`), carrying a single `NetworkVariable<int>
entryIndex`. The director host-spawns `CampaignNet` ONCE per session and keeps it for the
whole session (unlike `GameplayNet`, which `GameplaySession` despawns per world). This
cleanly separates "scene policy object the owner authors" (the director) from "replicated
session state" (`CampaignNet`), and makes the Part A instruction literally correct while
still giving a session-lifetime replicated index. See §5 for the full lifetime/ordering
rules and why a director-as-NetworkObject was rejected.

Concretely:

1. **Install the campaign selector in `Awake` (scene load, before `OnSessionLive`):**
   `BallPitGameAdapter` exposes `SiteSelector { get; set; }`. The director sets
   `adapter.SiteSelector = new CampaignPitSiteSelector(director)` in `Awake`,
   **replacing** `DefaultPitSiteSelector`. Because the active prefab is the only one
   instantiated, the selector returns the single live `PitSite` in `query.Sites` (see §4).
   Hook point: `adapter.SiteSelector` setter (public, already exists). This is a plain
   scene `Awake`, so it is deterministically ordered BEFORE the kit's `OnSessionLive`
   (which the kit raises after scene sync); the director being a plain MonoBehaviour is
   exactly why this ordering holds (a host-spawned NetworkObject's `OnNetworkSpawn` would
   NOT — see §5.4, Finding 5 resolution).

2. **Build the first pit (REV 4 — GATED, not in `Awake`):** the director does NOT instantiate
   the first prefab in `Awake`, because on a resume the entry to build is not knowable at
   `Awake` (the save `Restore` runs inside the first `BuildRoutine`, long after scene
   `Awake` — see §7/§10). Instead the adapter calls a director method
   `EnsureFirstPit(string savedSiteId)` from `AttachHost`/`AttachClient` **immediately
   before** `PitSites.SelectAndActivate(...)` (which is where `PitSites.FindAll()` runs). That
   is the earliest point at which the resume save's `Fill.SiteId` is in hand, and it is still
   strictly before `FindAll`, so a `PitSite` exists when selection runs. `EnsureFirstPit`
   resolves the start index (§10), instantiates that entry's prefab, and the adapter's
   existing path then builds that single live site with `AttachSession`; entry[0]'s
   `colorCount` is injected via the adapter seam in §3. See §5/§10 for the exact host/client
   ordering.

3. **Detect drain:** re-route `DepositDriver`'s `onWin`. Today `GameplaySession`
   constructs the driver with `OnWinHost`. We change `GameplaySession` (copy-before-edit)
   so that, when a `CampaignDirector` is present, the `onWin` callback goes to the
   director (`director.OnActivePitDrained()`); the director decides advance-vs-win and, on
   the final pit, calls back the **unchanged** win path. See §5.

4. **Run the advance cycle:** transition → destroy prefab + rebuild → index++ (replicated)
   → instantiate next → rebuild. The rebuild reuses `PitWorldDisposed`→`PitWorldBuilt` via
   the controller. Because `BuildRoutine`/`DisposePit` are **private** and `AttachSession`
   is single-shot, the director needs a small **additive public rebuild entry point on the
   controller** (see §4.3). It does NOT call `DetachSession` (that restarts NetPit).

Rationale for not branching inside `BallPitGameAdapter`: the adapter is the kit contract
(identity/scenes/saves/session lifecycle) and already large; the campaign is gameplay
policy. Keeping policy in `CampaignDirector` + a thin controller rebuild seam matches §0b
("build systems, not one-off scenes") and keeps the adapter's A-deliverable behavior intact
for freeplay (freeplay keeps `DefaultPitSiteSelector`).

---

## 3. Injecting `colorCount` per entry

`colorCount` is `PitSettings.colorCount` (int). On a **new game** it is set by
`BallPitSettingsMap.Apply(hs, inspectorBase, venue)` from the lobby `ColorCount` setting.
For the campaign the value comes from the entry, not the lobby.

**Chosen mechanism:** the director writes the entry's color count into the controller's
`settings.colorCount` **before** the build of that entry, and the build path reads
`settings`/the request's `Settings`. Two concrete injection points depending on path:

- **Host, entry[0] (via the adapter's normal `AttachHost`):** `AttachHost` builds
  `settings = BallPitSettingsMap.Apply(ctx.Settings, controller.settings, VenueConfig)`.
  The SINGLE effective seam is a one-line adapter override applied **immediately after**
  that `Apply(...)` call: **copy-before-edit `BallPitGameAdapter.AttachHost`** to apply
  `director?.ApplyEntryColors(settings)` right after `Apply` (it sets
  `settings.colorCount = director.CurrentEntry.colorCount` when a director is present).
  Traced: `BallPitSettingsMap.Apply` is the ONLY writer of `colorCount` in the host build
  path — line 80 of `BallPitSettingsMap.cs`, `s.colorCount = hs.GetInt(ColorCount)` — and it
  OVERWRITES whatever `controller.settings.colorCount` held. So setting
  `controller.settings.colorCount` BEFORE `Apply` would be dead for entry[0] (Apply clobbers
  it); only the post-`Apply` `ApplyEntryColors(settings)` seam takes effect. Nothing else in
  `AttachHost` re-reads `colorCount` from the lobby after `Apply`, so overriding there (before
  `controller.AttachSession`) is the correct, race-free seam. The override is clamped to the
  supported range per §12. **The `ApplyEntryColors` call goes ONLY in the new-game (`else`)
  branch beside `BallPitSettingsMap.Apply`.** On a resume the adapter takes
  `settings = save.Fill.ApplyTo(controller.settings)` (traced, `AttachHost` resume branch), so
  the saved pit's own `colorCount` is authoritative and must NOT be overridden by the entry.

- **Host, entry[i>0] (via the director's rebuild seam):** the director passes the new
  `PitSettings` (cloned from the current, with `colorCount`, `pitX/Y/Z`, `ballDiameter`,
  `seed` set from the new entry's prefab `PitSite`/profile) directly into `RebuildActivePit`
  (§4.3), which feeds it to `BuildRoutine` without going through `BallPitSettingsMap.Apply`.
  This is the only path where setting `colorCount` on the `PitSettings` instance is
  load-bearing (no `Apply` clobbers it here), so the field-set belongs here — NOT on the
  entry[0] path above. No adapter involvement.

- **Client (both paths) — VERIFIED FACT, the header is authoritative.** The NetPit header
  DOES carry `colorCount` and the pit dims. Traced `NetPit.SendHeader`:
  `w.WriteValueSafe(s.ballDiameter); w.WriteValueSafe(s.colorCount); w.WriteValueSafe(s.seed);`
  (and `s.pitX/pitY/pitZ` immediately above), and the client's `OnHeader` reads a full
  `PitSettings s` (`r.ReadValueSafe(out s.colorCount)` etc.) and exposes it as
  `PendingSettings`. So on every build the client receives the authoritative `colorCount`
  + dims from the host and builds its local `PitWorld` from `PendingSettings` — the client
  NEVER computes `colorCount` itself for the ball sim. **The header is authoritative for
  the ball-sim `colorCount`/dims; the replicated `entryIndex` is authoritative only for
  which PREFAB (art/`PitSite`) the client instantiates.** The two must agree by
  construction — the host's build `PitSettings.colorCount` is forced to `entry.colorCount`
  (entry[0] via `ApplyEntryColors(settings)` after `Apply`; entry[i>0] via the cloned
  `PitSettings` passed into `RebuildActivePit`), and the host then sends that same
  `colorCount` in the header, so the client's `PendingSettings.colorCount` equals
  `entries[entryIndex].colorCount` automatically. The client's palette/colour-blind display
  is a separate VISUAL concern driven by `PaletteApplier` off that same `colorCount`.
  **Invariant:** if a client ever instantiates the prefab for `entryIndex` but the header
  reports a different `colorCount`, the HEADER wins for the sim (the client already builds
  from `PendingSettings`); the prefab mismatch would only be an art/dims discrepancy, which
  the §12 editor validation guards against by requiring each entry's prefab `PitSite` dims
  to be internally consistent.

---

## 4. The pit-rebuild seam (the hard part)

### 4.1 Why `DetachSession`/`AttachSession` cannot be the between-pit path
`AttachSession` is single-shot (`sessionStarted || pending != null` guard). The only way
to call it again is `DetachSession`, which does `netPit.Dispose()` — unregistering NGO
message handlers and clearing `authority.Events`. A following `AttachSession` would call
`NetPit.StartHost` again. That is a **NetPit restart**, which §1b/§9/§10 forbid for the
in-session shipment switch. So this path is rejected.

### 4.2 Why a naive "DisposePit + BuildRoutine" also isn't enough alone
`BuildRoutine` as authority creates a **new** `PitAuthority`. But `NetPit.A` still points
at the OLD authority (set once in `StartHost`). After a bare rebuild, NetPit would stream
the old (disposed) authority's state and never tell clients to rebuild. The wire-format is
frozen, but **re-pointing `A` and re-issuing headers is additive logic, not a wire change**.

### 4.3 Chosen approach: an additive controller `RebuildActivePit` + an additive NetPit `RebindAuthority` (host) + `ResetClientWorldForRebuild` (client)

**The naive "just re-send the header" does NOT work on the client — the existing guard
blocks it.** Traced `NetPit.OnHeader`:
```csharp
private void OnHeader(ulong sender, FastBufferReader r)
{
    if (HeaderPending || CW != null) return; // duplicate from the Hello re-send; the first one wins
    ...
}
```
`CW` (the client's `PitWorld`) is set in `AttachClientWorld` (`CW = world; R = replica;`)
and is **never nulled anywhere in NetPit** — `Dispose()` clears handlers and `A.Events` but
does not touch `CW`/`R`/`Synced`/`HeaderPending`. Verified by reading the whole file. So
once a client has synced its first pit (`CW != null`), a re-sent header is silently dropped
and the client never rebuilds. The fix therefore needs TWO additive seams in NetPit (host
re-point + re-header, AND a client-side world reset), plus an explicit host→client signal so
the client resets before the new header arrives. The `entryIndex.OnValueChanged` callback on
`CampaignNet` is that signal (it already must fire on the client to swap the prefab, §5.3).

**Host:** add, copy-before-edit, to `BallPitFinalController` a **new public method** (host
only), in a NEW partial file `BallPitFinalController.Campaign.cs`:

```
public void RebuildActivePit(PitSettings newSettings, Bounds newBounds, IPitEnvironment newEnv,
                             IPitSite newSite, Camera newOverview, Action<bool,string> onBuilt)
```

It must, in order:
1. set `settings = newSettings; Environment = newEnv; activeSite = newSite; overviewCamera = newOverview;`
   (`settings = newSettings` mirrors `BeginHost`'s `settings = r.Settings; // HUD and tools use
   the session values` — traced `BallPitFinalController.Session.cs` line 148 — so
   `controller.settings` tracks the live pit after a rebuild instead of staying at entry[0]'s
   values; `settings` is a public field of the same partial class, so the new partial can
   assign it.)
2. `StartCoroutine(BuildRoutine(newSettings, newBounds, asAuthority:true, onBuilt:(ok,msg)=>{…}))`.
   `BuildRoutine` already begins with `DisposePit()` (raises `PitWorldDisposed` for the old
   world — `GameplaySession.TearDown` runs, despawning the per-world `GameplayNet`), builds
   the new `PitWorld` + new `PitAuthority`, then in the `onBuilt` success branch:
3. **re-bind NetPit to the new authority** via a NEW additive
   `NetPit.RebindAuthority(PitAuthority newAuthority)` (host-only; details below), then
4. `MarkWorldReady()` (raises `PitWorldBuilt` → `GameplaySession.OnPitWorldBuilt` rebuilds
   `GameplayCore` bound to the same `GameState`, re-spawns the per-world `GameplayNet`,
   re-adds the deposit driver) and invoke `onBuilt(true,null)`.

**`NetPit.RebindAuthority(PitAuthority newAuthority)` — concrete host checklist** (reuses
the existing Header/Ready/Rel/Snap messages and `Protocol = 3`; NO new message ids, NO wire
change). In order:
1. `A = newAuthority; newAuthority.Events = net;` — re-point the single authority reference
   (the only other place `A` is assigned is `StartHost`; verified by reading the file).
2. **Reset the host live-event batch** so no old-world event bytes leak into the new world:
   `evLen = 0; evCount = 0; evBaseTick = 0; sendAccum = 0;` (these are the host batch fields
   feeding `FlushEvents`).
3. **Per connected link**, reset the per-link world state to the NEW world:
   - `link.streaming = false;` (so the client's next `OnReady` re-queues a sync)
   - `link.live = false;` (stop snapshots until the new sync completes)
   - `link.lastSent = new int[A.Store.Capacity];` — REALLOCATE to the NEW capacity (it was
     sized to the old `A.Store.Capacity` in `OnClientConnected`; a smaller new capacity
     would otherwise index out of range in `SendSnapshots`).
   - `link.syncDoneMarker = null;`
   - `link.outq.Clear();` — drop any queued old-world sync/live batches.
   - `SendHeader(link);` — re-send the header (now reading the NEW `A`), with
     `forceFull:true` carried so the client's subsequent `OnReady` triggers a FULL resync
     regardless of checksum (see step below + Finding 3 resolution). **This direct
     `SendHeader` is only a BEST-EFFORT FAST PATH (REV 4).** The client's `OnHeader` drops it
     while `CW != null`, i.e. whenever it reaches the client before that client has run
     `ResetClientWorldForRebuild()`. The AUTHORITATIVE race-closer is the Hello-driven re-send
     (`ResetClientWorldForRebuild` ends with `SendHello()`, host `OnHello` then calls
     `SendHeader(link)` because step 3 left `link.streaming == false`) — see "Who calls
     `ResetClientWorldForRebuild`" below. An implementer must NOT treat this direct send as
     sufficient and skip the Hello wiring.
4. Because `OnReady`'s existing `bool full = checksum != A.W.FillChecksum;` is NOT reliable
   between pits (two entries with equal dims+seed+colorCount produce equal checksums), add a
   per-link `bool forceFullNextReady` flag set to `true` in step 3 and OR it into the `full`
   decision in `OnReady`: `bool full = forceFullNextReady || checksum != A.W.FillChecksum;`
   then clear it. This guarantees a full resync on the first `OnReady` after every rebind,
   driven by the host KNOWING it advanced — never by a fill difference. This is a strictly
   additive field + one extra `||` term; no wire change (the Ready message is unchanged).

**Client:** the client's `OnHeader` guard (`CW != null`) must be cleared BEFORE the re-sent
header arrives. Add a NEW additive `NetPit.ResetClientWorldForRebuild()` (client-only) that
clears exactly the per-world client fields so the next header is accepted and a clean
rebuild runs, and then **re-sends `Hello` as its FINAL step (REV 4)**:
```
CW = null; R = null; Synced = false; HeaderPending = false;
events.Clear();
for (int p = 0; p < heldBy.Length; p++) heldBy[p] = -1;
haveClock = false; clockOffset = 0; hostChecksum = 0;
PendingSettings = null; PendingBounds = default;
SendHello();   // FINAL step — private instance method of NetPit, reachable only from inside NetPit
```
(These are every client-side per-world field I found by reading NetPit: `CW`, `R`, `Synced`,
`HeaderPending`, the `events` queue, `heldBy[]`, `haveClock`/`clockOffset`, `hostChecksum`,
`PendingSettings`, `PendingBounds`. `LocalPlayer` is deliberately NOT cleared — the player
slot is stable across a rebuild.) This method is the ONE place `CW` is nulled, and `SendHello()`
is its last statement. `SendHello()` is `private void SendHello()` (traced, `NetPit.cs`; it
writes `Protocol` and sends `MsgHello` to `NetworkManager.ServerClientId` as `ReliableSequenced`
via `NM.CustomMessagingManager`), so it is only callable from inside `NetPit` — which is
exactly why the re-send lives in `ResetClientWorldForRebuild` and nowhere else. Client-only: the
method begins with `if (IsHost) return;` (`public readonly bool IsHost`, traced) so it can never
fire a Hello from the host instance.

**The thin client-side controller dispose hook (§5.3 step 4) — full signature + body.** Before
the client calls `net.ResetClientWorldForRebuild()` it must tear down its current client
`PitWorld` so the controller's `Update` rebuild trigger fires. That trigger is guarded by
`world == null`:
```csharp
if (netPit.HeaderPending && !building && world == null && !clientBuildFailed) StartClientBuild();
```
so the hook MUST leave `world == null` (and should raise `PitWorldDisposed` so
`GameplaySession.TearDown` runs on the client and the per-world `GameplayNet` binding is
dropped). The private `DisposePit()` already does exactly this — traced in
`BallPitFinalController.cs`:
```csharp
private void DisposePit()
{
    buildEpoch++; building = false;
    if (pitWorldReady) { pitWorldReady = false; RaisePitWorldDisposed(world); } // → GameplaySession.TearDown
    if (builtEnvironment != null) { builtEnvironment.Teardown(); builtEnvironment = null; }
    DevDisposeLayout();
    bench.Stop();
    authority?.Dispose(); authority = null;   // no-op on a client (authority is null there)
    replica = null;
    gpu?.Dispose(); gpu = null;
    world?.Dispose(); world = null;           // leaves world == null → Update's rebuild guard fires
    dynamicCount = 0;
}
```
So the new partial file `BallPitFinalController.Campaign.cs` adds a thin **client-only** public
wrapper that reuses it (same class, so the private method is reachable):
```csharp
// BallPitFinalController.Campaign.cs — client only. Called by the director's
// CampaignNet.entryIndex.OnValueChanged path BEFORE net.ResetClientWorldForRebuild(), so the
// re-sent header is accepted and Update's (world == null) rebuild guard fires.
public void DisposeClientPitForRebuild()
{
    // reuses the private teardown: raises PitWorldDisposed (GameplaySession.TearDown runs),
    // disposes the client PitWorld/replica/gpu and leaves world == null.
    // authority?.Dispose() inside is a no-op on the client (authority == null there).
    DisposePit();
}
```
This is the ONLY controller-side action needed in §5.3 step 4; it must leave `world == null`
(verified above — `DisposePit` sets `world = null`), runs on the client only, and is counted in
the §14 bounded-set smell check. A hook that disposed only `netPit`'s `CW` without nulling
`world` would silently break the client rebuild, which is why the body is pinned to
`DisposePit()` and nothing else.

**Who calls `ResetClientWorldForRebuild` and in what order (the host→client signal):** the
client subscribes to `CampaignNet.entryIndex.OnValueChanged`. When the host advances (writes
`entryIndex.Value = i+1`), on each client the callback runs and (in order): plays the local
transition (visual), destroys the old entry prefab + instantiates
`entries[newIndex].pitPrefab`, calls `controller.DisposeClientPitForRebuild()`
(the client-only additive hook specified earlier in this section — `=> DisposePit()`, leaves
`world == null`), then calls `net.ResetClientWorldForRebuild()`. After that the host's re-sent header is accepted,
`HeaderPending` goes true, and the client's existing `Update` path
(`HeaderPending → BuildRoutine(asAuthority:false) → AttachClientWorld → OnReady`) rebuilds
and the forced full resync repopulates the balls. Ordering note: the host should send the
re-header only after clients have had the chance to reset; since `OnValueChanged` for the
index is a reliable `NetworkVariable` update and the re-header is a reliable named message,
the simplest safe ordering is **host writes `entryIndex` first, then performs the local
rebuild + `RebindAuthority`/`SendHeader`** so the index change is in flight before (or
concurrent with) the header. If a header arrives at a client before its reset ran, the guard
drops it; to make this race-free, `RebindAuthority` also sets the per-link `forceFullNextReady`
and the client, upon finishing its reset, sends `Hello` again (the existing protocol-3
mechanism) which makes the host `OnHello` re-send the header to that link once it is no
longer `streaming`. **The trigger is pinned (REV 4): `NetPit.ResetClientWorldForRebuild()`
itself calls `SendHello()` as its FINAL step. There is no "or the director triggers it"
alternative** — the director is an external `MonoBehaviour` and cannot reach the private
`SendHello()`/`OnHello` (traced: both are `private` in `NetPit.cs`), so that branch was
infeasible and is deleted. **This reuses the existing Hello re-send to close the ordering race
— no new message, wire format unchanged.**

*Why the two sends together are race-free (traced `OnHello`: `if (links.TryGetValue(sender,
out var link) && !link.streaming) SendHeader(link);`).* The host writes `entryIndex`, plays
the transition and rebuilds; the client resets on the index callback and sends Hello. Two
orderings exist:
- **Client Hello reaches the host BEFORE `RebindAuthority` ran:** `link.streaming` is still
  `true` (old world) so `OnHello` ignores it — but then `RebindAuthority` step 3's direct
  `SendHeader` is sent strictly later than the client's reset (the client reset BEFORE sending
  that Hello), so the client has `CW == null` and ACCEPTS it. The fast path wins.
- **Client Hello reaches the host AFTER `RebindAuthority` ran:** step 3 already set
  `link.streaming = false`, so `OnHello` re-sends the header; the client reset is already done,
  so it is accepted. Here the direct send from step 3 may have been dropped by the client's
  `CW != null` guard — the Hello-driven send is what recovers it. This is the authoritative
  closer.

A duplicate (both sends reach a reset client) is harmless: `OnHeader`'s `HeaderPending ||
CW != null` guard keeps the first and drops the second, and `forceFullNextReady` is cleared
only in `OnReady`, which runs once. Once the client has sent `Ready` the host sets
`link.streaming = true` and any late Hello is ignored.
[owner in-editor check]: confirm `ResetClientWorldForRebuild` ends with `SendHello()` and that
the host re-sends the header on the resulting `OnHello` (watch for the `[BallPitNet] header:`
client log line after an advance, and for no client left stuck at "waiting for the host's
world").

**Honest risk flag — this is the §1b KEY INTEGRATION RISK and remains the one piece only an
in-editor multi-peer test can fully prove.** The design above is now complete against the
real client guard (it was not before). [owner in-editor check] items specific to the rebuild:
- The full-resync forced path actually repopulates the client world (watch for the
  "synced, ledger matches host" log after a rebuild, not a ledger-mismatch error).
- NGO client-connect DURING a rebuild window: a client joining mid-rebuild gets the new
  header (it will — `SendHeader` reads `A`, now the new world — and `OnClientConnected` sizes
  `lastSent` to the new `A.Store.Capacity`).
- Host local player + client player objects (NGO `NetworkObject`s spawned by
  `PlayerSpawner`) are NOT re-spawned by a pit rebuild and persist across it; player slots
  are NetPit slots 0–3, independent of the pit world, and `LocalPlayer` is preserved by the
  reset. Confirm players don't need re-slotting. [owner in-editor check]

### 4.4 Fallback if `RebindAuthority` proves unsafe
If in-editor testing shows re-headering connected clients corrupts state, the documented
fallback is the **transition-covered full session bounce is NOT allowed** (NetPit restart
is forbidden). Instead, the lower-risk fallback is: keep NetPit bound to a **stable
authority that owns a reusable world buffer** and have the rebuild REFILL the existing
`PitWorld`/`PitAuthority` in place (new ball count/colors) rather than constructing new
instances — turning "rebuild" into "re-fill + resync". This is a larger controller change
and is noted as a **Plan B** for the implementer only if Plan A (§4.3) fails testing. Do
NOT build Plan B pre-emptively.

### 4.5 One pit of balls at a time — confirmed
Only the active entry's prefab is instantiated; the previous is `Destroy`ed before/at the
rebuild. `BuildRoutine` disposes the old `PitWorld` (`world?.Dispose()`) before the new one
exists past the yield, and the GPU renderer/authority are disposed in `DisposePit`. So at
most one live `PitWorld` + one set of balls exists. **Confirmed by trace** of `DisposePit`
+ `BuildRoutine` ordering.

---

## 5. Offline vs networked paths

### 5.1 Component shape (DECIDED — two objects, resolving Findings 4 & 8)
- **`CampaignDirector : MonoBehaviour`** (plain scene object, matches Part A step 7). It is
  the policy brain and holds NO `NetworkVariable`. It keeps a plain `int localIndex` that is
  the source of truth OFFLINE and a cached mirror of the replicated index when networked.
  It reads/writes the replicated index only through `CampaignNet` (below) when one exists.
- **`CampaignNet : NetworkBehaviour`** (sealed, host-spawned registered network prefab,
  modeled exactly on `GameplayNet`). Holds the single `NetworkVariable<int> entryIndex`
  (`Everyone`-read, `Server`-write) and a `static CampaignNet Instance` set in
  `OnNetworkSpawn` / cleared in `OnNetworkDespawn` (same pattern as `GameplayNet.Instance`).
  The host writes `entryIndex.Value` on advance; clients subscribe to
  `entryIndex.OnValueChanged`. Because `entryIndex.Value` is only ever touched on a spawned
  `NetworkBehaviour`, the NGO "read/write before spawn throws" hazard cannot occur — the
  director never touches `entryIndex` directly; it goes through `CampaignNet.Instance` which
  is null until spawned. **Hard invariant:** every `entryIndex.Value` access is inside
  `CampaignNet` and therefore always post-spawn; offline there is no `CampaignNet` at all and
  the director uses `localIndex` exclusively. This removes the "unspawned NetworkBehaviour
  holding a NetworkVariable" question entirely (Finding 8) — the NetworkBehaviour only ever
  exists spawned.
- **Why two objects, not one `CampaignDirector : NetworkBehaviour`.** A single networked
  director was considered and **rejected** for two concrete, traced reasons:
  (a) Part A step 7 instructs the owner to place a plain `CampaignDirector` object in the
  scene; a host-spawned NetworkObject cannot be a hand-placed scene object the owner wires in
  the inspector the same way (it would have to be a registered prefab, contradicting Part A),
  and (b) ORDERING (Finding 5): a host-spawned NetworkObject's `OnNetworkSpawn` runs AFTER
  the kit's `OnSessionLive`, so a networked director could NOT instantiate entry[0] before
  `PitSites.FindAll()` runs in `AttachHost` (and the director also could not exist yet for the
  adapter to call into). A plain scene MonoBehaviour's `Awake` runs on
  scene load, BEFORE `OnSessionLive` — which is exactly when `CampaignDirector.Instance`
  must exist and the selector must be installed, so the adapter's pre-`FindAll`
  `EnsureFirstPit` call (§2 item 2) always finds a live director. Splitting policy (scene MonoBehaviour) from replicated state
  (host-spawned `CampaignNet`) satisfies both. The policy/walk logic lives once in the
  director (no duplication), mirroring how `GameplaySession` (a `MonoBehaviour`) owns the
  gameplay loop while delegating replication to the host-spawned `GameplayNet`.

### 5.2 Authority & who advances
The HOST (server) is the only peer that runs drain detection (`DepositDriver` is host-only)
and the only writer of `entryIndex` (on `CampaignNet`). On advance the host: writes
`CampaignNet.Instance.SetIndex(i+1)` (a host-only setter that sets `entryIndex.Value = i+1`,
replicating), then runs the transition + rebuild. Offline there is no `CampaignNet`; the
single peer is the authority and uses the director's `localIndex`. The director keeps
`localIndex` in sync with the replicated value on every peer (set it from
`entryIndex.OnValueChanged` when networked, or directly offline), so the rest of the director
reads one field regardless of mode.

### 5.3 How the client follows without owning logic
The client never runs `DepositDriver`, never decides to advance. It subscribes to
`CampaignNet.entryIndex.OnValueChanged`. When the index changes (host advanced), the client,
in order:
1. updates `director.localIndex = newIndex`,
2. plays the SAME `IPitTransition` locally (visual only),
3. destroys its current entry prefab instance, instantiates
   `definition.entries[newIndex].pitPrefab` (so its local `PitSite` dims/art match),
4. disposes its old client `PitWorld` by calling the additive client-only controller hook
   `controller.DisposeClientPitForRebuild()` (specified in §4.3 — it reuses the private
   `DisposePit()`, raises `PitWorldDisposed`, and leaves `world == null` so the controller's
   `Update` rebuild guard fires), then calls `net.ResetClientWorldForRebuild()` (§4.3) so the
   `OnHeader` guard is cleared,
5. the controller's existing header-driven client rebuild (§4.3, driven by the re-sent
   header after the client's `Hello` re-send) resyncs the balls with a forced full resync.

The replicated index is the one piece of campaign state on the wire — satisfying the
steering requirement that "a selector not derived from lobby settings MUST replicate the
active index/siteId to clients." The siteId itself need not be separately replicated because
`entries[index].pitPrefab` deterministically yields the prefab (hence its `PitSite.SiteId`)
on every peer; the index IS the replicated siteId by proxy. [owner in-editor check]:
confirm host and client resolve the same prefab→PitSite→SiteId for a given index (they load
the same `CampaignDefinition` asset; asset references are deterministic across peers).

### 5.4 Channel choice: a dedicated session-lifetime `CampaignNet` (DECIDED)
`GameplayNet` is **spawned per world build and despawned on teardown** (§1.5), so it is
destroyed during every rebuild — unsuitable to hold a value that must survive the rebuild.
The replicated index therefore lives on a dedicated object. Two options were considered:

- **(A) Dedicated host-spawned registered network prefab `CampaignNet`** (spawned once at
  session-live timing, despawned at session end), carrying `entryIndex`. Survives rebuilds.
- **(B) A scene `NetworkObject` carrying `entryIndex`.** Rejected: scene NetworkObjects in
  this kit are not how `GameplayNet` (the proven exemplar) works; host-spawning a registered
  prefab is the traced, working pattern. A scene NetworkObject would also re-introduce the
  Part A "plain scene object" confusion (the director stays plain; the networked piece is a
  prefab the owner does NOT hand-place).

**Chosen: (A).** The DIRECTOR (a plain scene MonoBehaviour) host-spawns `CampaignNet` once,
not per world. Exact timing and ordering (resolving Finding 5 & 7):
- The director sets `CampaignDirector.Instance` and installs the selector in `Awake` (before
  `OnSessionLive`), as §2 requires; the FIRST prefab is instantiated by `EnsureFirstPit`,
  called by the adapter just before `PitSites.FindAll()` (§2 item 2, §10) — not in `Awake`.
- The director host-spawns `CampaignNet` the FIRST time it observes the NGO session is live
  AND it is the server. It detects session-live by the same signal `GameplaySession` uses:
  it spawns inside its own guarded check `NetworkManager.Singleton != null &&
  NetworkManager.Singleton.IsServer && NetworkManager.Singleton.IsListening`, driven from a
  one-shot check in `Update`/`OnEnable` (poll until listening, then spawn once and set a
  `netSpawned` latch). This mirrors `GameplaySession.SpawnGameplayNet` (`Instantiate(prefab)`
  → `GetComponent<CampaignNet>()` → `no.Spawn(true)`), but gated to ONCE PER SESSION, not per
  world. `CampaignNet.OnNetworkSpawn` sets `CampaignNet.Instance`; immediately after spawn the
  host sets `entryIndex.Value = director.localIndex` — 0 on a fresh run, or the index
  `EnsureFirstPit` already resolved for the resumed pit (§7/§10). No pit swap is involved: the
  prefab that is already built IS entry `localIndex`.
- **Timing guarantee vs the first drain (Finding 7):** `CampaignNet` is spawned at
  session-live, which is many seconds/minutes before any pit can be fully drained (a drain
  requires selling every ball by hand/tool). So `CampaignNet.Instance` is reliably non-null
  before the first `OnActivePitDrained`. The §6 re-route reads `CampaignNet.Instance` LAZILY
  at drain time (not captured at `DepositDriver` construction), so even the degenerate
  tiny-pool case where the first `OnPitWorldBuilt` runs inside `AttachHost` before
  `CampaignNet` spawns is safe: the driver's callback resolves `Instance`/`director` at the
  moment of drain, not at build. The documented degrade: if `Instance`/director is somehow
  still null at drain (spawn failed), the callback falls through to `OnWinHost` (a single-pit
  "win"), logged. [owner in-editor check]: confirm `CampaignNet` spawns before the first pit
  is drainable (it always will in practice).
- The first pit is instantiated by `EnsureFirstPit` BEFORE `CampaignNet` spawns — that is fine,
  because it does not need the replicated index on the host: a fresh run starts at 0, and a
  resume derives the entry from the save's `Fill.SiteId` (known in `AttachHost` before
  `FindAll`; NOT from the save participant, whose `Restore` runs later inside the first build
  — §7/§10). The replicated index only matters from the FIRST advance onward (and for a
  late-joining client, §10 step 4), by which time `CampaignNet` is long since spawned.

### 5.5 MultiplayerKit/NGO seams used (public API only)
- `BallPitGameAdapter.SiteSelector` (set) — install the campaign selector.
- `BallPitGameAdapter.GameState` (read) — the persisted economy store (untouched by the
  director except to persist the index alongside it, §7).
- `BallPitKitInstaller.Instance.Adapter` — how `GameplaySession` already finds the adapter;
  the director uses the same accessor.
- `NetworkManager.Singleton` / `IsServer` / `IsListening` / `NetworkObject.Spawn(true)` /
  `Despawn(true)` — same calls `GameplaySession.SpawnGameplayNet`/`TearDown` make (the
  director host-spawns `CampaignNet` once per session, despawns at session end).
- `NetworkVariable<int>` (+ `OnValueChanged`) — same construct as `GameplayNet.money`; lives
  on `CampaignNet`, not the director.
- Controller public API: `PitWorldBuilt`, `PitWorldDisposed`, `Authority`, `Net`, `World`,
  `IsPitWorldReady`, `ActiveSite`, plus the NEW additive `RebuildActivePit` (§4.3) and the
  NEW additive client-only `DisposeClientPitForRebuild()` (§4.3, used in §5.3 step 4).
- Director: the NEW public `CampaignDirector.EnsureFirstPit(string savedSiteId)` (§2 item 2,
  §10), called by the adapter just before `PitSites.SelectAndActivate`.
- NetPit: the NEW additive host `RebindAuthority(PitAuthority)` and client
  `ResetClientWorldForRebuild()` (§4.3; ends with the private `SendHello()`), plus a per-link
  `forceFullNextReady` field. **No
  wire change** (reuses Header/Ready/Rel/Snap/Hello and `Protocol = 3`).

---

## 6. Re-routing the drain signal (and keeping the final win unchanged)

Today: `GameplaySession.OnPitWorldBuilt` does
`depositDriver = new DepositDriver(controller, core, attribution, …, OnWinHost)`.
`OnWinHost` sets `core.SiteCompleted=true`, builds `ResultsData`, broadcasts via
`GameplayNet.HostBroadcastWin` (→ `ResultsPanel`).

Change (copy-before-edit `GameplaySession.cs`): pass a callback that consults the director
**lazily, at drain time** (NOT captured at `DepositDriver` construction) — this is the key
to Finding 7's timing. `CampaignDirector.Instance` is a static set in the plain scene
MonoBehaviour's `Awake` (so it is non-null from scene load on every peer that has a director;
it does NOT depend on any NGO spawn), and `CampaignNet.Instance` (the replicated index
carrier) is resolved only when the director actually needs to write/read the index:

```
Action onPitDrained = () =>
{
    var dir = CampaignDirector.Instance;      // null offline-without-campaign OR freeplay (no director in scene)
    if (dir != null && dir.HasMorePits)  dir.OnActivePitDrained();   // advance, NO win
    else                                 OnWinHost();                // final pit OR no campaign → EXISTING win path
};
depositDriver = new DepositDriver(controller, core, attribution, () => controller.ActiveSite as PitSite, onPitDrained);
```

**Timing invariant (Finding 7):** the lambda resolves `CampaignDirector.Instance` at the
moment of drain, never at construction. Even if the first `OnPitWorldBuilt` runs inside
`AttachHost` (the adapter can build the first world + raise `PitWorldBuilt` synchronously on
the FCC path) before `CampaignNet` has spawned, the `DepositDriver` captures only the lambda,
not a resolved director — so the first pit cannot "win" prematurely as long as a director is
in the scene. `CampaignDirector.Instance` is set in the director's `Awake` (scene load), i.e.
BEFORE any `OnPitWorldBuilt`, so it is reliably non-null at the first drain whenever a
campaign is present. `dir.OnActivePitDrained()` then checks `CampaignNet.Instance`/mode to
read/advance the index; `CampaignNet` is guaranteed spawned long before any drain (§5.4). The
documented degrade: if `CampaignDirector.Instance` is null (pure freeplay, no director in the
scene) OR the director has no more pits, the callback falls through to `OnWinHost` — the
original single-pit "win", exactly as today.

- **Non-final pit:** `dir.OnActivePitDrained()` runs the advance cycle (§4). It must NOT
  call `OnWinHost` and must NOT set `core.SiteCompleted` permanently for the run — BUT note
  `SiteCompleted` is a per-core flag used only for `requiresSiteCompleted` prereqs; since a
  fresh `GameplayCore` is built on the next `PitWorldBuilt`, `SiteCompleted` naturally resets
  to false for the new pit. That is the correct per-pit semantics. **Confirmed by trace**
  (`GameplayCore` is new per build; `SiteCompleted` defaults false; only `EconomySaveParticipant.Restore`
  could set it, and only on a resume).
- **Final pit:** `OnWinHost()` runs exactly as today → results panel, `core.SiteCompleted=true`,
  save marked completed. **The existing win/results path is untouched** — the director only
  decides whether to reach it.

`CampaignDirector.HasMorePits` = `CurrentIndex < definition.entries.Count - 1`.
Offline with NO campaign (pure freeplay/dev): `CampaignDirector.Instance == null`, so the
original `OnWinHost` behavior is preserved verbatim (freeplay still "wins" on drain).

---

## 7. Persisting the campaign index

The kit world save is the real persistence (money/upgrades go through
`BallPitGameState` + `IBallPitSaveParticipant` blobs). The campaign index should ride the
**same** mechanism so a resume returns to the right pit. Chosen: a new
`IBallPitSaveParticipant` (`CampaignSaveParticipant`, copying the `IBallPitSaveParticipant` SHAPE of
`EconomySaveParticipant`) that captures/restores a single versioned `int entryIndex` (+
version byte). It is registered on the host via `adapter.RegisterSaveParticipant(...)` — the
same public API `GameplaySession` uses — which keeps the index inside the SAME world-save
blob as money/upgrades, so they can never desync on resume.

**Registration LIFETIME differs from `EconomySaveParticipant` — do NOT copy its per-world
registration.** Traced `GameplaySession.cs`: `EconomySaveParticipant` is
`new`-ed and registered INSIDE `OnPitWorldBuilt` (lines 121–122, bound to the fresh per-world
`core`) and UNregistered in `TearDown` (lines 192–194) — i.e. re-registered on EVERY world
build/rebuild. The campaign index must NOT follow that per-world cycle: it carries
SESSION-scoped state that must survive every rebuild and be available to `CaptureWorldSave`
at any time. So **`CampaignSaveParticipant` is registered ONCE per session by the director,
host-side, inside `EnsureFirstPit`** (REV 4 — §2 item 2/§10: the adapter calls it in
`AttachHost` before `FindAll`, which is before the first build's `Restore`) behind a
`participantRegistered` latch, and UNregistered once at session end (`OnDestroy`/
`OnDisable` of the director), NOT inside the per-world `OnPitWorldBuilt`/`TearDown` cycle.
*Why not the `CampaignNet` spawn moment (rev 3 said so):* traced
`BallPitWorldRestorer.RestoreGame` — each saved blob is handed to the participant found BY TAG
at restore time (`FindParticipant(participants, tag)`); with no participant registered yet the
blob is parked in `game.UnclaimedExtensions` and never delivered. The `CampaignNet` spawn poll
runs from `Update` with no ordering guarantee against the controller's `BeginHost` →
`BuildRoutine` → `OnHostBuilt` → `r.Restore` chain, so the participant must be registered
earlier, at the deterministic pre-`AttachSession` point. (Also: the adapter's `participants`
list is NOT cleared by `ResetSession` — traced — so the director's explicit unregister at
teardown is required, else the next session's `RegisterSaveParticipant` throws
`Duplicate save participant tag`.) This keeps the captured index stable across the
dispose→rebuild the participant must outlive.
It carries the director's current index (read from `CampaignNet.Instance`/`localIndex`), not
a per-`core` value, so it does not need rebinding on a rebuild the way the economy
participant (bound to the fresh `core`) does.

- `SaveManager.cs` is NOT used (it is the retired Bathroom-Sim Steam saver; §1.9).
- **RESUME ordering (REV 4 — corrected; the rev-3 "restored index is available before the
  director picks the first prefab in `Awake`" claim was FALSE and is deleted).** Traced:
  the participant `Restore` does NOT run before scene `Awake`. It runs inside the controller's
  first `BuildRoutine`: `BallPitFinalController.Session.cs` `BeginHost` →
  `BuildRoutine(..., onBuilt: OnHostBuilt)` → `OnHostBuilt` → `r.Restore?.Invoke(world,
  authority)` (= the adapter's `RunRestore` → `BallPitWorldRestorer.Apply(..., participants)` →
  `RestoreGame` → `p.Restore(blob)`), which then runs `NetPit.StartHost`. That is strictly
  AFTER scene `Awake` AND after `AttachHost`'s `PitSites.FindAll()`. So the restored index
  cannot decide which prefab to instantiate first.
  **Chosen mechanism (ONE): (a) GATE the first instantiate behind the earliest point the
  resumed pit is known — and that source is the save's `Fill.SiteId`, not the participant.**
  `AttachHost` already holds the resolved save (`ResolveSave(context.ResumeSave)`, with
  `save.Fill.SiteId`) before it calls `PitSites.SelectAndActivate(SiteSelector, …,
  resume ? save.Fill.SiteId : null, …)`. The adapter seam calls
  `CampaignDirector.Instance?.EnsureFirstPit(resume ? save.Fill.SiteId : null)` on the line
  directly before that call (§2 item 2, §10). `EnsureFirstPit` maps `savedSiteId` to the entry
  whose `pitPrefab.GetComponentInChildren<PitSite>(true).SiteId` equals it (read off the prefab
  ASSET, no instantiate needed for the lookup) and instantiates THAT prefab, so the resumed
  host's `FindAll`, `ResumeSiteMismatch` and `Restore` all see the correct site.
  **Why not option (b) (always build entry[0] first, then swap via `entryIndex.OnValueChanged`)
  — rejected on a trace, not preference:** on the host the first build of a resume is not a
  throwaway. `AttachHost` runs `ResumeSiteMismatch(save, site, profile)` against the selected
  site (a saved entry-k pit vs entry[0]'s profile dims/anchor → `FailHostSetup(true, …)`, the
  session ends with `SaveRestoreFailed`) and `RunRestore` loads the saved entry-k fill into that
  world; the swap would never get to run. Also the host never `RebindAuthority`s on a resume
  (it built the resumed pit directly), so the §4.3 client reset path (which depends on a
  host-side rebind to make `OnHello` re-send, §4.3) cannot be reused as a "correct to the
  restored entry" path.
  **What the participant does on `Restore`:** it passes the restored `int` to
  `CampaignDirector.OnIndexRestored(int)` (host-only). The director reconciles it with the
  siteId-derived `localIndex`: if `entries[restored]` has the SAME `PitSite.SiteId` as the
  built entry (the duplicate-prefab case, where several entries reuse one prefab), it adopts the
  restored index into `localIndex` (no rebuild — same prefab); if the SiteIds differ it keeps the
  siteId-derived index and logs a Warning (the built world is the ground truth). If the save
  has no campaign blob (a pre-campaign / freeplay save) `Restore` is never called and
  `EnsureFirstPit` falls back to entry[0] (siteId not found → Warning, entry[0]; the adapter's
  own `ResumeSiteMismatch` then reports a loud, existing error if it truly does not match).
  `CampaignNet`, when it spawns, takes `entryIndex.Value = localIndex` (§5.4).
  For the FIRST playable version, **resume-to-mid-campaign is still NICE-TO-HAVE** (owner call,
  §16 Q2); the primary flow is a fresh run from entry[0], which goes through the SAME
  `EnsureFirstPit` gate with `savedSiteId == null`. The participant is written so the index is
  always captured.
  [owner in-editor check]: resume a save made at entry k>0 and confirm the host builds entry k's
  prefab (not entry[0]) and `OnIndexRestored` logs a matching index.

---

## 8. Confirmations required by the spec

- **NetPit is not restarted between pits:** the design never calls `DetachSession` for an
  advance; it reuses `DisposePit`→`BuildRoutine` (via the additive `RebuildActivePit`) and
  re-points NetPit with the additive host `RebindAuthority` + client
  `ResetClientWorldForRebuild` (no `Dispose`/`StartHost`). NGO session, transport, and player
  NetworkObjects stay up. **Confirmed by trace** that the only NetPit teardown is `Dispose`
  (in `DetachSession`/`OnDestroy`), which the advance path avoids.
- **Wire format unchanged:** the rebind seams reuse the existing Header/Ready/Rel/Snap/Hello
  messages and `Protocol = 3`; no new message ids, no field changes. The new
  `forceFullNextReady` is a host-side per-link bool OR-ed into the existing `OnReady` full
  decision — it changes WHICH existing resync path runs, not the wire. **Confirmed** — the
  change is purely which `PitAuthority`/`PitWorld` the existing code reads, the client-world
  reset so the re-header is accepted, and a forced full resync.
- **One pit of balls at a time:** §4.5, confirmed by `DisposePit`/`BuildRoutine` ordering.
- **Money/upgrades persist, not reset:** §1.6, confirmed — they live in session-lifetime
  `BallPitGameState`; the director never touches them.

---

## 9. Files to create / edit

### New files (namespace `BallPitGameplay.Campaign`, Assembly-CSharp)
Folder `Assets/Game/BallPitGameplay/Campaign/`:
1. `CampaignDefinition.cs` — `[CreateAssetMenu] class CampaignDefinition : ScriptableObject`
   with `List<CampaignEntry> entries`; `[System.Serializable] class CampaignEntry { GameObject
   pitPrefab; int colorCount; /* later: availableTools */ }`. Validation: `entry.pitPrefab.
   GetComponentInChildren<PitSite>(true)` must be non-null (editor-time Error; the `PitSite` may
   sit on the prefab root OR on any child, including an inactive one — `includeInactive: true`
   covers both and is the ONE lookup used everywhere in this design: here, in §12, and in
   `EnsureFirstPit`'s siteId match; it matches `PitSites.FindAll()`, which finds sites
   anywhere in the hierarchy, inactive included), `colorCount` clamped to **2–8** (§12; the supported
   palette/lobby range) with an editor Warning if out of range or if it exceeds the number of
   placed containers, and an editor Error if the prefab's `PitSite.ProfileKey` does not
   resolve in the VenueConfig (mirrors `AttachHost`'s `TryGetProfile` hard-fail — §12).
2. `CampaignDirector.cs` — **`sealed class CampaignDirector : MonoBehaviour`** (plain scene
   object; NOT a NetworkObject — §5.1). Holds the `CampaignDefinition`, a plain `int
   localIndex`, `static CampaignDirector Instance` (set in `Awake`), `CurrentIndex`,
   `HasMorePits`, `CurrentEntry`, `ApplyEntryColors(PitSettings)`,
   `EnsureFirstPit(string savedSiteId)` (REV 4: resolves the start index — saved-siteId lookup
   on a host resume, `CampaignNet.Instance`'s index on a client if already spawned, else 0 —
   destroys any stale instance, instantiates that prefab, and on the host registers the
   `CampaignSaveParticipant` once; idempotent per session), `OnIndexRestored(int)` (host;
   reconcile per §7), `OnActivePitDrained()`, the
   advance coroutine, the per-session one-shot `CampaignNet` host-spawn (§5.4), prefab
   instantiate/destroy, the `entryIndex.OnValueChanged` subscription (client follow, §5.3),
   and `IPitTransition transition`.
3. `CampaignNet.cs` — **`sealed class CampaignNet : NetworkBehaviour`** (modeled on
   `GameplayNet`). Holds `NetworkVariable<int> entryIndex` (Everyone-read/Server-write),
   `static CampaignNet Instance` (set in `OnNetworkSpawn`, cleared in `OnNetworkDespawn`), a
   host-only `SetIndex(int)` setter, and exposes `entryIndex.OnValueChanged` for the director.
   Registered in the kit `NetworkPrefabsList` exactly like `BallPitGameplayNet.prefab`
   (prefab `BallPitCampaignNet.prefab`). Host-spawned ONCE per session by the director.
4. `CampaignPitSiteSelector.cs` — `class CampaignPitSiteSelector : IPitSiteSelector`
   returning the single live entry's `PitSite` from `query.Sites`.
5. `IPitTransition.cs` — `interface IPitTransition { IEnumerator Play(); }` — a SINGLE
   coroutine spanning dispose→build (the director does `yield return transition.Play()`
   between dispose and build, matching Part B). The `Play(bool leaving)` two-call variant is
   NOT used (Finding 9 resolved — one signature only).
6. `FadeTransition.cs` — stub `MonoBehaviour` implementing `IPitTransition`: screen
   fade + "Next shipment…" text, then yields a short hold. No real art.
7. `CampaignSaveParticipant.cs` — `IBallPitSaveParticipant` capturing/restoring the index
   (`Restore` forwards to `CampaignDirector.OnIndexRestored`). Registered ONCE per session by
   the director inside `EnsureFirstPit` (NOT per-world like `EconomySaveParticipant`, and NOT
   at the `CampaignNet` spawn poll; §7).

### Existing files to edit (COPY-BEFORE-EDIT each)
8. `Assets/Game/BallPitGameplay/Economy/GameplaySession.cs` — re-route the deposit `onWin`
   through `CampaignDirector.Instance`, resolved LAZILY at drain time (§6). Small, additive;
   offline/freeplay unchanged when `Instance == null`.
9. `Assets/Game/BallPitSession/BallPitGameAdapter.cs` — THREE one-line seams, each a null-safe
   `CampaignDirector.Instance?.…` call (REV 4): (i) in `AttachHost`, directly BEFORE
   `PitSites.SelectAndActivate`: `CampaignDirector.Instance?.EnsureFirstPit(resume ?
   save.Fill.SiteId : null);` (`save`/`resume` are already in scope there); (ii) in
   `AttachClient`, directly before its `SelectAndActivate`:
   `CampaignDirector.Instance?.EnsureFirstPit(null);`; (iii) in `AttachHost`'s NEW-GAME `else`
   branch only, right after `BallPitSettingsMap.Apply`:
   `CampaignDirector.Instance?.ApplyEntryColors(settings);` (§3 — never on the resume branch).
   The director's `Awake` (before `OnSessionLive`) only sets `Instance` and the selector, so the
   adapter always finds a live director, and `EnsureFirstPit` runs before
   `PitSites.FindAll()` so it finds the single live `PitSite`. Keep `DefaultPitSiteSelector` as
   the freeplay default (no director in the scene → all three seams are no-ops).
10. `Assets/_Prototypes/BallPitFinal/BallPitFinalController.Campaign.cs` (NEW partial file) —
    add the additive public host `RebuildActivePit(...)` (§4.3) and the additive client-only
    `public void DisposeClientPitForRebuild() => DisposePit();` (§4.3, used in §5.3 step 4 —
    reuses the private `DisposePit`, raises `PitWorldDisposed`, leaves `world == null`). A new
    partial keeps the copied controller body minimal.
11. `Assets/_Prototypes/BallPitFinal/NetPit.cs` — add the additive host
    `RebindAuthority(PitAuthority)`, the client `ResetClientWorldForRebuild()` (ends with
    the private `SendHello()` — REV 4), and the
    per-link `forceFullNextReady` field with the one extra `||` term in `OnReady` (§4.3).
    **Wire format unchanged** (reuses Header/Ready/Rel/Snap/Hello; `Protocol = 3`). This is
    the one edit to the frozen file; strictly additive (new public methods; reset per-link
    buffers; re-send the existing header; force a full resync on the first post-rebind Ready).

### Assets (owner)
- `Assets/Game/BallPitGameplay/Campaign/MainCampaign.asset` — authored `CampaignDefinition`
  (Part A step 6).
- `Assets/Game/BallPitGameplay/Campaign/BallPitCampaignNet.prefab` — the `CampaignNet`
  network prefab, added to the kit `NetworkPrefabsList` (mirrors `BallPitGameplayNet.prefab`).

### Asset (owner, Part A step 6)
`Assets/Game/BallPitGameplay/Campaign/MainCampaign.asset` — authored `CampaignDefinition`.

---

## 10. Ordering: who instantiates the first pit, and when (REV 4 — gated, not in `Awake`)

The adapter's `OnSessionLive` selects a site from `PitSites.FindAll()`. For the right entry
to be the selected site, its prefab must be instantiated **before** selection runs — and on a
resume the right entry is not known at scene `Awake` (the save `Restore` runs later, inside
the first `BuildRoutine`; §7). Chosen ordering (host and client):
**The director is a plain scene MonoBehaviour, so `Awake` runs on scene load — BEFORE the
kit's `OnSessionLive` (which the kit raises after scene sync). That is why the director is
NOT a host-spawned NetworkObject** (whose `OnNetworkSpawn` runs AFTER `OnSessionLive`, too
late for the adapter to call into it — see §5.1/§5.4, Finding 5 resolved). `Awake` installs
`Instance` + the selector; the FIRST prefab is instantiated by `EnsureFirstPit`, called from
the adapter's attach code immediately before `PitSites.FindAll()`. One component runs the
whole ordering; there is no "or" and no flag:

1. `CampaignDirector.Awake` (scene load) sets `CampaignDirector.Instance = this` and
   `adapter.SiteSelector = new CampaignPitSiteSelector(this)`. It instantiates NOTHING (REV 4).
   (The kit drives `OnSessionLive` after scene sync; the `BallPitKitInstaller` does NOT call
   `OnSessionLive` synchronously during scene `Awake` — confirmed by the adapter being the
   `OnSessionLive` entry point, not an `Awake`. So `Instance` and the selector are in place
   before any adapter attach code runs.)
2. **GATED first instantiate (REV 4, the single mechanism — §7).** In `OnSessionLive` →
   `AttachHost`/`AttachClient`, the adapter calls
   `CampaignDirector.Instance?.EnsureFirstPit(savedSiteId)` on the line directly BEFORE
   `PitSites.SelectAndActivate(...)` (where `PitSites.FindAll()` runs). Host: `savedSiteId =
   resume ? save.Fill.SiteId : null` — the save is already resolved at this point (the only
   source of the resumed pit available before `FindAll`; the participant's `Restore` is NOT —
   it runs inside the first `BuildRoutine`, after this). `EnsureFirstPit` resolves
   `startIndex` = (host resume) the entry whose
   `pitPrefab.GetComponentInChildren<PitSite>(true).SiteId == savedSiteId`, else 0;
   destroys any stale instance (idempotent per session), instantiates
   `entries[startIndex].pitPrefab`, sets `localIndex = startIndex`, and on the host registers
   `CampaignSaveParticipant` once (§7). The `PitSite` therefore exists when `FindAll` runs.
3. The adapter builds that single live site normally; the selector returns the one live
   `PitSite`; `colorCount` is injected per §3 (host seam after `BallPitSettingsMap.Apply`,
   new-game branch only). On a resume, the first build's `Restore` later delivers the saved
   index to the participant → `OnIndexRestored` reconciles (§7); no rebuild occurs.
4. On the CLIENT, `EnsureFirstPit(null)` resolves `startIndex` as: `CampaignNet.Instance`'s
   `entryIndex.Value` if `CampaignNet` is ALREADY spawned on this peer (a client that joins
   mid-run — a host-spawned, not-yet-despawned NetworkObject is synchronized to a joiner
   during connect, i.e. before the kit's post-scene-sync `OnSessionLive`), else 0 (the common
   fresh-run case: both peers start at entry[0] and `CampaignNet` has not spawned yet).
   [owner in-editor check]: confirm a mid-run joiner sees `CampaignNet.Instance != null` with
   the host's current index at `AttachClient` time. If it is null the client builds the
   entry[0] prefab (art/anchor) and its header-driven world is still correct (the header
   carries dims + `colorCount` and wins, §3; `ApplyClientSite` only logs an Error for the
   profile mismatch and the host's full resync covers a wrong anchor). That degraded case is
   art-only and is a follow-up; **a client MUST NOT be "corrected" by re-running the §5.3
   advance/reset path outside a host rebind**: `ResetClientWorldForRebuild` only re-obtains a
   header via the host's `OnHello` while that link is not `streaming`, which holds after a
   host `RebindAuthority` but NOT for an already-synced client when the host did not rebind.
   The rev-3 claim that a client "corrects via the index callback if the host is further
   along" is therefore removed. (Resume and mid-run join are NICE-TO-HAVE for v1, §16 Q2; the
   fresh-run path needs no index wait at all.)

For the first playable version, auto-building the first pit (entry[0] on a fresh run) on
session start is explicitly allowed; the garage-door trigger / outside-interview / moving-spawns-inside is a documented
**follow-up, not this deliverable**.

---

## 11. Error handling (concrete, per operation)

- **`CampaignDefinition` empty / null:** fatal at session start. Director logs
  `Debug.LogError("[CampaignDirector] no CampaignDefinition / empty entries")` and falls back
  to leaving the adapter's `DefaultPitSiteSelector` behavior (i.e. acts as if no campaign) so
  the scene still runs a single pit. Recoverable → degrade to freeplay-like single pit.
- **Entry `pitPrefab` null or missing `PitSite`:** fatal for THAT entry. Log Error; if it is
  entry[0], degrade as above; if mid-run, the director stops advancing and leaves the current
  (drained) pit, logging that the campaign is misauthored. Do NOT crash the session.
- **`RebuildActivePit` build failure (`onBuilt(false, msg)`):** the controller's existing
  `FailHost`/`clientBuildFailed` paths already exist for the initial build; for a rebuild the
  director logs Error and leaves the player in a "between pits" state (previous pit disposed).
  Recoverable only by the owner re-authoring/restarting. v1 behavior: an empty pit area + a
  logged Error (no in-run retry). Flagged as an owner UX call in §16 Q5 (retry/abort-to-menu
  is a follow-up, not v1).
- **`RebindAuthority`/`ResetClientWorldForRebuild` on a client that never synced:** if a
  client is mid-INITIAL-sync (`CW == null` still) when a rebuild happens, `OnHeader`'s guard
  is already open (it blocks only when `CW != null`), so `ResetClientWorldForRebuild` is a
  harmless reset (its closing `SendHello()` just makes the host `OnHello` re-send the header,
  since that link is not yet `streaming`; a duplicate header is dropped by the
  `HeaderPending` guard) and the normal first-sync proceeds against the new `A`. If a client is
  mid-sync and times out, the existing watchdog (`BallPitGameAdapter.ClientWatchdog`,
  `ClientSyncSeconds`) leaves. No new handling needed. [owner in-editor check]: confirm the
  watchdog's `builtAt` resets sensibly across a rebuild (it keys off the client's build, which
  re-runs on the re-header).
- **Replicated index read before `CampaignNet` is spawned:** cannot happen — `entryIndex` is
  only ever touched inside `CampaignNet` (always post-spawn), and the director reads
  `CampaignNet.Instance` (null until spawned) or its own `localIndex` offline. There is no
  code path that reads `entryIndex.Value` on an unspawned object. Writing is host-only and
  only after a drain (long after spawn).
- **Transition throws:** the director wraps `yield return transition.Play()` so an exception
  in the stub does not strand the rebuild; log Error and continue to the build (the transition
  is cosmetic).
- **Save participant restore corrupt:** `CampaignSaveParticipant.Restore` try/catches like
  `EconomySaveParticipant`, logs a Warning and does NOT call `OnIndexRestored`, so the
  siteId-derived `localIndex` from `EnsureFirstPit` (§7) stands (the built world is the ground
  truth); with a fresh run and no blob that is entry[0].
- **`EnsureFirstPit` cannot resolve/instantiate (null `CampaignDefinition`, null prefab, or a
  resume `savedSiteId` that matches no entry):** a matching-nothing `savedSiteId` logs a
  Warning and falls back to entry[0] (the adapter's own `ResumeSiteMismatch` then reports the
  real mismatch loudly and ends the session via `FailHostSetup` — existing behavior); a
  null definition/prefab follows the first two bullets above (degrade, no instantiate, the
  selector falls back to the default).

## 12. Validation of external inputs

- `CampaignEntry.colorCount`: required, int, **clamp to 2–8** (Finding 6). Rationale
  (traced): although `PitSettings.colorCount` is `[Range(1,16)]`, the lobby schema is
  `SettingDefinition.Int(ColorCount, "Colors", 4, 2, 8)` (min 2, max 8) and game-design §5
  intends ~3–6. Because the campaign writes `settings.colorCount` DIRECTLY via the §3 host
  seam, it bypasses the lobby's 2–8 clamp — so the campaign is INTENTIONALLY allowed to pick
  any value in the palette-supported range, but it must not exceed what `PaletteApplier` and
  the shipped palettes handle. Clamp to 2–8 (the lobby/palette range). State in the doc: the
  campaign intentionally bypasses the lobby schema clamp but stays inside the palette range.
  Out-of-range → clamp + editor Warning.
- **Container-count check (Finding 6):** add an editor Warning if an entry's `colorCount`
  EXCEEDS the number of placed `BallContainer`s in the scene (via `ContainerRegistry.All` at
  edit time, or a count field on the validator). A pit whose `colorCount` > placed containers
  can never be fully drained — the win (`StateTotal(Stored) == Count`) would never fire
  because balls of the uncontainered color(s) have nowhere to be sold. This is a hard
  campaign-authoring footgun; warn loudly.
- `CampaignEntry.pitPrefab`: required, and `pitPrefab.GetComponentInChildren<PitSite>(true)`
  must be non-null — the `PitSite` may be on the root or any child, active or inactive (checked by an editor
  validator mirroring `VenueValidator`/`BallContainerValidator` patterns). Missing → Error,
  entry skipped/degraded (§11).
- **PitSite profileKey must resolve in VenueConfig (editor-time, mirrors `AttachHost`).** Add
  an editor-time ERROR when an entry's `pitPrefab.GetComponentInChildren<PitSite>(true).ProfileKey` is NOT present in the
  resolved VenueConfig profiles. Traced: `BallPitGameAdapter.AttachHost` does
  `if (site != null && !VenueConfig.TryGetProfile(site.ProfileKey, out profile)) { FailHostSetup(resume, $"site '{site.SiteId}' names profile '{site.ProfileKey}', which VenueConfig does not define"); return; }`
  (`BallPitGameAdapter.cs` lines 171–172) — i.e. an unresolved profileKey HARD-FAILS the
  session at runtime. The `CampaignPitSiteSelector` returns the lone live `PitSite`
  regardless of its profileKey (§1.3/§5.3), so it does not catch this; the hard-fail still
  happens in `AttachHost`'s `TryGetProfile`. The §12 validator must therefore check it at
  author time: for each entry, load the VenueConfig (`VenueConfig.Load()` — the same
  accessor `VenueValidator`/`VenueGizmos`/`PlayerSpawnPoint` use) and report an Error if
  `!config.TryGetProfile(entry.pitPrefab.GetComponentInChildren<PitSite>(true).ProfileKey, out _)`.
  (REV 4: `GetComponentInChildren<PitSite>(true)` is the ONE lookup, identical to §9 item 1 —
  the `PitSite` may be on the prefab root or any child, active or inactive, matching Part A
  step 3 and `PitSites.FindAll()`. A `null` result is the "missing `PitSite`" Error below.)
  This surfaces the Part A step 3a "add the profile" omission at edit time instead of as a
  runtime `FailHostSetup` on the first build. (Null/missing `PitSite` is already the Error
  above; this check runs only when a `PitSite` exists.)
- **Duplicate `SiteId` across entries (REV 4):** editor Warning if two entries'
  `GetComponentInChildren<PitSite>(true).SiteId` are equal (same prefab reused). Legal at
  runtime (only one is ever live), but a host resume maps `save.Fill.SiteId` → the FIRST
  matching entry (§7) and relies on `CampaignSaveParticipant`'s restored index to
  disambiguate (`OnIndexRestored` adopts it when the SiteIds match).
- `entries` list: required, ≥1. Empty → degrade (§11).
- Replicated `entryIndex` (on `CampaignNet`): server-write only; clients treat any value ≥
  `entries.Count` as the last entry (defensive clamp) and log once.

## 13. Invariants and ownership

- **Single live pit / single `PitSite`:** owned by `CampaignDirector` (instantiate/destroy)
  + enforced structurally by `DisposePit` before `BuildRoutine`. Why here: only the director
  knows the sequence; the controller only knows "a" pit.
- **Authority = host writes index + advances:** the director decides to advance (host-only,
  since drain detection via `DepositDriver` is host-only); the actual replicated write goes
  through `CampaignNet.SetIndex` guarded on `IsServer`. Offline the director uses `localIndex`.
  Why split: the director is a plain scene object (Part A), `CampaignNet` owns the wire state.
- **Money/upgrades persistence:** owned by `BallPitGameState` (adapter) — the director MUST
  NOT reset it. Enforced by not referencing it except through the read-only economy.
- **Win only at final entry:** owned by the re-routed `onWin` in `GameplaySession` consulting
  `director.HasMorePits`. Why there: that is the existing single win chokepoint.

## 14. Testability

- **Unit-testable (no Unity runtime):** `CampaignDefinition` walk logic can be factored into a
  plain-C# `CampaignProgress` helper (`int Index`, `bool HasMore`, `Advance()`, `Entry Current`)
  and unit-tested. `GameplayCore` economy persistence across a simulated rebuild is already
  plain-C# testable (new `GameplayCore` bound to the same `BallPitGameState` keeps money) —
  add a test asserting money survives a core swap.
- **Integration-only (owner, in-editor):** the NetPit `RebindAuthority` + client
  `ResetClientWorldForRebuild` re-header + forced-full-resync rebuild; the dispose→build hitch
  under the transition; host+client advancing together; resume-to-index. These need the editor
  + a second peer and are the manual checklist (§15).
- A design smell to watch: `RebindAuthority` + `ResetClientWorldForRebuild` are deliberately
  bounded to {re-point A; reset host event batch; per-link {streaming/live/syncDoneMarker,
  reallocate `lastSent`, clear `outq`, re-send header (best-effort fast path), set
  `forceFullNextReady`}} on the host and {clear CW/R/Synced/HeaderPending/events/heldBy/clock/
  checksum/pending, then `SendHello()` as the FINAL step (the authoritative header re-send
  trigger — REV 4)} on the client. If
  either method grows beyond that enumerated set, the seam is too wide — reconsider Plan B
  (§4.4). The exact field lists are in §4.3.
- The controller client dispose hook `DisposeClientPitForRebuild()` is bounded to a single
  body — `=> DisposePit()` — and nothing else. If it grows past that one delegation (e.g.
  starts touching `netPit`'s `CW`/`R` directly or skips `DisposePit`), it is wrong: the hook
  exists only to reuse the existing teardown so `world == null` and `PitWorldDisposed` both
  hold. Any addition is a smell.

## 15. Owner compile-check + manual test list (NGO codegen is editor-only)

Because there is no Unity compiler here, the owner must, in-editor:
- **NGO codegen:** confirm `CampaignNet : NetworkBehaviour` with `NetworkVariable<int>
  entryIndex` compiles (NGO weaver runs on domain reload) and that `CampaignNet` is in the kit
  `NetworkPrefabsList` (prefab `BallPitCampaignNet.prefab`, same as `BallPitGameplayNet.prefab`).
  `CampaignDirector` is a plain `MonoBehaviour` — no weaver involvement.
- If any `[ServerRpc]`/`[ClientRpc]` is added later, confirm the weaver accepts the signatures
  (value-type/`FixedString`/`INetworkSerializable` params only). The v1 `CampaignNet` needs NO
  RPC (index write + `OnValueChanged` suffice) — prefer keeping it RPC-free.
- Confirm the additive `NetPit.RebindAuthority` / `ResetClientWorldForRebuild` compile and the
  `Protocol = 3` constant is unchanged. Confirm `ResetClientWorldForRebuild` ENDS with
  `SendHello()` and that the host re-sends the header on the resulting `OnHello` (§4.3).
- Confirm the three adapter seams (`EnsureFirstPit` x2 before `SelectAndActivate`,
  `ApplyEntryColors` in the new-game branch only) are present and null-safe (§9 item 9).
- **Manual flow test (host/offline):** spawn in warehouse → entry[0] builds → sort/sell all →
  transition stub plays → entry[1] builds in place → drain → results panel shows.
- **Manual co-op test (host + 1 client):** both see entry[0]; buy an upgrade (shared pot);
  drain entry[0]; confirm the CLIENT follows the pit switch (index replicates, client resets +
  rebuilds with a forced full resync — watch for "synced, ledger matches host") and
  money/upgrades are intact; drain the final entry; both see results.
- **Resume check (nice-to-have, §16 Q2):** save at entry k>0, resume; the host must build
  entry k's prefab (not entry[0]); `OnIndexRestored` logs a matching index; money/upgrades
  intact; `CampaignNet.entryIndex` = k after it spawns (§7).
- **Scalability check:** add a third prefab + entry; confirm no code change needed.
- **Regression:** with NO `CampaignDirector` in the scene (pure freeplay), the single-pit
  drain still "wins" exactly as today (adapter uses `DefaultPitSiteSelector`, `onWin` falls to
  `OnWinHost`).

---

## 16. Open questions / ambiguities for the owner

These are genuine OWNER CALLS (not unresolved design decisions — those are all decided in
§§2–14). Each has a chosen default; confirm or override.

1. **NetPit rebind (the risk) — the ONE edit to the frozen file.** The design adds TWO
   strictly-additive seams to `NetPit.cs`: host `RebindAuthority` (re-point `A`, reset the
   host event batch, per-link {reset streaming/live/syncDoneMarker, reallocate `lastSent`,
   clear `outq`, re-send the existing header, set `forceFullNextReady`}) AND client
   `ResetClientWorldForRebuild` (clear the per-world client fields so the `OnHeader` guard
   accepts the new header, then calls the private `SendHello()` as its final step so the host's
   `OnHello` re-sends the header), plus one per-link `forceFullNextReady` field OR-ed into `OnReady`.
   No wire change (reuses Header/Ready/Rel/Snap/Hello; `Protocol = 3`). This is the only way
   to rebuild mid-session WITHOUT a NetPit restart, given `A` is bound once at `StartHost` AND
   the client `OnHeader` guard blocks a re-header while `CW != null`. If you'd rather not touch
   `NetPit` at all, the alternative is the in-place re-fill (Plan B, §4.4), a bigger controller
   change. **Recommend Plan A.** (Confirm the two-method NetPit edit is acceptable.)
2. **Resume-to-mid-campaign.** `CampaignSaveParticipant` is written so the index is always
   captured. Is HONORING a resume (host builds the saved entry's prefab) required for v1, or is
   "fresh run from entry[0]" enough for now? Chosen default: the gated `EnsureFirstPit` path
   and the participant are specified and written (REV 4: the siteId-keyed gate is the same
   code path the fresh run uses, so the resume branch is a few lines, not a separate
   mechanism), but resume/mid-run-join are not v1 acceptance items. (§7, §10.)
3. **Mid-run rebuild failure UX.** If a rebuild fails mid-campaign, v1 leaves an empty pit
   area + a logged Error (no in-run retry). Acceptable for v1, or do you want
   retry/abort-to-menu? Chosen default: logged error, no retry (follow-up). (§11.)

Resolved since review round 1 (were open, now decided — listed so the owner sees the calls):
- **`colorCount` / header** — VERIFIED the header encodes `colorCount` + dims and is
  AUTHORITATIVE for the client ball sim; the index selects only the prefab. No owner input
  needed. (§3, Finding 2.)
- **Director object model** — DECIDED: `CampaignDirector` stays a PLAIN scene MonoBehaviour
  (matches Part A step 7); the replicated index lives on a separate host-spawned `CampaignNet`
  prefab (mirrors `GameplayNet`). No scene-NetworkObject ambiguity. (§5.1/§5.4, Findings 4/5.)
- **Transition shape** — DECIDED: `IPitTransition.Play()`, a single coroutine spanning
  dispose→build. The two-call variant is removed. (§9 item 5, Finding 9.)
- **colorCount range** — DECIDED: clamp 2–8 (lobby/palette range), campaign intentionally
  bypasses the lobby clamp; editor warns if colorCount > placed containers. (§12, Finding 6.)

---

## 17. Trace citations (for the implementer)

- Adapter selector seam + single build: `BallPitGameAdapter.cs` — `SiteSelector` property,
  `OnSessionLive`, `AttachHost` (`PitSites.SelectAndActivate`, `BallPitSettingsMap.Apply`,
  `controller.AttachSession`), `AttachClient`, `GameState`, `OnSessionEnding`.
- Controller lifecycle: `BallPitFinalController.Session.cs` — `AttachSession` guard,
  `DetachSession` (`netPit.Dispose`), `OnHostBuilt` (`NetPit.StartHost(... authority)`,
  `MarkWorldReady`). `BallPitFinalController.cs` — private `BuildRoutine`
  (`authority = new PitAuthority(w)`), private `DisposePit` (`RaisePitWorldDisposed`,
  `authority?.Dispose`, `world?.Dispose`). `BallPitFinalController.View.cs` — `PitWorldBuilt`,
  `PitWorldDisposed`, `MarkWorldReady`, `World/Authority/Net/ActiveSite/IsPitWorldReady`.
- Drain/win: `DepositDriver.cs` — `CheckWin` (`StateTotal(Stored)==Count`), `onWin`.
  `GameplaySession.cs` — `new DepositDriver(... OnWinHost)`, `OnWinHost`
  (`net.HostBroadcastWin` / `WinResults`). `ResultsPanel.cs` — `session.WinResults += Show`.
- Economy persistence: `GameplayCore.cs` — `Money`/`LevelOf`/`SetLevel` via `state`
  (`BallPitGameState`); `GameplaySession.OnPitWorldBuilt` — `new GameplayCore(..., adapter.GameState)`.
  `EconomySaveParticipant.cs` — `IBallPitSaveParticipant` pattern + `adapter.RegisterSaveParticipant`.
  Registration lifetime: `GameplaySession.cs` registers it in `OnPitWorldBuilt` (lines
  121–122) and unregisters in `TearDown` (lines 192–194) — PER WORLD; `CampaignSaveParticipant`
  deliberately registers ONCE per session instead (§7).
- NetPit binding (re-read for round 1): `NetPit.cs` — `StartHost` (`new NetPit(...) { A =
  authority }`, `authority.Events = net`; the ONLY assignment to `A` besides the new
  `RebindAuthority`), `SendHeader` (writes `s.pitX/pitY/pitZ`, `s.ballDiameter`,
  **`s.colorCount`**, `s.seed`, …, `A.W.FillChecksum`), `OnHeader` (client guard
  `if (HeaderPending || CW != null) return;`; reads a full `PitSettings` incl. `colorCount`),
  `AttachClientWorld` (`CW = world; R = replica;`), `OnReady` (`bool full = checksum !=
  A.W.FillChecksum`), per-link `Link { streaming, live, outq, lastSent = new
  int[A.Store.Capacity], syncDoneMarker }`, host batch `evBuf/evLen/evCount/evBaseTick`,
  `Dispose` (clears handlers + `A.Events`, does NOT null `CW`). Verified `CW` is never nulled
  anywhere → the re-header-only approach would be dropped by the guard (Finding 1).
- Hello / header re-send (round 3): `NetPit.cs` — `private void SendHello()` (writes `Protocol`,
  `SendNamedMessage(MsgHello, ServerClientId, …, ReliableSequenced)`), `private void
  OnHello` (`if (links.TryGetValue(sender, out var link) && !link.streaming) SendHeader(link);`),
  `private void SendHeader(Link)`, `OnHeader` guard `if (HeaderPending || CW != null) return;`,
  `public readonly bool IsHost`, `StartClient` (the only existing `SendHello()` callers:
  `net.SendHello()` / `OnLocalClientConnected`).
- Resume ordering (round 3): `BallPitFinalController.Session.cs` — `BeginHost` (`settings =
  r.Settings;` line 148, then `BuildRoutine(..., (ok,msg) => OnHostBuilt(r, ok, msg))`),
  `OnHostBuilt` (`string err = r.Restore?.Invoke(world, authority);` THEN
  `NetPit.StartHost`, THEN `MarkWorldReady`). `BallPitGameAdapter.cs` — `AttachHost` (resume:
  `ResolveSave` → `PitSites.SelectAndActivate(SiteSelector, …, resume ? save.Fill.SiteId :
  null, …)` → `ResumeSiteMismatch` → `settings = save.Fill.ApplyTo(...)`; new game: `Apply`),
  `RunRestore` → `BallPitWorldRestorer.Apply(…, participants)`, `AttachClient` (selection with
  `savedSiteId == null`, `ApplyClientSite`), `RegisterSaveParticipant` (throws on duplicate
  tag), `ResetSession` (does NOT clear `participants`). `BallPitWorldRestorer.RestoreGame` —
  `FindParticipant(participants, tag)`; no participant → `game.UnclaimedExtensions`.
  `PitSites.cs` — `PitSiteQuery.SavedSiteId`, `FindAll()` (`FindObjectsOfType<PitSite>(true)`).
- NGO exemplar: `GameplayNet.cs` — `sealed NetworkBehaviour`, `NetworkVariable<long> money`
  (Everyone/Server), static `Instance` set in `OnNetworkSpawn`/cleared in `OnNetworkDespawn`,
  host-spawned by `GameplaySession.SpawnGameplayNet` (`Instantiate` → `GetComponent` →
  `Spawn(true)`), despawned in `TearDown`. `CampaignNet` copies this, spawned once per session.
- Settings/colors: `BallPitSettingsMap.cs` — `ColorCount` key, schema
  `Int(ColorCount, "Colors", 4, 2, 8)`, `Apply` (`s.colorCount = hs.GetInt(ColorCount)`).
  `PitSettings.cs` — `public int colorCount` `[Range(1,16)]` (campaign clamps to 2–8, §12).
- SaveManager (retired, NOT used): `Assets/Scripts/Game/Save/SaveManager.cs`.

---

## 18. Responses to design-review round 1 (`design-review.json` / `design-review.md`)

Every HIGH and MEDIUM finding is addressed by editing the sections above; the NITs are
addressed too. Summary of each:

- **Finding 1 [HIGH] — re-header blocked by the `OnHeader` guard.** FIXED in §4.3. The
  re-header-only mechanism was wrong. Added a client-side `NetPit.ResetClientWorldForRebuild()`
  (clears `CW`/`R`/`Synced`/`HeaderPending`/`events`/`heldBy`/clock/`hostChecksum`/
  `PendingSettings`/`PendingBounds` — the exact per-world client fields), driven off
  `CampaignNet.entryIndex.OnValueChanged` plus a client `Hello` re-send to close the ordering
  race, so the host's re-sent header is accepted and the client rebuilds. Exact fields listed.
- **Finding 2 [MEDIUM] — colorCount IS in the header.** FIXED in §3. Removed the hedge; the
  header is stated as AUTHORITATIVE for the client ball-sim `colorCount`/dims (verified
  `SendHeader`/`OnHeader`); the index is authoritative only for which PREFAB to instantiate;
  the two agree by construction (host sets `settings.colorCount` then sends it).
- **Finding 3 [MEDIUM] — checksum-mismatch resync not guaranteed.** FIXED in §4.3. Added a
  per-link `forceFullNextReady` flag set in `RebindAuthority` and OR-ed into `OnReady`'s `full`
  decision, so the host forces a full resync on the first post-rebind Ready regardless of
  checksum (the host knows it advanced). Added to the §12 validation framing and §15 test.
- **Finding 4 [MEDIUM] — director object model contradictory.** FIXED in §2/§5.1/§5.4/§10/§13.
  DECIDED: `CampaignDirector` is a plain scene `MonoBehaviour` (matches Part A step 7); the
  replicated index lives on a separate host-spawned `CampaignNet : NetworkBehaviour` prefab
  (mirrors `GameplayNet`), spawned once per session. `Instance` on the director is set in
  `Awake`; `CampaignNet.Instance` in `OnNetworkSpawn`. Consistent across all sections.
- **Finding 5 [MEDIUM] — entry[0] vs OnSessionLive ordering.** FIXED in §5.1/§5.4/§10. Because
  the director is a plain MonoBehaviour, its `Awake` (scene load) runs BEFORE `OnSessionLive`,
  so it instantiates entry[0] + installs the selector in `Awake` — one deterministic ordering,
  no "or", no flag. A host-spawned director (whose `OnNetworkSpawn` runs after `OnSessionLive`)
  was explicitly rejected for this reason. *(Rev 4 refinement: `Awake` installs `Instance` + the
  selector; the first prefab is instantiated by `EnsureFirstPit` just before `FindAll`, §2/§10 —
  see R3 Finding 2. The plain-MonoBehaviour decision is unchanged.)*
- **Finding 6 [MEDIUM] — colorCount range / container check.** FIXED in §9/§12. Clamp
  `CampaignEntry.colorCount` to 2–8 (lobby/palette range); state the campaign intentionally
  bypasses the lobby clamp; add an editor Warning when `colorCount` > placed containers (else
  the pit is undrainable and the win never fires).
- **Finding 7 [MEDIUM] — drain re-route depends on Instance timing.** FIXED in §5.4/§6. The §6
  lambda resolves `CampaignDirector.Instance` LAZILY at drain time (not captured);
  `CampaignDirector.Instance` is set in `Awake` (before any build) so it is reliably non-null
  at the first drain; `CampaignNet` is guaranteed spawned long before any drain. Documented the
  null-director fall-through to `OnWinHost` as the intended degrade.
- **Finding 8 [NIT] — offline NetworkBehaviour with unused NetworkVariable.** RESOLVED by the
  two-object split (§5.1): `entryIndex` lives only on `CampaignNet`, which exists only spawned,
  so the "unspawned NetworkBehaviour holding a NetworkVariable" case never occurs. Hard
  invariant stated.
- **Finding 9 [NIT] — IPitTransition two signatures.** FIXED in §9 item 5: `IEnumerator Play()`
  only; the `Play(bool leaving)` variant deleted. §16 no longer re-opens it.
- **Finding 10 [NIT] — RebindAuthority per-link reset under-specified.** FIXED in §4.3/§14:
  enumerated the concrete host checklist (re-point `A`; reset host event batch; per-link reset
  streaming/live/syncDoneMarker, reallocate `lastSent = new int[A.Store.Capacity]`, clear
  `outq`, `SendHeader`, set `forceFullNextReady`) and the client reset field list.

No findings were backlogged or dismissed. The §1b KEY INTEGRATION RISK (mid-session networked
rebuild) is now specified completely against the real client guard; it remains the item that
only an in-editor multi-peer test can finally prove, and is flagged as such ([owner in-editor
check]) — not as an unresolved design decision.

---

### Responses to design-review round 2 (`design-review.json` / `design-review.md`)

Round 2 returned CHANGES_REQUESTED with one MEDIUM and three NITs. All four are FIXED by
editing the sections above; none backlogged or dismissed.

- **R2 Finding 1 [MEDIUM] — the "thin client-side dispose hook" was named but never
  specified.** FIXED in §4.3, §5.3 step 4, §5.5, §9 item 10, §14. The hook is now given an
  explicit signature and body: `public void DisposeClientPitForRebuild() => DisposePit();` in
  `BallPitFinalController.Campaign.cs`, client-only. §4.3 quotes the traced `DisposePit()`
  body (`BallPitFinalController.cs`) showing it raises `PitWorldDisposed` (→
  `GameplaySession.TearDown` on the client), that `authority?.Dispose()` is a no-op on the
  client (authority is null there), and that it leaves `world == null` so the controller's
  `Update` rebuild guard (`world == null`) fires. §5.3 step 4 now calls it by name before
  `net.ResetClientWorldForRebuild()`. Added to the §14 bounded-set smell check (the hook must
  stay a single `=> DisposePit()` delegation). This closes the gap where an implementer could
  have disposed the world without nulling it and silently broken the client rebuild.
- **R2 Finding 2 [NIT] — redundant/conflicting host colorCount injection.** FIXED in §3. The
  entry[0] host path no longer says "set `controller.settings.colorCount` … and additionally";
  it now states the SINGLE effective seam is `ApplyEntryColors(settings)` immediately after
  `BallPitSettingsMap.Apply`, and explains that a pre-`Apply` field-set is DEAD for entry[0]
  because `Apply` (`BallPitSettingsMap.cs` line 80, `s.colorCount = hs.GetInt(ColorCount)`)
  overwrites it. The field-set `PitSettings.colorCount` is kept ONLY in the entry[i>0] rebuild
  path, where a cloned `PitSettings` is passed straight into `RebuildActivePit` with no `Apply`
  to clobber it. The §3 "agree by construction" invariant was reworded to match.
- **R2 Finding 3 [NIT] — validator did not require the prefab's PitSite.ProfileKey to
  resolve.** FIXED in §12 (and cross-referenced in §9 item 1). Added an editor-time Error when
  an entry's `pitPrefab.PitSite.ProfileKey` is not present in the resolved VenueConfig,
  mirroring `BallPitGameAdapter.AttachHost`'s `TryGetProfile` hard-fail (`BallPitGameAdapter.cs`
  lines 171–172). The validator loads VenueConfig via `VenueConfig.Load()` (the same accessor
  `VenueValidator`/`VenueGizmos`/`PlayerSpawnPoint` use) and surfaces the Part A step 3a
  "add the profile" omission at author time rather than as a runtime `FailHostSetup`.
- **R2 Finding 4 [NIT] — CampaignSaveParticipant registration lifetime differs from
  EconomySaveParticipant.** FIXED in §7 (and §9 item 7, §17). §7 now states the participant is
  registered ONCE by the director at session-live (the same one-shot moment it host-spawns
  `CampaignNet`) and unregistered at session end — explicitly NOT in the per-world
  `OnPitWorldBuilt`/`TearDown` cycle that `EconomySaveParticipant` uses (traced:
  `GameplaySession.cs` registers it at lines 121–122 in `OnPitWorldBuilt`, unregisters at
  lines 192–194 in `TearDown`). This keeps the session-scoped index available to
  `CaptureWorldSave` across every rebuild, like `CampaignNet` itself. *(Superseded in rev 4:
  the registration MOMENT moved from the `CampaignNet` spawn poll to `EnsureFirstPit`; the
  once-per-session lifetime is unchanged. See R3 Finding 2.)*

---

### Responses to design-review round 3 (`design-review.json` / `design-review.md`)

Round 3 returned CHANGES_REQUESTED with two MEDIUM and three NIT findings (revision 4 applies
all five; none backlogged or dismissed). Each was re-traced against the real worktree sources
before editing. No settled architecture was reopened: `CampaignDirector` stays a plain scene
`MonoBehaviour`, `CampaignNet` the session-lifetime host-spawned index carrier, the selector
install, the `onWin` re-route, `IPitTransition`, and the additive-only NetPit seams with the
wire format / `Protocol = 3` unchanged are all as in rev 3.

- **R3 Finding 1 [MEDIUM] — Hello re-send trigger was an infeasible either/or.** FIXED in
  §4.3, §5.5, §9 item 11, §14, §15, §16 Q1, §17. Traced `NetPit.cs`: `SendHello()` and
  `OnHello` are `private`, so the director could not call it. `NetPit.ResetClientWorldForRebuild()`
  now MUST end with `SendHello()` (added to the enumerated client-reset body as its FINAL step;
  guarded `if (IsHost) return;`). The "or the director triggers it" alternative is deleted. The
  bounded-change list (§14) now includes `SendHello()`. The `[owner in-editor check]` is reworded
  to "confirm `ResetClientWorldForRebuild` ends with `SendHello()` and the host re-sends the
  header on the resulting `OnHello`". `RebindAuthority` step 3's direct `SendHeader` is restated
  as a best-effort fast path (client drops it while `CW != null`), with the Hello-driven re-send
  as the authoritative race-closer, and a two-case ordering argument added (Hello before vs.
  after `RebindAuthority`; `OnHello` only acts while `!link.streaming`, which step 3 resets).
- **R3 Finding 2 [MEDIUM] — resume ordering claim contradicted the real save-restore flow.**
  FIXED in §2 item 2, §3, §5.1, §5.4, §5.5, §7, §9 items 2/7/9, §10, §11, §12, §15, §16 Q2,
  §17. Traced: the save `Restore` runs inside the first `BuildRoutine`
  (`BeginHost` → `BuildRoutine` → `OnHostBuilt` → `r.Restore`), after scene `Awake` and after
  `AttachHost`'s `FindAll()`. The "available before `Awake`" claim and its owner check are
  deleted. **ONE mechanism chosen: option (a), gate the first instantiate** — via a new
  `CampaignDirector.EnsureFirstPit(savedSiteId)` that the adapter calls immediately before
  `PitSites.SelectAndActivate` in `AttachHost` (`resume ? save.Fill.SiteId : null`) and
  `AttachClient` (`null`). The pit to build is derived from the save's `Fill.SiteId` (known
  there, unlike the participant blob). **Option (b) was rejected on a trace, not preference:**
  `AttachHost` on a resume validates the selected site against the save
  (`ResumeSiteMismatch` → `FailHostSetup`) and `RunRestore` loads the saved fill into the built
  world, so an "entry[0] throwaway then swap" would fail the session before any swap, and the
  host performs no `RebindAuthority` on a resume (the client reset path depends on one).
  Consequences pinned in the same edit: (i) `CampaignSaveParticipant` registers inside
  `EnsureFirstPit` (before the first `Restore`), because `RestoreGame` hands blobs to
  participants found by tag and parks the rest in `UnclaimedExtensions`, and unregisters on
  director teardown (the adapter's `participants` list survives `ResetSession`);
  (ii) `Restore` forwards to `OnIndexRestored`, which reconciles with the siteId-derived index
  (adopts it only for duplicate-prefab entries with the same SiteId); (iii) `ApplyEntryColors`
  is new-game-branch only, since a resume takes `save.Fill.ApplyTo`; (iv) `Awake` now only sets
  `Instance` + the selector; (v) the rev-3 claim that a client "corrects via the index callback"
  is removed, because the reset path only recovers a header while the host link is not
  `streaming` (true after a host rebind, false for a synced client with no rebind) — clients
  take `CampaignNet.Instance`'s index in `EnsureFirstPit` if already spawned, else 0, flagged as
  an `[owner in-editor check]` and art-only if degraded; (vi) a duplicate-`SiteId` editor
  Warning in §12. Resume itself stays NICE-TO-HAVE for v1 (§16 Q2), now with a correct design.
- **R3 Finding 3 [NIT] — `PitSite` lookup inconsistent.** FIXED in §9 item 1, §12 (both the
  "must contain" bullet and the VenueConfig profile check), and `EnsureFirstPit`'s siteId match:
  one lookup, `entry.pitPrefab.GetComponentInChildren<PitSite>(true)`, with the statement that the
  `PitSite` may be on the root or any child, active or inactive (matches Part A step 3 and
  `PitSites.FindAll()`).
- **R3 Finding 4 [NIT] — `RebuildActivePit` didn't set `settings`.** FIXED in §4.3 step 1: now
  `settings = newSettings; Environment = newEnv; activeSite = newSite; overviewCamera =
  newOverview;`, mirroring `BeginHost`'s `settings = r.Settings` (`Session.cs` line 148).
- **R3 Finding 5 [NIT] — double header send described loosely.** RESOLVED by Finding 1: step-3
  `SendHeader` is labeled a best-effort fast path and the Hello-driven re-send the authoritative
  closer; the duplicate is documented as harmless (`HeaderPending || CW != null` guard;
  `forceFullNextReady` cleared only in `OnReady`).

Not verifiable here and unchanged in status: the actual networked rebuild round-trip, NGO
weaver acceptance of `CampaignNet`, whether a mid-run joiner already holds `CampaignNet`
at `AttachClient` time, and the resume path — all `[owner in-editor check]` / manual-test items
(§15). No Unity compile was attempted (none exists on this machine; not a defect).

---

## 19. Implementation findings (rev 5 addendum)

Added after the code was written. The approved sections above are NOT rewritten; this records what tracing the real
sources found while implementing, as additive clarifications. Architecture is unchanged. Each gap is closed with the
smallest additive seam, none changes the wire format or `Protocol = 3`. Nothing here was compiled or run (no Unity
on the agent machine); see `HANDOFF-D.md` for the owner's compile check and play tests, and `DECISIONS-D.md` for the
decisions and known limits.

| # | Gap (found by tracing) | Closure |
|---|---|---|
| G1 | `NetPit.HostUpdate/OnIntent/OnReady/OnHello/OnClientConnected/OnClientDisconnected` keep reading the old, already-disposed `PitAuthority`/`PitWorld` for the whole async rebuild (`BuildRoutine` yields while the Random fill loads, and `controller.Update` still calls `netPit.HostUpdate`). A join, leave, intent or Hello in that window hits a disposed world, and a joiner would be sent the old header. | `NetPit.BeginRebuild()` plus a private `rebuilding` flag guarding those entry points; `RebindAuthority` clears it and sends the new header to every link. |
| G2 | §4.4's client hook `DisposeClientPitForRebuild() => DisposePit()` leaves the controller's `Environment`, `activeSite`, `overviewCamera` and `prepareClientSettings` pointing at the destroyed pit, so the next client build would use the wrong anchor and environment. | New `BallPitFinalController.RebindClientSite(env, site, overview, prepareClientSettings)`, called between dispose and `ResetClientWorldForRebuild`. `DisposeClientPitForRebuild` stays exactly `=> DisposePit()`. |
| G3 | After a rebuild the adapter's host state is stale: `fillAtBuild` (so a save would write pit 0's FILL and `SiteId` after pit 2), `ActiveEnvironment`, and `slotMirror`'s `IHopperOps` bound to the disposed authority. | `BallPitGameAdapter.OnCampaignPitRebuilt(PitSiteEnvironment)`, `PitSlotMirror.RebindOps`, `HopperOwnership.ForgetParked`. |
| G4 | `Object.Destroy` is deferred and `PitSites.FindAll()` uses `FindObjectsOfType<PitSite>(true)`; calling `SelectAndActivate` right after swapping prefabs can return the old site or drop one as a duplicate siteId. | On advance the director does not call `SelectAndActivate`: it deactivates and destroys the old instance and calls the public `PitSites.Activate(site, new List<PitSite>{site})`. `FindAll` is only used for the first pit, before any swap. |
| G5 | Offline and dev-host `GameplayCore` is built with `gameState == null`, so money and upgrade levels are per-core and a new core after a rebuild resets them (§1.6 assumed every path used `adapter.GameState`). | `GameplaySession` keeps a campaign-only `localState = new BallPitGameState()` and passes it instead of null when a director exists. Sessions already persist via `adapter.GameState`. |
| G6 | `DepositDriver.CheckWin` (which calls `onWin`) runs inside `RunStepListeners` inside `FixedUpdate`; disposing the world from that stack would destroy the authority mid-step. | `OnActivePitDrained` only starts a coroutine whose first statement is `yield return null`. |

Related refinements recorded in `DECISIONS-D.md`: D1 (namespace `BallPitGameplay`, not `.Campaign`), D4
(`IPitTransition.Play(IEnumerator work)` instead of `Play()`), D5 (resume via the siteId-keyed gated
`EnsureFirstPit`, the participant only reconciles), D6 (the full NetPit seam set and why not Plan B).

Not verifiable here and unchanged in status: the networked rebuild round trip, NGO weaver acceptance of
`CampaignNet`, whether a mid-run joiner already holds `CampaignNet` at `AttachClient` time, and the resume path.
These are owner in-editor checks (`HANDOFF-D.md` §4 and §7).
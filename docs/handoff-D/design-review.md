# Design Review: DESIGN-D (CampaignDirector), round 4 (re-verification of F1-F5)

Scope: narrow. Verify only that round-3 findings F1-F5 were correctly applied in DESIGN-D
revision 4. Settled architecture was not reopened. No Unity compile exists on this machine
and none is claimed. All traces below were read directly from the worktree sources
(`.worktrees/campaign-director/Assets/...`). Note: the repo-wide grep tool does not index
`.worktrees` (gitignored), so sources were read through the shell instead.

Verdict: 0 HIGH, 0 MEDIUM, 0 NIT open. **APPROVED.**

## Finding-by-finding verification

### F1 [MEDIUM]: Hello re-send trigger pinned. APPLIED CORRECTLY.
Source check (`Assets/_Prototypes/BallPitFinal/NetPit.cs`):
- `private void SendHello()` at line 472. It writes `Protocol` and sends `MsgHello` to
  `NetworkManager.ServerClientId`, `ReliableSequenced`. Private, so only reachable from
  inside `NetPit`. Matches the design's description.
- `private void OnHello` (line 187): `if (links.TryGetValue(sender, out var link) && !link.streaming) SendHeader(link);`
  Matches the design's quoted trace.
- `public readonly bool IsHost` (line 51), so the design's `if (IsHost) return;` guard on the
  client-only method is valid.
- `OnHeader` guard (line 483): `if (HeaderPending || CW != null) return;` which is the reason
  the direct header is dropped until the client resets. Matches.
- No existing `RebindAuthority` / `ResetClientWorldForRebuild` in the file, so both are
  genuinely additive.

Design check (DESIGN-D.md):
- Lines ~383-404: `ResetClientWorldForRebuild()` body ends with `SendHello();` marked
  "FINAL step", with the statement that `SendHello()` is private and therefore the re-send
  lives only here.
- Line ~469-472: "The trigger is pinned (REV 4)... There is no 'or the director triggers it'
  alternative". The alternative is deleted (the only remaining mention is the explicit
  deletion note).
- Line ~367-374: RebindAuthority step 3 `SendHeader(link)` is labelled a BEST-EFFORT FAST
  PATH, the Hello-driven re-send is named the AUTHORITATIVE closer, and implementers are told
  not to skip the Hello wiring.
- `SendHello()` is added to the bounded-change list (lines ~1092-1097, ~1352, and the file
  list item 11 at ~899) and the in-editor check is reworded.

### F2 [MEDIUM]: Resume ordering corrected, ONE mechanism chosen. APPLIED CORRECTLY.
Source check:
- `BallPitFinalController.Session.cs`: `BeginHost` -> `StartCoroutine(BuildRoutine(..., (ok,msg) => OnHostBuilt(r, ok, msg)))`
  (line 150); `OnHostBuilt` (line 155) runs `r.Restore?.Invoke(world, authority)` (line 159),
  then `NetPit.StartHost`, then `MarkWorldReady()` (line 165). So Restore runs inside the first
  BuildRoutine, after scene `Awake`. The design's corrected trace matches.
- `BallPitGameAdapter.AttachHost` (lines 157-200): `save = ResolveSave(context.ResumeSave)` is
  resolved BEFORE `PitSites.SelectAndActivate(SiteSelector, ..., resume ? save.Fill.SiteId : null, ...)`
  (line 167). So `save.Fill.SiteId` is in hand strictly before `FindAll`, which supports the
  design's chosen gate point. `ResumeSiteMismatch(save, site, profile)` (line 186) and
  `restore = (w,a) => RunRestore(save,w,a)` (line 200) confirm the design's reason for
  rejecting option (b): a throwaway entry[0] build on resume would fail `ResumeSiteMismatch`
  and `FailHostSetup(true, ...)` before any swap could run.
- `AttachClient` (line 304-306) calls `SelectAndActivate(..., null, ...)` with no save, matching
  the design's `EnsureFirstPit(null)` on the client.

Design check:
- §7 (line ~763-806): the false "available before the director's Awake" claim is explicitly
  deleted. ONE mechanism is chosen: (a) gate the first instantiate, keyed on the save's
  `Fill.SiteId` (not the participant), via `EnsureFirstPit(resume ? save.Fill.SiteId : null)`
  directly before `SelectAndActivate`. Option (b) is explicitly rejected with a trace-based
  reason that I verified above.
- §2 item 2 and §10: `Awake` instantiates nothing; `EnsureFirstPit` is the single first
  instantiate; consistent with §7 and the file-list entry (~881-887). No "or" language remains.
- The participant's `Restore` is repurposed to `OnIndexRestored` reconciliation (no rebuild),
  with defined fallbacks (no match -> Warning + entry[0]; duplicate SiteId -> first match,
  Warning in the validator).
- Resume remains NICE-TO-HAVE (§16 Q2), consistent with the owner-facing scope.

### F3 [NIT]: PitSite lookup standardized. APPLIED.
`GetComponentInChildren<PitSite>(true)` is used consistently at §9 item 1 (~837), §12
(~1037, 1042, 1051), and EnsureFirstPit (~779, 944), with the explicit statement that the
`PitSite` may be on the root or any child. No bare `GetComponentInChildren<PitSite>()` remains.

### F4 [NIT]: `settings = newSettings` in RebuildActivePit. APPLIED.
§4.3 step 1 (~332) reads `settings = newSettings; Environment = newEnv; ...` with the
justification that it mirrors `BeginHost` (`settings = r.Settings;`, Session.cs line 148,
verified). `settings` is a public field of the same partial class, so assignable.

### F5 [NIT]: Direct SendHeader labelled best-effort. APPLIED.
Covered under F1: step 3 text at ~367-374 describes the direct send as a fast path and the
Hello-driven re-send as authoritative. Also summarized at ~1394 and in the checklist at ~1094.

## Verified Assumptions
- NetPit: `SendHello` private (line 472), `OnHello` private and gated on `!link.streaming`
  (lines 187-189), `IsHost` public readonly (51), `OnHeader` dedupe guard (483), no existing
  rebind/reset methods.
- BallPitFinalController.Session.cs: Restore runs in `OnHostBuilt` (159) before
  `NetPit.StartHost` and `MarkWorldReady` (165); `settings = r.Settings` at 148.
- BallPitGameAdapter.AttachHost: save resolved before `SelectAndActivate` (163-167);
  resume branch uses `ResumeSiteMismatch` and `RunRestore`; AttachClient passes no save.

## Unverified / Wrong Assumptions
- None found within the narrow scope.
- Not verifiable here and correctly flagged by the design as `[owner in-editor check]`: the
  runtime network round-trip (RebindAuthority + ResetClientWorldForRebuild + Hello re-send),
  mid-run joiner seeing `CampaignNet.Instance`, and resume-at-entry-k behavior. These need
  the Unity editor and a second peer and are not defects.

## Findings
None open. (HIGH: 0, MEDIUM: 0, NIT: 0.)

Verdict: **APPROVED**

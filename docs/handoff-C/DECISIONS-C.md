# DECISIONS-C — Kiddie-Pool Vertical Slice (deposit feel + visible fill)

Design decisions made for Deliverable C, with the owner questions that drove them at the
top. Authoritative spec: `BRIEF-C-kiddie-pool-slice.md`; approved design: `.agents/DESIGN-C.md`;
binding game direction: `.kiro/steering/game-design.md` (§2, §2b, §1b/§1c, §9b).

No Unity compiler runs on the agent machine — every decision was traced against the real
worktree sources; the OWNER compiles and play-tests.

## Owner questions (decide before/while play-testing)

These are the open calls from DESIGN-C's header. The chosen default is implemented; each is
cheap to change after a playtest.

- **Q1 — Fill-visual style for the placeholder.** CHOSEN: a **shaped, scaled colored mesh**
  (`MassFillView`) as the default — one child `MeshRenderer` whose local Y scale/position
  lerps inside a fill-volume box, O(1) in sold count, no new compute/shader. A literal
  instanced "clump of balls" (`InstancedBallFillView`) ships as an opt-in alternative,
  unassigned. Confirm you want the shaped mesh as default, or make the instanced clump the
  default per area.
- **Q2 — Does the thrown ball settle as a REAL ball, or convert on cross?** CHOSEN:
  **settle-briefly-then-convert** — the ball you threw stays a real active ball, physically
  lands in the container, then is quietly demoted into the fill after a short dwell. Honest
  "I saw my ball land" feel; costs at most a handful of real balls (hands-only, one thrower).
- **Q3 — Dwell before a landed ball converts.** CHOSEN: ~0.6 s OR on-rest (smoothed speed
  below a small threshold), whichever first. `SettlingDeposits.DefaultDwellSeconds = 0.6f`,
  stillness threshold `0.15`. Pure feel tuning.
- **Q4 — Kiddie-pool ball count + dimensions.** CHOSEN: `pitX=10, pitY=2, pitZ=10 = 200`
  balls at `ballDiameter = 0.5` m — a shallow, wide pool. Added by the `Create Kiddie
  Profile` menu item as DATA in the VenueConfig (no hardcoded size). Confirm the count and
  whether 0.5 m balls look right in a kids' pool (the profile carries its own ballDiameter if
  you want them smaller there).
- **Q5 — Keep dev-only keys for the shop/counter?** CHOSEN: keep BOTH behind documented dev
  flags, OFF by default — `ShopPanel.devToggleKey` (Tab opens the shop for station-less
  testing) and `GameplayHud.showColorCountsDev` (re-shows the old per-color counter). Say if
  you'd rather they were deleted outright.
- **Q6 — Colorblind for the fill.** CHOSEN: the fill reads the SAME post-colorblind display
  color the balls use on THIS peer (client-local). Implemented by `PaletteApplier.DisplayColor`
  (the applied, colorblind-mapped color), pushed to `MassFillView.SetColor` by GameplaySession.

## Design decisions (summary — full rationale + traces in DESIGN-C.md)

1. **Deposit = sell-on-cross, settle-after.** `DepositDriver.Sell` no longer demotes on
   cross; the credit calls (`attribution.Consume` with the on-cross pos, `core.AddSale`,
   `container.NotifySold`) fire exactly as before, and a new host-only `SettlingDeposits`
   helper demotes the ball (the pre-existing `PitAuthority.Deposit`) after the dwell. The win
   latches on the step the final dwell completes (the settler is ticked every step, before
   `CheckWin`). No pit/NetPit/collapse change — only an existing demote relocated in time.

2. **Visible container fill = a cheap colored mass.** `MassFillView` (default) drives only a
   dedicated child mesh inside a fill-volume box — NEVER the container root (the regression
   that flattened containers and sank them under the deck). The fill fraction comes from the
   already-replicated ledger (`readModel.SoldFraction`), exact on every peer with no extra
   traffic; the color comes from `PaletteApplier.DisplayColor` so it matches the balls incl.
   colorblind. `DefaultFillView` stays as the inert fallback.

3. **Sale juice reuses the existing events.** `ContainerSaleFx` subscribes to the container's
   existing `Sold` C# event (fired on cross) and plays a placeholder clip + optional particle
   pop at `IntakeWorldCenter`. No parallel path, no new events.

4. **In-world UpgradeStation replaces the Tab shop.** A `Terminal` `IInteractable` the player
   aims at and activates; it opens `ShopPanel`. The shop OPENS only via the station (Tab is
   behind the dev flag); it CLOSES via the panel's own direct Escape read and the station's
   proximity auto-close — both run while the cursor is claimed, where the interactor is
   blocked. The kiddie pool places NO station (tutorial has no upgrades).

5. **Per-color counter HUD removed.** `GameplayHud` no longer draws the per-color counter by
   default (progress is read from scenery — the shrinking pit + filling containers). Money,
   carry, and the feed stay. The old block is behind `showColorCountsDev`.

6. **Carry-2 first-ball throw fix.** The next hopper ball now promotes ~1.1 ball diameters
   BEHIND the hand along the aim (`ArmsTool.PromoteOffset`), so it no longer interpenetrates
   the just-thrown ball and knocks it sideways. No `PitAuthority` change.

7. **Kiddie-pool authoring is DATA + two menu helpers, not a special mode.** Above-ground is
   just an anchor at floor level; the forced-high walls are the INVISIBLE collision proxies
   (always ≥ fill WallTop) — the authored VISIBLE pool wall is independent art and can stop at
   the ball surface. `rimHeight` tunes the proxy, not the look. The `Create Kiddie Profile`
   and `Create Placeholder Container` menu items seed the data/prefab; the owner authors the
   scene per HANDOFF-C.

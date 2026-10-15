# Phase A — Unity editor setup (players + venue)

The Phase A CODE is integrated (36 files under `Assets/`). This is the editor work to
make it run: layers, assets, the player prefab, a venue scene with pit "sites", and kit
registration. Self-contained so you can hand it to Claude. Engine: Unity 2022.3.14,
URP 14, NGO 1.7.1, Steamworks.NET. Full reference: the delivery's `HANDOFF-A.md` and
`DECISIONS.md` (in `_review_phaseA/A/`).

## What this gets you
First-person networked players in the pit: walk, look, jump, climb walls/mantle, stand
on the ball pile, grab/throw balls by looking at them. Up to 4 players see each other.
The pit lives in an authored "venue" (a pool in a scene you build). NOT a game yet
(no containers/selling/money — that's Phase B).

## Order: do 1-4 first (compile + core), then 5-7 (venue + prefab), then test.

---

## 1. Compile first
Open the project, open the Console. The old Phish player scripts (`PlayerMovement`,
`SpawnManager`, `ThirdPersonCamera`, etc. in `Scripts/Game`) are now dead but harmless
(different names, no collision) — leave them until A compiles, then they can be deleted.
Expect possible errors in the risky NGO/Steam calls HANDOFF-A §8 lists — paste any to Kiro.
Do not proceed to scene work until the Console is clean.

## 2. Layers + physics matrix (Project Settings > Tags and Layers)
Add two user layers with these EXACT names (code resolves by name):
- `Player`
- `PlayerBody`

Physics matrix (Project Settings > Physics, collision matrix):
- `Player` collides with: Default/venue art, pit-proxy layer (default 0), ball (8), Player. (Player×ball MUST be on.)
- `PlayerBody` collides with: nothing (uncheck all).
- The ball layer stays 8.
`PlayerLayers.Validate` logs an Error if Player is missing or Player×ball is off; a Warning for Player×Player off or missing PlayerBody.

## 3. Create the default assets (menu items the code added)
- `Tools > Ball Pit > Create Default Venue Config` → makes `Assets/Game/BallPitGameplay/Resources/BallPitVenueConfig.asset` (the 5 pit sizes). Loaded automatically.
- `Tools > Ball Pit > Create Default Ball Visual Config` → `Assets/Game/BallPitGameplay/Config/BallVisualConfig.asset`. Assign it to the `BallPitFinalController.ballVisuals` field (null = today's look).
- `Ball Pit > Player > Create Default PlayerConfig` → `Assets/Game/BallPitGameplay/Player/PlayerConfig.asset`.
- Create a physics material `Assets/Game/BallPitGameplay/Player/PlayerZeroFriction.physicMaterial`: Dynamic/Static Friction 0, Bounciness 0, Friction Combine = Minimum, Bounce Combine = Minimum.

## 4. Build the player prefab  `Assets/Game/BallPitGameplay/Player/Player.prefab`
Root GameObject `Player`, layer `Player`:
- `NetworkObject` (defaults)
- `ClientNetworkTransform` (the existing one in `Scripts/Multiplayer/NGO`): Sync Position XYZ on; Sync Rotation Y only (X/Z off); Interpolate on.
- `Rigidbody`: mass 60, drag 0, angular drag 0, Use Gravity OFF, Is Kinematic ON, Interpolate None, Collision Detection = Continuous Speculative, Freeze Rotation XYZ.
- `CapsuleCollider`: Direction Y, center (0,0.9,0), radius 0.35, height 1.8, material = PlayerZeroFriction.
- `PlayerSlot`, then `PlayerCapabilities` — BOTH ABOVE `PlayerAvatar` in component order (NGO calls OnNetworkSpawn in order).
- `PlayerAvatar`: set `config` = PlayerConfig.asset. (Reset auto-fills refs, or they resolve in Awake.)
- `PlayerMotor`, `PlayerClimber` (no fields).
- `PlayerView`: wire head, body, holdPoint, headCamera, audioListener; `hudPrefab` = PlayerHud.prefab (step 4b); ownerBodyMode = ShadowsOnly.
- `PlayerInputReader`: legacy axes Horizontal/Vertical/Mouse X/Mouse Y, degreesPerMouseUnit 2, keys Space/LeftShift/R/E/Tab, mouse 0/1.
- `PlayerInteractor`: interactableMask = Nothing (Phase B sets it).

Children:
- `Body` (layer `PlayerBody`, no components) → child `ModelSlot` (layer PlayerBody; drop a placeholder capsule mesh here for testing).
- `Head` (layer `Player`, local pos (0,1.62,0)):
  - `Camera`: ENABLED OFF, Near 0.05, Far 400, Clear Flags Skybox. Add `UniversalAdditionalCameraData` (Render Type Base, Post Processing on).
  - `AudioListener`: ENABLED OFF.
  - child `HoldPoint` at local (0.25,-0.28,0.55), no components.

After building, run `Ball Pit > Player > Apply PlayerConfig to Selected Prefab` to write capsule/head/holdpoint from the config.

### 4b. PlayerHud.prefab  `Assets/Game/BallPitGameplay/Player/PlayerHud.prefab`
Canvas (Screen Space Overlay, Sort Order 10), CanvasScaler (1920×1080, match 0.5), `PlayerHud` component. NO GraphicRaycaster, NO EventSystem.
- child `Crosshair`: RectTransform centered 8×8, Image → PlayerHud.crosshair.
- child `Hint`: bottom-center, TextMeshProUGUI (empty, centered) → PlayerHud.hint.

## 5. Gameplay scene objects (your existing BallPitGame scene)
Must exist OUTSIDE any site root:
- `BallPitFinalController` (already there). Assign `ballVisuals`. Keep `devToolsInSession` OFF for shipping.
- `PlayerSpawner`: set `playerPrefab` = Player.prefab's NetworkObject (SAME asset you register with the kit in step 7).
- **An authored Directional Light** (IMPORTANT: a venue/site scene gets no auto light — without one the pit is unlit. HANDOFF-A NIT 12).
- Remove: any extra Camera/AudioListener (a new scene's Main Camera), the old Phish `SpawnManager` and third-person camera objects, any NetworkManager.

## 6. Author a venue (at least one PitSite)
For each pit size you want to ship, add a `PitSite` (anywhere in the scene):
- `siteId`: unique, lowercase `[a-z0-9_.]`, 1-32 chars. Never rename a shipped id.
- `profileKey`: a VenueConfig size (small/medium/large/huge/giant).
- `anchor`: a Transform at the pool floor center, UNROTATED.
- `siteRoot`: (optional) a GameObject toggled on only when this site is active (holds the pool art, spawn points).
- `venueBounds`: box (relative to anchor) covering everywhere balls can be. Use the PitSite context-menu **Fit Venue Bounds To Pit**.
- `rimHeight`: pool rim height above floor.
- `overviewCamera`: (optional) a disabled camera for the loading view.
Then: model the pool around the cyan interior gizmo. REMOVE any art collider inside that interior (validator Error). Add `PlayerSpawnPoint`s (≥4 per site), optional `OutOfBoundsVolume`s and `ClimbableSurface`s.
For a first test you can author just ONE site matching the lobby's default size (`large`).
Run `Tools > Ball Pit > Validate Venue` until 0 errors.

## 7. Register the player prefab with the kit
- Open the `NetworkPrefabsList` asset assigned to `MultiplayerKitBootstrap.networkPrefabs` (in the MainMenu scene). Add `Player.prefab`.
- Leave `MultiplayerKitBootstrap.playerPrefab` EMPTY (setting it would double-spawn).

## 8. Test (host single-player first, then with a friend)
Host-only first (editor):
- New Game → loading view → you spawn first-person at a spawn point, camera takes over.
- WASD walk, mouse look, Space jump; walk to the rim and drop into the pit; stand on the pile; climb a wall; mantle out.
- Aim at a ball (crosshair tints, ball highlights) → LMB grab → it rides lower-right → LMB drop / RMB throw.
- Press L (debug build + devToolsInSession) → check=OK.
Then with a friend (second real machine): both spawn, see each other move, both grab/throw; `check=OK` on both.
Full checklist: HANDOFF-A §10.

## Known issues (don't be alarmed)
- MEDIUM: while CARRYING a ball on the host, mantle/step-up can stall (held ball blocks the clearance probe). Fix pending. Carrying + walking is fine.
- A site-less scene is unlit until you add a light (step 5) — HANDOFF-A NIT 12.
- Several NITs in HANDOFF-A §11; none blocking.

## After it works
- Delete the dead Phish player scripts (`Scripts/Game/PlayerMovement.cs`, `SpawnManager.cs`, `ThirdPersonCamera.cs`, and if unused `PlayerReadyHandshake.cs`, `PlayerProgressService.cs`, `PlayerContracts.cs`, `SteamPlayerIdentityProvider.cs`) — tell Kiro and I'll remove them cleanly (they reference each other).
- Phase B (containers/sell/money/upgrades) is the next build.

# Phase 3 — Unity editor setup for the MultiplayerKit lobby

You are helping build the Unity scenes/prefabs for a co-op game's lobby/menu system.
All C# is already written and integrated in the project; this is pure Unity-editor
work (scenes + uGUI prefabs + wiring serialized fields). You do NOT have the repo —
everything you need is in this document. The user is in the Unity editor and will do
the clicking; give precise, ordered instructions and the exact inspector field names.

Engine: Unity 2022.3.14 LTS, URP 14.0.9, Netcode for GameObjects 1.7.1,
Steamworks.NET, legacy Input Manager, uGUI + TextMeshPro.

## IMPORTANT: build it FUNCTIONAL, not pretty (polish is a separate later phase)
This pass is about getting the menu/lobby WORKING, not looking good. Build plain
uGUI (default buttons, gray panels) and get the smoke test + multiplayer test to
pass. Do NOT spend time on art, theming, animations, custom sprites, or fancy
layout now — the game will get a dedicated UI art pass later, and the view scripts
separate logic from appearance (they only reference components, not their look), so
the whole thing can be restyled later WITHOUT touching code. Styling now = styling
twice. Keep it minimal: a consistent font (the project's Montserrat SDF), readable
text, sane spacing. That's it.

## Architecture (so you understand what you're wiring)
A reusable "MultiplayerKit" package provides a lobby/session system: main menu →
lobby ROOM (create, invite, settings, ready) → Start → loads a gameplay scene →
host-authoritative netcode. It is UI-driven: plain-C# "presenters" hold the logic,
uGUI "views" (MonoBehaviours with [SerializeField] fields) are dumb and must be
wired in prefabs. There is NO code-only way to drive the menu; the prefabs are
required. The kit creates its own NetworkManager in code at startup, so NO scene
may contain a NetworkManager (the bootstrap throws if it finds one). Only one
EventSystem may exist, and it lives on the KitOverlay prefab.

Scene already set up by the user (menu scene "MainMenu"):
- Main Camera
- SteamManager (Steamworks.NET MonoBehaviour)
- BallPitKitInstaller (fields: gameplaySceneName="BallPitGame", menuSceneName="MainMenu")
- MultiplayerKitBootstrap (fields left empty)
- SaveManager (per-player save, harmless, kept)
Gameplay scene "BallPitGame" = the existing ball-pit scene, Build index 1; MainMenu
is Build index 0. No NetworkManager/EventSystem/old-lobby objects in either.

## Build order (IMPORTANT: build the CORE set first, test, THEN the rest)
Core set (enough for New Game → lobby room → set settings → Start → gameplay):
1. KitOverlay prefab
2. The 4 SettingRow prefabs + RosterRow prefab (needed by the room)
3. MenuCanvas prefab with just the MainMenu + LobbyRoom screens
Then SMOKE TEST (see end). Only after it works, add FriendsGames, PublicBrowser,
LoadGame screens + their row prefabs.

## Common conventions
- Canvas: Screen Space - Overlay, Canvas Scaler = Scale With Screen Size, reference
  1920x1080, match 0.5, Graphic Raycaster.
- All text = TextMeshProUGUI; inputs = TMP_InputField; dropdowns = TMP_Dropdown;
  avatars = RawImage. Every panel that shows/hides has a CanvasGroup.
- Put prefabs in Assets/MultiplayerKit/UI/Prefabs/.
- Every view script validates its serialized fields on enable and logs an error if
  any are null — so wire ALL listed fields or the screen won't bind.

---

## PREFAB 1 — KitOverlay.prefab  (Canvas Sort Order 100, will be DontDestroyOnLoad at runtime)
Root GameObject "KitOverlay": Canvas (Sort Order 100) + Canvas Scaler + Graphic
Raycaster + script **KitOverlay**.
Children:
- **EventSystem**: EventSystem + StandaloneInputModule. (The ONLY EventSystem in the project.)
- **BusyOverlay** (CanvasGroup, dim Image, script **BusyOverlayView**):
  children: Spinner (Image), Message (TMP), CancelButton (Button + child Label TMP).
  BusyOverlayView fields: group (CanvasGroup), spinner (RectTransform),
  spinnerDegreesPerSecond=270, message (TMP_Text), cancelButton (Button).
- **Toast** (CanvasGroup, bg Image, Button, script **ToastView**): child Text (TMP).
  ToastView fields: group, background (Image), text (TMP_Text), dismissButton (Button),
  infoSeconds=3, warningSeconds=5, errorSeconds=6, infoColor, warningColor, errorColor,
  fadeSeconds=0.2.
- **SessionMenu** (CanvasGroup, dim Image, script **SessionMenuView**) — KEEP THIS
  GAMEOBJECT ACTIVE in the prefab (its Awake registers a static instance). Children:
  Title (TMP), buttons ResumeButton/InviteButton/SaveNowButton/SaveAndQuitButton/
  LeaveButton (+ label TMPs), and a Roster (RosterView, showReadyColumn=false) with
  Viewport→Content. SessionMenuView fields: group, resumeButton, inviteButton,
  saveNowButton, saveAndQuitButton, leaveButton, leaveLabel (TMP_Text),
  roster (RosterView), openWithKey=true, toggleKey=Escape.
- **PasswordPrompt** (CanvasGroup, dim Image, script **PasswordPromptView**):
  Window with Title, Body, ErrorLine, PasswordInput (TMP_InputField, content type
  Password, char limit 32), OkButton, CancelButton. Fields: group, title (TMP_Text),
  body (TMP_Text), errorLine (TMP_Text), passwordInput (TMP_InputField), okButton, cancelButton.
- **ConfirmDialog** (CanvasGroup, dim Image, script **ConfirmDialogView**):
  Window with Title, Body, ConfirmButton(+Label), CancelButton(+Label). Fields:
  group, title (TMP_Text), body (TMP_Text), confirmButton, confirmLabel (TMP_Text),
  cancelButton, cancelLabel (TMP_Text).
- **ErrorPopup** (CanvasGroup, dim Image, script **ErrorPopupView**) — KEEP IT THE
  LAST sibling (draw order; it must appear above everything). Window with Title,
  Message, OkButton. Fields: group, message (TMP_Text), okButton (Button).
Root **KitOverlay** fields: busy, toast, sessionMenu, passwordPrompt, confirmDialog, errorPopup.
Save as prefab, instance it into MainMenu. Remove any other EventSystem from the scene.

## PREFAB 2 — SettingRow prefabs (4 variants) + SettingGroupHeader
Each row: a GameObject with HorizontalLayoutGroup + LayoutElement(minHeight ~48).
Base fields on ALL four (inherited): label (TMP_Text), lockIcon (GameObject).
- **SettingRow_Toggle.prefab**, script **ToggleSettingRow**: + toggle (Toggle).
  Children: Label (TMP), LockIcon (GameObject), Toggle.
- **SettingRow_Slider.prefab**, script **SliderSettingRow**: + slider (Slider),
  valueText (TMP_Text), commitDelay=0.25. Children: Label, LockIcon, Slider, Value (TMP).
- **SettingRow_Dropdown.prefab**, script **DropdownSettingRow**: + dropdown (TMP_Dropdown).
  Children: Label, LockIcon, Dropdown.
- **SettingRow_Input.prefab**, script **InputSettingRow**: + input (TMP_InputField, single line).
  Children: Label, LockIcon, Input.
- **SettingGroupHeader.prefab**: a bold TextMeshProUGUI (~28pt) + LayoutElement(minHeight 40).

## PREFAB 3 — RosterRow.prefab
GameObject with HorizontalLayoutGroup + LayoutElement(minHeight 64), script **RosterRowView**.
Children: Avatar (RawImage 56x56 + script **AvatarView**), Name (TMP), YouTag (TMP "You"),
Crown (Image), Ready(root GameObject → Icon Image + Text TMP), KickButton (Button + child Icon).
RosterRowView fields: avatar (AvatarView), nameText (TMP_Text), youTag (GameObject),
crown (GameObject), readyRoot (GameObject), readyIcon (Image), readyText (TMP_Text),
readySprite (Sprite), notReadySprite (Sprite), kickButton (Button).
AvatarView fields: image (RawImage), placeholder (Texture).

## PREFAB 4 — MenuCanvas.prefab  (Canvas Sort Order 0)
Root "MenuCanvas": Canvas (Sort Order 0) + Canvas Scaler + Graphic Raycaster + script **MenuRouter**.
MenuRouter fields: mainMenu, friendsGames, publicBrowser, loadGame, lobbyRoom (each is the
CanvasGroup of that screen), backKey=Escape.
NOTE: MenuRouter requires ALL FIVE CanvasGroup fields non-null to validate. For the core
test you still need all five screen objects to EXIST (each a child with a CanvasGroup),
but FriendsGames/PublicBrowser/LoadGame can be near-empty placeholder panels for now
(an empty GameObject + CanvasGroup) — their view scripts only bind when shown, and you
won't navigate to them during the core smoke test. Build MainMenu + LobbyRoom fully.

Children (screens, each with CanvasGroup):
- **MainMenuScreen**, script **MainMenuView**: buttons NewGame, Continue (+ a subtitle
  TMP), Load, FriendsGames, BrowsePublic, Quit. Fields: newGameButton, continueButton,
  continueSubtitle (TMP_Text), loadButton, friendsGamesButton, browsePublicButton,
  quitButton, router (the MenuRouter).
- **LobbyRoomScreen**, script **LobbyRoomView**:
  Header texts: LobbyName, Privacy, PlayerCount, ResumeLabel (all TMP).
  Body: a RosterPanel (contains a RosterView: rowPrefab=RosterRow, content=a RectTransform,
  showReadyColumn=true) and a SettingsPanel (contains a SettingsPanelView).
  ActionBar: LeaveButton, InviteButton, ReadyToggle (Toggle), StartBlockedReason (TMP),
  StartButton. LobbyRoomView fields: lobbyName, privacy, playerCount, resumeLabel (TMP),
  roster (RosterView), settingsPanel (SettingsPanelView), leaveButton, inviteButton,
  readyToggle (Toggle), startBlockedReason (TMP_Text), startButton (Button).
  - **RosterView** (on the roster sub-object) fields: rowPrefab (RosterRowView=the RosterRow
    prefab), content (RectTransform under a Viewport), showReadyColumn=true.
  - **SettingsPanelView** (on the settings sub-object) fields: content (RectTransform),
    groupHeaderPrefab (SettingGroupHeader TMP), togglePrefab (ToggleSettingRow),
    sliderPrefab (SliderSettingRow), dropdownPrefab (DropdownSettingRow),
    inputPrefab (InputSettingRow).
- **FriendsGamesScreen / PublicBrowserScreen / LoadGameScreen**: for the core test, just a
  GameObject + CanvasGroup each (placeholders). Build them fully later (fields below).
Save as prefab, instance into MainMenu.

Drag KitOverlay and MenuCanvas instances into MainMenu. Confirm KitOverlay has the only EventSystem.

---

## SMOKE TEST (do this once the core set above is built)
Play the MainMenu scene. Expect in the Console: the kit boots, "KitRuntime published",
and NO "remove the NetworkManager in scene" exception. The main menu shows.
- Click New Game → a LOBBY ROOM appears (NOT the game), you are in the roster, the
  settings panel has rows.
- Change a setting or two. Click Start → it loads BallPitGame, builds the pit, starts
  host netcode. You are in the ball pit.
- In the pit, press L → the log line should include check=OK.
If a screen fails to appear or logs "field is null", a serialized field on that view
is unwired — fix the named field. If it throws about a NetworkManager in the scene,
delete the NetworkManager object. Report the exact Console error to the user's Kiro.

## LATER screens (after the smoke test passes)
- **FriendsGamesView** fields: rowPrefab (FriendLobbyRowView), content (RectTransform),
  refreshButton, refreshSpinner (GameObject), emptyMessage (TMP_Text), backButton, router.
  FriendLobbyRow.prefab → FriendLobbyRowView: avatar (AvatarView), friendName, lobbyName,
  playerCount, privacy (all TMP), passwordIcon (GameObject), status (TMP), joinButton.
- **PublicBrowserView** fields: rowPrefab (PublicLobbyRowView), content, refreshButton,
  refreshSpinner, message (TMP), backButton, distanceDropdown (TMP_Dropdown),
  regionDropdown (TMP_Dropdown), hideFullToggle, hidePasswordToggle, hideInProgressToggle,
  filterPrefab (BrowserFilterRowView), settingFiltersContent (RectTransform),
  filterDebounceSeconds=0.3, router.
  PublicLobbyRow.prefab → PublicLobbyRowView: nameText, settingsLine, region, playerCount
  (TMP), passwordIcon (GameObject), status (TMP), joinButton, ownerAvatar (AvatarView, optional).
  BrowserFilterRow.prefab → BrowserFilterRowView: label (TMP), dropdown (TMP_Dropdown).
- **LoadGameView** fields: rowPrefab (SaveSlotRowView), content, message (TMP), backButton, router.
  SaveSlotRow.prefab → SaveSlotRowView: nameText, details, settingsLine (TMP),
  versionWarning (GameObject), versionText (TMP), loadButton, deleteButton.

## Known non-issues (don't chase)
- At 1M balls, fps drops when the camera is close to the pit — it's shader fill cost,
  not a bug.
- A kick/disconnect reason may show "connection lost" instead of the specific reason
  over the Steam transport — cosmetic; the session still ends correctly.

## Namespaces (for reference)
Kit view scripts are in namespace `MultiplayerKit.UI`; core types in `MultiplayerKit`;
the adapter/installer in `BallPitSession`. All are in or auto-referenced by
Assembly-CSharp, so no using-directive gymnastics are needed in the editor.

# Project Structure

```
Assets/
├── Scenes/
│   ├── Main Menu.unity          # Lobby UI, host/join flow
│   └── Game Scene.unity         # In-game fishing scene
│
├── Scripts/
│   ├── Game/
│   │   ├── GameSessionBootstrap.cs          # Wires SteamLobby.OnSessionReady to game init
│   │   └── Fishing/
│   │       ├── FishingPlayerProgressService.cs   # IPlayerProgressService impl (PlayerPrefs stub)
│   │       └── FishingProgressHandshake.cs       # NetworkBehaviour; gates gameplay via INetworkSpawnPolicy
│   │
│   ├── Multiplayer/                         # Reusable multiplayer core (portable across projects)
│   │   ├── Core/
│   │   │   ├── MultiplayerContracts.cs      # All shared interfaces: IMatchDiscovery, ISessionBootstrap, etc.
│   │   │   ├── LobbyUiPresenter.cs          # Binds IMatchDiscovery events to LobbyPlayerList UI
│   │   │   ├── SteamLobbyDiscoveryAdapter.cs
│   │   │   └── SessionState/
│   │   │       ├── MultiplayerSessionState.cs        # Enum: Idle → Discovering → Joining → SyncingPlayerData → InSession → Leaving
│   │   │       └── MultiplayerSessionStateMachine.cs # Enforces valid state transitions
│   │   ├── NGO/
│   │   │   ├── NgoSessionCoordinator.cs     # ISessionBootstrap impl; wraps NetworkManager host/client start
│   │   │   ├── NetworkMovementSync.cs       # NetworkBehaviour; server-authoritative movement with client prediction
│   │   │   └── NetworkVoiceRouter.cs        # NetworkBehaviour; routes compressed voice via ServerRpc → ClientRpc + VoicePacketBus
│   │   ├── Steam/
│   │   │   ├── SteamLobbyDiscoveryService.cs  # IMatchDiscovery impl; wraps SteamMatchmaking callbacks
│   │   │   ├── SteamInviteService.cs
│   │   │   └── SteamPlayerIdentityProvider.cs # IPlayerIdentityProvider impl; returns SteamID as string
│   │   └── Samples/
│   │       └── MultiplayerSampleBootstrap.cs  # Minimal direct-join scene bootstrap
│   │
│   └── Steamworks.NET/
│       ├── SteamManager.cs      # Singleton; initializes Steamworks SDK
│       └── SteamLobby.cs        # MonoBehaviour facade; composes all multiplayer services, DontDestroyOnLoad
│
├── Tests/
│   └── EditMode/
│       ├── MultiplayerSessionStateMachineTests.cs  # NUnit tests for state machine transitions
│       └── Phish.EditModeTests.asmdef
│
├── com.rlabrecque.steamworks.net/   # Steamworks.NET package (vendored)
├── TextMesh Pro/                    # TMP package assets
│
├── Player.prefab                    # Player prefab: NetworkMovementSync + PlayerMovement + NetworkVoiceRouter + PlayerVoice + FishingProgressHandshake
├── DontDestroyNetworkManager.cs     # Keeps NetworkManager alive across scene loads
├── ConnectionHandler.cs             # Handles NGO connect/disconnect callbacks; returns to main menu on host drop
├── MainMenuUI.cs                    # Main menu panel controller; wires buttons to SteamLobby
├── LobbyPlayerList.cs               # UI: manages up to 8 PlayerSlotUI slots
├── PlayerSlotUI.cs                  # UI: single player slot (name + avatar)
├── PlayerMovement.cs                # Local input → NetworkMovementSync; respects INetworkSpawnPolicy
├── PlayerVoice.cs                   # NetworkBehaviour; captures Steam voice, feeds circular buffer to AudioSource
└── EnableCamera.cs
```

## Key Architectural Patterns

- **Interface-driven services** — multiplayer logic is expressed through contracts in `MultiplayerContracts.cs` (`IMatchDiscovery`, `ISessionBootstrap`, `IPlayerIdentityProvider`, `IPlayerProgressService`, `INetworkSpawnPolicy`). Concrete implementations live in `Steam/`, `NGO/`, and `Game/`.
- **SteamLobby as facade** — `SteamLobby.cs` is the single MonoBehaviour that composes all services and exposes events to the rest of the game. UI and game code talk to `SteamLobby`, not to services directly.
- **State machine guards session flow** — `MultiplayerSessionStateMachine` enforces the valid transition graph. Use `TryTransition` for guarded moves; `ForceState` only for resets/error recovery.
- **Gameplay gating via INetworkSpawnPolicy** — `PlayerMovement` checks `INetworkSpawnPolicy.CanEnableGameplayForLocalPlayer()` before processing input. `FishingProgressHandshake` implements this to block input until server confirms progress is loaded.
- **VoicePacketBus** — static event bus decoupling `NetworkVoiceRouter` (receives packets) from `PlayerVoice` (plays them back). Do not add game logic to this bus.
- **Client prediction + server reconciliation** — `PlayerMovement` applies movement locally immediately; `NetworkMovementSync` sends to server and reconciles if drift exceeds threshold.
- **DontDestroyOnLoad** — `NetworkManager` and `SteamLobby` persist across scene loads. Avoid adding other persistent singletons without a clear reason.

## Where to Add New Code

| What you're building | Where it goes |
|---|---|
| New fishing gameplay mechanic | `Assets/Scripts/Game/` |
| New networked player component | `Assets/Scripts/Multiplayer/NGO/` |
| New Steam feature (achievements, etc.) | `Assets/Scripts/Multiplayer/Steam/` |
| New service contract/interface | `Assets/Scripts/Multiplayer/Core/MultiplayerContracts.cs` |
| Unit-testable logic (no Unity deps) | Extract to a plain C# class; add tests in `Assets/Tests/EditMode/` |
| UI for a new panel | `Assets/` root (scene-level MonoBehaviours) or a new subfolder if it grows |

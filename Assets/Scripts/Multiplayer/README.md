# Reusable Multiplayer Core

This folder contains reusable Steam P2P + NGO multiplayer building blocks intended to be shared across projects.

## Modules

- `Core/`
  - `IMatchDiscovery`, `ISessionBootstrap`, and progression/spawn contracts.
  - `MultiplayerSessionStateMachine` lifecycle transitions.
  - `LobbyUiPresenter` optional UI adapter support.
- `Steam/`
  - Steam identity provider, invite service, and lobby discovery service.
- `NGO/`
  - Session bootstrapping and generic networking components.
- `Samples/`
  - `MultiplayerSampleBootstrap` for minimal direct-join scenes.

## Direct-Join Flow

1. Host creates a Steam friends lobby.
2. Joiners use either:
   - Steam friend overlay invites, or
   - quick join to open lobby list.
3. Lobby entry triggers NGO host/client startup.
4. Fishing progress handshake gates gameplay enablement.

## Required Scene Setup

1. A `NetworkManager` with Steam Networking Sockets transport.
2. A `SteamLobby` instance in bootstrap scene.
3. Player prefab includes:
   - `NetworkMovementSync`
   - `PlayerMovement`
   - `NetworkVoiceRouter`
   - `PlayerVoice`
   - `FishingProgressHandshake`

## Extension Points

- Replace `FishingPlayerProgressService` with cloud/account service.
- Swap `LobbyUiPresenter` with game-specific UI without changing backend services.
- Replace movement/voice gameplay modules while reusing network transport/router classes.

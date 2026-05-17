# Tech Stack

## Engine & Runtime
- **Unity** (C#) — primary engine
- **Unity Netcode for GameObjects (NGO)** — multiplayer networking layer
- **Netcode Transport: SteamNetworkingSocketsTransport** — P2P transport over Steam

## Libraries & Packages
- **Steamworks.NET** (`com.rlabrecque.steamworks.net`) — Steam API wrapper for lobbies, identity, voice, and invites
- **TextMesh Pro** — UI text rendering
- **Unity Test Framework (NUnit)** — edit-mode unit tests

## Key APIs in Use
- `SteamMatchmaking` — lobby creation, joining, listing
- `SteamUser` / `SteamFriends` — identity, avatars, voice recording/decompression
- `NetworkManager` (NGO) — host/client lifecycle, scene management
- `NetworkBehaviour` / `NetworkVariable` / `ServerRpc` / `ClientRpc` — networked object patterns

## Assembly Definitions
- `Phish.Multiplayer.SessionState` — session state machine (no Unity/NGO dependencies, fully testable)
- `Phish.EditModeTests` — edit-mode test assembly

## Common Commands

Unity does not use a CLI build system directly. Use the Unity Editor for building and testing.

| Task | How |
|---|---|
| Run edit-mode tests | Unity Editor → Window → General → Test Runner → EditMode → Run All |
| Build the project | Unity Editor → File → Build Settings → Build |
| Open project | Open Unity Hub, select the `Phish` project folder |

> There is no `package.json`, `Makefile`, or external build script. All compilation is handled by the Unity Editor.

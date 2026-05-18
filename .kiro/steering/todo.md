---
inclusion: manual
---

# TODO / Known Issues

Use `#todo` in chat to pull this into context when planning work.

---

## Bugs

- **Player movement not synced for remote players** — `NetworkMovementSync` replaced `NetworkTransform` on the Player prefab. Remote players' positions are not visible to other clients. Rotation is also server-side only with no ClientRpc broadcast. Needs reconciliation pass on `NetworkMovementSync` and `PlayerMovement`.

---

## Features / Polish

- **Host settings panel** — `MainMenuUI.OnHostLobbyRequested` currently falls through to defaults (8 players, friends-only). Needs a real panel with max-players input, friends-only toggle, and a Start button that calls `OnHostSettingsConfirmed(maxPlayers, friendsOnly)`. See TODO comments in `MainMenuUI.cs`.

- **Lobby browser UI** — `JoinLobby` button currently only opens the Steam friends overlay. Future: show a panel listing friends' active games and public lobbies. `SteamLobbyDiscoveryService.JoinRandomOpenLobby` and `JoinLobby(CSteamID)` are already implemented and waiting for UI.

- **Invite system** — No in-game invite flow yet. Currently players join via Steam overlay "Join Game". Future: `ActivateGameOverlayInviteDialog(lobbyId)` for host-initiated invites.

- **`SyncingPlayerData` state never entered** — The state machine has a `SyncingPlayerData` state between `Joining` and `InSession` but `SteamLobby` skips it, going straight to `InSession`. Wire up once progress sync is real (see `FishingProgressHandshake`).

- **`ConnectionHandler.HandleClientConnected`** — Server-side block is empty. Add NPC spawn or initial game state sync per client here when needed.

- **`SteamLobbyDiscoveryAdapter.JoinLobby`** — Only fires `JoinRequested` event, does not actually call `SteamMatchmaking.JoinLobby`. Needs wiring when lobby browser is built.

---

## Inspector / Unity Config

- **NetworkManager scenes list** — Ensure "Game Scene" is registered in NetworkManager's allowed scenes for NGO scene management. Required for `SceneManager.LoadScene` to work correctly.

---

## Code Quality

- **`PlayerMovement.rotationSpeed` field is dead** — Hardcoded `200f` is used in `HandleRotation` instead of the serialized field.
- **`MainMenuUI.UpdatePlayerList`** — Dead method, never called. Remove when lobby UI is built out.
- **`SpawnManager.spawnCount` never resets** — If a session ends and restarts without reloading the scene, spawn indices will be wrong. Reset on session end if needed.

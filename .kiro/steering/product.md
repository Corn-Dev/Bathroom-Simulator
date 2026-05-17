# Product: Phish

Phish is a multiplayer fishing game built in Unity. Players connect via Steam, join or host lobbies, and fish together in a shared game session.

## Core Features

- Steam-based lobby system (friends-only and open lobbies)
- Real-time multiplayer via Unity Netcode for GameObjects (NGO) over Steam P2P transport
- Proximity/session voice chat using Steam Voice API
- Player progress tracking per Steam identity
- Host-controlled game start with scene sync across all clients

## Current State

Early development. Core multiplayer infrastructure (lobby, session lifecycle, movement sync, voice routing) is in place. Fishing gameplay mechanics are stubbed out and pending implementation.

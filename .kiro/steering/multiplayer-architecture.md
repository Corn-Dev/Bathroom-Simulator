---
inclusion: auto
name: multiplayer-architecture
description: Use when editing multiplayer, Steam lobby, or NGO scripts
---
# Multiplayer Architecture

Scripts/Multiplayer/ is designed as a reusable package — it should 
work in future games without modification. Game-specific code belongs 
in Scripts/Game/, not inside the multiplayer package.

SteamLobby.cs fires OnSessionReady when the NGO session is ready. 
Game code listens to that event and takes over from there. Nothing 
flows the other way.
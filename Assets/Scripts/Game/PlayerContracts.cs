using System;

// Small game-side contracts for the networked player-character layer (player prefab,
// progress gate, identity). These were previously declared in the retired lobby
// MultiplayerContracts.cs; they are kept here, decoupled from the lobby, because the
// player scripts (PlayerMovement, PlayerReadyHandshake, PlayerProgressService) still
// use them. The ball pit itself does not spawn NGO player objects, so none of this is
// wired yet — it is scaffolding for when networked player characters are added.

/// <summary>Gates whether the local player's gameplay should be enabled yet.</summary>
public interface INetworkSpawnPolicy
{
    bool CanEnableGameplayForLocalPlayer();
}

/// <summary>Loads the local player's persistent progress before gameplay is enabled.</summary>
public interface IPlayerProgressService
{
    void BeginLoadProgress(string identityKey, Action<bool> onCompleted);
}

/// <summary>Supplies a stable local identity key (e.g. the Steam id) for progress/save keying.</summary>
public interface IPlayerIdentityProvider
{
    string GetLocalIdentityKey();
}

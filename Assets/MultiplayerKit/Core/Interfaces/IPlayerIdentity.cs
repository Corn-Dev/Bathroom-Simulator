// Local and remote player names and avatar textures. Design: 01 §4.5; Steam implementation 03 §8
// (persona and avatar callbacks, KitTimeouts.Avatar). Texture ownership: the service owns every texture it
// returns (LRU cache of 128, evicted textures destroyed) and destroys the rest only in ReleaseAll.
// Views build Sprites from them and must never Destroy the texture.

using System;
using UnityEngine;

namespace MultiplayerKit
{
    public interface IPlayerIdentity
    {
        PlayerId LocalId { get; }
        string LocalDisplayName { get; }

        /// <summary>Stable string for per-player save file names (= LocalId.ToString()).</summary>
        string LocalIdentityKey { get; }

        /// <summary>"" if unknown; requests persona info as a side effect.</summary>
        string GetDisplayName(PlayerId player);

        /// <summary>
        /// Cached Texture2D (RGBA32, rows already flipped, medium avatar 64x64). done is invoked immediately on
        /// a cache hit, else when the avatar or persona arrives, or with null after KitTimeouts.Avatar or if
        /// the player has no avatar.
        /// </summary>
        void RequestAvatar(PlayerId player, Action<PlayerId, Texture2D> done);

        /// <summary>Name or avatar updated.</summary>
        event Action<PlayerId> IdentityChanged;

        /// <summary>Destroys cached textures; called from the bootstrap's OnDestroy.</summary>
        void ReleaseAll();
    }
}

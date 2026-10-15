// Local and remote player names and medium avatar textures. Design: 01 §4.5, 03 §8.6, 06 S10.
// Avatar code moved from SteamLobby.GetSteamAvatar (GetImageSize, GetImageRGBA, row flip, RGBA32 texture), but a
// texture is returned instead of a Sprite (the UI builds sprites) and -1 means "still loading" (the old code
// treated it as "no avatar"). GetMediumFriendAvatar: > 0 build now; -1 wait for AvatarImageLoaded_t; 0 ask
// RequestUserInformation and retry once on the avatar PersonaStateChange_t. No answer in KitTimeouts.Avatar →
// done(player, null). Textures are owned here: a 128-entry LRU destroys evicted textures, ReleaseAll destroys the
// rest. IdentityChanged is coalesced and raised from Flush.

using System;
using System.Collections.Generic;
using Steamworks;
using UnityEngine;

namespace MultiplayerKit.Steam
{
    public sealed class SteamPlayerIdentity : IPlayerIdentity, ISteamFlushable, IDisposable
    {
        private const int CacheCapacity = 128;
        private const int MaxAvatarSide = 512;
        private const string UnknownPersona = "[unknown]";
        private const EPersonaChange NameFlags = EPersonaChange.k_EPersonaChangeName | EPersonaChange.k_EPersonaChangeNameFirstSet;

        private sealed class PendingAvatar
        {
            public readonly List<Action<PlayerId, Texture2D>> Callbacks = new List<Action<PlayerId, Texture2D>>();
            public bool WaitingForPersona;   // handle 0: RequestUserInformation was sent, retry once on the avatar change
            public float Deadline = float.NaN;
        }

        private readonly Dictionary<ulong, LinkedListNode<KeyValuePair<ulong, Texture2D>>> cache =
            new Dictionary<ulong, LinkedListNode<KeyValuePair<ulong, Texture2D>>>();
        private readonly LinkedList<KeyValuePair<ulong, Texture2D>> recency = new LinkedList<KeyValuePair<ulong, Texture2D>>();
        private readonly Dictionary<ulong, PendingAvatar> pending = new Dictionary<ulong, PendingAvatar>();
        private readonly HashSet<ulong> personaRequested = new HashSet<ulong>();
        private readonly HashSet<ulong> changed = new HashSet<ulong>();
        private readonly List<ulong> scratch = new List<ulong>();

        private Callback<AvatarImageLoaded_t> avatarLoaded;
        private Callback<PersonaStateChange_t> personaChanged;
        private bool callbacksRegistered;
        private bool registrationWarned;
        private bool disposed;
        private PlayerId localId;

        public SteamPlayerIdentity()
        {
            TryRegisterCallbacks();
        }

        public PlayerId LocalId
        {
            get
            {
                if (!localId.IsValid && SteamPlatform.IsInitialized) localId = SteamIds.ToPlayer(SteamUser.GetSteamID());
                return localId;
            }
        }

        public string LocalDisplayName => SteamPlatform.IsInitialized ? SteamFriends.GetPersonaName() ?? "" : "";

        /// <summary>LocalId decimal, the string SteamPlayerIdentityProvider produced; "" while Steam is unavailable.</summary>
        public string LocalIdentityKey
        {
            get
            {
                PlayerId id = LocalId;
                return id.IsValid ? id.ToString() : "";
            }
        }

        public event Action<PlayerId> IdentityChanged;

        public string GetDisplayName(PlayerId player)
        {
            if (!player.IsValid || !SteamPlatform.IsInitialized) return "";
            CSteamID id = SteamIds.ToSteam(player);
            string name = SteamFriends.GetFriendPersonaName(id);
            if (!string.IsNullOrEmpty(name) && name != UnknownPersona) return name;
            if (personaRequested.Add(player.Value)) SteamFriends.RequestUserInformation(id, false);
            return "";
        }

        public void RequestAvatar(PlayerId player, Action<PlayerId, Texture2D> done)
        {
            if (!player.IsValid || disposed || !SteamPlatform.IsInitialized || !TryRegisterCallbacks())
            {
                done?.Invoke(player, null);
                return;
            }
            if (cache.TryGetValue(player.Value, out LinkedListNode<KeyValuePair<ulong, Texture2D>> node))
            {
                recency.Remove(node);
                recency.AddFirst(node);
                done?.Invoke(player, node.Value.Value);
                return;
            }
            if (pending.TryGetValue(player.Value, out PendingAvatar waiting))
            {
                if (done != null) waiting.Callbacks.Add(done);
                return;
            }

            CSteamID id = SteamIds.ToSteam(player);
            int handle = SteamFriends.GetMediumFriendAvatar(id);
            if (handle > 0)
            {
                done?.Invoke(player, BuildAndCache(player, handle));
                return;
            }
            var request = new PendingAvatar();
            if (handle == 0)
            {
                if (!SteamFriends.RequestUserInformation(id, false))
                {
                    done?.Invoke(player, null);   // Steam has the persona and it has no avatar
                    return;
                }
                request.WaitingForPersona = true;
            }
            if (done != null) request.Callbacks.Add(done);
            pending[player.Value] = request;
        }

        /// <summary>Destroys every cached texture. Pending requests are dropped without a callback (end of life).</summary>
        public void ReleaseAll()
        {
            foreach (KeyValuePair<ulong, Texture2D> entry in recency) DestroyTexture(entry.Value);
            recency.Clear();
            cache.Clear();
            pending.Clear();
            changed.Clear();
            personaRequested.Clear();
        }

        public void Flush(float now)
        {
            if (disposed || !SteamPlatform.IsInitialized || !TryRegisterCallbacks()) return;
            if (pending.Count > 0) ExpirePending(now);
            if (changed.Count == 0) return;
            scratch.Clear();
            scratch.AddRange(changed);
            changed.Clear();
            foreach (ulong raw in scratch) SteamPlatform.Raise(IdentityChanged, new PlayerId(raw), "IdentityChanged");
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            callbacksRegistered = false;
            avatarLoaded?.Dispose();
            personaChanged?.Dispose();
            avatarLoaded = null;
            personaChanged = null;
        }

        private void OnAvatarLoaded(AvatarImageLoaded_t loaded)
        {
            PlayerId player = SteamIds.ToPlayer(loaded.m_steamID);
            if (!player.IsValid || !pending.TryGetValue(player.Value, out PendingAvatar request)) return;
            pending.Remove(player.Value);
            Texture2D texture = loaded.m_iImage > 0 ? BuildAndCache(player, loaded.m_iImage) : null;
            Complete(player, request, texture);
        }

        private void OnPersonaChanged(PersonaStateChange_t change)
        {
            PlayerId player = SteamIds.ToPlayer(change.m_ulSteamID);
            if (!player.IsValid) return;
            bool avatar = (change.m_nChangeFlags & EPersonaChange.k_EPersonaChangeAvatar) != 0;
            bool name = (change.m_nChangeFlags & NameFlags) != 0;
            if (!avatar && !name) return;
            changed.Add(player.Value);
            personaRequested.Remove(player.Value);
            if (!avatar) return;

            // A new avatar replaces the cached one; views re-request on IdentityChanged.
            if (cache.TryGetValue(player.Value, out LinkedListNode<KeyValuePair<ulong, Texture2D>> node)) Evict(node);
            if (!pending.TryGetValue(player.Value, out PendingAvatar request) || !request.WaitingForPersona) return;

            request.WaitingForPersona = false;   // the single retry
            int handle = SteamFriends.GetMediumFriendAvatar(SteamIds.ToSteam(player));
            if (handle == -1) return;            // now loading: AvatarImageLoaded_t completes it
            pending.Remove(player.Value);
            Complete(player, request, handle > 0 ? BuildAndCache(player, handle) : null);
        }

        private void ExpirePending(float now)
        {
            scratch.Clear();
            foreach (KeyValuePair<ulong, PendingAvatar> entry in pending)
            {
                if (float.IsNaN(entry.Value.Deadline)) entry.Value.Deadline = now + KitTimeouts.Avatar;
                else if (now >= entry.Value.Deadline) scratch.Add(entry.Key);
            }
            foreach (ulong raw in scratch)
            {
                PendingAvatar request = pending[raw];
                pending.Remove(raw);
                Complete(new PlayerId(raw), request, null);
            }
            scratch.Clear();
        }

        private static void Complete(PlayerId player, PendingAvatar request, Texture2D texture)
        {
            foreach (Action<PlayerId, Texture2D> done in request.Callbacks)
            {
                SteamPlatform.Raise(done, player, texture, "RequestAvatar done");
            }
        }

        // Moved from SteamLobby.GetSteamAvatar: Steam rows are top-down, Unity textures bottom-up.
        private Texture2D BuildAndCache(PlayerId player, int image)
        {
            if (!SteamUtils.GetImageSize(image, out uint width, out uint height)) return null;
            if (width == 0 || height == 0 || width > MaxAvatarSide || height > MaxAvatarSide) return null;

            int rowBytes = (int)width * 4;
            int size = rowBytes * (int)height;
            var rgba = new byte[size];
            if (!SteamUtils.GetImageRGBA(image, rgba, size)) return null;
            var flipped = new byte[size];
            for (int y = 0; y < (int)height; y++)
            {
                Buffer.BlockCopy(rgba, y * rowBytes, flipped, ((int)height - 1 - y) * rowBytes, rowBytes);
            }

            var texture = new Texture2D((int)width, (int)height, TextureFormat.RGBA32, false);
            texture.name = "SteamAvatar_" + player;
            texture.LoadRawTextureData(flipped);
            texture.Apply();

            if (cache.TryGetValue(player.Value, out LinkedListNode<KeyValuePair<ulong, Texture2D>> old)) Evict(old);
            while (cache.Count >= CacheCapacity && recency.Last != null) Evict(recency.Last);
            cache[player.Value] = recency.AddFirst(new KeyValuePair<ulong, Texture2D>(player.Value, texture));
            return texture;
        }

        private void Evict(LinkedListNode<KeyValuePair<ulong, Texture2D>> node)
        {
            cache.Remove(node.Value.Key);
            recency.Remove(node);
            DestroyTexture(node.Value.Value);
        }

        private static void DestroyTexture(Texture2D texture)
        {
            if (texture != null) UnityEngine.Object.Destroy(texture);
        }

        private bool TryRegisterCallbacks()
        {
            if (callbacksRegistered) return true;
            if (disposed) return false;
            if (!SteamPlatform.IsInitialized)
            {
                if (!registrationWarned) KitLog.Warning("SteamPlayerIdentity: Steam is not initialized; callbacks are registered once it is.");
                registrationWarned = true;
                return false;
            }
            avatarLoaded = Callback<AvatarImageLoaded_t>.Create(OnAvatarLoaded);
            personaChanged = Callback<PersonaStateChange_t>.Create(OnPersonaChanged);
            callbacksRegistered = true;
            if (registrationWarned) KitLog.Info("SteamPlayerIdentity: Steam callbacks registered late.");
            return true;
        }
    }
}

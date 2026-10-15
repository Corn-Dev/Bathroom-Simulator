// Raw platform lobby: membership, metadata, member data, chat, owner. Holds no session policy (kick
// protocol, password, phase rules live in SessionController). Design: 01 §4.1; Steam mapping 03 §3-§8.1.
// Main thread only. Every completion callback is invoked exactly once; callers may pass null.

using System;
using System.Collections.Generic;

namespace MultiplayerKit
{
    public interface ILobbyService
    {
        /// <summary>Platform initialized (Steam: SteamPlatform.IsInitialized, 06 §1).</summary>
        bool IsAvailable { get; }

        /// <summary>None when not in a lobby.</summary>
        LobbyId Current { get; }

        bool InLobby { get; }
        PlayerId LocalPlayer { get; }

        /// <summary>Platform lobby owner; None when not in a lobby.</summary>
        PlayerId Owner { get; }

        bool IsLocalOwner { get; }

        /// <summary>Snapshot, rebuilt on change (coalesced per frame); empty when not in a lobby.</summary>
        IReadOnlyList<LobbyMember> Members { get; }

        /// <summary>Live platform query, not the Members snapshot; false when not in a lobby.</summary>
        bool IsMember(PlayerId player);

        // Lifecycle. Only one Create/Join may be in flight; a second call completes with Busy.
        // Failures: PlatformUnavailable, LobbyCreateFailed, LobbyNotFound, LobbyFull, LobbyAccessDenied,
        // Timeout (KitTimeouts.CreateJoin), Cancelled (Leave while pending).
        void Create(LobbyPrivacy privacy, int maxMembers, Action<KitResult<LobbyId>> done);
        void Join(LobbyId lobby, Action<KitResult<LobbyId>> done);

        /// <summary>Idempotent; no events are raised for the local leave.</summary>
        void Leave();

        // Owner-only mutators. Return false (and log Warning) if not owner or not in a lobby.
        bool SetPrivacy(LobbyPrivacy privacy);
        bool SetJoinable(bool joinable);
        bool SetMaxMembers(int maxMembers);

        /// <summary>Value null or "" deletes the key.</summary>
        bool SetData(string key, string value);

        /// <summary>Writes every pair with one change notification.</summary>
        bool SetDataBatch(IReadOnlyDictionary<string, string> pairs);

        // Readable by every member.

        /// <summary>"" when missing (never null).</summary>
        string GetData(string key);

        IReadOnlyDictionary<string, string> GetAllData();

        /// <summary>"" when missing (never null).</summary>
        string GetMemberData(PlayerId member, string key);

        bool SetLocalMemberData(string key, string value);

        /// <summary>Broadcast to every member including the sender. Over 2048 UTF-8 bytes returns false.</summary>
        bool SendChat(string message);

        /// <summary>After Create or Join success, before the done callback.</summary>
        event Action<LobbyId> Entered;

        /// <summary>Join, leave, persona or member data change.</summary>
        event Action<IReadOnlyList<LobbyMember>> MembersChanged;

        event Action<PlayerId> MemberJoined;

        /// <summary>Left, disconnected, kicked by the platform, banned.</summary>
        event Action<PlayerId> MemberLeft;

        /// <summary>Any lobby-level key changed (coalesced per frame).</summary>
        event Action LobbyDataChanged;

        event Action<PlayerId> MemberDataChanged;

        /// <summary>Sender, text (UTF-8 decoded, trailing NUL trimmed). The sender comes from the platform.</summary>
        event Action<PlayerId, string> ChatReceived;

        /// <summary>Previous, current. The kit never transfers ownership, so this always means "host left".</summary>
        event Action<PlayerId, PlayerId> OwnerChanged;

        /// <summary>The local member was removed by the platform (rare on Steam).</summary>
        event Action<KitError> LostLobby;
    }
}

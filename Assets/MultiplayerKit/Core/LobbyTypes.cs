// Shared lobby value types: privacy, phase, member snapshot, browser/friends summary,
// and the canonical wire strings for privacy ("s.privacy") and phase ("k.phase").
// Design: 01 §3 (types), 01 §5.3 and 03 §3.1 (privacy strings), 01 §7 (phase strings).
// Wire strings are case-sensitive and match exactly what the host writes; parsing never throws.

using System;
using System.Collections.Generic;

namespace MultiplayerKit
{
    /// <summary>Lobby visibility. Steam: Private, FriendsOnly, Public (03 §3.1).</summary>
    public enum LobbyPrivacy { InviteOnly = 0, FriendsOnly = 1, Public = 2 }

    /// <summary>Lobby-wide lifecycle phase, written by the host to metadata key "k.phase".</summary>
    public enum LobbyPhase { Room = 0, Starting = 1, InGame = 2 }

    /// <summary>One member of the joined lobby. Immutable snapshot, rebuilt by the platform service on change.</summary>
    public sealed class LobbyMember
    {
        public PlayerId Id { get; }

        /// <summary>Never null; "" until the platform persona resolves.</summary>
        public string DisplayName { get; }

        /// <summary>True for the lobby owner (the session host).</summary>
        public bool IsOwner { get; }

        public bool IsLocal { get; }

        /// <summary>Member data "k.ready" == "1"; the owner is always ready.</summary>
        public bool IsReady { get; }

        public LobbyMember(PlayerId id, string displayName, bool isOwner, bool isLocal, bool isReady)
        {
            Id = id;
            DisplayName = displayName ?? "";
            IsOwner = isOwner;
            IsLocal = isLocal;
            IsReady = isReady || isOwner;
        }
    }

    /// <summary>One lobby as seen from the public browser or the friends list (not joined).</summary>
    public sealed class LobbySummary
    {
        private static readonly IReadOnlyDictionary<string, string> EmptySettings =
            new Dictionary<string, string>(StringComparer.Ordinal);

        public LobbyId Id { get; }

        /// <summary>None if not yet known.</summary>
        public PlayerId Owner { get; }

        /// <summary>s.lobbyName, or "" if unset. Never null.</summary>
        public string Name { get; }

        public int MemberCount { get; }
        public int MaxMembers { get; }
        public LobbyPrivacy Privacy { get; }

        /// <summary>k.hasPw == "1".</summary>
        public bool HasPassword { get; }

        public LobbyPhase Phase { get; }
        public bool JoinInProgress { get; }

        /// <summary>k.game, k.proto and k.ver match the local game (01 §7).</summary>
        public bool IsCompatible { get; }

        /// <summary>Published setting values keyed WITHOUT the "s." prefix. Never null.</summary>
        public IReadOnlyDictionary<string, string> PublishedSettings { get; }

        public bool IsFull => MemberCount >= MaxMembers;

        public bool IsJoinableNow => IsCompatible && !IsFull && (Phase == LobbyPhase.Room || JoinInProgress);

        public LobbySummary(
            LobbyId id,
            PlayerId owner,
            string name,
            int memberCount,
            int maxMembers,
            LobbyPrivacy privacy,
            bool hasPassword,
            LobbyPhase phase,
            bool joinInProgress,
            bool isCompatible,
            IReadOnlyDictionary<string, string> publishedSettings)
        {
            Id = id;
            Owner = owner;
            Name = name ?? "";
            MemberCount = memberCount;
            MaxMembers = maxMembers;
            Privacy = privacy;
            HasPassword = hasPassword;
            Phase = phase;
            JoinInProgress = joinInProgress;
            IsCompatible = isCompatible;
            PublishedSettings = CopySettings(publishedSettings);
        }

        // Defensive copy so a summary stays immutable while presenters hold it.
        private static IReadOnlyDictionary<string, string> CopySettings(IReadOnlyDictionary<string, string> source)
        {
            if (source == null || source.Count == 0) return EmptySettings;
            var copy = new Dictionary<string, string>(source.Count, StringComparer.Ordinal);
            foreach (KeyValuePair<string, string> pair in source)
            {
                if (pair.Key == null) continue;
                copy[pair.Key] = pair.Value ?? "";
            }
            return copy;
        }
    }

    /// <summary>
    /// Canonical wire strings for <see cref="LobbyPrivacy"/> (the "privacy" setting value, "s.privacy")
    /// and <see cref="LobbyPhase"/> ("k.phase"). Callers choose their own fallback when parsing fails
    /// (for example the summary parser maps unknown privacy to FriendsOnly and unknown phase to Room, 03 §8.2).
    /// </summary>
    public static class LobbyEnums
    {
        public const string PrivacyInviteOnly = "inviteOnly";
        public const string PrivacyFriendsOnly = "friendsOnly";
        public const string PrivacyPublic = "public";

        public const string PhaseRoom = "room";
        public const string PhaseStarting = "starting";
        public const string PhaseInGame = "inGame";

        public static string ToKey(LobbyPrivacy privacy)
        {
            switch (privacy)
            {
                case LobbyPrivacy.InviteOnly: return PrivacyInviteOnly;
                case LobbyPrivacy.FriendsOnly: return PrivacyFriendsOnly;
                case LobbyPrivacy.Public: return PrivacyPublic;
                default: throw new ArgumentOutOfRangeException(nameof(privacy), privacy, "Unknown LobbyPrivacy.");
            }
        }

        public static string ToKey(LobbyPhase phase)
        {
            switch (phase)
            {
                case LobbyPhase.Room: return PhaseRoom;
                case LobbyPhase.Starting: return PhaseStarting;
                case LobbyPhase.InGame: return PhaseInGame;
                default: throw new ArgumentOutOfRangeException(nameof(phase), phase, "Unknown LobbyPhase.");
            }
        }

        /// <summary>Exact, case-sensitive match. Null or unknown text returns false (privacy = FriendsOnly).</summary>
        public static bool TryParsePrivacy(string key, out LobbyPrivacy privacy)
        {
            switch (key)
            {
                case PrivacyInviteOnly: privacy = LobbyPrivacy.InviteOnly; return true;
                case PrivacyFriendsOnly: privacy = LobbyPrivacy.FriendsOnly; return true;
                case PrivacyPublic: privacy = LobbyPrivacy.Public; return true;
                default: privacy = LobbyPrivacy.FriendsOnly; return false;
            }
        }

        /// <summary>Exact, case-sensitive match. Null or unknown text returns false (phase = Room).</summary>
        public static bool TryParsePhase(string key, out LobbyPhase phase)
        {
            switch (key)
            {
                case PhaseRoom: phase = LobbyPhase.Room; return true;
                case PhaseStarting: phase = LobbyPhase.Starting; return true;
                case PhaseInGame: phase = LobbyPhase.InGame; return true;
                default: phase = LobbyPhase.Room; return false;
            }
        }
    }
}

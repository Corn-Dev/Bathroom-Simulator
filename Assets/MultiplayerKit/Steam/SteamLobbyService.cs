// Steam lobby, part 1 of 2: lifecycle (Create, Join, Leave, 15 s timeouts), owner tracking, membership callbacks,
// callback registration and Dispose. Part 2 (SteamLobbyService.Data.cs) holds metadata, the owner-only mutators
// (moved there to keep this file near the 400-line limit), member data, chat, the Members snapshot and Flush.
// Design: 01 §4.1, 03 §3, §7, §8.1, 06 S4.
// Create completes on its LobbyCreated_t CallResult (the LobbyEnter_t that follows only marks members dirty), Join
// on its LobbyEnter_t CallResult. Entered is raised before done; every done runs exactly once. MemberJoined and
// MemberLeft are raised straight from LobbyChatUpdate_t, never coalesced, so a joiner's MemberJoined always
// precedes the ChatReceived of its first kit line (the host's password bookkeeping relies on it).

using System;
using Steamworks;

namespace MultiplayerKit.Steam
{
    public sealed partial class SteamLobbyService : ILobbyService, ISteamFlushable, IDisposable
    {
        private const int SteamMaxMembers = 250;

        private const uint EnteredFlag = (uint)EChatMemberStateChange.k_EChatMemberStateChangeEntered;
        private const uint LeftFlags = (uint)(EChatMemberStateChange.k_EChatMemberStateChangeLeft
            | EChatMemberStateChange.k_EChatMemberStateChangeDisconnected
            | EChatMemberStateChange.k_EChatMemberStateChangeKicked
            | EChatMemberStateChange.k_EChatMemberStateChangeBanned);

        private enum PendingOp { None, Create, Join }

        // Created in the constructor, or by the first Flush once Steam is up (03 §7); disposed in Dispose.
        private CallResult<LobbyCreated_t> createCall;
        private CallResult<LobbyEnter_t> joinCall;
        private Callback<LobbyEnter_t> lobbyEnter;
        private Callback<LobbyChatUpdate_t> chatUpdate;
        private Callback<LobbyDataUpdate_t> dataUpdate;
        private Callback<LobbyChatMsg_t> chatMessage;
        private Callback<LobbyKicked_t> kicked;
        private Callback<PersonaStateChange_t> personaChange;
        private bool callbacksRegistered;
        private bool registrationWarned;
        private bool disposed;

        private LobbyId current;
        private PlayerId cachedOwner;   // compared with GetLobbyOwner in Flush to raise OwnerChanged
        private PlayerId localPlayer;

        // The single in-flight Create or Join.
        private PendingOp pendingOp;
        private LobbyId pendingTarget;
        private Action<KitResult<LobbyId>> pendingDone;
        private float pendingDeadline = float.NaN;   // set by the first Flush after the request
        private LobbyId unclaimedEnter;              // LobbyEnter_t that arrived before an unanswered Create

        public SteamLobbyService()
        {
            TryRegisterCallbacks();
        }

        public bool IsAvailable => !disposed && SteamPlatform.IsInitialized;
        public LobbyId Current => current;
        public bool InLobby => current.IsValid;
        public bool IsLocalOwner => InLobby && LocalPlayer.IsValid && Owner == LocalPlayer;

        public PlayerId LocalPlayer
        {
            get
            {
                if (!localPlayer.IsValid && SteamPlatform.IsInitialized) localPlayer = SteamIds.ToPlayer(SteamUser.GetSteamID());
                return localPlayer;
            }
        }

        /// <summary>Live GetLobbyOwner, falling back to the cached owner while Steam reports none.</summary>
        public PlayerId Owner
        {
            get
            {
                if (!InLobby || !SteamPlatform.IsInitialized) return PlayerId.None;
                PlayerId live = SteamIds.ToPlayer(SteamMatchmaking.GetLobbyOwner(SteamIds.ToSteam(current)));
                return live.IsValid ? live : cachedOwner;
            }
        }

        public event Action<LobbyId> Entered;
        public event Action<PlayerId> MemberJoined;
        public event Action<PlayerId> MemberLeft;
        public event Action<PlayerId, PlayerId> OwnerChanged;
        public event Action<KitError> LostLobby;

        /// <summary>Live member loop, not the Members snapshot (03 §8.1); approval relies on it.</summary>
        public bool IsMember(PlayerId player)
        {
            if (!InLobby || !player.IsValid || !SteamPlatform.IsInitialized) return false;
            CSteamID lobby = SteamIds.ToSteam(current);
            CSteamID target = SteamIds.ToSteam(player);
            int count = SteamMatchmaking.GetNumLobbyMembers(lobby);
            for (int i = 0; i < count; i++)
            {
                if (SteamMatchmaking.GetLobbyMemberByIndex(lobby, i) == target) return true;
            }
            return false;
        }

        public void Create(LobbyPrivacy privacy, int maxMembers, Action<KitResult<LobbyId>> done)
        {
            if (!CanBegin("Create", done)) return;
            int max = Math.Max(1, Math.Min(SteamMaxMembers, maxMembers));
            SteamAPICall_t call = SteamMatchmaking.CreateLobby(ToLobbyType(privacy), max);
            if (call == SteamAPICall_t.Invalid)
            {
                KitLog.Warning("CreateLobby returned no call handle.");
                SteamPlatform.Raise(done, KitResult<LobbyId>.Fail(KitError.LobbyCreateFailed, "no call handle"), "Create done");
                return;
            }
            BeginPending(PendingOp.Create, LobbyId.None, done);
            createCall.Set(call);
        }

        public void Join(LobbyId lobby, Action<KitResult<LobbyId>> done)
        {
            if (!CanBegin("Join", done)) return;
            CSteamID target = SteamIds.ToSteam(lobby);
            if (!SteamIds.IsUsable(target))
            {
                SteamPlatform.Raise(done, KitResult<LobbyId>.Fail(KitError.LobbyNotFound, "not a Steam lobby id"), "Join done");
                return;
            }
            SteamAPICall_t call = SteamMatchmaking.JoinLobby(target);
            if (call == SteamAPICall_t.Invalid)
            {
                KitLog.Warning("JoinLobby returned no call handle.");
                SteamPlatform.Raise(done, KitResult<LobbyId>.Fail(KitError.Unknown, "no call handle"), "Join done");
                return;
            }
            BeginPending(PendingOp.Join, lobby, done);
            joinCall.Set(call);
        }

        /// <summary>Idempotent. Leaves the current lobby, cancels a pending Create/Join (done gets Cancelled). No events.</summary>
        public void Leave()
        {
            LobbyId leaving = current;
            LobbyId target = pendingTarget;
            LobbyId unclaimed = unclaimedEnter;
            bool hadPending = pendingOp != PendingOp.None;
            Action<KitResult<LobbyId>> done = hadPending ? TakePending(true) : null;
            ClearLobbyState();
            LeaveQuietly(leaving, false);
            if (target != leaving) LeaveQuietly(target, false);
            if (unclaimed != leaving) LeaveQuietly(unclaimed, false);
            if (hadPending) SteamPlatform.Raise(done, KitResult<LobbyId>.Fail(KitError.Cancelled, "left while pending"), "lobby done");
        }

        public void Dispose()
        {
            if (disposed) return;
            Action<KitResult<LobbyId>> done = pendingOp != PendingOp.None ? TakePending(true) : null;
            disposed = true;
            callbacksRegistered = false;
            createCall?.Dispose();
            joinCall?.Dispose();
            lobbyEnter?.Dispose();
            chatUpdate?.Dispose();
            dataUpdate?.Dispose();
            chatMessage?.Dispose();
            kicked?.Dispose();
            personaChange?.Dispose();
            if (done != null) SteamPlatform.Raise(done, KitResult<LobbyId>.Fail(KitError.Cancelled, "disposed"), "lobby done");
        }

        // Steam answers.

        private void OnLobbyCreated(LobbyCreated_t result, bool ioFailure)
        {
            if (pendingOp != PendingOp.Create) return;
            LobbyId id = SteamIds.ToLobby(result.m_ulSteamIDLobby);
            LobbyId unclaimed = unclaimedEnter;
            Action<KitResult<LobbyId>> done = TakePending(false);
            if (ioFailure || result.m_eResult != EResult.k_EResultOK || !id.IsValid)
            {
                string detail = ioFailure ? "I/O failure" : result.m_eResult.ToString();
                KitLog.Warning("CreateLobby failed: " + detail + ".");
                LeaveQuietly(unclaimed, true);
                SteamPlatform.Raise(done, KitResult<LobbyId>.Fail(KitError.LobbyCreateFailed, detail), "Create done");
                return;
            }
            if (unclaimed != id) LeaveQuietly(unclaimed, true);
            EnterLobby(id, LocalPlayer);
            WriteCreateKeys();
            CompleteEntered(id, done);
        }

        private void OnLobbyJoined(LobbyEnter_t result, bool ioFailure)
        {
            if (pendingOp != PendingOp.Join) return;
            LobbyId target = pendingTarget;
            Action<KitResult<LobbyId>> done = TakePending(false);
            if (ioFailure)
            {
                KitLog.Warning("JoinLobby " + target + " failed: I/O failure.");
                LeaveQuietly(target, false);
                SteamPlatform.Raise(done, KitResult<LobbyId>.Fail(KitError.Timeout, "I/O failure"), "Join done");
                return;
            }
            var response = (EChatRoomEnterResponse)result.m_EChatRoomEnterResponse;
            LobbyId id = SteamIds.ToLobby(result.m_ulSteamIDLobby);
            if (response != EChatRoomEnterResponse.k_EChatRoomEnterResponseSuccess || !id.IsValid)
            {
                KitLog.Warning("JoinLobby " + target + " refused: " + response + ".");
                SteamPlatform.Raise(done, KitResult<LobbyId>.Fail(MapEnterResponse(response), response.ToString()), "Join done");
                return;
            }
            EnterLobby(id, SteamIds.ToPlayer(SteamMatchmaking.GetLobbyOwner(SteamIds.ToSteam(id))));
            CompleteEntered(id, done);
        }

        // Also fires for CreateLobby and JoinLobby, in either order relative to their CallResults (03 §7).
        private void OnLobbyEnterBroadcast(LobbyEnter_t entered)
        {
            LobbyId id = SteamIds.ToLobby(entered.m_ulSteamIDLobby);
            if (!id.IsValid) return;
            if (id == current)
            {
                MarkMembersDirty();
                return;
            }
            var response = (EChatRoomEnterResponse)entered.m_EChatRoomEnterResponse;
            if (response != EChatRoomEnterResponse.k_EChatRoomEnterResponseSuccess) return;
            if (pendingOp == PendingOp.Join && id == pendingTarget) return;   // its CallResult completes the join
            if (pendingOp == PendingOp.Create)
            {
                // Most likely the lobby being created; settled by its LobbyCreated_t, a failure or the timeout.
                if (unclaimedEnter != id) LeaveQuietly(unclaimedEnter, true);
                unclaimedEnter = id;
                return;
            }
            LeaveQuietly(id, true);   // late entry after a timeout or cancel
        }

        private void OnChatUpdate(LobbyChatUpdate_t update)
        {
            if (!InLobby || update.m_ulSteamIDLobby != current.Value) return;
            MarkMembersDirty();
            PlayerId user = SteamIds.ToPlayer(update.m_ulSteamIDUserChanged);
            if (!user.IsValid || user == LocalPlayer) return;
            uint change = update.m_rgfChatMemberStateChange;
            if ((change & EnteredFlag) != 0) SteamPlatform.Raise(MemberJoined, user, "MemberJoined");
            else if ((change & LeftFlags) != 0) SteamPlatform.Raise(MemberLeft, user, "MemberLeft");
        }

        private void OnKicked(LobbyKicked_t kick)
        {
            if (!InLobby || kick.m_ulSteamIDLobby != current.Value) return;
            LobbyId lost = current;
            KitLog.Warning("Steam removed the local player from lobby " + lost + ".");
            ClearLobbyState();
            LeaveQuietly(lost, false);
            SteamPlatform.Raise(LostLobby, KitError.ConnectionLost, "LostLobby");
        }

        // Steam has no owner-changed callback, so Flush compares after membership or data updates (03 §7). An unknown
        // cached owner is filled in silently: only a change between two known owners means "host left".
        private void CheckOwner()
        {
            PlayerId live = SteamIds.ToPlayer(SteamMatchmaking.GetLobbyOwner(SteamIds.ToSteam(current)));
            if (!live.IsValid || live == cachedOwner) return;
            PlayerId previous = cachedOwner;
            cachedOwner = live;
            if (!previous.IsValid) return;
            KitLog.Info("Lobby owner changed from " + previous + " to " + live + ".");
            SteamPlatform.Raise(OwnerChanged, previous, live, "OwnerChanged");
        }

        // Pending operation helpers.

        private bool CanBegin(string what, Action<KitResult<LobbyId>> done)
        {
            if (!IsAvailable || !TryRegisterCallbacks())
            {
                SteamPlatform.WarnOnce("Steam lobby " + what + ": Steam is not initialized.");
                SteamPlatform.Raise(done, KitResult<LobbyId>.Fail(KitError.PlatformUnavailable), what + " done");
                return false;
            }
            string busy = pendingOp != PendingOp.None ? "another Create or Join is in flight"
                : InLobby ? "already in lobby " + current + "; Leave first" : null;
            if (busy == null) return true;
            KitLog.Warning("Steam lobby " + what + " refused: " + busy + ".");
            SteamPlatform.Raise(done, KitResult<LobbyId>.Fail(KitError.Busy, busy), what + " done");
            return false;
        }

        private void BeginPending(PendingOp op, LobbyId target, Action<KitResult<LobbyId>> done)
        {
            pendingOp = op;
            pendingTarget = target;
            pendingDone = done;
            pendingDeadline = float.NaN;
            unclaimedEnter = LobbyId.None;
        }

        // Clears the pending operation and returns its done; cancelCall drops the Steam CallResult binding.
        private Action<KitResult<LobbyId>> TakePending(bool cancelCall)
        {
            Action<KitResult<LobbyId>> done = pendingDone;
            if (cancelCall && pendingOp == PendingOp.Create) createCall?.Cancel();
            if (cancelCall && pendingOp == PendingOp.Join) joinCall?.Cancel();
            pendingOp = PendingOp.None;
            pendingTarget = LobbyId.None;
            pendingDone = null;
            pendingDeadline = float.NaN;
            unclaimedEnter = LobbyId.None;
            return done;
        }

        private void CheckPendingTimeout(float now)
        {
            if (pendingOp == PendingOp.None) return;
            if (float.IsNaN(pendingDeadline))
            {
                pendingDeadline = now + KitTimeouts.CreateJoin;
                return;
            }
            if (now < pendingDeadline) return;
            bool join = pendingOp == PendingOp.Join;
            LobbyId target = pendingTarget;
            LobbyId unclaimed = unclaimedEnter;
            Action<KitResult<LobbyId>> done = TakePending(true);
            KitLog.Warning("Lobby " + (join ? "join " + target : "create") + " timed out after " + KitTimeouts.CreateJoin + " s.");
            if (join) LeaveQuietly(target, false);   // a later LobbyEnter_t is left by the orphan rule as well
            LeaveQuietly(unclaimed, true);
            SteamPlatform.Raise(done, KitResult<LobbyId>.Fail(KitError.Timeout, join ? "JoinLobby" : "CreateLobby"), "lobby done");
        }

        private void EnterLobby(LobbyId id, PlayerId owner)
        {
            current = id;
            cachedOwner = owner;
            ResetDataState();
            RebuildMembers();   // may still be empty right after LobbyCreated_t; Flush rebuilds again
            MarkAllDirty();
        }

        private void CompleteEntered(LobbyId id, Action<KitResult<LobbyId>> done)
        {
            KitLog.Info("Entered lobby " + id + ".");
            SteamPlatform.Raise(Entered, id, "Entered");
            SteamPlatform.Raise(done, KitResult<LobbyId>.From(id), "lobby done");
        }

        private void ClearLobbyState()
        {
            current = LobbyId.None;
            cachedOwner = PlayerId.None;
            ResetDataState();
        }

        private static void LeaveQuietly(LobbyId id, bool orphan)
        {
            if (!id.IsValid || !SteamPlatform.IsInitialized) return;
            if (orphan) KitLog.Warning("Left orphan lobby " + id + ".");
            try
            {
                SteamMatchmaking.LeaveLobby(SteamIds.ToSteam(id));
            }
            catch (Exception e)
            {
                KitLog.Warning("LeaveLobby(" + id + ") failed: " + e.GetType().Name);
            }
        }

        private bool TryRegisterCallbacks()
        {
            if (callbacksRegistered) return true;
            if (disposed) return false;
            if (!SteamPlatform.IsInitialized)
            {
                if (!registrationWarned) KitLog.Warning("SteamLobbyService: Steam is not initialized; callbacks are registered once it is.");
                registrationWarned = true;
                return false;
            }
            createCall = CallResult<LobbyCreated_t>.Create(OnLobbyCreated);
            joinCall = CallResult<LobbyEnter_t>.Create(OnLobbyJoined);
            lobbyEnter = Callback<LobbyEnter_t>.Create(OnLobbyEnterBroadcast);
            chatUpdate = Callback<LobbyChatUpdate_t>.Create(OnChatUpdate);
            dataUpdate = Callback<LobbyDataUpdate_t>.Create(OnDataUpdate);
            chatMessage = Callback<LobbyChatMsg_t>.Create(OnChatMessage);
            kicked = Callback<LobbyKicked_t>.Create(OnKicked);
            personaChange = Callback<PersonaStateChange_t>.Create(OnPersonaChange);
            callbacksRegistered = true;
            if (registrationWarned) KitLog.Info("SteamLobbyService: Steam callbacks registered late.");
            return true;
        }

        // 01 §4.1 / 03 §7 enter-response mapping.
        private static KitError MapEnterResponse(EChatRoomEnterResponse response)
        {
            switch (response)
            {
                case EChatRoomEnterResponse.k_EChatRoomEnterResponseDoesntExist: return KitError.LobbyNotFound;
                case EChatRoomEnterResponse.k_EChatRoomEnterResponseFull: return KitError.LobbyFull;
                case EChatRoomEnterResponse.k_EChatRoomEnterResponseNotAllowed:
                case EChatRoomEnterResponse.k_EChatRoomEnterResponseBanned:
                case EChatRoomEnterResponse.k_EChatRoomEnterResponseLimited:
                case EChatRoomEnterResponse.k_EChatRoomEnterResponseClanDisabled:
                case EChatRoomEnterResponse.k_EChatRoomEnterResponseCommunityBan:
                case EChatRoomEnterResponse.k_EChatRoomEnterResponseMemberBlockedYou:
                case EChatRoomEnterResponse.k_EChatRoomEnterResponseYouBlockedMember:
                    return KitError.LobbyAccessDenied;
                default: return KitError.Unknown;
            }
        }
    }
}

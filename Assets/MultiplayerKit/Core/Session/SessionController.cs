// The single session orchestrator (decision 7): owns the state machine and runs every flow across lobby,
// network, invites, saves and the game adapter. Design: 01 §6 (API, transitions, rules), 02 (flows), 03 §3-§16,
// 04 §6-§10. One sealed partial class split by flow (06 §2.5). This file: constructor, every private field
// (02 §0), properties, events, the Tick dispatcher, join-request routing (01 §6.3), presence (02 §10) and the
// raise helpers. .Room: settings, roster, chat. .Host: create, start, approval, auth, kick. .Join: join,
// password, phase following, client connect. .Live: go-live, adapter wrappers, saves. .Teardown: leave,
// teardown, host left. Main thread only. Time arrives only through Tick; event handlers use the last Tick time.

using System;
using System.Collections.Generic;

namespace MultiplayerKit
{
    public sealed partial class SessionController
    {
        // Controller tunables (02 §3d.8, §3d.11.3, §10.1).
        private const int MaxPasswordAttempts = 3;      // client: wrong passwords before Fail(WrongPassword)
        private const int MaxAuthFailures = 5;          // host: wrong auth lines before a session ban
        private const float PresenceRefreshSeconds = 30f;

        // Texts and log markers (02 §2d.3 start-blocked reasons are quoted by 05 §5.1).
        private const string BlockedNotInLobby = "Not in a lobby";
        private const string BlockedPendingAuth = "Waiting for a player to enter the password";
        private const string BlockedNotReady = "Waiting for players to be ready";
        private const string AlreadyJoiningText = "Already joining a game";
        private const string FallbackLobbyName = "Co-op game";
        private const string CaptureNullDetail = "capture returned null";

        // Services. saves is null without the WorldSaves capability; readyGate is the optional IGameReadyGate.
        private readonly IGameAdapter adapter;
        private readonly ILobbyService lobby;
        private readonly ISessionNetwork network;
        private readonly IInviteService invites;
        private readonly IPlayerIdentity identity;
        private readonly IWorldSaveStore saves;
        private readonly IGameReadyGate readyGate;
        private readonly SessionStateMachine machine = new SessionStateMachine();
        private readonly HostSettingsSchema schema;
        private readonly GameCapabilities capabilities;
        private readonly bool worldSaves;

        // Session fields (02 §0). Every one is reset by ClearSessionFields at the end of Teardown.
        private HostSettings settings;
        private PlayerId sessionHost;                   // owner captured at create or entry; never updated (flow 7)
        private LobbyPhase phase;
        private string joinPassword;                    // client plaintext, memory only (approval payload)
        private LobbyId joinTarget;
        private int joinAttempts;
        private bool authSent;                          // an auth line was sent for the current attempt
        private float authDeadline;
        private bool awaitingPassword;                  // prompt open: the Joining deadline is suspended
        private bool awaitingLobbyData;                 // entered, but k.game is not readable yet
        private readonly Dictionary<PlayerId, float> pendingAuth = new Dictionary<PlayerId, float>();
        private readonly Dictionary<PlayerId, int> authFailures = new Dictionary<PlayerId, int>();
        private readonly HashSet<PlayerId> bans = new HashSet<PlayerId>();
        private readonly HashSet<PlayerId> startRoster = new HashSet<PlayerId>();
        private readonly HashSet<string> resumeLocked = new HashSet<string>(StringComparer.Ordinal);
        private WorldSaveData resumeSave;
        private string sessionSlotId;
        private bool isLive;                            // OnSessionLive returned; OnSessionEnding is owed
        private bool reachedInGame;                     // InGame was entered (Teardown's autosave condition)
        private bool awaitingWorldReady;                // host: OnSessionLive done, polling IGameReadyGate
        private float stateDeadline = float.PositiveInfinity;
        private double basePlaytime;                    // seconds already stored in the slot (02 §6c.5)
        private float liveSince;                        // clock at InGame or at the last successful save
        private float lastAutosave;
        private LobbyId pendingJoin;                    // overlay join waiting for ConfirmPendingJoin
        private bool canStart;
        private string startBlockedReason = BlockedNotInLobby;

        // Plumbing.
        private float clock;                            // last Tick time
        private bool clockStarted;
        private float nextPresenceRefresh;
        private PresenceInfo lastPresence;
        private bool presenceSent;
        private bool quitting;                          // MarkQuitting: Teardown skips LoadLocalScene
        private bool tearingDown;                       // Teardown re-entrancy guard
        private readonly List<string> changedSettingKeys = new List<string>();   // HostSettings.Changed, flushed by Room
        private readonly List<ToastMessage> deferredToasts = new List<ToastMessage>(); // shown after teardown
        private readonly List<PlayerId> scratchPlayers = new List<PlayerId>();

        /// <summary>
        /// Builds the schema (throws on programmer error, 01 §4.8), subscribes to every service event and publishes
        /// the in-menu presence (02 §1 step 4.1). saves may be null when the adapter has no WorldSaves capability.
        /// </summary>
        public SessionController(IGameAdapter adapter, ILobbyService lobby, ISessionNetwork network,
            IInviteService invites, IPlayerIdentity identity, IWorldSaveStore saves)
        {
            this.adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
            this.lobby = lobby ?? throw new ArgumentNullException(nameof(lobby));
            this.network = network ?? throw new ArgumentNullException(nameof(network));
            this.invites = invites ?? throw new ArgumentNullException(nameof(invites));
            this.identity = identity ?? throw new ArgumentNullException(nameof(identity));
            this.saves = saves;

            ValidateAdapter(adapter);
            schema = HostSettingsSchema.Build(adapter);
            capabilities = adapter.Capabilities;
            bool savesCapability = (capabilities & GameCapabilities.WorldSaves) != 0;
            worldSaves = savesCapability && saves != null;
            if (savesCapability && saves == null) KitLog.Warning("WorldSaves capability set but no IWorldSaveStore given; saving is off.");
            readyGate = adapter as IGameReadyGate;
            ReplaceSettings(new HostSettings(schema));

            machine.Changed += OnMachineChanged;
            lobby.MembersChanged += OnMembersChanged;
            lobby.MemberJoined += OnMemberJoined;
            lobby.MemberLeft += OnMemberLeft;
            lobby.LobbyDataChanged += OnLobbyDataChanged;
            lobby.MemberDataChanged += OnMemberDataChanged;
            lobby.ChatReceived += OnChatReceived;
            lobby.OwnerChanged += OnOwnerChanged;
            lobby.LostLobby += OnLostLobby;
            network.NetworkSceneLoaded += OnNetworkSceneLoaded;
            network.NetworkSceneLoadFailed += OnNetworkSceneLoadFailed;
            network.ClientConnected += OnClientConnected;
            network.ClientDisconnected += OnClientDisconnected;
            network.LocalDisconnected += OnLocalDisconnected;
            invites.JoinRequested += OnJoinRequested;

            UpdatePresence();
        }

        // Read-only state for presenters (01 §6.2).

        public SessionState State => machine.State;
        public SessionError LastError => machine.LastError;

        /// <summary>Local player is the session host (owner at create). A peer that inherits Steam ownership is not.</summary>
        public bool IsHost => sessionHost.IsValid && sessionHost == lobby.LocalPlayer;

        public LobbyId CurrentLobby => lobby.Current;
        public PlayerId LocalPlayer => lobby.LocalPlayer;
        public LobbyPhase Phase => phase;
        public IReadOnlyList<LobbyMember> Members => lobby.Members;
        public HostSettingsSchema Schema => schema;
        public IReadOnlyHostSettings Settings => settings;
        public GameCapabilities Capabilities => capabilities;

        /// <summary>The slot being resumed; null for a new game.</summary>
        public WorldSaveSlotInfo ResumeSlot { get; private set; }

        public bool CanStart => canStart;

        /// <summary>Null when CanStart.</summary>
        public string StartBlockedReason => canStart ? null : startBlockedReason;

        /// <summary>Re-read live; false makes the menu disable its networked buttons (05 §4).</summary>
        public bool IsPlatformAvailable => lobby.IsAvailable;

        /// <summary>Host: kicked this session but still listed in Members. Always false on clients.</summary>
        public bool IsBeingRemoved(PlayerId player) => IsHost && bans.Contains(player);

        /// <summary>Bootstrap OnApplicationQuit: Teardown then skips the menu scene load (02 §1 step 6).</summary>
        public void MarkQuitting()
        {
            quitting = true;
        }

        // Events the UI binds to (all main thread).

        public event Action<SessionStateChange> StateChanged;
        public event Action<IReadOnlyList<LobbyMember>> MembersChanged;
        public event Action<string> SettingChanged;
        public event Action<LobbyPhase> PhaseChanged;
        public event Action<bool, string> CanStartChanged;
        public event Action<LobbyId, bool> PasswordRequired;          // lobby, previousAttemptWrong
        public event Action<LobbyId> JoinRequestNeedsConfirm;
        public event Action<ToastMessage> Toast;
        public event Action<WorldSaveSlotInfo> WorldSaved;

        public void AcknowledgeError()
        {
            if (State == SessionState.Error) machine.TryTransition(SessionState.MainMenu);
        }

        /// <summary>False outside a lobby or when the platform overlay is unavailable.</summary>
        public bool OpenInviteOverlay()
        {
            if (!lobby.InLobby || !IsInSession(State)) return false;
            try
            {
                return invites.OpenInviteOverlay(lobby.Current);
            }
            catch (Exception e)
            {
                KitLog.Error("IInviteService.OpenInviteOverlay threw.", e);
                return false;
            }
        }

        /// <summary>Opens the Steam friends list overlay (no lobby required). Used by the main-menu
        /// "Join Friends" button as a stand-in until the in-app friends-games browser is built.</summary>
        public bool OpenFriendsOverlay()
        {
            try
            {
                return invites.OpenFriendsOverlay();
            }
            catch (Exception e)
            {
                KitLog.Error("IInviteService.OpenFriendsOverlay threw.", e);
                return false;
            }
        }

        /// <summary>Driven by the bootstrap's Update with Time.unscaledTime: deadlines, auth, autosave, presence.</summary>
        public void Tick(float nowSeconds)
        {
            if (!clockStarted)
            {
                // Deadlines set before the first Tick (the bootstrap's launch-arg join in Start) used clock 0.
                clockStarted = true;
                float shift = nowSeconds - clock;
                if (shift > 0f)
                {
                    stateDeadline += shift;
                    authDeadline += shift;
                }
            }
            clock = nowSeconds;

            switch (State)
            {
                case SessionState.CreatingLobby:
                    if (clock > stateDeadline)
                    {
                        KitLog.Warning("Lobby creation timed out.");
                        FailSession(SessionEndReason.Error, new SessionError(KitError.Timeout, null, "create timeout"));
                    }
                    break;
                case SessionState.Joining:
                    TickJoining();
                    break;
                case SessionState.LobbyRoom:
                    if (IsHost) TickPendingAuth();
                    break;
                case SessionState.StartingGame:
                    if (IsHost)
                    {
                        TickPendingAuth();
                        TickHostStarting();
                    }
                    else
                    {
                        TickClientStarting();
                    }
                    break;
                case SessionState.InGame:
                    if (IsHost)
                    {
                        TickPendingAuth();
                        TickAutosave();
                    }
                    break;
            }

            if (clock >= nextPresenceRefresh)
            {
                nextPresenceRefresh = clock + PresenceRefreshSeconds;
                UpdatePresence();
            }
        }

        // Join requests from the overlay or rich presence (01 §6.3, 02 §9.4).
        private void OnJoinRequested(LobbyId target)
        {
            if (!target.IsValid) return;
            if (target == lobby.Current || target == joinTarget)
            {
                KitLog.Info("Join request for the current lobby ignored.");
                return;
            }

            switch (State)
            {
                case SessionState.MainMenu:
                case SessionState.Error:   // JoinLobby acknowledges the error first
                    KitResult r = JoinLobby(target);
                    if (!r.Ok) KitLog.Warning("Requested join failed: " + r);
                    break;
                case SessionState.CreatingLobby:
                case SessionState.Joining:
                case SessionState.Leaving:
                    RaiseToast(ToastSeverity.Info, SessionErrorMessages.Resolve(KitError.Busy, AlreadyJoiningText));
                    break;
                default:
                    pendingJoin = target;   // a newer request replaces the pending one (02 §9.7)
                    Emit(JoinRequestNeedsConfirm, target, "JoinRequestNeedsConfirm");
                    break;
            }
        }

        /// <summary>Answers JoinRequestNeedsConfirm. Accept leaves the current session (host autosaves), then joins.</summary>
        public void ConfirmPendingJoin(bool accept)
        {
            LobbyId target = pendingJoin;
            pendingJoin = LobbyId.None;
            if (!accept || !target.IsValid) return;

            if (IsInSession(State)) LeaveTo(SessionEndReason.LocalLeft, false);
            KitResult r = JoinLobby(target);
            if (!r.Ok) KitLog.Warning("Join after confirm failed: " + r);
        }

        // Flow 10. The rule itself is SessionRules.PresenceJoinable; this only builds and dedupes the snapshot.
        private void UpdatePresence()
        {
            var info = new PresenceInfo(PresenceStatus.InMenu, LobbyId.None, 0, 0);
            SessionState s = State;
            if (!tearingDown && lobby.InLobby && IsInSession(s))
            {
                int count = Members.Count;
                int max = settings.GetInt(CoreSettingKeys.MaxPlayers);
                bool joinable = SessionRules.PresenceJoinable(phase, CurrentPrivacy(), count, max,
                    settings.GetBool(CoreSettingKeys.JoinInProgress));
                info = new PresenceInfo(s == SessionState.InGame ? PresenceStatus.InGame : PresenceStatus.InLobbyRoom,
                    joinable ? lobby.Current : LobbyId.None, count, max);
            }

            if (presenceSent && info == lastPresence) return;
            lastPresence = info;
            presenceSent = true;
            try
            {
                invites.SetPresence(info);
            }
            catch (Exception e)
            {
                KitLog.Error("IInviteService.SetPresence threw.", e);
            }
        }

        // CanStart first, so presenters reading it from StateChanged see the new value.
        private void OnMachineChanged(SessionStateChange change)
        {
            RecomputeCanStart();
            Emit(StateChanged, change, "StateChanged");
            UpdatePresence();
        }

        // Helpers shared by the partials (settings and phase helpers live in .Room, adapter validation in .Live).

        /// <summary>In a lobby with a room, starting or live session.</summary>
        private static bool IsInSession(SessionState s)
        {
            return s == SessionState.LobbyRoom || s == SessionState.StartingGame || s == SessionState.InGame;
        }

        private KitResult Busy(string operation)
        {
            KitLog.Warning(operation + " ignored in state " + State + ".");
            return KitResult.Fail(KitError.Busy, operation);
        }

        private void RaiseToast(ToastSeverity severity, string text)
        {
            Emit(Toast, new ToastMessage(severity, text), "Toast");
        }

        // One failing subscriber must not keep the others from the event.
        private static void Emit<T>(Action<T> handlers, T arg, string eventName)
        {
            if (handlers == null) return;
            foreach (Delegate handler in handlers.GetInvocationList())
            {
                try
                {
                    ((Action<T>)handler)(arg);
                }
                catch (Exception e)
                {
                    KitLog.Error("SessionController." + eventName + " handler threw.", e);
                }
            }
        }

        private static void Emit<T1, T2>(Action<T1, T2> handlers, T1 a, T2 b, string eventName)
        {
            if (handlers == null) return;
            foreach (Delegate handler in handlers.GetInvocationList())
            {
                try
                {
                    ((Action<T1, T2>)handler)(a, b);
                }
                catch (Exception e)
                {
                    KitLog.Error("SessionController." + eventName + " handler threw.", e);
                }
            }
        }
    }
}

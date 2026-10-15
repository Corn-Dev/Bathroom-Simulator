// Pure session state machine: the 01 §6.1 transition table, checked transitions and Fail. Design: 01 §6.1.
// There is no ForceState: every transition is checked, and an illegal request is a bug that is logged at
// Warning and not applied. SessionController owns the machine and runs teardown before moving to MainMenu or
// Error. No UnityEngine use here (logging goes through KitLog), so the whole table is EditMode-testable.

using System;

namespace MultiplayerKit
{
    public sealed class SessionStateMachine
    {
        /// <summary>Starts at MainMenu.</summary>
        public SessionState State { get; private set; } = SessionState.MainMenu;

        /// <summary>The reason of the current Error state; default(SessionError) in every other state.</summary>
        public SessionError LastError { get; private set; }

        /// <summary>Raised after every applied transition (never for same-state requests or ignored ones).</summary>
        public event Action<SessionStateChange> Changed;

        /// <summary>
        /// The 01 §6.1 table. MainMenu → CreatingLobby, Joining. CreatingLobby → LobbyRoom, Leaving, Error.
        /// Joining → LobbyRoom, StartingGame, Leaving, Error. LobbyRoom → StartingGame, Leaving, Error.
        /// StartingGame → InGame, Leaving, Error. InGame → Leaving, Error. Leaving → MainMenu. Error → MainMenu.
        /// Every other pair, including X → X, is false here (same-state requests are handled by the instance).
        /// </summary>
        public static bool IsAllowed(SessionState from, SessionState to)
        {
            switch (from)
            {
                case SessionState.MainMenu:
                    return to == SessionState.CreatingLobby || to == SessionState.Joining;
                case SessionState.CreatingLobby:
                    return to == SessionState.LobbyRoom || to == SessionState.Leaving || to == SessionState.Error;
                case SessionState.Joining:
                    return to == SessionState.LobbyRoom || to == SessionState.StartingGame
                        || to == SessionState.Leaving || to == SessionState.Error;
                case SessionState.LobbyRoom:
                    return to == SessionState.StartingGame || to == SessionState.Leaving || to == SessionState.Error;
                case SessionState.StartingGame:
                    return to == SessionState.InGame || to == SessionState.Leaving || to == SessionState.Error;
                case SessionState.InGame:
                    return to == SessionState.Leaving || to == SessionState.Error;
                case SessionState.Leaving:
                case SessionState.Error:
                    return to == SessionState.MainMenu;
                default:
                    return false;
            }
        }

        /// <summary>Fail is legal from CreatingLobby, Joining, LobbyRoom, StartingGame and InGame.</summary>
        public static bool CanFail(SessionState state)
        {
            return IsAllowed(state, SessionState.Error);
        }

        /// <summary>True for the current state (a same-state request succeeds) and for every legal target.</summary>
        public bool CanTransition(SessionState to)
        {
            return to == State || IsAllowed(State, to);
        }

        /// <summary>
        /// Same state: true, no event. Legal: applied, Changed raised, true. Illegal: Warning, nothing changes,
        /// false. Prefer Fail(error) for Error so the UI gets a reason; TryTransition(Error) records Unknown.
        /// </summary>
        public bool TryTransition(SessionState to)
        {
            if (to == State) return true;
            if (!IsAllowed(State, to))
            {
                KitLog.Warning("Illegal session transition " + State + " -> " + to + " ignored.");
                return false;
            }
            Enter(to, to == SessionState.Error ? new SessionError(KitError.Unknown) : default(SessionError));
            return true;
        }

        /// <summary>
        /// Enters Error with the reason, legal only from CanFail states; elsewhere (MainMenu, Leaving, Error, so a
        /// second failure signal after the first) it logs Warning and is ignored. Code None is stored as Unknown.
        /// </summary>
        public void Fail(SessionError error)
        {
            if (!CanFail(State))
            {
                KitLog.Warning("Session Fail(" + error + ") ignored in state " + State + ".");
                return;
            }
            if (error.Code == KitError.None)
            {
                error = new SessionError(KitError.Unknown, error.UserMessage, error.Detail);
            }
            Enter(SessionState.Error, error);
        }

        private void Enter(SessionState to, SessionError error)
        {
            SessionState from = State;
            State = to;
            LastError = to == SessionState.Error ? error : default(SessionError);
            KitLog.Debug("Session " + from + " -> " + to + (to == SessionState.Error ? " (" + error + ")" : ""));

            Action<SessionStateChange> handler = Changed;
            if (handler == null) return;
            try
            {
                handler(new SessionStateChange(from, to, LastError));
            }
            catch (Exception e)
            {
                KitLog.Error("SessionStateMachine.Changed handler threw on " + from + " -> " + to + ".", e);
            }
        }
    }
}

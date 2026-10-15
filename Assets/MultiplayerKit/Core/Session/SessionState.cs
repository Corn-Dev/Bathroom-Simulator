// Session states, the state-change event payload, and the non-fatal toast notice the UI binds to.
// Design: 01 §6.1 (SessionState, SessionStateChange), 01 §6.2 (ToastSeverity, ToastMessage).
// Transitions between states are checked by SessionStateMachine; SessionController raises both payloads.

namespace MultiplayerKit
{
    public enum SessionState
    {
        MainMenu = 0,
        CreatingLobby = 1,
        Joining = 2,
        LobbyRoom = 3,
        StartingGame = 4,
        InGame = 5,
        Leaving = 6,
        Error = 7
    }

    /// <summary>One transition, raised after the state has changed.</summary>
    public readonly struct SessionStateChange
    {
        public readonly SessionState From;
        public readonly SessionState To;

        /// <summary>Meaningful only when To == Error; default otherwise.</summary>
        public readonly SessionError Error;

        public SessionStateChange(SessionState from, SessionState to, SessionError error = default(SessionError))
        {
            From = from;
            To = to;
            Error = error;
        }

        public override string ToString()
        {
            return To == SessionState.Error ? From + " -> Error (" + Error + ")" : From + " -> " + To;
        }
    }

    public enum ToastSeverity { Info, Warning, Error }

    /// <summary>Non-fatal notice (SessionController.Toast). Text is user-facing; never null.</summary>
    public readonly struct ToastMessage
    {
        public readonly ToastSeverity Severity;
        public readonly string Text;

        public ToastMessage(ToastSeverity severity, string text)
        {
            Severity = severity;
            Text = text ?? "";
        }

        public override string ToString()
        {
            return Severity + ": " + Text;
        }
    }
}

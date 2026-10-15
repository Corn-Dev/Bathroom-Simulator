// The one presenter behind the busy overlay, password prompt, error popup, toasts and the join-confirm dialog
// (05 §3.2), so the session-driven overlay rules live in a single EditMode-testable class. Plain class that talks
// only to SessionController; KitOverlay implements IOverlayView by forwarding to its child widgets.
// Design: 05 §3.2 (busy table, texts), §3.3, §10; 02 §3d.5 (password), §9.5-§9.7 (join confirm); 06 U9.

using System;

namespace MultiplayerKit.UI
{
    public interface IOverlayView
    {
        /// <summary>message null hides the busy overlay.</summary>
        void ShowBusy(string message, bool canCancel);

        void ShowPasswordPrompt(string lobbyLabel, bool previousWrong);
        void HidePasswordPrompt();

        /// <summary>User message only; never SessionError.Detail.</summary>
        void ShowError(string message);

        void HideError();
        void EnqueueToast(ToastSeverity severity, string text);
        void ShowConfirm(string title, string body, string confirmLabel, Action onConfirm, Action onCancel);
    }

    public sealed class OverlayPresenter
    {
        public const string CreatingText = "Creating lobby\u2026";
        public const string JoiningText = "Joining game\u2026";
        public const string StartingText = "Starting game\u2026";
        public const string LeavingText = "Leaving\u2026";
        public const string ErrorFallbackText = "Something went wrong.";
        public const string DefaultLobbyLabel = "this game";
        public const string GameSavedText = "Game saved.";

        private const string JoinTitle = "Join another game?";
        private const string JoinBody = "You will leave your current game.";
        private const string JoinSaveSuffix = " Your world will be saved first.";
        private const string JoinConfirmLabel = "Join";

        // "The next WorldSaved is a manual save" (05 §3.2). SessionMenuPresenter sets it right before SaveNow or
        // SaveAndQuit and clears it right after; Core raises WorldSaved synchronously inside those calls, so
        // autosaves never see it.
        private static bool manualSavePending;

        private readonly SessionController session;
        private readonly IOverlayView view;
        private bool bound;
        private bool passwordVisible;
        private LobbyId pendingJoinLobby;
        private string pendingJoinLabel;
        private int joinConfirmId;

        public OverlayPresenter(SessionController session, IOverlayView view)
        {
            this.session = session ?? throw new ArgumentNullException(nameof(session));
            this.view = view ?? throw new ArgumentNullException(nameof(view));
        }

        public static void BeginManualSave()
        {
            manualSavePending = true;
        }

        public static void EndManualSave()
        {
            manualSavePending = false;
        }

        public void Bind()
        {
            if (bound) return;
            bound = true;
            session.StateChanged += OnStateChanged;
            session.PasswordRequired += OnPasswordRequired;
            session.JoinRequestNeedsConfirm += OnJoinRequestNeedsConfirm;
            session.Toast += OnToast;
            session.WorldSaved += OnWorldSaved;
            Render();
        }

        public void Unbind()
        {
            if (!bound) return;
            bound = false;
            session.StateChanged -= OnStateChanged;
            session.PasswordRequired -= OnPasswordRequired;
            session.JoinRequestNeedsConfirm -= OnJoinRequestNeedsConfirm;
            session.Toast -= OnToast;
            session.WorldSaved -= OnWorldSaved;
            joinConfirmId++;   // a join dialog still open can no longer answer
        }

        /// <summary>Joining → CancelJoin; CreatingLobby or StartingGame → Leave; ignored in other states.</summary>
        public void OnBusyCancelClicked()
        {
            if (!bound) return;
            switch (session.State)
            {
                case SessionState.Joining:
                    session.CancelJoin();
                    break;
                case SessionState.CreatingLobby:
                case SessionState.StartingGame:
                    session.Leave();
                    break;
            }
        }

        /// <summary>Hands the text straight to SubmitPassword and keeps no copy (05 §3.3). The spinner returns.</summary>
        public void OnPasswordSubmitted(string password)
        {
            if (!bound) return;
            HidePasswordPrompt();
            session.SubmitPassword(password);
            RenderBusy();
        }

        public void OnPasswordCancelled()
        {
            if (!bound) return;
            HidePasswordPrompt();
            session.CancelJoin();
            RenderBusy();
        }

        public void OnErrorAcknowledged()
        {
            if (!bound) return;
            view.HideError();
            session.AcknowledgeError();
        }

        /// <summary>FriendsGames and PublicBrowser call this, through IKitDialogs, right before JoinLobby (05 §6, §7).</summary>
        public void SetPendingJoinLabel(LobbyId lobby, string label)
        {
            pendingJoinLobby = lobby;
            pendingJoinLabel = label;
        }

        private void OnStateChanged(SessionStateChange change)
        {
            if (!bound) return;
            if (change.From == SessionState.Joining && change.To != SessionState.Joining)
            {
                pendingJoinLobby = LobbyId.None;
                pendingJoinLabel = null;
            }
            Render();
        }

        // Applied on Bind and on every state change (05 §2.3: correct on the first frame).
        private void Render()
        {
            SessionState state = session.State;
            if (state != SessionState.Joining) HidePasswordPrompt();   // the prompt only exists while joining

            RenderBusy();

            if (state == SessionState.Error) view.ShowError(ErrorText());
            else view.HideError();
        }

        // 05 §3.2 busy table. While joining with the prompt open the spinner is hidden, so the two never stack.
        private void RenderBusy()
        {
            switch (session.State)
            {
                case SessionState.CreatingLobby:
                    view.ShowBusy(CreatingText, true);
                    break;
                case SessionState.Joining:
                    if (passwordVisible) view.ShowBusy(null, false);
                    else view.ShowBusy(JoiningText, true);
                    break;
                case SessionState.StartingGame:
                    view.ShowBusy(StartingText, true);
                    break;
                case SessionState.Leaving:
                    view.ShowBusy(LeavingText, false);
                    break;
                default:
                    view.ShowBusy(null, false);
                    break;
            }
        }

        private void HidePasswordPrompt()
        {
            if (!passwordVisible) return;
            passwordVisible = false;
            view.HidePasswordPrompt();
        }

        // 02 §3d.5: the only password UI. The label is the lobby name the screen supplied, else "this game".
        private void OnPasswordRequired(LobbyId lobby, bool previousWrong)
        {
            if (!bound || session.State != SessionState.Joining) return;

            bool named = lobby == pendingJoinLobby && !string.IsNullOrEmpty(pendingJoinLabel);
            passwordVisible = true;
            RenderBusy();
            view.ShowPasswordPrompt(named ? pendingJoinLabel : DefaultLobbyLabel, previousWrong);
        }

        // 05 §3.2, 02 §9.5-§9.7. A newer request replaces the open dialog, and replacing runs the old dialog's
        // cancel. Core already holds the newer target by then, so only the latest dialog may answer.
        private void OnJoinRequestNeedsConfirm(LobbyId lobby)
        {
            if (!bound) return;

            int id = ++joinConfirmId;
            bool saveFirst = session.IsHost && session.State == SessionState.InGame
                && (session.Capabilities & GameCapabilities.WorldSaves) != 0;
            view.ShowConfirm(JoinTitle, JoinBody + (saveFirst ? JoinSaveSuffix : ""), JoinConfirmLabel,
                () => AnswerJoin(id, true), () => AnswerJoin(id, false));
        }

        private void AnswerJoin(int id, bool accept)
        {
            if (!bound || id != joinConfirmId) return;
            joinConfirmId++;   // answered; a second callback from the same dialog is ignored
            session.ConfirmPendingJoin(accept);
        }

        private void OnToast(ToastMessage toast)
        {
            if (!bound || string.IsNullOrEmpty(toast.Text)) return;
            view.EnqueueToast(toast.Severity, toast.Text);
        }

        // Autosaves are silent; save failures already arrive as toasts from Core.
        private void OnWorldSaved(WorldSaveSlotInfo slot)
        {
            if (!bound || !manualSavePending) return;
            manualSavePending = false;
            view.EnqueueToast(ToastSeverity.Info, GameSavedText);
        }

        private string ErrorText()
        {
            string message = session.LastError.UserMessage;
            return string.IsNullOrEmpty(message) ? ErrorFallbackText : message;
        }
    }
}

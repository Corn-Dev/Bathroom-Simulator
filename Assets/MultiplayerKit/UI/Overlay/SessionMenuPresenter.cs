// Presenter of the in-game session menu (05 §3.6): opens only in game and closes on any state change away from it;
// button rules (Save Now and Save & Quit for a host with world saves, Leave always, labelled "End game" for the
// host); confirms for Save & Quit and Leave; invite; kick through the shared RosterPresenter, so an in-game kick takes
// the same confirm -> Session.Kick path as the lobby room (05 §5.2, 02 §5 step 1). Plain class; talks only to Core.
// Design: 05 §3.2 (manual-save toast flag), §3.6, §5.2, §10; 02 §5, §6b, §6c; 06 U16.

using System;
using System.Collections.Generic;

namespace MultiplayerKit.UI
{
    public interface ISessionMenuView
    {
        void SetOpen(bool open);
        void SetButtons(bool saveNow, bool saveAndQuit, bool leave, bool invite);

        /// <summary>"Leave game" (client) or "End game" (host).</summary>
        void SetLeaveLabel(string label);

        /// <summary>The lobby room's rows, ready column hidden.</summary>
        void ShowRoster(IReadOnlyList<RosterRowModel> rows);
    }

    public sealed class SessionMenuPresenter
    {
        public const string LeaveGameLabel = "Leave game";
        public const string EndGameLabel = "End game";
        public const string OverlayUnavailableText = "The Steam overlay is unavailable. Enable it in Steam settings to invite friends.";

        private const string EndGameSavedBody = "End the game for everyone? Your world will be saved first.";
        private const string EndGameBody = "This ends the game for everyone.";
        private const string LeaveGameBody = "Leave this game?";
        private const string LeaveConfirmLabel = "Leave";
        private const string SaveAndQuitTitle = "Save and quit?";
        private const string SaveAndQuitBody = "Your world will be saved, then the game ends for everyone.";
        private const string SaveAndQuitLabel = "Save & Quit";

        private readonly KitServices services;
        private readonly SessionController session;
        private readonly ISessionMenuView view;
        private readonly IKitDialogs dialogs;
        private readonly RosterPresenter roster;
        private bool bound;
        private bool open;

        public SessionMenuPresenter(KitServices services, ISessionMenuView view, IKitDialogs dialogs)
        {
            this.services = services ?? throw new ArgumentNullException(nameof(services));
            this.view = view ?? throw new ArgumentNullException(nameof(view));
            this.dialogs = dialogs ?? NullKitDialogs.Instance;
            session = services.Session;
            roster = new RosterPresenter(services, new RosterRows(view), this.dialogs, false);   // showReadyColumn: false
        }

        public bool IsOpen => open;

        public void Bind()
        {
            if (bound) return;
            bound = true;
            session.StateChanged += OnStateChanged;
            session.SettingChanged += OnSettingChanged;
            RenderButtons();
            view.SetOpen(false);
        }

        public void Unbind()
        {
            if (!bound) return;
            Close();
            bound = false;
            session.StateChanged -= OnStateChanged;
            session.SettingChanged -= OnSettingChanged;
        }

        /// <summary>Closes an open menu; opens it only while State == InGame.</summary>
        public void Toggle()
        {
            if (!bound) return;
            if (open) Close();
            else if (session.State == SessionState.InGame) Open();
        }

        public void OnResume()
        {
            Close();
        }

        public void OnInvite()
        {
            if (!bound) return;
            if (!session.OpenInviteOverlay()) dialogs.Toast(ToastSeverity.Warning, OverlayUnavailableText);
        }

        /// <summary>
        /// Host with world saves. The manual-save flag turns the WorldSaved that SaveNow raises into "Game saved."
        /// (05 §3.2). Busy is silent (05 §10); a full slot list gets its own text, other failures For(code).
        /// </summary>
        public void OnSaveNow()
        {
            if (!bound || !CanSave()) return;

            KitResult<WorldSaveSlotInfo> saved;
            OverlayPresenter.BeginManualSave();
            try
            {
                saved = session.SaveNow();
            }
            finally
            {
                OverlayPresenter.EndManualSave();
            }

            if (saved.Ok || saved.Error == KitError.Busy) return;
            dialogs.Toast(ToastSeverity.Warning, saved.Detail == WorldSaveNames.SlotsFullDetail
                ? SessionErrorMessages.SaveSlotsFull
                : SessionErrorMessages.For(saved.Error));
        }

        public void OnSaveAndQuit()
        {
            if (!bound || !CanSave()) return;
            dialogs.Confirm(SaveAndQuitTitle, SaveAndQuitBody, SaveAndQuitLabel, ConfirmSaveAndQuit);
        }

        /// <summary>Host: "End game", which ends the session for everyone. Client: "Leave game". Both confirm first.</summary>
        public void OnLeave()
        {
            if (!bound) return;
            if (session.IsHost) dialogs.Confirm(EndGameLabel, WorldSaves() ? EndGameSavedBody : EndGameBody, EndGameLabel, ConfirmLeave);
            else dialogs.Confirm(LeaveGameLabel, LeaveGameBody, LeaveConfirmLabel, ConfirmLeave);
        }

        /// <summary>The roster's kick button: RosterPresenter confirms, then calls Session.Kick (05 §5.2).</summary>
        public void OnKick(PlayerId player)
        {
            if (!bound) return;
            roster.OnKickClicked(player);
        }

        // A failed save raises Core's "Save failed. The game was not closed." toast and stays in game (02 §6c.3),
        // so the menu stays open for a retry or "End game".
        private void ConfirmSaveAndQuit()
        {
            if (!bound) return;
            OverlayPresenter.BeginManualSave();
            try
            {
                session.SaveAndQuit();
            }
            finally
            {
                OverlayPresenter.EndManualSave();
            }
        }

        // Host: Teardown autosaves first and still leaves if that save fails, so a host is never trapped (02 §6c.2).
        private void ConfirmLeave()
        {
            if (!bound) return;
            session.Leave();
        }

        private void OnStateChanged(SessionStateChange change)
        {
            if (!bound) return;
            if (change.To != SessionState.InGame) Close();
            RenderButtons();
        }

        private void OnSettingChanged(string key)
        {
            if (!bound) return;
            if (key == CoreSettingKeys.JoinInProgress) RenderButtons();
        }

        private void Open()
        {
            open = true;
            RenderButtons();
            view.SetOpen(true);
            roster.Bind();   // the player list renders and listens only while the menu is open
        }

        private void Close()
        {
            if (!open) return;
            open = false;
            roster.Unbind();
            view.SetOpen(false);
        }

        // 05 §3.6 button rules. Invite shows while late joiners can get in: with joinInProgress off the lobby is not
        // joinable during the game (decision 4), so an invite could only fail.
        private void RenderButtons()
        {
            bool host = session.IsHost;
            bool saves = host && WorldSaves();
            bool invite = session.State == SessionState.InGame && session.Settings.GetBool(CoreSettingKeys.JoinInProgress);
            view.SetButtons(saves, saves, true, invite);
            view.SetLeaveLabel(host ? EndGameLabel : LeaveGameLabel);
        }

        private bool CanSave()
        {
            return session.IsHost && WorldSaves();
        }

        private bool WorldSaves()
        {
            return (session.Capabilities & GameCapabilities.WorldSaves) != 0 && services.Saves != null;
        }

        // Hands the RosterPresenter's rows to the menu's roster.
        private sealed class RosterRows : IRosterView
        {
            private readonly ISessionMenuView view;

            public RosterRows(ISessionMenuView view)
            {
                this.view = view;
            }

            public void ShowRows(IReadOnlyList<RosterRowModel> rows)
            {
                view.ShowRoster(rows);
            }
        }
    }
}

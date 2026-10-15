// SessionController, teardown part: Leave (02 §6), the ordered and re-entrancy-safe Teardown (01 §6.1), the two
// ways a session ends (LeaveTo → MainMenu, FailSession → Error, teardown always first), and host-left
// detection (02 §7: OwnerChanged, MemberLeft of the session host, LostLobby). No host migration (decision 11):
// a peer that inherits Steam ownership leaves like everyone else.

using System;

namespace MultiplayerKit
{
    public sealed partial class SessionController
    {
        /// <summary>Any state but MainMenu; idempotent. In Error it acknowledges the error (02 §6b.2).</summary>
        public void Leave()
        {
            switch (State)
            {
                case SessionState.MainMenu:
                case SessionState.Leaving:
                    return;
                case SessionState.Error:
                    AcknowledgeError();
                    return;
                default:
                    LeaveTo(SessionEndReason.LocalLeft, false);
                    return;
            }
        }

        // Leaving → Teardown → MainMenu, then the toasts teardown queued (02 §6a.2, §6b.2).
        private void LeaveTo(SessionEndReason reason, bool skipAutosave)
        {
            if (tearingDown || !machine.TryTransition(SessionState.Leaving)) return;
            Teardown(reason, skipAutosave);
            machine.TryTransition(SessionState.MainMenu);
            FlushDeferredToasts();
        }

        // Teardown → Error. Ignored while tearing down and outside the Fail states, so the first signal wins.
        private void FailSession(SessionEndReason reason, SessionError error)
        {
            if (tearingDown || !SessionStateMachine.CanFail(State)) return;
            KitLog.Debug("Ending the session (" + reason + "): " + error);
            Teardown(reason, false);
            machine.Fail(error);
            FlushDeferredToasts();
        }

        // 02 §7b.3. The room variant of the message while joining or in the room; the in-game text otherwise.
        private void EndForHostLeft()
        {
            if (IsHost) return;
            SessionState s = State;
            if (s != SessionState.Joining && !IsInSession(s)) return;
            KitLog.Info("The session host left (" + s + ").");
            string message = s == SessionState.Joining || s == SessionState.LobbyRoom ? SessionErrorMessages.HostClosedLobby : null;
            FailSession(SessionEndReason.HostLeft, new SessionError(KitError.HostLeft, message));
        }

        // 02 §7b.1: the kit never transfers ownership, so any change means the host is gone.
        private void OnOwnerChanged(PlayerId previous, PlayerId current)
        {
            if (tearingDown || !previous.IsValid) return;
            SessionState s = State;
            if (s == SessionState.MainMenu || s == SessionState.CreatingLobby || s == SessionState.Leaving || s == SessionState.Error) return;
            if (IsHost)
            {
                KitLog.Warning("Lobby ownership moved away from the session host; ignored.");
                return;
            }
            if (current == LocalPlayer) KitLog.Info("This player became the lobby owner; it does not take over as host.");
            EndForHostLeft();
        }

        // The platform removed the local member (rare). Clients: host left. Host: the session cannot go on.
        private void OnLostLobby(KitError error)
        {
            if (tearingDown) return;
            SessionState s = State;
            if (s != SessionState.Joining && !IsInSession(s)) return;
            KitLog.Info("The platform removed this player from the lobby: " + error);
            if (IsHost) FailSession(SessionEndReason.Error, new SessionError(error == KitError.None ? KitError.ConnectionLost : error));
            else EndForHostLeft();
        }

        // 01 §6.1, in order, each step isolated so one failure cannot skip the rest. Runs before the state becomes
        // MainMenu or Error. A second call while one runs is ignored.
        private void Teardown(SessionEndReason reason, bool skipAutosave)
        {
            if (tearingDown) return;
            tearingDown = true;
            bool host = IsHost;
            try
            {
                Step("autosave", () => AutosaveOnTeardown(reason, skipAutosave));
                if (isLive)
                {
                    isLive = false;   // OnSessionEnding exactly once, while the NetworkManager is still valid
                    Step("OnSessionEnding", () => adapter.OnSessionEnding(reason));
                }
                if (host && network.IsRunning) Step("disconnect clients", DisconnectAllClients);
                Step("network shutdown", ShutdownNetwork);
                Step("leave lobby", lobby.Leave);
                Step("presence", UpdatePresence);   // InMenu: tearingDown is set and the lobby is gone
                Step("reset", ClearSessionFields);
                if (!quitting) Step("menu scene", LoadMenuScene);
            }
            finally
            {
                tearingDown = false;
            }
        }

        private static void Step(string name, Action action)
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                KitLog.Error("Teardown step '" + name + "' threw; continuing.", e);
            }
        }

        // Best effort, so clients read HostLeft rather than ConnectionLost (02 §6c.2); no-op for unconnected members.
        private void DisconnectAllClients()
        {
            foreach (LobbyMember member in Members)
            {
                if (!member.IsLocal) network.DisconnectClient(member.Id, KitError.HostLeft);
            }
        }

        // The approval rules belong to this session only; with no handler the network fails closed.
        private void ShutdownNetwork()
        {
            network.ApprovalHandler = null;
            network.Shutdown();
        }

        // Always, except while quitting; the network implementation no-ops when the menu is already active.
        private void LoadMenuScene()
        {
            network.LoadLocalScene(adapter.MenuSceneName);
        }

        // Every 02 §0 session field, plus the controller's own session state.
        private void ClearSessionFields()
        {
            ReplaceSettings(new HostSettings(schema));
            sessionHost = PlayerId.None;
            phase = LobbyPhase.Room;
            joinPassword = null;
            joinTarget = LobbyId.None;
            joinAttempts = 0;
            authSent = false;
            authDeadline = 0f;
            awaitingPassword = false;
            awaitingLobbyData = false;
            pendingAuth.Clear();
            authFailures.Clear();
            bans.Clear();
            startRoster.Clear();
            resumeLocked.Clear();
            resumeSave = null;
            ResumeSlot = null;
            sessionSlotId = null;
            isLive = false;
            reachedInGame = false;
            awaitingWorldReady = false;
            stateDeadline = float.PositiveInfinity;
            basePlaytime = 0d;
            liveSince = 0f;
            lastAutosave = 0f;
            pendingJoin = LobbyId.None;
            scratchPlayers.Clear();
        }

        private void FlushDeferredToasts()
        {
            if (deferredToasts.Count == 0) return;
            ToastMessage[] toasts = deferredToasts.ToArray();
            deferredToasts.Clear();
            foreach (ToastMessage toast in toasts) Emit(Toast, toast, "Toast");
        }
    }
}

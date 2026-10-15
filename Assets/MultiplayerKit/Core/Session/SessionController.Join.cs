// SessionController, join part (client): JoinLobby and the common join tail (02 §3d), the single post-join
// password prompt with attempt counting (02 §3d.5-8.5), admission into the room or straight into the game
// (02 §3d.9, §4), following the host's phase (02 §2f), the one client connect path (02 §4.5), client go-live
// (02 §4.12-14, 03 §11.2 step 7) and disconnect classification (02 §7c).

using System;
using System.Globalization;

namespace MultiplayerKit
{
    public sealed partial class SessionController
    {
        /// <summary>
        /// From MainMenu (an Error is acknowledged first). password null = unknown; a passworded lobby then raises
        /// PasswordRequired after entering. PlatformUnavailable also raises a Toast (02 §3d.1).
        /// </summary>
        public KitResult JoinLobby(LobbyId lobby, string password = null)
        {
            if (State == SessionState.Error) AcknowledgeError();
            if (State != SessionState.MainMenu) return Busy("JoinLobby");
            if (!lobby.IsValid) return KitResult.Fail(KitError.InvalidArgument, "invalid lobby id");
            if (!this.lobby.IsAvailable)
            {
                RaiseToast(ToastSeverity.Error, SessionErrorMessages.For(KitError.PlatformUnavailable));
                return KitResult.Fail(KitError.PlatformUnavailable);
            }

            joinTarget = lobby;
            joinPassword = password;
            joinAttempts = 0;
            phase = LobbyPhase.Room;
            if (!machine.TryTransition(SessionState.Joining)) return KitResult.Fail(KitError.Busy, "JoinLobby");
            EnterJoinTarget();
            return KitResult.Success;
        }

        // 02 §3d.2: also the re-entry after a wrong-password kick (step 6). lobby.Join comes last because the
        // service may complete synchronously.
        private void EnterJoinTarget()
        {
            authSent = false;
            awaitingPassword = false;
            awaitingLobbyData = false;
            stateDeadline = clock + KitTimeouts.CreateJoin;
            lobby.Join(joinTarget, OnJoinDone);
        }

        private void OnJoinDone(KitResult<LobbyId> result)
        {
            if (tearingDown) return;
            if (State != SessionState.Joining)
            {
                if (result.Ok && lobby.Current == result.Value) lobby.Leave();   // cancelled meanwhile (02 §6a.3)
                return;
            }
            if (!result.Ok)
            {
                KitLog.Warning("Joining lobby " + joinTarget + " failed: " + result);
                FailSession(SessionEndReason.Error, new SessionError(result.Error, null, result.Detail));
                return;
            }
            EvaluateJoinedLobby();
        }

        // 02 §3d.4: compatibility, host capture, joinability, then password or admission.
        private void EvaluateJoinedLobby()
        {
            awaitingLobbyData = false;
            string game = lobby.GetData(KitKeys.Game);
            if (game.Length == 0)
            {
                awaitingLobbyData = true;   // data still propagating; OnLobbyDataChanged re-runs this (03 §6)
                return;
            }
            if (!IsCompatibleLobby(game))
            {
                KitLog.Info("Lobby " + joinTarget + " is not compatible with this game version.");
                FailSession(SessionEndReason.Error, new SessionError(KitError.IncompatibleVersion));
                return;
            }

            // Captured once; on a re-entry a different owner means the original host is gone (flow 7).
            PlayerId owner = lobby.Owner;
            if (!sessionHost.IsValid)
            {
                sessionHost = owner;
            }
            else if (owner != sessionHost)
            {
                EndForHostLeft();
                return;
            }

            ReadPhaseFromLobby();
            bool joinInProgress = lobby.GetData(SettingCodec.ToLobbyKey(CoreSettingKeys.JoinInProgress)) == KitKeys.FlagOn;
            if (phase == LobbyPhase.InGame && !joinInProgress)
            {
                KitLog.Info("Lobby " + joinTarget + " is in game and does not allow joining in progress.");
                FailSession(SessionEndReason.Error, new SessionError(KitError.LobbyNotJoinable));
                return;
            }

            if (lobby.GetData(KitKeys.HasPassword) == KitKeys.FlagOn)
            {
                if (joinPassword == null) RaisePasswordPrompt(false);
                else SendAuth();
                return;
            }
            AdmitToSession();
        }

        private bool IsCompatibleLobby(string game)
        {
            return string.Equals(game, adapter.GameId, StringComparison.Ordinal)
                && string.Equals(lobby.GetData(KitKeys.Proto), KitVersion.Protocol.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
                && string.Equals(lobby.GetData(KitKeys.Version), adapter.GameVersion, StringComparison.Ordinal);
        }

        // 02 §3d.4.5: no client deadline while the prompt is open; the host's auth deadline still applies.
        private void RaisePasswordPrompt(bool previousAttemptWrong)
        {
            awaitingPassword = true;
            stateDeadline = float.PositiveInfinity;
            Emit(PasswordRequired, joinTarget, previousAttemptWrong, "PasswordRequired");
        }

        /// <summary>Answers PasswordRequired; ignored unless Joining with the prompt open (02 §3d.6).</summary>
        public void SubmitPassword(string password)
        {
            if (State != SessionState.Joining || !awaitingPassword)
            {
                KitLog.Warning("SubmitPassword ignored in state " + State + ".");
                return;
            }
            awaitingPassword = false;
            joinPassword = password ?? "";
            if (lobby.InLobby && lobby.Current == joinTarget) SendAuth();
            else EnterJoinTarget();   // removed after a wrong attempt or the host's deadline: rejoin, password known
        }

        public void CancelJoin()
        {
            if (State != SessionState.Joining)
            {
                KitLog.Warning("CancelJoin ignored in state " + State + ".");
                return;
            }
            LeaveTo(SessionEndReason.LocalLeft, false);
        }

        // 02 §3d.7.
        private void SendAuth()
        {
            string hash = PasswordHasher.Hash(joinTarget, joinPassword);
            if (!SendChatLine(LobbyChatCodec.Verb.Auth, sessionHost, hash))
            {
                KitLog.Error("Sending the password check failed.");
                FailSession(SessionEndReason.Error, new SessionError(KitError.Unknown, null, "auth not sent"));
                return;
            }
            authSent = true;
            authDeadline = clock + KitTimeouts.AuthReply;
            stateDeadline = float.PositiveInfinity;   // entry is done; the reply deadline takes over (3d.8.5)
        }

        private void OnAuthOk()
        {
            if (State == SessionState.Joining && !awaitingPassword && !awaitingLobbyData) AdmitToSession();
        }

        // 02 §3d.8 and §5.8-9. A wrong-password kick while joining re-prompts; any other kick ends the session.
        private void OnKickReceived(KitError reason)
        {
            SessionState s = State;
            if (s == SessionState.Joining && reason == KitError.WrongPassword)
            {
                lobby.Leave();
                bool counted = authSent;   // a host-deadline kick for a user who never submitted does not count
                if (counted) joinAttempts++;
                authSent = false;
                if (joinAttempts >= MaxPasswordAttempts)
                {
                    KitLog.Info("Wrong password " + joinAttempts + " times; giving up.");
                    FailSession(SessionEndReason.Error, new SessionError(KitError.WrongPassword));
                    return;
                }
                RaisePasswordPrompt(counted);
                return;
            }
            if (s != SessionState.Joining && !IsInSession(s)) return;
            KitLog.Info("Removed by the host: " + reason);
            FailSession(SessionEndReason.Kicked, new SessionError(reason));
        }

        // 02 §3d.8.5, in this order.
        private void TickJoining()
        {
            if (!awaitingPassword && clock > stateDeadline)
            {
                KitLog.Warning("Joining lobby " + joinTarget + " timed out.");
                FailSession(SessionEndReason.Error, new SessionError(KitError.Timeout, null, "join timeout"));
                return;
            }
            if (authSent && clock > authDeadline)
            {
                KitLog.Warning("No answer from the host to the password check.");
                FailSession(SessionEndReason.Error, new SessionError(KitError.Timeout, null, "auth reply timeout"));
            }
        }

        // 02 §3d.9: room or starting → LobbyRoom (FollowPhase may move on); inGame → StartingGame (flow 4).
        private void AdmitToSession()
        {
            authSent = false;
            stateDeadline = float.PositiveInfinity;
            settings.ApplyLobbyData(lobby.GetAllData());
            changedSettingKeys.Clear();   // the room reads Settings when it opens
            lobby.SetLocalMemberData(KitKeys.Ready, KitKeys.FlagOff);
            ReadPhaseFromLobby();

            if (phase == LobbyPhase.InGame)
            {
                if (!machine.TryTransition(SessionState.StartingGame)) return;
                stateDeadline = clock + KitTimeouts.StartingGame;
            }
            else if (!machine.TryTransition(SessionState.LobbyRoom))
            {
                return;
            }
            FollowPhase();
            UpdatePresence();
        }

        // 02 §2f, clients only: one connect path for room clients and mid-game joiners.
        private void FollowPhase()
        {
            if (IsHost || phase == LobbyPhase.Room) return;
            if (State == SessionState.LobbyRoom)
            {
                if (!machine.TryTransition(SessionState.StartingGame)) return;
                // The host may sit in its world-ready gate before it publishes inGame (03 §11.2).
                stateDeadline = clock + KitTimeouts.StartingGame + KitTimeouts.WorldReadySeconds;
            }
            if (phase != LobbyPhase.InGame || State != SessionState.StartingGame || network.IsRunning) return;

            // Invalid: not written yet, the deadline covers a host that never writes it. Compared with the captured
            // host, not the live owner, so a stale or forged value cannot redirect the client.
            if (!PlayerId.TryParse(lobby.GetData(KitKeys.Server), out PlayerId server)) return;
            if (server != sessionHost)
            {
                KitLog.Warning("k.server does not match the lobby host; waiting.");
                return;
            }
            BeginClientConnect(server);
        }

        // 02 §4.5; repeated metadata updates never restart NGO.
        private void BeginClientConnect(PlayerId server)
        {
            if (network.IsRunning) return;
            var payload = new ConnectionPayload(KitVersion.Protocol, adapter.GameVersion, LocalPlayer,
                PasswordHasher.Hash(lobby.Current, joinPassword ?? ""));
            stateDeadline = clock + KitTimeouts.StartingGame;   // connect, approval and scene sync
            KitResult started = network.StartClient(server, payload);
            if (!started.Ok)
            {
                KitLog.Error("NGO StartClient failed: " + started);
                FailSession(SessionEndReason.Error, new SessionError(KitError.NetworkStartFailed, null, started.Detail));
            }
        }

        // 03 §11.2 step 7: synchronized into the gameplay scene → OnSessionLive → InGame.
        private void OnClientSceneLoaded(string sceneName)
        {
            if (!string.Equals(sceneName, adapter.GameplaySceneName, StringComparison.Ordinal))
            {
                KitLog.Error("Synchronized into scene '" + KitLog.Truncate(sceneName) + "', not the gameplay scene.");
                FailSession(SessionEndReason.Error, new SessionError(KitError.SceneLoadFailed, null, sceneName));
                return;
            }
            if (GoLive(false)) EnterInGame();
        }

        private void TickClientStarting()
        {
            if (clock <= stateDeadline) return;
            KitLog.Warning("Timed out waiting for the host to start or for the scene sync.");
            FailSession(SessionEndReason.Error, new SessionError(KitError.Timeout, null, "starting timeout"));
        }

        // 02 §7c ClassifyDisconnect. Raised by the network only after StartHost/StartClient, never for our Shutdown.
        private void OnLocalDisconnected(KitError reason, string rawReason)
        {
            if (tearingDown) return;
            SessionState s = State;
            if (s != SessionState.StartingGame && s != SessionState.InGame) return;
            string detail = KitLog.Truncate(rawReason);

            if (IsHost)
            {
                // Transport failure; the world is intact, so Teardown(Error) autosaves.
                KitLog.Info("The host lost its network session: " + reason + " " + detail);
                FailSession(SessionEndReason.Error, new SessionError(KitError.ConnectionLost, null, detail));
                return;
            }
            if (reason == KitError.HostLeft)
            {
                EndForHostLeft();
                return;
            }
            if (IsRejectionCode(reason))
            {
                KitLog.Info("The host refused or removed this player: " + reason);
                bool removed = reason == KitError.Kicked || reason == KitError.Banned;
                FailSession(removed ? SessionEndReason.Kicked : SessionEndReason.Error, new SessionError(reason, null, detail));
                return;
            }
            if (lobby.Owner != sessionHost || !lobby.IsMember(sessionHost))
            {
                EndForHostLeft();
                return;
            }
            KitLog.Info("Lost the connection to the host: " + detail);
            FailSession(SessionEndReason.ConnectionLost, new SessionError(KitError.ConnectionLost, null, detail));
        }

        private static bool IsRejectionCode(KitError reason)
        {
            switch (reason)
            {
                case KitError.ApprovalRejected:
                case KitError.IncompatibleVersion:
                case KitError.Banned:
                case KitError.LobbyAccessDenied:
                case KitError.WrongPassword:
                case KitError.LobbyFull:
                case KitError.LobbyNotJoinable:
                case KitError.Kicked:
                    return true;
                default:
                    return false;
            }
        }
    }
}

// SessionController, host part: new game (02 §2a), lobby create, Start (decision 8, 02 §2e), connection approval
// (01 §6.3 order + 03 §10.2 guard), go-live through the optional IGameReadyGate (04 §9.3), member join/leave
// (02 §3d.10, 03 §13), the password auth check (02 §3d.11-12), kick and ban (02 §5) and the NGO client hooks
// forwarded to the adapter (03 §14). Resume from a save (02 §2b) is in .Live to keep this file under 400 lines.

using System;
using System.Collections.Generic;

namespace MultiplayerKit
{
    public sealed partial class SessionController
    {
        /// <summary>MainMenu only. PlatformUnavailable is returned without a toast; the menu shows it (02 §2a.2.2).</summary>
        public KitResult HostNewGame()
        {
            if (State != SessionState.MainMenu) return Busy("HostNewGame");
            if (!lobby.IsAvailable) return KitResult.Fail(KitError.PlatformUnavailable);
            PrepareHostSession(null);
            BeginCreate();
            return KitResult.Success;
        }

        // HostFromSave, ContinueLatest and PrepareHostSession live in SessionController.Live.cs with the other save code.

        private void BeginCreate()
        {
            if (!machine.TryTransition(SessionState.CreatingLobby)) return;
            phase = LobbyPhase.Room;
            stateDeadline = clock + KitTimeouts.CreateJoin;
            lobby.Create(CurrentPrivacy(), settings.GetInt(CoreSettingKeys.MaxPlayers), OnCreateDone);
        }

        // 02 §2a.4. A result after cancel or timeout leaves that lobby again (02 §6a.3).
        private void OnCreateDone(KitResult<LobbyId> result)
        {
            if (tearingDown) return;
            if (State != SessionState.CreatingLobby)
            {
                if (result.Ok && lobby.Current == result.Value) lobby.Leave();
                return;
            }
            if (!result.Ok)
            {
                KitLog.Warning("Lobby creation failed: " + result);
                FailSession(SessionEndReason.Error, new SessionError(result.Error, null, result.Detail));
                return;
            }

            sessionHost = lobby.LocalPlayer;
            stateDeadline = float.PositiveInfinity;
            if (settings.GetString(CoreSettingKeys.LobbyName).Length == 0)
            {
                string name = identity.LocalDisplayName;
                settings.TrySet(CoreSettingKeys.LobbyName, string.IsNullOrEmpty(name) ? FallbackLobbyName : name + "'s game");
            }
            changedSettingKeys.Clear();
            PublishAll();
            lobby.SetLocalMemberData(KitKeys.Ready, KitKeys.FlagOn);   // the owner is always ready
            machine.TryTransition(SessionState.LobbyRoom);
            UpdatePresence();
            RecomputeCanStart();
        }

        /// <summary>Host in LobbyRoom: Busy otherwise; InvalidArgument with Detail = StartBlockedReason when !CanStart.</summary>
        public KitResult StartGame()
        {
            if (!IsHost || State != SessionState.LobbyRoom) return Busy("StartGame");
            RecomputeCanStart();
            if (!canStart) return KitResult.Fail(KitError.InvalidArgument, startBlockedReason);
            BeginHostStart();
            return KitResult.Success;
        }

        // Decision 8 steps 1-3 (02 §2e.2).
        private void BeginHostStart()
        {
            var batch = new Dictionary<string, string>(StringComparer.Ordinal);
            if (schema.TryGet(CommonSettings.Keys.WorldSeed, out SettingDefinition seed) && seed.Kind == SettingKind.Int
                && settings.GetInt(seed.Key) == 0)
            {
                // Before anything else, so the save snapshot records the real seed.
                settings.TrySetInt(seed.Key, new Random(Environment.TickCount).Next(1, int.MaxValue));
                if (!seed.HostOnly) batch[SettingCodec.ToLobbyKey(seed.Key)] = settings.GetRaw(seed.Key);
                FlushSettingChanges();
            }
            batch[KitKeys.Phase] = KitKeys.PhaseStarting;
            if (!lobby.SetDataBatch(batch)) KitLog.Warning("Publishing phase 'starting' failed.");
            lobby.SetJoinable(SessionRules.ShouldBeJoinable(LobbyPhase.Starting, settings.GetBool(CoreSettingKeys.JoinInProgress)));
            SetPhase(LobbyPhase.Starting);

            if (!machine.TryTransition(SessionState.StartingGame)) return;
            stateDeadline = clock + KitTimeouts.StartingGame;

            // Everyone in the room at Start is exempt from the joinInProgress approval rule (01 §6.3).
            startRoster.Clear();
            foreach (LobbyMember m in Members) startRoster.Add(m.Id);
            network.ApprovalHandler = Approve;

            KitResult started = network.StartHost();
            if (!started.Ok)
            {
                KitLog.Error("NGO StartHost failed: " + started);
                FailSession(SessionEndReason.Error, new SessionError(KitError.NetworkStartFailed, null, started.Detail));
                return;
            }
            KitResult loading = network.LoadNetworkScene(adapter.GameplaySceneName);
            if (!loading.Ok)
            {
                KitLog.Error("Loading the gameplay scene failed: " + loading);
                FailSession(SessionEndReason.Error, new SessionError(KitError.SceneLoadFailed, null, loading.Detail));
            }
        }

        // Installed as ISessionNetwork.ApprovalHandler. The host's own connection and the duplicate-PlayerId check
        // are handled by the network implementation before this runs (03 §10.2).
        private ApprovalDecision Approve(ApprovalRequest request)
        {
            KitError reason = ApprovalRejection(request);
            if (reason == KitError.None) return ApprovalDecision.Accept();
            KitLog.Info("Approval rejected client " + request.ClientId + " player " + request.Payload.Player + ": " + reason);
            return ApprovalDecision.Reject(reason);
        }

        // 01 §6.3 order, then the 03 §10.2 not-InGame guard.
        private KitError ApprovalRejection(ApprovalRequest request)
        {
            if (!request.PayloadValid) return KitError.ApprovalRejected;
            ConnectionPayload p = request.Payload;
            if (p.KitProtocol != KitVersion.Protocol || !string.Equals(p.GameVersion, adapter.GameVersion, StringComparison.Ordinal))
            {
                return KitError.IncompatibleVersion;
            }
            if (bans.Contains(p.Player)) return KitError.Banned;
            if (!lobby.IsMember(p.Player)) return KitError.LobbyAccessDenied;   // live query, not the Members snapshot
            if (LobbyHasPassword)
            {
                string expected = PasswordHasher.Hash(lobby.Current, settings.GetString(CoreSettingKeys.Password));
                if (!PasswordHasher.Matches(expected, p.PasswordHash)) return KitError.WrongPassword;
            }
            if (network.ConnectedCount + network.PendingApprovedCount >= settings.GetInt(CoreSettingKeys.MaxPlayers))
            {
                return KitError.LobbyFull;   // ConnectedCount includes the host's own server client
            }
            if (phase == LobbyPhase.InGame && !settings.GetBool(CoreSettingKeys.JoinInProgress) && !startRoster.Contains(p.Player))
            {
                return KitError.LobbyNotJoinable;
            }
            if (State != SessionState.InGame) return KitError.LobbyNotJoinable;   // only out-of-protocol connects
            return KitError.None;
        }

        private void OnNetworkSceneLoaded(string sceneName)
        {
            if (tearingDown || State != SessionState.StartingGame || isLive) return;
            if (IsHost) OnHostSceneLoaded(sceneName);
            else OnClientSceneLoaded(sceneName);
        }

        // 02 §2e.4: the host goes live before it advertises, so the game's network layer listens before any client
        // can learn k.server (03 §11.1).
        private void OnHostSceneLoaded(string sceneName)
        {
            if (!string.Equals(sceneName, adapter.GameplaySceneName, StringComparison.Ordinal)) return;
            if (!GoLive(true)) return;
            if (readyGate == null)
            {
                PublishLive();
                return;
            }
            awaitingWorldReady = true;
            stateDeadline = clock + KitTimeouts.WorldReadySeconds;
        }

        private void OnNetworkSceneLoadFailed(string sceneName, KitError error)
        {
            if (tearingDown || State != SessionState.StartingGame) return;
            KitLog.Error("Network scene '" + KitLog.Truncate(sceneName) + "' failed to load: " + error);
            FailSession(SessionEndReason.Error, new SessionError(KitError.SceneLoadFailed, null, sceneName));
        }

        // Host StartingGame: the ready gate (04 §9.3), or the 60 s backstop for a scene that never loads (03 §12).
        private void TickHostStarting()
        {
            if (awaitingWorldReady)
            {
                string failure = SafeGateFailure();
                if (failure != null)
                {
                    KitLog.Error("The game reported a start failure: " + failure);
                    FailSession(SessionEndReason.Error, new SessionError(KitError.Unknown, failure));
                    return;
                }
                if (SafeGateReady())
                {
                    awaitingWorldReady = false;
                    PublishLive();
                    return;
                }
            }
            if (clock <= stateDeadline) return;

            if (awaitingWorldReady)
            {
                KitLog.Error("The host world was not ready within " + KitTimeouts.WorldReadySeconds + " s.");
                FailSession(SessionEndReason.Error, new SessionError(KitError.Timeout, SessionErrorMessages.GameStartTimedOut));
            }
            else
            {
                KitLog.Error("The gameplay scene did not finish loading within " + KitTimeouts.StartingGame + " s.");
                FailSession(SessionEndReason.Error, new SessionError(KitError.SceneLoadFailed, null, "timeout"));
            }
        }

        // 02 §2e.4.4 (decision 8 step 4): advertise the live host, then enter InGame (autosave clock starts).
        private void PublishLive()
        {
            var batch = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { KitKeys.Phase, KitKeys.PhaseInGame },
                { KitKeys.Server, LocalPlayer.ToString() },
            };
            if (!lobby.SetDataBatch(batch)) KitLog.Warning("Publishing phase 'inGame' failed.");
            lobby.SetJoinable(SessionRules.ShouldBeJoinable(LobbyPhase.InGame, settings.GetBool(CoreSettingKeys.JoinInProgress)));
            SetPhase(LobbyPhase.InGame);
            EnterInGame();
        }

        // 02 §3d.10, owner only. The roster row itself arrives through the service's MembersChanged.
        private void OnMemberJoined(PlayerId player)
        {
            if (tearingDown || !IsHost || !IsInSession(State) || player == LocalPlayer) return;
            if (bans.Contains(player))
            {
                KitLog.Info("Banned player " + player + " re-entered the lobby; kicking again.");
                SendKick(player, KitError.Banned);
                return;
            }
            if (LobbyHasPassword)
            {
                pendingAuth[player] = clock + KitTimeouts.HostAuthDeadline;
                RecomputeCanStart();
            }
            else if (phase != LobbyPhase.InGame)
            {
                startRoster.Add(player);   // covers members admitted during starting
            }
        }

        // Client: the session host leaving ends the session (02 §7b.2). Host: every NGO client must stay a member.
        private void OnMemberLeft(PlayerId player)
        {
            if (tearingDown || !lobby.InLobby) return;
            if (!IsHost)
            {
                if (sessionHost.IsValid && player == sessionHost) EndForHostLeft();
                return;
            }
            pendingAuth.Remove(player);
            if (network.IsRunning) network.DisconnectClient(player, KitError.LobbyAccessDenied);   // 03 §13 invariant
            RecomputeCanStart();
            UpdatePresence();
        }

        // 02 §3d.11: the auth line, in every phase; approval re-checks the same hash at connect time.
        private void HandleAuth(PlayerId sender, string hash)
        {
            if (!lobby.IsMember(sender)) return;
            if (bans.Contains(sender))
            {
                SendKick(sender, KitError.Banned);
                return;
            }
            if (!LobbyHasPassword)
            {
                SendChatLine(LobbyChatCodec.Verb.AuthOk, sender, "");   // harmless; covers a password cleared mid-join
                return;
            }

            string expected = PasswordHasher.Hash(lobby.Current, settings.GetString(CoreSettingKeys.Password));
            if (PasswordHasher.Matches(expected, hash))
            {
                pendingAuth.Remove(sender);
                authFailures.Remove(sender);
                SendChatLine(LobbyChatCodec.Verb.AuthOk, sender, "");   // idempotent for a repeated auth
                if (phase != LobbyPhase.InGame) startRoster.Add(sender);
                RecomputeCanStart();
                return;
            }

            authFailures.TryGetValue(sender, out int failures);
            failures++;
            authFailures[sender] = failures;
            pendingAuth.Remove(sender);   // re-added when they enter again
            if (failures >= MaxAuthFailures)
            {
                bans.Add(sender);
                SendKick(sender, KitError.Banned);
            }
            else
            {
                SendKick(sender, KitError.WrongPassword);
            }
            KitLog.Info("Wrong password from " + sender + " (" + failures + " of " + MaxAuthFailures + ").");
            RecomputeCanStart();
        }

        // 02 §3d.12: a member who never proves the password is removed after HostAuthDeadline (no ban).
        private void TickPendingAuth()
        {
            if (pendingAuth.Count == 0) return;
            scratchPlayers.Clear();
            foreach (KeyValuePair<PlayerId, float> entry in pendingAuth)
            {
                if (clock > entry.Value) scratchPlayers.Add(entry.Key);
            }
            if (scratchPlayers.Count == 0) return;

            foreach (PlayerId player in scratchPlayers)
            {
                pendingAuth.Remove(player);
                KitLog.Info("Player " + player + " did not enter the password in time.");
                SendKick(player, KitError.WrongPassword);
            }
            RecomputeCanStart();
        }

        /// <summary>
        /// Host, room or in game (02 §5): bans for this session, sends the kick line and drops the NGO connection.
        /// Busy for clients, self or other states; InvalidArgument if the player is neither a member nor connected.
        /// </summary>
        public KitResult Kick(PlayerId player)
        {
            if (!IsHost || !IsInSession(State) || player == LocalPlayer) return Busy("Kick");
            bool connected = network.IsRunning && network.TryGetClientId(player, out _);
            if (!player.IsValid || (!lobby.IsMember(player) && !connected))
            {
                KitLog.Warning("Kick: " + player + " is not in this session.");
                return KitResult.Fail(KitError.InvalidArgument, "not a member");
            }

            string name = identity.GetDisplayName(player);
            bans.Add(player);
            pendingAuth.Remove(player);
            SendKick(player, KitError.Kicked);
            if (network.IsRunning) network.DisconnectClient(player, KitError.Kicked);
            KitLog.Info("Kicked " + player + "; banned for this session.");

            RecomputeCanStart();
            Emit(MembersChanged, Members, "MembersChanged");   // the row now shows IsBeingRemoved
            string who = string.IsNullOrEmpty(name) ? "The player" : name;
            RaiseToast(ToastSeverity.Info, SessionErrorMessages.Resolve(KitError.Kicked, who + " was removed."));
            return KitResult.Success;
        }

        // 03 §14: NGO client events, host only, after go-live.
        private void OnClientConnected(ulong clientId, PlayerId player)
        {
            if (tearingDown || !IsHost || !isLive) return;
            SafeAdapter(() => adapter.OnPlayerJoined(player, clientId), "OnPlayerJoined");
        }

        private void OnClientDisconnected(ulong clientId, PlayerId player)
        {
            if (tearingDown || !IsHost || !isLive) return;
            SafeAdapter(() => adapter.OnPlayerLeft(player, clientId), "OnPlayerLeft");
        }
    }
}

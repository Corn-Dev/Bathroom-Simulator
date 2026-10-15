// SessionController, room part: host setting edits and their lobby side effects (02 §2c, password-alone rule),
// ready and CanStart (02 §2d), lobby-data, roster and member-data routing (02 §2c.3, §2f), the create-time
// metadata batch (02 §2a.4.5) and kit chat routing (02 §3d.8, §3d.11, 03 §6). Design: 01 §6.2-§6.3, 03 §3-§6.
// The password value never reaches lobby metadata: only k.hasPw is written, and ToLobbyData excludes it.

using System;
using System.Collections.Generic;
using System.Globalization;

namespace MultiplayerKit
{
    public sealed partial class SessionController
    {
        /// <summary>
        /// Host only. NotOwner, UnknownKey, LockedAfterStart (phase past room, or the password with others in the
        /// room) and LockedByResume return without side effects; otherwise HostSettings.TrySet decides and the
        /// lobby is updated. Clamped tells the presenter to re-read the value.
        /// </summary>
        public SettingSetResult TrySetSetting(string key, string value)
        {
            SettingSetResult allowed = CheckEditable(key, out SettingDefinition def);
            if (allowed != SettingSetResult.Ok)
            {
                if (allowed == SettingSetResult.UnknownKey) KitLog.Warning("TrySetSetting: unknown setting '" + KitLog.Truncate(key) + "'.");
                return allowed;
            }

            changedSettingKeys.Clear();
            SettingSetResult result = settings.TrySet(def.Key, value);
            if (result != SettingSetResult.Ok && result != SettingSetResult.Clamped)
            {
                changedSettingKeys.Clear();
                return result;   // Unchanged or WrongKind: nothing to publish
            }

            if (ApplySettingSideEffects(def)) result = SettingSetResult.Clamped;
            if (!changedSettingKeys.Contains(def.Key)) changedSettingKeys.Add(def.Key);
            FlushSettingChanges();
            RecomputeCanStart();
            return result;
        }

        /// <summary>Owner, in a lobby, not locked by phase, resume or the password rule, and not pinned by a capability.</summary>
        public bool IsSettingEditable(string key)
        {
            return CheckEditable(key, out SettingDefinition def) == SettingSetResult.Ok && !def.IsFixed;
        }

        private SettingSetResult CheckEditable(string key, out SettingDefinition def)
        {
            def = null;
            if (!IsHost || !lobby.InLobby || !IsInSession(State)) return SettingSetResult.NotOwner;
            if (!schema.TryGet(key, out def)) return SettingSetResult.UnknownKey;
            if (phase != LobbyPhase.Room && !def.EditableAfterStart) return SettingSetResult.LockedAfterStart;
            if (resumeLocked.Contains(def.Key)) return SettingSetResult.LockedByResume;

            // Members already in the room never sent a hash for a new password, so approval at Start would reject
            // them; setting, changing and clearing are all locked once someone else is in (02 §2c).
            if (IsPasswordKey(def.Key) && Members.Count > 1) return SettingSetResult.LockedAfterStart;
            return SettingSetResult.Ok;
        }

        // 02 §2c step 2. Returns true when maxPlayers had to be raised to the member count.
        private bool ApplySettingSideEffects(SettingDefinition def)
        {
            bool clamped = false;
            var batch = new Dictionary<string, string>(StringComparer.Ordinal);
            switch (def.Key)
            {
                case CoreSettingKeys.Privacy:
                    lobby.SetPrivacy(CurrentPrivacy());
                    break;
                case CoreSettingKeys.MaxPlayers:
                {
                    int max = settings.GetInt(CoreSettingKeys.MaxPlayers);
                    if (max < Members.Count)
                    {
                        settings.TrySetInt(CoreSettingKeys.MaxPlayers, Members.Count);
                        max = settings.GetInt(CoreSettingKeys.MaxPlayers);
                        clamped = true;
                    }
                    lobby.SetMaxMembers(max);
                    break;
                }
                case CoreSettingKeys.Password:
                    batch[KitKeys.HasPassword] = LobbyHasPassword ? KitKeys.FlagOn : KitKeys.FlagOff;
                    break;
                case CoreSettingKeys.JoinInProgress:
                    if (phase != LobbyPhase.Room)
                    {
                        lobby.SetJoinable(SessionRules.ShouldBeJoinable(phase, settings.GetBool(CoreSettingKeys.JoinInProgress)));
                    }
                    break;
            }

            if (!def.HostOnly && !IsPasswordKey(def.Key)) batch[SettingCodec.ToLobbyKey(def.Key)] = settings.GetRaw(def.Key);
            if (batch.Count > 0 && !lobby.SetDataBatch(batch)) KitLog.Warning("Publishing setting '" + def.Key + "' failed.");
            UpdatePresence();   // privacy, maxPlayers and joinInProgress feed presence; deduped otherwise
            return clamped;
        }

        // Raises SettingChanged for every key HostSettings reported since the last flush, then the in-game hook.
        private void FlushSettingChanges()
        {
            if (changedSettingKeys.Count == 0) return;
            string[] keys = changedSettingKeys.ToArray();
            changedSettingKeys.Clear();
            foreach (string key in keys)
            {
                Emit(SettingChanged, key, "SettingChanged");
                if (State == SessionState.InGame && isLive && schema.TryGet(key, out SettingDefinition def) && def.EditableAfterStart)
                {
                    IReadOnlyHostSettings current = settings;
                    SafeAdapter(() => adapter.OnSettingsChanged(current, key), "OnSettingsChanged");
                }
            }
        }

        /// <summary>Client in the room only; the owner is always ready (Busy for the host).</summary>
        public KitResult SetReady(bool ready)
        {
            if (State != SessionState.LobbyRoom || IsHost || !lobby.InLobby) return Busy("SetReady");
            return lobby.SetLocalMemberData(KitKeys.Ready, ready ? KitKeys.FlagOn : KitKeys.FlagOff)
                ? KitResult.Success
                : KitResult.Fail(KitError.Unknown, "SetLocalMemberData failed");
        }

        // 02 §2d.3: CanStartChanged fires only when the flag or the reason changes.
        private void RecomputeCanStart()
        {
            string reason = ComputeStartBlockedReason();
            bool can = reason == null;
            if (can == canStart && string.Equals(reason, startBlockedReason, StringComparison.Ordinal)) return;
            canStart = can;
            startBlockedReason = reason;
            Emit(CanStartChanged, can, reason, "CanStartChanged");
        }

        // First match wins, in the 02 §2d.3 order.
        private string ComputeStartBlockedReason()
        {
            if (!IsHost || State != SessionState.LobbyRoom) return BlockedNotInLobby;
            if (pendingAuth.Count > 0) return BlockedPendingAuth;
            if (settings.GetBool(CoreSettingKeys.RequireReady))
            {
                foreach (LobbyMember m in Members)
                {
                    // A kicked member that has not left yet never blocks Start (03 §13 step 4).
                    if (!m.IsOwner && !m.IsReady && !bans.Contains(m.Id)) return BlockedNotReady;
                }
            }
            return SafeValidateSettings();
        }

        // Clients: settings from metadata, then the phase (02 §2c.3, §2f). Joining: the delayed entry check (3d.4.1).
        private void OnLobbyDataChanged()
        {
            if (tearingDown || !lobby.InLobby) return;
            if (State == SessionState.Joining)
            {
                if (awaitingLobbyData) EvaluateJoinedLobby();
                return;
            }
            if (!IsInSession(State) || IsHost) return;   // the host writes the data; its own echo carries nothing new

            changedSettingKeys.Clear();
            settings.ApplyLobbyData(lobby.GetAllData());
            FlushSettingChanges();
            ReadPhaseFromLobby();
            FollowPhase();
        }

        // Client: an unreadable k.phase keeps the current phase.
        private void ReadPhaseFromLobby()
        {
            if (LobbyEnums.TryParsePhase(lobby.GetData(KitKeys.Phase), out LobbyPhase p)) SetPhase(p);
        }

        private void OnMembersChanged(IReadOnlyList<LobbyMember> members)
        {
            if (tearingDown || !lobby.InLobby) return;
            SessionState s = State;
            if (s == SessionState.MainMenu || s == SessionState.Leaving || s == SessionState.Error) return;
            Emit(MembersChanged, Members, "MembersChanged");
            if (IsHost) RecomputeCanStart();
            UpdatePresence();
        }

        private void OnMemberDataChanged(PlayerId member)
        {
            if (!tearingDown && IsHost) RecomputeCanStart();
        }

        // 02 §2a.4.5: one batch; compatibility keys first so a racing browser never sees a half-written lobby (03 §4).
        private void PublishAll()
        {
            var batch = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { KitKeys.Game, adapter.GameId },
                { KitKeys.Proto, KitVersion.Protocol.ToString(CultureInfo.InvariantCulture) },
                { KitKeys.Version, adapter.GameVersion },
                { KitKeys.Phase, LobbyEnums.ToKey(phase) },
                { KitKeys.HasPassword, LobbyHasPassword ? KitKeys.FlagOn : KitKeys.FlagOff },
                { KitKeys.Resume, resumeSave != null ? KitKeys.FlagOn : KitKeys.FlagOff },
            };
            foreach (KeyValuePair<string, string> pair in settings.ToLobbyData()) batch[pair.Key] = pair.Value;
            if (!lobby.SetDataBatch(batch)) KitLog.Warning("Writing the lobby metadata failed.");
        }

        // Kit chat (01 §7, 03 §6). The sender comes from the platform; lines aimed at someone else, our own echo and
        // lines from anyone but the session host (client side) are dropped.
        private void OnChatReceived(PlayerId sender, string text)
        {
            if (tearingDown || !lobby.InLobby) return;
            if (!LobbyChatCodec.TryDecode(text, out LobbyChatCodec.Verb verb, out PlayerId target, out string arg))
            {
                KitLog.Debug("Ignored lobby chat from " + sender + ": " + KitLog.Truncate(text));
                return;
            }
            if (sender == LocalPlayer || target != LocalPlayer) return;

            if (IsHost)
            {
                if (verb == LobbyChatCodec.Verb.Auth) HandleAuth(sender, arg);
                return;
            }
            if (!sessionHost.IsValid || sender != sessionHost)
            {
                if (verb == LobbyChatCodec.Verb.Kick) KitLog.Warning("Kick from non-host " + sender + " ignored.");
                return;
            }
            if (verb == LobbyChatCodec.Verb.AuthOk) OnAuthOk();
            else if (verb == LobbyChatCodec.Verb.Kick) OnKickReceived(LobbyChatCodec.ParseKickReason(arg));
        }

        private bool SendChatLine(LobbyChatCodec.Verb verb, PlayerId target, string arg)
        {
            string line;
            try
            {
                line = LobbyChatCodec.Encode(verb, target, arg);
            }
            catch (ArgumentException e)
            {
                KitLog.Error("Lobby chat '" + LobbyChatCodec.VerbText(verb) + "' not sent: " + e.Message);
                return false;
            }
            if (lobby.SendChat(line)) return true;
            KitLog.Warning("Lobby chat '" + LobbyChatCodec.VerbText(verb) + "' to " + target + " was not sent.");
            return false;
        }

        // 02 §3d.13. Steam has no kick API; NGO approval enforces bans and the password (03 §13).
        private void SendKick(PlayerId player, KitError reason)
        {
            SendChatLine(LobbyChatCodec.Verb.Kick, player, ((int)reason).ToString(CultureInfo.InvariantCulture));
        }

        // Settings and phase helpers shared by the partials.

        // Every settings instance reports its changes into changedSettingKeys; FlushSettingChanges raises them.
        private void ReplaceSettings(HostSettings next)
        {
            if (settings != null) settings.Changed -= OnSettingValueChanged;
            settings = next;
            settings.Changed += OnSettingValueChanged;
            changedSettingKeys.Clear();
        }

        private void OnSettingValueChanged(string key)
        {
            if (!changedSettingKeys.Contains(key)) changedSettingKeys.Add(key);
        }

        private void SetPhase(LobbyPhase next)
        {
            if (next == phase) return;
            phase = next;
            Emit(PhaseChanged, next, "PhaseChanged");
            UpdatePresence();
        }

        private LobbyPrivacy CurrentPrivacy()
        {
            LobbyEnums.TryParsePrivacy(settings.GetString(CoreSettingKeys.Privacy), out LobbyPrivacy privacy);
            return privacy;
        }

        private bool LobbyHasPassword => settings.GetString(CoreSettingKeys.Password).Trim().Length > 0;

        private static bool IsPasswordKey(string key)
        {
            return string.Equals(key, CoreSettingKeys.Password, StringComparison.Ordinal);
        }
    }
}

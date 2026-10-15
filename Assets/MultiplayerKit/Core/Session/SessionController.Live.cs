// SessionController, live and save part: resume from a save (02 §2b, 04 §6.1, moved here from .Host for size),
// go-live (decision 8 step 7) with the adapter contract of 01 §4.8, the try/catch wrappers for every other
// adapter callback, manual save, Save & Quit (02 §6c.3: save first, stay InGame on failure), the autosave
// cadence (01 §6.3), the slot and playtime rules (02 §6c.5) and the host autosave Teardown runs first (01 §6.1).
// All saving is host-only and needs the WorldSaves capability.

using System;
using System.Collections.Generic;
using System.Globalization;

namespace MultiplayerKit
{
    public sealed partial class SessionController
    {
        /// <summary>
        /// Loads the slot and runs the adapter's IWorldSaveValidator before any lobby exists. SaveNotFound,
        /// SaveCorrupt, SaveIoFailed or IncompatibleVersion: Toast, stay in MainMenu (02 §2b.2).
        /// </summary>
        public KitResult HostFromSave(string slotId)
        {
            if (State != SessionState.MainMenu) return Busy("HostFromSave");
            if (!worldSaves) return KitResult.Fail(KitError.InvalidArgument, "world saves are not enabled");
            if (!lobby.IsAvailable) return KitResult.Fail(KitError.PlatformUnavailable);

            KitResult<WorldSaveData> loaded;
            try
            {
                loaded = saves.Load(slotId);
            }
            catch (Exception e)
            {
                loaded = KitResult<WorldSaveData>.Fail(KitError.SaveIoFailed, e.GetType().Name + ": " + e.Message);
            }
            if (loaded.Ok && loaded.Value == null) loaded = KitResult<WorldSaveData>.Fail(KitError.SaveCorrupt, "no data");
            if (!loaded.Ok)
            {
                KitLog.Warning("Loading save '" + KitLog.Truncate(slotId) + "' failed: " + loaded);
                RaiseToast(ToastSeverity.Error, SessionErrorMessages.ForLoadingSave(loaded.Error));
                return KitResult.Fail(loaded.Error, loaded.Detail);
            }

            if (adapter is IWorldSaveValidator validator)
            {
                KitResult check;
                try
                {
                    check = validator.ValidateWorldSave(loaded.Value);
                }
                catch (Exception e)
                {
                    KitLog.Error("IWorldSaveValidator.ValidateWorldSave threw.", e);
                    check = KitResult.Fail(KitError.SaveCorrupt, e.GetType().Name);
                }
                if (!check.Ok)
                {
                    KitLog.Warning("Save '" + KitLog.Truncate(slotId) + "' rejected by the game: " + check);
                    RaiseToast(ToastSeverity.Error, SessionErrorMessages.ForLoadingSave(check.Error));
                    return check;
                }
            }

            PrepareHostSession(loaded.Value);
            BeginCreate();
            return KitResult.Success;
        }

        /// <summary>Hosts the newest slot. No slot: Toast "No saved games", stay in MainMenu.</summary>
        public KitResult ContinueLatest()
        {
            if (State != SessionState.MainMenu) return Busy("ContinueLatest");
            if (!worldSaves) return KitResult.Fail(KitError.InvalidArgument, "world saves are not enabled");

            KitResult<WorldSaveSlotInfo> latest;
            try
            {
                latest = saves.GetLatest();
            }
            catch (Exception e)
            {
                latest = KitResult<WorldSaveSlotInfo>.Fail(KitError.SaveIoFailed, e.GetType().Name + ": " + e.Message);
            }
            if (latest.Ok && latest.Value == null) latest = KitResult<WorldSaveSlotInfo>.Fail(KitError.SaveNotFound);
            if (!latest.Ok)
            {
                bool none = latest.Error == KitError.SaveNotFound;
                if (!none) KitLog.Warning("Reading the latest save failed: " + latest);
                RaiseToast(none ? ToastSeverity.Info : ToastSeverity.Error,
                    none ? SessionErrorMessages.NoSavedGames : SessionErrorMessages.ForLoadingSave(latest.Error));
                return KitResult.Fail(latest.Error, latest.Detail);
            }
            return HostFromSave(latest.Value.SlotId);
        }

        // 02 §2a.2.3 / §2b.2: fresh settings; a resume applies the snapshot, re-caps maxPlayers and locks the world keys.
        private void PrepareHostSession(WorldSaveData save)
        {
            ReplaceSettings(new HostSettings(schema));
            resumeLocked.Clear();
            resumeSave = save;
            ResumeSlot = save != null ? save.Info : null;
            sessionSlotId = save != null ? save.Info.SlotId : null;   // autosave overwrites the resumed slot
            basePlaytime = save != null ? save.Info.PlaytimeSeconds : 0d;
            if (save == null) return;

            settings.ApplySnapshot(save.Info.SettingsSnapshot);
            if (settings.GetInt(CoreSettingKeys.MaxPlayers) > adapter.MaxPlayersCap)
            {
                settings.TrySetInt(CoreSettingKeys.MaxPlayers, adapter.MaxPlayersCap);
            }

            IReadOnlyCollection<string> worldKeys = null;
            try
            {
                worldKeys = adapter.WorldShapingKeys;
            }
            catch (Exception e)
            {
                KitLog.Error("IGameAdapter.WorldShapingKeys threw.", e);
            }
            if (worldKeys != null)
            {
                foreach (string key in worldKeys)
                {
                    if (key != null && schema.Has(key)) resumeLocked.Add(key);
                }
            }
            changedSettingKeys.Clear();   // the room reads Settings when it opens
        }

        // Exactly once per session per peer. An exception is session-fatal: "The game failed to start."
        private bool GoLive(bool asHost)
        {
            var context = new SessionLiveContext(network.NativeHandle, asHost, LocalPlayer, settings,
                asHost ? resumeSave : null, network);
            try
            {
                adapter.OnSessionLive(context);
            }
            catch (Exception e)
            {
                KitLog.Error("IGameAdapter.OnSessionLive threw; ending the session.", e);
                FailSession(SessionEndReason.Error,
                    new SessionError(KitError.Unknown, SessionErrorMessages.GameFailedToStart, e.GetType().Name));
                return false;
            }
            if (State != SessionState.StartingGame) return false;   // the adapter ended the session inside the call
            isLive = true;
            return true;
        }

        // Host after PublishLive, client after GoLive. Starts the autosave and playtime clocks.
        private void EnterInGame()
        {
            stateDeadline = float.PositiveInfinity;
            reachedInGame = true;
            liveSince = clock;
            lastAutosave = clock;
            machine.TryTransition(SessionState.InGame);
        }

        /// <summary>Host in game only (Busy otherwise). SaveIoFailed with Detail "capture returned null" when the game has nothing to save.</summary>
        public KitResult<WorldSaveSlotInfo> SaveNow()
        {
            if (!IsHost || State != SessionState.InGame || !isLive)
            {
                KitLog.Warning("SaveNow ignored in state " + State + ".");
                return KitResult<WorldSaveSlotInfo>.Fail(KitError.Busy, "SaveNow");
            }
            if (!worldSaves) return KitResult<WorldSaveSlotInfo>.Fail(KitError.InvalidArgument, "world saves are not enabled");
            return SaveWorld("manual");
        }

        /// <summary>
        /// Host: save, then leave without a second autosave. A failed save shows a Toast and stays InGame; the
        /// host's Leave ("End game") still leaves after one more attempt (02 §6c.3). Clients: Leave.
        /// </summary>
        public void SaveAndQuit()
        {
            if (State != SessionState.InGame)
            {
                KitLog.Warning("SaveAndQuit ignored in state " + State + ".");
                return;
            }
            if (!IsHost || !worldSaves)
            {
                Leave();
                return;
            }

            KitResult<WorldSaveSlotInfo> saved = SaveNow();
            if (!saved.Ok)
            {
                RaiseToast(ToastSeverity.Error, SessionErrorMessages.SaveAndQuitFailed);
                return;
            }
            LeaveTo(SessionEndReason.SaveAndQuit, true);
        }

        // 01 §6.3: every autosaveMinutes of InGame time, counted from the last save. A schema without the key (or
        // 0) means off. A failure warns and retries at the next interval.
        private void TickAutosave()
        {
            if (!worldSaves || !isLive) return;
            int minutes = AutosaveMinutes();
            if (minutes <= 0 || clock - lastAutosave < minutes * 60f) return;

            lastAutosave = clock;
            KitResult<WorldSaveSlotInfo> saved = SaveWorld("autosave");
            if (!saved.Ok && saved.Detail != CaptureNullDetail) RaiseToast(ToastSeverity.Warning, SaveFailureText(saved));
        }

        // 01 §6.1 teardown step 1: host, live, reached InGame, WorldSaves, and reason LocalLeft, Error or SaveAndQuit
        // (unless Save & Quit just saved). A failure is queued as a toast for after the state change (02 §6c.2).
        private void AutosaveOnTeardown(SessionEndReason reason, bool skipAutosave)
        {
            if (!IsHost || !isLive || !reachedInGame || !worldSaves) return;
            bool wanted = reason == SessionEndReason.LocalLeft || reason == SessionEndReason.Error
                || (reason == SessionEndReason.SaveAndQuit && !skipAutosave);
            if (!wanted) return;

            KitResult<WorldSaveSlotInfo> saved = SaveWorld("leaving, " + reason);
            if (!saved.Ok && saved.Detail != CaptureNullDetail)
            {
                deferredToasts.Add(new ToastMessage(ToastSeverity.Error, SaveFailureText(saved)));
            }
        }

        // Capture, write the session slot (the first save of a new game creates it), then advance the playtime
        // base: playtime = basePlaytime + (now - liveSince); a failed save changes neither value (02 §6c.5).
        private KitResult<WorldSaveSlotInfo> SaveWorld(string trigger)
        {
            byte[] payload = SafeCapture();
            if (payload == null || payload.Length == 0)
            {
                KitLog.Warning("World save (" + trigger + ") skipped: the game returned no data.");
                return KitResult<WorldSaveSlotInfo>.Fail(KitError.SaveIoFailed, CaptureNullDetail);
            }

            double playtime = basePlaytime + Math.Max(0f, clock - liveSince);
            KitResult<WorldSaveSlotInfo> result;
            try
            {
                result = saves.Save(sessionSlotId, DisplayNameFor(), playtime, adapter.GameVersion, settings.ToSnapshot(), payload);
            }
            catch (Exception e)
            {
                result = KitResult<WorldSaveSlotInfo>.Fail(KitError.SaveIoFailed, e.GetType().Name + ": " + e.Message);
            }
            if (result.Ok && result.Value == null) result = KitResult<WorldSaveSlotInfo>.Fail(KitError.SaveIoFailed, "no slot returned");
            if (!result.Ok)
            {
                KitLog.Error("World save (" + trigger + ") failed: " + result);
                return result;
            }

            sessionSlotId = result.Value.SlotId;
            basePlaytime = playtime;
            liveSince = clock;
            lastAutosave = clock;
            KitLog.Info("World saved (" + trigger + ") to slot " + sessionSlotId + ".");
            Emit(WorldSaved, result.Value, "WorldSaved");
            return result;
        }

        // 02 §6c.5: lobbyName + " — " + local date and time, at most 48 chars; the name is cut, not the date.
        private string DisplayNameFor()
        {
            string stamp = DateTime.Now.ToString("g", CultureInfo.CurrentCulture);
            string lobbyName = settings.GetString(CoreSettingKeys.LobbyName);
            if (lobbyName.Length == 0) return SettingCodec.SanitizeText(stamp, WorldSaveMetaJson.MaxDisplayNameLength);

            const string separator = " \u2014 ";
            int room = WorldSaveMetaJson.MaxDisplayNameLength - separator.Length - stamp.Length;
            if (room < 1) return SettingCodec.SanitizeText(lobbyName, WorldSaveMetaJson.MaxDisplayNameLength);
            return SettingCodec.SanitizeText(lobbyName, room) + separator + stamp;
        }

        // Core reads autosaveMinutes only if the game's schema has it (01 §5.5).
        private int AutosaveMinutes()
        {
            if (!schema.TryGet(CommonSettings.Keys.AutosaveMinutes, out SettingDefinition def) || def.Kind != SettingKind.Int) return 0;
            return settings.GetInt(def.Key);
        }

        private static string SaveFailureText(KitResult<WorldSaveSlotInfo> failed)
        {
            return failed.Detail == WorldSaveNames.SlotsFullDetail ? SessionErrorMessages.SaveSlotsFull : SessionErrorMessages.SaveFailed;
        }

        // Adapter callbacks other than OnSessionLive: logged at Error and swallowed (01 §4.8).

        private static void SafeAdapter(Action call, string callback)
        {
            try
            {
                call();
            }
            catch (Exception e)
            {
                KitLog.Error("IGameAdapter." + callback + " threw.", e);
            }
        }

        // An exception counts as "nothing to save".
        private byte[] SafeCapture()
        {
            try
            {
                return adapter.CaptureWorldSave();
            }
            catch (Exception e)
            {
                KitLog.Error("IGameAdapter.CaptureWorldSave threw.", e);
                return null;
            }
        }

        // Null when valid; an exception or empty text counts as valid.
        private string SafeValidateSettings()
        {
            try
            {
                string message = adapter.ValidateSettings(settings);
                return string.IsNullOrEmpty(message) ? null : message;
            }
            catch (Exception e)
            {
                KitLog.Error("IGameAdapter.ValidateSettings threw.", e);
                return null;
            }
        }

        private bool SafeGateReady()
        {
            try
            {
                return readyGate.IsHostWorldReady;
            }
            catch (Exception e)
            {
                KitLog.Error("IGameReadyGate.IsHostWorldReady threw.", e);
                return false;
            }
        }

        // An exception is reported as the generic start failure, so the session ends instead of hanging.
        private string SafeGateFailure()
        {
            try
            {
                return readyGate.FailureMessage;
            }
            catch (Exception e)
            {
                KitLog.Error("IGameReadyGate.FailureMessage threw.", e);
                return SessionErrorMessages.GameFailedToStart;
            }
        }

        // 01 §4.8: an invalid adapter identity is a programmer error and fails at startup (MaxPlayersCap: schema Build).
        private static void ValidateAdapter(IGameAdapter a)
        {
            string id = a.GameId;
            bool idOk = !string.IsNullOrEmpty(id) && id.Length <= 24;
            for (int i = 0; idOk && i < id.Length; i++)
            {
                char c = id[i];
                idOk = (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_';
            }
            if (!idOk) throw new InvalidOperationException("IGameAdapter.GameId must match ^[a-z0-9_]{1,24}$.");

            string version = a.GameVersion;
            if (string.IsNullOrEmpty(version) || version.Length > 32)
            {
                throw new InvalidOperationException("IGameAdapter.GameVersion must be 1-32 characters.");
            }
            if (string.IsNullOrEmpty(a.GameplaySceneName) || string.IsNullOrEmpty(a.MenuSceneName))
            {
                throw new InvalidOperationException("IGameAdapter scene names must not be empty.");
            }
        }
    }
}

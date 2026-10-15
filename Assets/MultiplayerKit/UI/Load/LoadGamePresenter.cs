// Load game presenter (05 §8): the host's world-save slots from IWorldSaveStore (synchronous calls), newest first,
// with name, last-saved time, playtime, the snapshot's settings line (the LobbyRowFormatter rule of the lobby lists)
// and a version warning that never blocks loading, because whether an old payload loads is the adapter's decision.
// Load → Session.HostFromSave. Core toasts its own load and validation failures (SessionController.Live.cs, 02 §2b.2),
// so only PlatformUnavailable, which Core returns without a toast, is toasted here; Busy and InvalidArgument (world
// saves off, the screen is hidden then) stay silent. Every failed load reloads the list, so a vanished slot
// disappears. Delete asks first and is the only store write the UI makes. Load and Delete work only in MainMenu, so the
// UI can never delete the slot a running session autosaves to; the confirm callback checks the state again.
// Plain class; talks only to Core. Design: 05 §8, §10; 02 §2b; 06 U42.

using System;
using System.Collections.Generic;

namespace MultiplayerKit.UI
{
    public sealed class SaveSlotRowModel
    {
        public string SlotId;
        public string Name;

        /// <summary>UpdatedUtc as local "g" time in the current culture.</summary>
        public string Updated;

        /// <summary>"45m", "2h 14m", "&lt;1m".</summary>
        public string Playtime;

        public string SettingsLine;
        public bool VersionMismatch;

        /// <summary>"Saved with v{GameVersion}" when VersionMismatch, else null.</summary>
        public string VersionText;

        /// <summary>Load and Delete are interactable (State == MainMenu).</summary>
        public bool Interactable;
    }

    public interface ILoadGameView
    {
        void ShowRows(IReadOnlyList<SaveSlotRowModel> rows);

        /// <summary>null hides the message.</summary>
        void SetMessage(string message);
    }

    public sealed class LoadGamePresenter
    {
        public const string ReadFailedText = "Couldn't read saved games.";
        public const string NoSavesText = "No saved games yet.";
        public const string UnnamedSaveText = "Saved game";
        public const string VersionPrefix = "Saved with v";
        public const string UnknownVersionText = "Saved with another version";

        private const string DeleteBody = "This can't be undone.";
        private const string DeleteLabel = "Delete";

        private static readonly IReadOnlyList<WorldSaveSlotInfo> NoSlots = Array.AsReadOnly(new WorldSaveSlotInfo[0]);
        private static readonly IReadOnlyList<SaveSlotRowModel> NoRows = Array.AsReadOnly(new SaveSlotRowModel[0]);

        private readonly SessionController session;
        private readonly IWorldSaveStore saves;
        private readonly string gameVersion;
        private readonly ILoadGameView view;
        private readonly IKitDialogs dialogs;
        private IReadOnlyList<WorldSaveSlotInfo> slots = NoSlots;
        private bool bound;

        public LoadGamePresenter(KitServices services, ILoadGameView view, IKitDialogs dialogs)
        {
            if (services == null) throw new ArgumentNullException(nameof(services));
            session = services.Session;
            saves = services.Saves;
            gameVersion = services.GameVersion;
            this.view = view ?? throw new ArgumentNullException(nameof(view));
            this.dialogs = dialogs ?? NullKitDialogs.Instance;
        }

        /// <summary>Lists the slots at once (05 §2.3); a session that just autosaved shows its new time.</summary>
        public void Bind()
        {
            if (bound) return;
            bound = true;
            session.StateChanged += OnStateChanged;
            Reload();
        }

        public void Unbind()
        {
            if (!bound) return;
            bound = false;
            session.StateChanged -= OnStateChanged;
        }

        /// <summary>Session.HostFromSave. On success CreatingLobby follows; the room later shows the resume label.</summary>
        public void OnLoad(string slotId)
        {
            if (!bound || string.IsNullOrEmpty(slotId) || session.State != SessionState.MainMenu) return;

            KitResult result = session.HostFromSave(slotId);
            if (result.Ok || result.Error == KitError.Busy) return;
            if (result.Error == KitError.PlatformUnavailable)
            {
                dialogs.Toast(ToastSeverity.Error, SessionErrorMessages.For(result.Error));
            }
            Reload();
        }

        /// <summary>Confirm, then IWorldSaveStore.Delete; a failure toasts, and the list is reloaded either way.</summary>
        public void OnDelete(string slotId, string name)
        {
            if (!bound || saves == null || string.IsNullOrEmpty(slotId) || session.State != SessionState.MainMenu) return;
            string title = "Delete \"" + (string.IsNullOrEmpty(name) ? UnnamedSaveText : name) + "\"?";
            dialogs.Confirm(title, DeleteBody, DeleteLabel, () => ConfirmDelete(slotId));
        }

        /// <summary>
        /// One row per slot, in store order (newest first). VersionMismatch: the slot's GameVersion differs (ordinal)
        /// from gameVersion; the row stays loadable. SettingsLine: LobbyRowFormatter.SettingsLine on the snapshot.
        /// </summary>
        public static IReadOnlyList<SaveSlotRowModel> BuildRows(IReadOnlyList<WorldSaveSlotInfo> slots,
            HostSettingsSchema schema, string gameVersion, bool interactable)
        {
            if (slots == null || slots.Count == 0) return NoRows;

            var rows = new List<SaveSlotRowModel>(slots.Count);
            for (int i = 0; i < slots.Count; i++)
            {
                WorldSaveSlotInfo info = slots[i];
                if (info == null) continue;
                bool mismatch = !string.Equals(info.GameVersion, gameVersion ?? "", StringComparison.Ordinal);
                rows.Add(new SaveSlotRowModel
                {
                    SlotId = info.SlotId,
                    Name = string.IsNullOrEmpty(info.DisplayName) ? UnnamedSaveText : info.DisplayName,
                    Updated = UiFormat.AbsoluteTime(info.UpdatedUtc),
                    Playtime = UiFormat.Playtime(info.PlaytimeSeconds),
                    SettingsLine = LobbyRowFormatter.SettingsLine(info.SettingsSnapshot, schema),
                    VersionMismatch = mismatch,
                    VersionText = mismatch ? VersionTextFor(info.GameVersion) : null,
                    Interactable = interactable,
                });
            }
            return rows;
        }

        // A save without a version string (hand-edited metadata) still gets a readable warning.
        private static string VersionTextFor(string savedVersion)
        {
            return string.IsNullOrEmpty(savedVersion) ? UnknownVersionText : VersionPrefix + savedVersion;
        }

        private void ConfirmDelete(string slotId)
        {
            if (!bound || session.State != SessionState.MainMenu) return;   // a join or host started while the dialog was open

            KitResult result;
            try
            {
                result = saves.Delete(slotId);
            }
            catch (Exception e)
            {
                result = KitResult.Fail(KitError.SaveIoFailed, e.GetType().Name + ": " + e.Message);
            }
            if (!result.Ok)
            {
                KitLog.Warning("Deleting save '" + KitLog.Truncate(slotId) + "' failed: " + result);
                dialogs.Toast(ToastSeverity.Warning, SessionErrorMessages.For(result.Error));
            }
            Reload();
        }

        // 05 §8: a listing failure shows the message and a Warning toast; zero slots show the empty message.
        private void Reload()
        {
            KitResult<IReadOnlyList<WorldSaveSlotInfo>> listed;
            if (saves == null)
            {
                listed = KitResult<IReadOnlyList<WorldSaveSlotInfo>>.From(NoSlots);
            }
            else
            {
                try
                {
                    listed = saves.ListSlots();
                }
                catch (Exception e)
                {
                    listed = KitResult<IReadOnlyList<WorldSaveSlotInfo>>.Fail(KitError.SaveIoFailed, e.GetType().Name + ": " + e.Message);
                }
            }

            if (!listed.Ok)
            {
                KitLog.Warning("Load screen: listing the saves failed (" + listed + ").");
                slots = NoSlots;
                view.ShowRows(NoRows);
                view.SetMessage(ReadFailedText);
                dialogs.Toast(ToastSeverity.Warning, SessionErrorMessages.For(listed.Error));
                return;
            }

            slots = listed.Value ?? NoSlots;
            IReadOnlyList<SaveSlotRowModel> rows = RenderRows();
            view.SetMessage(rows.Count == 0 ? NoSavesText : null);
        }

        private IReadOnlyList<SaveSlotRowModel> RenderRows()
        {
            IReadOnlyList<SaveSlotRowModel> rows =
                BuildRows(slots, session.Schema, gameVersion, session.State == SessionState.MainMenu);
            view.ShowRows(rows);
            return rows;
        }

        // Load and Delete follow the state; the slots are not read again.
        private void OnStateChanged(SessionStateChange change)
        {
            if (bound && slots.Count > 0) RenderRows();
        }
    }
}

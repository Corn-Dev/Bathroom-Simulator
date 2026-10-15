// Presenter of the settings panel generated from the schema (05 §5.3): row specs from Session.Schema (groups in
// order, then definitions in order; invisible rows and empty groups skipped), every value rendered, editability
// taken from Core (IsSettingEditable; the padlock shows the host why a row is locked), and the only write path
// (OnUserChanged → TrySetSetting, handled per the 05 §5.3 result table). Plain class; talks only to SessionController.
// Beyond the 05 §5.3 subscription list: MembersChanged also re-evaluates editability, because the password locks as
// soon as a second member is in the room (02 §2c); and outside LobbyRoom every control is read-only (05 §5.1), so a
// late commit (a debounced slider flushed while the room closes) is dropped without a toast.
// Design: 05 §5.1, §5.3, §10; 02 §2c; 06 U25.

using System;
using System.Collections.Generic;

namespace MultiplayerKit.UI
{
    public interface ISettingsPanelView
    {
        /// <summary>Headers and rows, in order. The view keeps its rows when the list matches its last build.</summary>
        void Build(IReadOnlyList<SettingRowSpec> rows);

        /// <summary>raw is the canonical value, display its text (SettingControlRules.FormatValue).</summary>
        void RenderValue(string key, string raw, string display);

        void SetEditable(string key, bool editable, bool showLock);

        /// <summary>null hides the message; shown when no row is visible.</summary>
        void SetEmpty(string message);
    }

    /// <summary>One group header (IsHeader, HeaderLabel) or one setting row (Definition, Control) of the panel.</summary>
    public sealed class SettingRowSpec
    {
        public bool IsHeader;
        public string HeaderLabel;
        public SettingDefinition Definition;
        public SettingControl Control;
    }

    public sealed class SettingsPanelPresenter
    {
        public const string LockedText = "This setting can't be changed right now.";
        public const string PasswordControlCharactersText = "Password can't contain control characters.";
        public const string EmptyText = "The host hasn't changed any settings.";

        private readonly SessionController session;
        private readonly ISettingsPanelView view;
        private readonly IKitDialogs dialogs;
        private readonly List<SettingRowSpec> specs = new List<SettingRowSpec>();
        private readonly List<SettingDefinition> rows = new List<SettingDefinition>();   // visible definitions, in order
        private readonly HashSet<string> visibleKeys = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> mismatchLogged = new HashSet<string>(StringComparer.Ordinal);
        private bool bound;

        public SettingsPanelPresenter(KitServices services, ISettingsPanelView view, IKitDialogs dialogs)
        {
            if (services == null) throw new ArgumentNullException(nameof(services));
            session = services.Session;
            this.view = view ?? throw new ArgumentNullException(nameof(view));
            this.dialogs = dialogs ?? NullKitDialogs.Instance;
        }

        public void Bind()
        {
            if (bound) return;
            bound = true;
            session.SettingChanged += OnSettingChanged;
            session.StateChanged += OnStateChanged;
            session.PhaseChanged += OnPhaseChanged;
            session.MembersChanged += OnMembersChanged;

            BuildSpecs();
            view.Build(specs);
            view.SetEmpty(rows.Count == 0 ? EmptyText : null);
            foreach (SettingDefinition def in rows) Render(def);
            RefreshEditable();
        }

        public void Unbind()
        {
            if (!bound) return;
            bound = false;
            session.SettingChanged -= OnSettingChanged;
            session.StateChanged -= OnStateChanged;
            session.PhaseChanged -= OnPhaseChanged;
            session.MembersChanged -= OnMembersChanged;
        }

        /// <summary>The only write path: a row's key and its canonical (or typed) value, straight to TrySetSetting.</summary>
        public void OnUserChanged(string key, string canonicalValue)
        {
            if (!bound) return;
            if (!session.Schema.TryGet(key, out SettingDefinition def))
            {
                LogMismatchOnce(key, "change for an unknown setting");
                return;
            }
            if (session.State != SessionState.LobbyRoom)
            {
                KitLog.Debug("Settings panel: change of '" + def.Key + "' ignored in state " + session.State + ".");
                Render(def);
                return;
            }

            SettingSetResult result = session.TrySetSetting(def.Key, canonicalValue);
            switch (result)
            {
                case SettingSetResult.Ok:
                    break;   // SettingChanged re-renders the row
                case SettingSetResult.Unchanged:
                case SettingSetResult.Clamped:
                    Render(def);   // the control snaps to the stored value (a typed "007" shows as "7")
                    break;
                case SettingSetResult.NotOwner:
                case SettingSetResult.LockedAfterStart:
                case SettingSetResult.LockedByResume:
                    Render(def);
                    RefreshEditable();
                    dialogs.Toast(ToastSeverity.Warning, LockedText);
                    break;
                case SettingSetResult.WrongKind:
                    Render(def);   // also clears rejected password text
                    if (SettingControlRules.IsPassword(def)) dialogs.Toast(ToastSeverity.Warning, PasswordControlCharactersText);
                    else LogMismatchOnce(def.Key, "WrongKind for a value from its " + ControlOf(def.Key) + " control");
                    break;
                default:
                    Render(def);
                    LogMismatchOnce(def.Key, result + " from TrySetSetting");
                    break;
            }
        }

        private void OnSettingChanged(string key)
        {
            if (!bound || key == null || !visibleKeys.Contains(key)) return;
            if (session.Schema.TryGet(key, out SettingDefinition def)) Render(def);
        }

        private void OnStateChanged(SessionStateChange change)
        {
            if (bound) RefreshEditable();
        }

        private void OnPhaseChanged(LobbyPhase phase)
        {
            if (bound) RefreshEditable();
        }

        private void OnMembersChanged(IReadOnlyList<LobbyMember> members)
        {
            if (bound) RefreshEditable();
        }

        // 05 §5.3: groups in Schema.Groups order, definitions in schema order, no header for a group without rows.
        private void BuildSpecs()
        {
            specs.Clear();
            rows.Clear();
            visibleKeys.Clear();

            HostSettingsSchema schema = session.Schema;
            bool isHost = session.IsHost;
            GameCapabilities caps = session.Capabilities;
            foreach (string group in schema.Groups)
            {
                bool headerAdded = false;
                foreach (SettingDefinition def in schema.Definitions)
                {
                    if (!string.Equals(def.Group, group, StringComparison.Ordinal)) continue;
                    if (!SettingControlRules.IsVisible(def, isHost, caps)) continue;

                    if (!headerAdded)
                    {
                        specs.Add(new SettingRowSpec { IsHeader = true, HeaderLabel = SettingControlRules.GroupLabel(group) });
                        headerAdded = true;
                    }
                    specs.Add(new SettingRowSpec { Definition = def, Control = SettingControlRules.ControlFor(def) });
                    rows.Add(def);
                    visibleKeys.Add(def.Key);
                }
            }
        }

        // Settings is replaced for every session, so it is read here each time, never cached.
        private void Render(SettingDefinition def)
        {
            IReadOnlyHostSettings settings = session.Settings;
            view.RenderValue(def.Key, settings.GetRaw(def.Key), SettingControlRules.FormatValue(def, settings));
        }

        // Clients get every control non-interactable and no padlock. The host sees a padlock on rows Core locks
        // (resume, the password with others in the room, phase past room).
        private void RefreshEditable()
        {
            bool host = session.IsHost;
            bool inRoom = session.State == SessionState.LobbyRoom;
            foreach (SettingDefinition def in rows)
            {
                bool editable = session.IsSettingEditable(def.Key);
                view.SetEditable(def.Key, editable && inRoom, host && !editable);
            }
        }

        private string ControlOf(string key)
        {
            foreach (SettingRowSpec spec in specs)
            {
                if (!spec.IsHeader && spec.Definition.Key == key) return spec.Control.ToString();
            }
            return "unknown";
        }

        // A schema/UI mismatch is a programmer error: Error once per key (05 §5.3 result table).
        private void LogMismatchOnce(string key, string what)
        {
            string safeKey = key ?? "(null)";
            if (!mismatchLogged.Add(safeKey)) return;
            KitLog.Error("Settings panel: " + what + " '" + KitLog.Truncate(safeKey) + "'; the schema and the panel disagree.");
        }
    }
}

// Roster presenter (05 §5.2), shared by the lobby room (showReadyColumn true) and the in-game session menu (false),
// so an in-game kick takes the same confirm → Session.Kick path as one in the room (02 §5 step 1). BuildRows is the
// pure ordering and flag rule set, unit-tested; IsRemoving comes from Session.IsBeingRemoved, so a kicked member that
// has not left yet shows "Removing…" and never blocks Start (01 §6.3). Plain class; talks only to SessionController.
// Ownership: LobbyRoomView creates it in the room; SessionMenuPresenter creates it for the session menu and binds it
// only while the menu is open. RosterView is parent-driven and never creates one itself.
// Design: 05 §5.2, §3.6, §10; 02 §5; 06 U23.

using System;
using System.Collections.Generic;

namespace MultiplayerKit.UI
{
    public sealed class RosterRowModel
    {
        public PlayerId Player;
        public string Name;
        public bool IsHost;
        public bool IsLocal;
        public bool ShowReady;
        public bool IsReady;
        public bool CanKick;

        /// <summary>Host only: kicked (in the session ban list) but still in Members, 03 §13 step 4.</summary>
        public bool IsRemoving;
    }

    public interface IRosterView
    {
        void ShowRows(IReadOnlyList<RosterRowModel> rows);
    }

    public sealed class RosterPresenter
    {
        /// <summary>Shown while a member's persona name is still unresolved ("").</summary>
        public const string UnknownName = "Player";

        private const string KickBody = "They will be removed and can't rejoin this session.";
        private const string KickConfirmLabel = "Remove";

        private static readonly IReadOnlyList<RosterRowModel> NoRows = Array.AsReadOnly(new RosterRowModel[0]);

        private readonly SessionController session;
        private readonly IRosterView view;
        private readonly IKitDialogs dialogs;
        private readonly bool showReadyColumn;
        private readonly Func<PlayerId, bool> isRemoving;
        private bool bound;

        public RosterPresenter(KitServices services, IRosterView view, IKitDialogs dialogs, bool showReadyColumn)
        {
            if (services == null) throw new ArgumentNullException(nameof(services));
            session = services.Session;
            this.view = view ?? throw new ArgumentNullException(nameof(view));
            this.dialogs = dialogs ?? NullKitDialogs.Instance;
            this.showReadyColumn = showReadyColumn;
            isRemoving = session.IsBeingRemoved;
        }

        public void Bind()
        {
            if (bound) return;
            bound = true;
            session.MembersChanged += OnMembersChanged;
            session.SettingChanged += OnSettingChanged;
            session.CanStartChanged += OnCanStartChanged;   // Core recomputes it when a kick adds to the ban list
            Render();
        }

        public void Unbind()
        {
            if (!bound) return;
            bound = false;
            session.MembersChanged -= OnMembersChanged;
            session.SettingChanged -= OnSettingChanged;
            session.CanStartChanged -= OnCanStartChanged;
        }

        /// <summary>Confirm first, then Session.Kick. The row stays until MembersChanged removes it (no optimistic removal).</summary>
        public void OnKickClicked(PlayerId player)
        {
            if (!bound || !player.IsValid) return;
            string title = "Remove " + NameOf(player) + "?";
            dialogs.Confirm(title, KickBody, KickConfirmLabel, () => ConfirmKick(player));
        }

        /// <summary>
        /// 05 §5.2: host first, then the local player, then the rest in lobby order. Name falls back to "Player";
        /// ShowReady = showReady &amp;&amp; requireReady &amp;&amp; !IsHost &amp;&amp; !IsRemoving (the owner is always ready);
        /// CanKick = localIsHost &amp;&amp; !IsLocal, kept while IsRemoving so the host can send the kick again.
        /// </summary>
        public static IReadOnlyList<RosterRowModel> BuildRows(IReadOnlyList<LobbyMember> members,
            bool localIsHost, bool showReady, bool requireReady, Func<PlayerId, bool> isRemoving)
        {
            if (members == null || members.Count == 0) return NoRows;

            var rows = new List<RosterRowModel>(members.Count);
            for (int pass = 0; pass < 3; pass++)
            {
                for (int i = 0; i < members.Count; i++)
                {
                    LobbyMember m = members[i];
                    if (m == null) continue;
                    bool take = pass == 0 ? m.IsOwner : pass == 1 ? !m.IsOwner && m.IsLocal : !m.IsOwner && !m.IsLocal;
                    if (take) rows.Add(BuildRow(m, localIsHost, showReady, requireReady, isRemoving));
                }
            }
            return rows;
        }

        private static RosterRowModel BuildRow(LobbyMember m, bool localIsHost, bool showReady, bool requireReady,
            Func<PlayerId, bool> isRemoving)
        {
            bool removing = isRemoving != null && isRemoving(m.Id);
            return new RosterRowModel
            {
                Player = m.Id,
                Name = string.IsNullOrEmpty(m.DisplayName) ? UnknownName : m.DisplayName,
                IsHost = m.IsOwner,
                IsLocal = m.IsLocal,
                IsReady = m.IsReady,
                IsRemoving = removing,
                ShowReady = showReady && requireReady && !m.IsOwner && !removing,
                CanKick = localIsHost && !m.IsLocal,
            };
        }

        private void Render()
        {
            bool requireReady = session.Settings.GetBool(CoreSettingKeys.RequireReady);
            view.ShowRows(BuildRows(session.Members, session.IsHost, showReadyColumn, requireReady, isRemoving));
        }

        private void OnMembersChanged(IReadOnlyList<LobbyMember> members)
        {
            if (bound) Render();
        }

        private void OnSettingChanged(string key)
        {
            if (bound && key == CoreSettingKeys.RequireReady) Render();
        }

        private void OnCanStartChanged(bool canStart, string reason)
        {
            if (bound) Render();
        }

        // Busy (not host, not in a session) is silent (05 §10); a member who already left reads InvalidArgument.
        private void ConfirmKick(PlayerId player)
        {
            if (!bound) return;
            KitResult result = session.Kick(player);
            if (result.Ok)
            {
                Render();
                return;
            }
            if (result.Error != KitError.Busy) dialogs.Toast(ToastSeverity.Warning, SessionErrorMessages.For(result.Error));
        }

        private string NameOf(PlayerId player)
        {
            IReadOnlyList<LobbyMember> members = session.Members;
            if (members != null)
            {
                for (int i = 0; i < members.Count; i++)
                {
                    LobbyMember m = members[i];
                    if (m != null && m.Id == player && !string.IsNullOrEmpty(m.DisplayName)) return m.DisplayName;
                }
            }
            return UnknownName;
        }
    }
}

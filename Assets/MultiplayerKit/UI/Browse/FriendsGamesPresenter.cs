// Friends' games presenter (05 §6): friends currently in a lobby of this game, from IFriendsLobbyDiscovery, one row
// per lobby with the LobbyRowFormatter rules (an entry whose lobby data has not arrived shows "Loading…"). Rows sort
// joinable first, then by friend name. EntriesChanged only marks the list dirty and the view calls FlushIfDirty from
// LateUpdate, so 30 friends resolving in one frame cause one rebuild. Join: SetPendingJoinLabel, then
// Session.JoinLobby with no password; a passworded lobby prompts through the overlay (05 §3.2, 02 §3d.5), the one
// password path, so this screen has no password UI. Join is offered only in MainMenu. Join results: Busy stays
// silent and Core already toasts PlatformUnavailable (JoinLobby, 02 §3d.1), so only other codes get a toast here.
// Plain class; talks only to Core. Design: 05 §2.3, §6, §10; 02 §3b; 06 U34.

using System;
using System.Collections.Generic;

namespace MultiplayerKit.UI
{
    public sealed class FriendLobbyRowModel
    {
        public PlayerId Friend;
        public string FriendName;
        public LobbyId Lobby;

        /// <summary>LobbyRowFormatter.DisplayName; "" while the lobby data is loading.</summary>
        public string LobbyName;

        /// <summary>"2/4"; "" while loading.</summary>
        public string PlayerCount;

        /// <summary>"Friends only" / "Public"; "" while loading.</summary>
        public string PrivacyLabel;

        public bool HasPassword;
        public string Status;
        public bool CanJoin;
    }

    public interface IFriendsGamesView
    {
        void ShowRows(IReadOnlyList<FriendLobbyRowModel> rows);

        /// <summary>Spinner shown and Refresh disabled while true.</summary>
        void SetRefreshing(bool refreshing);

        /// <summary>null hides the message.</summary>
        void SetEmpty(string message);
    }

    public sealed class FriendsGamesPresenter
    {
        public const string LookingText = "Looking for friends' games\u2026";
        public const string NoneHostingText = "None of your friends are hosting right now.";

        private static readonly IReadOnlyList<FriendLobbyRowModel> NoRows = Array.AsReadOnly(new FriendLobbyRowModel[0]);

        private readonly SessionController session;
        private readonly IFriendsLobbyDiscovery friends;
        private readonly IPlayerIdentity identity;
        private readonly IFriendsGamesView view;
        private readonly IKitDialogs dialogs;
        private readonly Func<PlayerId, string> ownerName;
        private bool bound;
        private bool dirty;
        private int rowCount;

        public FriendsGamesPresenter(KitServices services, IFriendsGamesView view, IKitDialogs dialogs)
        {
            if (services == null) throw new ArgumentNullException(nameof(services));
            session = services.Session;
            friends = services.Friends;
            identity = services.Identity;
            this.view = view ?? throw new ArgumentNullException(nameof(view));
            this.dialogs = dialogs ?? NullKitDialogs.Instance;
            ownerName = OwnerName;
        }

        /// <summary>Subscribes, renders the last snapshot at once (05 §2.3), then starts a new scan.</summary>
        public void Bind()
        {
            if (bound) return;
            bound = true;
            friends.EntriesChanged += OnEntriesChanged;
            friends.RefreshCompleted += OnRefreshCompleted;
            session.StateChanged += OnStateChanged;
            Render();
            friends.Refresh();
            RenderStatus();   // the spinner shows from the first frame
        }

        /// <summary>Unsubscribes and stops the scan; Entries keeps its last snapshot for the next visit.</summary>
        public void Unbind()
        {
            if (!bound) return;
            bound = false;
            dirty = false;
            friends.EntriesChanged -= OnEntriesChanged;
            friends.RefreshCompleted -= OnRefreshCompleted;
            session.StateChanged -= OnStateChanged;
            friends.Stop();
        }

        /// <summary>Ignored while a scan is running.</summary>
        public void OnRefresh()
        {
            if (!bound || friends.IsRefreshing) return;
            friends.Refresh();
            RenderStatus();
        }

        /// <summary>
        /// Names the password prompt, then Session.JoinLobby(lobby) with no password. On success Joining follows and
        /// the busy overlay takes over.
        /// </summary>
        public void OnJoin(LobbyId lobby, string lobbyName)
        {
            if (!bound || !lobby.IsValid || session.State != SessionState.MainMenu) return;

            dialogs.SetPendingJoinLabel(lobby, lobbyName);
            KitResult result = session.JoinLobby(lobby);
            if (result.Ok || result.Error == KitError.Busy || result.Error == KitError.PlatformUnavailable) return;
            dialogs.Toast(ToastSeverity.Warning, SessionErrorMessages.For(result.Error));
        }

        /// <summary>Called by the view from LateUpdate: at most one rebuild per frame.</summary>
        public void FlushIfDirty()
        {
            if (bound && dirty) Render();
        }

        /// <summary>
        /// One row per entry. A loading entry shows the friend and "Loading…" only; a resolved one the lobby name
        /// (LobbyRowFormatter.DisplayName with the owner's name), count, privacy, padlock and status. CanJoin = no
        /// LobbyJoinBlock and canJoinNow (State == MainMenu). Sorted joinable first, then by friend name
        /// (ordinal, ignore case), then entry order. ownerName may be null.
        /// </summary>
        public static IReadOnlyList<FriendLobbyRowModel> BuildRows(IReadOnlyList<FriendLobbyEntry> entries, bool canJoinNow,
            Func<PlayerId, string> ownerName)
        {
            if (entries == null || entries.Count == 0) return NoRows;

            var items = new List<SortItem>(entries.Count);
            for (int i = 0; i < entries.Count; i++)
            {
                FriendLobbyEntry entry = entries[i];
                if (entry == null) continue;
                LobbyJoinBlock block = LobbyRowFormatter.BlockFor(entry.Lobby, entry.LobbyDataResolved, true);
                items.Add(new SortItem(BuildRow(entry, block, canJoinNow, ownerName), block == LobbyJoinBlock.None, i));
            }
            items.Sort(CompareItems);

            var rows = new FriendLobbyRowModel[items.Count];
            for (int i = 0; i < items.Count; i++) rows[i] = items[i].Row;
            return rows;
        }

        private static FriendLobbyRowModel BuildRow(FriendLobbyEntry entry, LobbyJoinBlock block, bool canJoinNow,
            Func<PlayerId, string> ownerName)
        {
            LobbySummary s = entry.Lobby;
            var row = new FriendLobbyRowModel
            {
                Friend = entry.Friend,
                FriendName = string.IsNullOrEmpty(entry.FriendName) ? RosterPresenter.UnknownName : entry.FriendName,
                Lobby = s.Id,
                LobbyName = "",
                PlayerCount = "",
                PrivacyLabel = "",
                Status = LobbyRowFormatter.StatusText(block, s.Phase),
                CanJoin = canJoinNow && block == LobbyJoinBlock.None,
            };
            if (!entry.LobbyDataResolved) return row;   // only the lobby id is known yet

            string owner = "";
            if (s.Owner == entry.Friend) owner = entry.FriendName;
            else if (s.Owner.IsValid && ownerName != null) owner = ownerName(s.Owner);

            row.LobbyName = LobbyRowFormatter.DisplayName(s, owner);
            row.PlayerCount = LobbyRowFormatter.PlayerCount(s);
            row.PrivacyLabel = UiFormat.Privacy(s.Privacy);
            row.HasPassword = s.HasPassword;
            return row;
        }

        private static int CompareItems(SortItem a, SortItem b)
        {
            if (a.Joinable != b.Joinable) return a.Joinable ? -1 : 1;
            int byName = string.Compare(a.Row.FriendName, b.Row.FriendName, StringComparison.OrdinalIgnoreCase);
            return byName != 0 ? byName : a.Index.CompareTo(b.Index);
        }

        private void OnEntriesChanged(IReadOnlyList<FriendLobbyEntry> entries)
        {
            if (bound) dirty = true;
        }

        private void OnRefreshCompleted()
        {
            if (bound) dirty = true;
        }

        // Join is offered only in MainMenu (05 §6).
        private void OnStateChanged(SessionStateChange change)
        {
            if (bound) dirty = true;
        }

        private void Render()
        {
            dirty = false;
            IReadOnlyList<FriendLobbyRowModel> rows = BuildRows(friends.Entries, session.State == SessionState.MainMenu, ownerName);
            rowCount = rows.Count;
            view.ShowRows(rows);
            RenderStatus();
        }

        // 05 §6 empty state: "Looking…" while scanning with no rows, "None…" after the scan found none.
        private void RenderStatus()
        {
            bool refreshing = friends.IsRefreshing;
            view.SetRefreshing(refreshing);
            view.SetEmpty(rowCount > 0 ? null : refreshing ? LookingText : NoneHostingText);
        }

        // The owner of a friend's lobby may be someone else; the name may still be "" (persona not resolved).
        private string OwnerName(PlayerId player)
        {
            return identity.GetDisplayName(player) ?? "";
        }

        private readonly struct SortItem
        {
            public readonly FriendLobbyRowModel Row;
            public readonly bool Joinable;
            public readonly int Index;

            public SortItem(FriendLobbyRowModel row, bool joinable, int index)
            {
                Row = row;
                Joinable = joinable;
                Index = index;
            }
        }
    }
}

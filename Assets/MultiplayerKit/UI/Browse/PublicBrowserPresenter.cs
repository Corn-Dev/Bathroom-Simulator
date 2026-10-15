// Public browser presenter (05 §7): fills the filter bar from the schema (fixed distance, region and hide controls,
// plus one generated dropdown per published Choice or Bool setting), requests the list for the current
// BrowserFilterState and renders the result. Every request carries a generation number, so a completion for an older
// request, or one that arrives after Unbind, is dropped (05 §2.3); the browser itself cancels the previous request
// (01 §4.3). The filter state is the view's, kept for the menu scene's lifetime and never persisted (05 §7), and is
// handed in at construction. Join is the FriendsGames path: SetPendingJoinLabel, JoinLobby with no password, the
// password via the overlay prompt; Busy stays silent and Core already toasts PlatformUnavailable.
// Plain class; talks only to Core. Design: 05 §2.3, §7, §10; 02 §3c; 06 U37.

using System;
using System.Collections.Generic;

namespace MultiplayerKit.UI
{
    public sealed class PublicLobbyRowModel
    {
        public LobbyId Lobby;

        /// <summary>LobbyRowFormatter.DisplayName.</summary>
        public string Name;

        public string PlayerCount;

        /// <summary>Label of the lobby's published region ("Any region" for "any"); "" when unknown.</summary>
        public string RegionLabel;

        public string SettingsLine;
        public bool HasPassword;
        public string Status;
        public bool CanJoin;

        /// <summary>For the row's optional owner avatar (06 U38); None when unknown.</summary>
        public PlayerId Owner;
    }

    /// <summary>One filter dropdown: its options (index 0 = Any) and the selected index.</summary>
    public sealed class BrowserFilterSpec
    {
        /// <summary>Schema key of a setting filter; "region" for the region dropdown; null for distance.</summary>
        public string Key;

        public string Label;
        public IReadOnlyList<string> Options;
        public int Selected;
    }

    public interface IPublicBrowserView
    {
        /// <summary>Options and selection of the fixed controls: distance and region dropdowns, the three hide toggles.</summary>
        void SetFixedFilters(BrowserFilterSpec distance, BrowserFilterSpec region, bool hideFull, bool hidePassworded,
            bool hideInProgress);

        /// <summary>One generated dropdown per published Choice or Bool setting, in this order.</summary>
        void BuildFilters(IReadOnlyList<BrowserFilterSpec> settingFilters);

        void ShowRows(IReadOnlyList<PublicLobbyRowModel> rows);

        /// <summary>Spinner shown and Refresh disabled while true.</summary>
        void SetRequesting(bool requesting);

        /// <summary>Empty or error state; null hides it.</summary>
        void SetMessage(string message);
    }

    public sealed class PublicBrowserPresenter
    {
        public const string NoMatchesText = "No public games match your filters.";
        public const string SearchFailedText = "Couldn't search for games.";
        public const string UnreachableText = "Couldn't reach Steam. Try again.";
        public const string DistanceLabel = "Distance";

        private static readonly IReadOnlyList<LobbySummary> NoLobbies = Array.AsReadOnly(new LobbySummary[0]);
        private static readonly IReadOnlyList<PublicLobbyRowModel> NoRows = Array.AsReadOnly(new PublicLobbyRowModel[0]);

        private readonly SessionController session;
        private readonly IPublicLobbyBrowser browser;
        private readonly IPlayerIdentity identity;
        private readonly IPublicBrowserView view;
        private readonly IKitDialogs dialogs;
        private readonly Func<PlayerId, string> ownerName;
        private BrowserFilterState filters;
        private IReadOnlyList<LobbySummary> results = NoLobbies;
        private bool bound;
        private bool requesting;
        private bool invalidQueryLogged;
        private int generation;

        /// <summary>filters null starts from the defaults (Nearby, any region, hide full).</summary>
        public PublicBrowserPresenter(KitServices services, IPublicBrowserView view, IKitDialogs dialogs,
            BrowserFilterState filters = null)
        {
            if (services == null) throw new ArgumentNullException(nameof(services));
            session = services.Session;
            browser = services.Browser;
            identity = services.Identity;
            this.view = view ?? throw new ArgumentNullException(nameof(view));
            this.dialogs = dialogs ?? NullKitDialogs.Instance;
            this.filters = filters ?? new BrowserFilterState();
            ownerName = OwnerName;
        }

        public BrowserFilterState Filters => filters;

        /// <summary>Builds the filter bar, clears the old list, then requests (05 §7).</summary>
        public void Bind()
        {
            if (bound) return;
            bound = true;
            session.StateChanged += OnStateChanged;
            RenderFilters();
            results = NoLobbies;
            view.ShowRows(NoRows);
            view.SetMessage(null);
            Refresh();
        }

        /// <summary>Drops any pending completion first, then cancels the request in the browser.</summary>
        public void Unbind()
        {
            if (!bound) return;
            bound = false;
            generation++;
            requesting = false;
            session.StateChanged -= OnStateChanged;
            browser.Cancel();
        }

        /// <summary>Stores the state and requests again. The view debounces the controls first (05 §7).</summary>
        public void OnFilterChanged(BrowserFilterState state)
        {
            if (!bound) return;
            if (state != null) filters = state;
            Refresh();
        }

        /// <summary>Ignored while a request is in flight (the button is disabled then).</summary>
        public void OnRefresh()
        {
            if (bound && !requesting) Refresh();
        }

        public void OnJoin(LobbyId lobby, string name)
        {
            if (!bound || !lobby.IsValid || session.State != SessionState.MainMenu) return;

            dialogs.SetPendingJoinLabel(lobby, name);
            KitResult result = session.JoinLobby(lobby);
            if (result.Ok || result.Error == KitError.Busy || result.Error == KitError.PlatformUnavailable) return;
            dialogs.Toast(ToastSeverity.Warning, SessionErrorMessages.For(result.Error));
        }

        /// <summary>
        /// One row per lobby (BlockFor with resolved: true, fromFriendsList: false). CanJoin = no LobbyJoinBlock and
        /// canJoinNow (State == MainMenu). Sorted joinable first, then member count descending, then name (ordinal,
        /// ignore case), then list order. ownerName names unnamed lobbies and may return "" or be null.
        /// </summary>
        public static IReadOnlyList<PublicLobbyRowModel> BuildRows(IReadOnlyList<LobbySummary> lobbies,
            HostSettingsSchema schema, bool canJoinNow, Func<PlayerId, string> ownerName)
        {
            if (lobbies == null || lobbies.Count == 0) return NoRows;

            var items = new List<SortItem>(lobbies.Count);
            for (int i = 0; i < lobbies.Count; i++)
            {
                LobbySummary s = lobbies[i];
                if (s == null) continue;
                LobbyJoinBlock block = LobbyRowFormatter.BlockFor(s, true, false);
                string owner = s.Owner.IsValid && ownerName != null ? ownerName(s.Owner) : "";
                var row = new PublicLobbyRowModel
                {
                    Lobby = s.Id,
                    Owner = s.Owner,
                    Name = LobbyRowFormatter.DisplayName(s, owner),
                    PlayerCount = LobbyRowFormatter.PlayerCount(s),
                    RegionLabel = RegionLabelOf(s, schema),
                    SettingsLine = LobbyRowFormatter.SettingsLine(s, schema),
                    HasPassword = s.HasPassword,
                    Status = LobbyRowFormatter.StatusText(block, s.Phase),
                    CanJoin = canJoinNow && block == LobbyJoinBlock.None,
                };
                items.Add(new SortItem(row, block == LobbyJoinBlock.None, s.MemberCount, i));
            }
            items.Sort(CompareItems);

            var rows = new PublicLobbyRowModel[items.Count];
            for (int i = 0; i < items.Count; i++) rows[i] = items[i].Row;
            return rows;
        }

        // A new request supersedes the previous one; its late completion fails the generation check.
        private void Refresh()
        {
            int request = ++generation;
            requesting = true;
            view.SetRequesting(true);   // before Request, which may complete synchronously
            LobbyBrowserQuery query = BrowserQueryBuilder.Build(filters, session.Schema);
            try
            {
                browser.Request(query, result => OnResult(request, result));
            }
            catch (Exception e)
            {
                KitLog.Error("IPublicLobbyBrowser.Request threw.", e);
                OnResult(request, KitResult<IReadOnlyList<LobbySummary>>.Fail(KitError.Unknown, e.GetType().Name));
            }
        }

        // 05 §7 result table.
        private void OnResult(int request, KitResult<IReadOnlyList<LobbySummary>> result)
        {
            if (!bound || request != generation) return;   // stale, or the screen closed
            requesting = false;
            view.SetRequesting(false);

            if (result.Ok)
            {
                results = result.Value ?? NoLobbies;
                IReadOnlyList<PublicLobbyRowModel> rows = RenderRows();
                view.SetMessage(rows.Count == 0 ? NoMatchesText : null);
                return;
            }

            switch (result.Error)
            {
                case KitError.Cancelled:
                    return;   // the browser was cancelled or shut down under us: nothing to show
                case KitError.InvalidArgument:
                    // A schema or filter mismatch is a programmer error; the user only gets the message.
                    if (!invalidQueryLogged)
                    {
                        invalidQueryLogged = true;
                        KitLog.Error("The public lobby browser refused the query (" + result + ").");
                    }
                    ShowFailure(SearchFailedText);
                    return;
                default:
                    ShowFailure(UnreachableText);
                    dialogs.Toast(ToastSeverity.Warning, SessionErrorMessages.For(result.Error));
                    return;
            }
        }

        private void ShowFailure(string message)
        {
            results = NoLobbies;
            view.ShowRows(NoRows);
            view.SetMessage(message);
        }

        private IReadOnlyList<PublicLobbyRowModel> RenderRows()
        {
            IReadOnlyList<PublicLobbyRowModel> rows =
                BuildRows(results, session.Schema, session.State == SessionState.MainMenu, ownerName);
            view.ShowRows(rows);
            return rows;
        }

        // Join is offered only in MainMenu (05 §6, §7).
        private void OnStateChanged(SessionStateChange change)
        {
            if (bound && results.Count > 0) RenderRows();
        }

        // Fixed controls first, then the generated ones; indices out of range fall back to their defaults.
        private void RenderFilters()
        {
            HostSettingsSchema schema = session.Schema;
            IReadOnlyList<string> regions = BrowserQueryBuilder.RegionOptions(schema);
            filters.DistanceIndex = InRange(filters.DistanceIndex, BrowserQueryBuilder.DistanceOptions.Count)
                ? filters.DistanceIndex : BrowserQueryBuilder.DefaultDistanceIndex;
            if (!InRange(filters.RegionIndex, regions.Count)) filters.RegionIndex = 0;

            var distance = new BrowserFilterSpec
            {
                Key = null,
                Label = DistanceLabel,
                Options = BrowserQueryBuilder.DistanceOptions,
                Selected = filters.DistanceIndex,
            };
            bool hasRegion = schema.TryGet(CoreSettingKeys.Region, out SettingDefinition regionDef);
            var region = new BrowserFilterSpec
            {
                Key = CoreSettingKeys.Region,
                Label = hasRegion ? regionDef.Label : CoreSettingKeys.Region,
                Options = regions,
                Selected = filters.RegionIndex,
            };
            view.SetFixedFilters(distance, region, filters.HideFull, filters.HidePassworded, filters.HideInProgress);

            IReadOnlyList<SettingDefinition> defs = BrowserQueryBuilder.FilterDefinitions(schema);
            var specs = new List<BrowserFilterSpec>(defs.Count);
            for (int i = 0; i < defs.Count; i++)
            {
                SettingDefinition def = defs[i];
                specs.Add(new BrowserFilterSpec
                {
                    Key = def.Key,
                    Label = def.Label,
                    Options = BrowserQueryBuilder.OptionsFor(def),
                    Selected = BrowserQueryBuilder.SelectedIndex(filters, def),
                });
            }
            view.BuildFilters(specs);
        }

        // "any" reads "Any region"; a value this schema does not know (another game version) shows nothing.
        private static string RegionLabelOf(LobbySummary s, HostSettingsSchema schema)
        {
            if (!s.PublishedSettings.TryGetValue(CoreSettingKeys.Region, out string value)) return "";
            if (string.Equals(value, CoreSettings.RegionAny, StringComparison.Ordinal)) return BrowserQueryBuilder.AnyRegionOption;
            if (schema == null || !schema.TryGet(CoreSettingKeys.Region, out SettingDefinition def)) return "";
            for (int i = 0; i < def.Choices.Count; i++)
            {
                if (string.Equals(def.Choices[i].Value, value, StringComparison.Ordinal)) return def.Choices[i].Label;
            }
            return "";
        }

        private static int CompareItems(SortItem a, SortItem b)
        {
            if (a.Joinable != b.Joinable) return a.Joinable ? -1 : 1;
            if (a.Members != b.Members) return b.Members.CompareTo(a.Members);
            int byName = string.Compare(a.Row.Name, b.Row.Name, StringComparison.OrdinalIgnoreCase);
            return byName != 0 ? byName : a.Index.CompareTo(b.Index);
        }

        private static bool InRange(int index, int count)
        {
            return index >= 0 && index < count;
        }

        // May be "" on first sight; "Unnamed game" then shows until the next refresh (05 §7).
        private string OwnerName(PlayerId player)
        {
            return identity.GetDisplayName(player) ?? "";
        }

        private readonly struct SortItem
        {
            public readonly PublicLobbyRowModel Row;
            public readonly bool Joinable;
            public readonly int Members;
            public readonly int Index;

            public SortItem(PublicLobbyRowModel row, bool joinable, int members, int index)
            {
                Row = row;
                Joinable = joinable;
                Members = members;
                Index = index;
            }
        }
    }
}

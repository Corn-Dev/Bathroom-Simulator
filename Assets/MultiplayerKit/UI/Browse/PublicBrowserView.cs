// Public browser screen (05 §7, §9.2): the filter bar (distance and region dropdowns; Hide full, Hide
// password-protected and Hide games in progress toggles; one generated BrowserFilterRowView per published Choice or
// Bool setting, appended to the Filters grid after the fixed controls), the lobby list over
// RowPool<PublicLobbyRowView>, Refresh with a spinner, the message line and Back. The view owns the
// BrowserFilterState for its own lifetime, the menu scene's (05 §7: kept while navigating, never persisted), and
// hands it to each new presenter. A control change updates that state at once; the presenter hears about it
// filterDebounceSeconds of unscaled time after the last change, so a burst of changes sends one request. Rendering
// goes through ...WithoutNotify behind a guard, so it never counts as a change. Refresh is disabled while a request
// runs; if it had keyboard focus, it gets the focus back when the request ends.
// Design: 05 §7, §9.2 (hierarchy, inspector fields), §10, §11; 06 U36.

using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MultiplayerKit.UI
{
    public sealed class PublicBrowserView : KitView, IPublicBrowserView
    {
        // Same rate as the busy overlay's default, in unscaled time.
        private const float SpinnerDegreesPerSecond = 270f;

        [SerializeField] private PublicLobbyRowView rowPrefab;
        [SerializeField] private RectTransform content;
        [SerializeField] private Button refreshButton;
        [SerializeField] private GameObject refreshSpinner;
        [SerializeField] private TMP_Text message;
        [SerializeField] private Button backButton;
        [SerializeField] private TMP_Dropdown distanceDropdown;
        [SerializeField] private TMP_Dropdown regionDropdown;
        [SerializeField] private Toggle hideFullToggle;
        [SerializeField] private Toggle hidePasswordToggle;
        [SerializeField] private Toggle hideInProgressToggle;
        [SerializeField] private BrowserFilterRowView filterPrefab;
        [SerializeField] private RectTransform settingFiltersContent;
        [SerializeField] private float filterDebounceSeconds = 0.3f;
        [SerializeField] private MenuRouter router;

        private readonly BrowserFilterState filters = new BrowserFilterState();
        private readonly List<string> optionScratch = new List<string>();
        private PublicBrowserPresenter presenter;
        private RowPool<PublicLobbyRowView> lobbyRows;
        private RowPool<BrowserFilterRowView> filterRows;
        private bool rendering;
        private bool changePending;
        private float lastChange;
        private bool refreshHadFocus;

        private void Awake()
        {
            if (refreshButton != null) refreshButton.onClick.AddListener(OnRefreshClicked);
            if (backButton != null) backButton.onClick.AddListener(OnBackClicked);
            if (distanceDropdown != null) distanceDropdown.onValueChanged.AddListener(OnDistanceChanged);
            if (regionDropdown != null) regionDropdown.onValueChanged.AddListener(OnRegionChanged);
            if (hideFullToggle != null) hideFullToggle.onValueChanged.AddListener(OnHideFullChanged);
            if (hidePasswordToggle != null) hidePasswordToggle.onValueChanged.AddListener(OnHidePasswordChanged);
            if (hideInProgressToggle != null) hideInProgressToggle.onValueChanged.AddListener(OnHideInProgressChanged);
        }

        protected override bool ValidateReferences()
        {
            return Require(rowPrefab, nameof(rowPrefab)) & Require(content, nameof(content))
                & Require(refreshButton, nameof(refreshButton)) & Require(refreshSpinner, nameof(refreshSpinner))
                & Require(message, nameof(message)) & Require(backButton, nameof(backButton))
                & Require(distanceDropdown, nameof(distanceDropdown)) & Require(regionDropdown, nameof(regionDropdown))
                & Require(hideFullToggle, nameof(hideFullToggle)) & Require(hidePasswordToggle, nameof(hidePasswordToggle))
                & Require(hideInProgressToggle, nameof(hideInProgressToggle)) & Require(filterPrefab, nameof(filterPrefab))
                & Require(settingFiltersContent, nameof(settingFiltersContent)) & Require(router, nameof(router));
        }

        protected override void OnBind(KitServices services)
        {
            presenter = new PublicBrowserPresenter(services, this, KitOverlay.Dialogs, filters);
            presenter.Bind();
        }

        // A change still waiting for its debounce is already in the state, so the next Bind requests with it.
        protected override void OnUnbind()
        {
            changePending = false;
            if (presenter != null) presenter.Unbind();
            presenter = null;
        }

        private void Update()
        {
            if (refreshSpinner != null && refreshSpinner.activeSelf)
            {
                refreshSpinner.transform.Rotate(0f, 0f, -SpinnerDegreesPerSecond * Time.unscaledDeltaTime);
            }

            if (!changePending || Time.unscaledTime - lastChange < filterDebounceSeconds) return;
            changePending = false;
            if (presenter != null) presenter.OnFilterChanged(filters);
        }

        // IPublicBrowserView

        public void SetFixedFilters(BrowserFilterSpec distance, BrowserFilterSpec region, bool hideFull, bool hidePassworded,
            bool hideInProgress)
        {
            rendering = true;
            try
            {
                FillDropdown(distanceDropdown, distance);
                FillDropdown(regionDropdown, region);
                if (hideFullToggle != null) hideFullToggle.SetIsOnWithoutNotify(hideFull);
                if (hidePasswordToggle != null) hidePasswordToggle.SetIsOnWithoutNotify(hidePassworded);
                if (hideInProgressToggle != null) hideInProgressToggle.SetIsOnWithoutNotify(hideInProgress);
            }
            finally
            {
                rendering = false;
            }
        }

        // Rows are instantiated into the Filters grid, so they follow the five fixed controls (05 §9.2).
        public void BuildFilters(IReadOnlyList<BrowserFilterSpec> settingFilters)
        {
            if (filterRows == null)
            {
                if (filterPrefab == null || settingFiltersContent == null) return;   // logged by ValidateReferences
                filterRows = new RowPool<BrowserFilterRowView>(filterPrefab, settingFiltersContent);
            }

            int count = settingFilters != null ? settingFilters.Count : 0;
            filterRows.Ensure(count);
            rendering = true;
            try
            {
                for (int i = 0; i < count; i++) filterRows[i].Bind(settingFilters[i], OnSettingFilterChanged);
            }
            finally
            {
                rendering = false;
            }
        }

        public void ShowRows(IReadOnlyList<PublicLobbyRowModel> rows)
        {
            if (lobbyRows == null)
            {
                if (rowPrefab == null || content == null) return;   // logged by ValidateReferences
                lobbyRows = new RowPool<PublicLobbyRowView>(rowPrefab, content);
            }

            int count = rows != null ? rows.Count : 0;
            lobbyRows.Ensure(count);
            IPlayerIdentity identity = Services != null ? Services.Identity : null;
            for (int i = 0; i < count; i++) lobbyRows[i].Bind(rows[i], identity, OnRowJoin);
        }

        public void SetRequesting(bool requesting)
        {
            if (refreshSpinner != null && refreshSpinner.activeSelf != requesting) refreshSpinner.SetActive(requesting);
            if (refreshButton == null) return;

            // uGUI drops the selection of a Selectable that turns non-interactable; give it back afterwards.
            EventSystem system = EventSystem.current;
            if (requesting)
            {
                if (refreshButton.interactable)
                {
                    refreshHadFocus = system != null && system.currentSelectedGameObject == refreshButton.gameObject;
                }
                refreshButton.interactable = false;
                return;
            }

            refreshButton.interactable = true;
            if (refreshHadFocus && system != null && system.currentSelectedGameObject == null
                && refreshButton.gameObject.activeInHierarchy)
            {
                system.SetSelectedGameObject(refreshButton.gameObject);
            }
            refreshHadFocus = false;
        }

        public void SetMessage(string text)
        {
            if (message == null) return;
            message.text = text ?? "";
            SetShown(message.gameObject, text != null);
        }

        // Control changes: the state first, the presenter after the debounce.

        private void OnDistanceChanged(int index)
        {
            if (rendering) return;
            filters.DistanceIndex = index;
            ScheduleChange();
        }

        private void OnRegionChanged(int index)
        {
            if (rendering) return;
            filters.RegionIndex = index;
            ScheduleChange();
        }

        private void OnHideFullChanged(bool on)
        {
            if (rendering) return;
            filters.HideFull = on;
            ScheduleChange();
        }

        private void OnHidePasswordChanged(bool on)
        {
            if (rendering) return;
            filters.HidePassworded = on;
            ScheduleChange();
        }

        private void OnHideInProgressChanged(bool on)
        {
            if (rendering) return;
            filters.HideInProgress = on;
            ScheduleChange();
        }

        private void OnSettingFilterChanged(string key, int index)
        {
            if (rendering || key == null) return;
            filters.SettingIndices[key] = index;
            ScheduleChange();
        }

        private void ScheduleChange()
        {
            changePending = true;
            lastChange = Time.unscaledTime;
        }

        // Clicks, forwarded to the current presenter.

        private void OnRefreshClicked()
        {
            if (presenter != null) presenter.OnRefresh();
        }

        private void OnBackClicked()
        {
            if (router != null) router.Show(MenuScreen.MainMenu);
        }

        private void OnRowJoin(LobbyId lobby, string lobbyName)
        {
            if (presenter != null) presenter.OnJoin(lobby, lobbyName);
        }

        private void FillDropdown(TMP_Dropdown dropdown, BrowserFilterSpec spec)
        {
            if (dropdown == null || spec == null) return;
            optionScratch.Clear();
            if (spec.Options != null)
            {
                for (int i = 0; i < spec.Options.Count; i++) optionScratch.Add(spec.Options[i] ?? "");
            }
            dropdown.ClearOptions();
            dropdown.AddOptions(optionScratch);
            dropdown.SetValueWithoutNotify(spec.Selected);
            dropdown.RefreshShownValue();
        }

        private static void SetShown(GameObject target, bool shown)
        {
            if (target != null && target.activeSelf != shown) target.SetActive(shown);
        }
    }
}

// The lobby room's settings panel (05 §5.3): group headers and one row per SettingRowSpec under content, pooled per
// control type with RowPool, a key → row map for rendering and editability, and the empty message (an instance of
// the group header prefab, so the inspector fields stay as in 05 §9.2). A Build whose spec list matches the last
// one keeps every row, so reopening the room keeps the scroll position (the list only differs when the schema or
// IsHost does). Parent-driven like the overlay widgets: LobbyRoomView creates the SettingsPanelPresenter and calls
// Attach with its OnUserChanged. FlushPending commits a debounced slider value at once (called right before Start).
// Design: 05 §5.3, §9.2 (inspector fields), §9.3 (row prefabs), §10; 06 U24.

using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace MultiplayerKit.UI
{
    public sealed class SettingsPanelView : KitView, ISettingsPanelView
    {
        [SerializeField] private RectTransform content;
        [SerializeField] private TMP_Text groupHeaderPrefab;
        [SerializeField] private ToggleSettingRow togglePrefab;
        [SerializeField] private SliderSettingRow sliderPrefab;
        [SerializeField] private DropdownSettingRow dropdownPrefab;
        [SerializeField] private InputSettingRow inputPrefab;

        private RowPool<TMP_Text> headers;
        private RowPool<ToggleSettingRow> toggles;
        private RowPool<SliderSettingRow> sliders;
        private RowPool<DropdownSettingRow> dropdowns;
        private RowPool<InputSettingRow> inputs;
        private TMP_Text emptyMessage;
        private bool poolsReady;
        private bool missingLogged;

        private readonly List<SettingRowSpec> built = new List<SettingRowSpec>();
        private readonly List<SettingRowView> activeRows = new List<SettingRowView>();
        private readonly Dictionary<string, SettingRowView> rowsByKey = new Dictionary<string, SettingRowView>(StringComparer.Ordinal);
        private bool hasBuilt;
        private Action<string, string> onUserChanged;

        /// <summary>Routes user commits of every row to the presenter (SettingsPanelPresenter.OnUserChanged).</summary>
        public void Attach(Action<string, string> userChanged)
        {
            onUserChanged = userChanged;
        }

        /// <summary>Drops the presenter; a later commit (a slider flushed while the room closes) goes nowhere.</summary>
        public void Detach()
        {
            onUserChanged = null;
        }

        /// <summary>Commits every value a row still holds back, so Start uses what the host sees.</summary>
        public void FlushPending()
        {
            for (int i = 0; i < activeRows.Count; i++) activeRows[i].FlushPending();
        }

        protected override bool ValidateReferences()
        {
            return Require(content, nameof(content)) & Require(groupHeaderPrefab, nameof(groupHeaderPrefab))
                & Require(togglePrefab, nameof(togglePrefab)) & Require(sliderPrefab, nameof(sliderPrefab))
                & Require(dropdownPrefab, nameof(dropdownPrefab)) & Require(inputPrefab, nameof(inputPrefab));
        }

        // LobbyRoomView drives this panel; it has nothing of its own to bind.
        protected override void OnBind(KitServices services)
        {
        }

        protected override void OnUnbind()
        {
        }

        // ISettingsPanelView

        public void Build(IReadOnlyList<SettingRowSpec> rows)
        {
            if (rows == null || !EnsurePools()) return;
            if (hasBuilt && MatchesLastBuild(rows)) return;

            built.Clear();
            activeRows.Clear();
            rowsByKey.Clear();

            int headerCount = 0, toggleCount = 0, sliderCount = 0, dropdownCount = 0, inputCount = 0;
            for (int i = 0; i < rows.Count; i++)
            {
                SettingRowSpec spec = rows[i];
                if (spec == null) continue;
                if (spec.IsHeader) headerCount++;
                else if (spec.Definition == null) continue;
                else if (spec.Control == SettingControl.Toggle) toggleCount++;
                else if (spec.Control == SettingControl.Slider) sliderCount++;
                else if (spec.Control == SettingControl.Dropdown) dropdownCount++;
                else inputCount++;
            }
            headers.Ensure(headerCount);
            toggles.Ensure(toggleCount);
            sliders.Ensure(sliderCount);
            dropdowns.Ensure(dropdownCount);
            inputs.Ensure(inputCount);

            // Sibling order is layout order; inactive pooled rows end up after the active ones.
            int sibling = 0, h = 0, t = 0, s = 0, d = 0, n = 0;
            for (int i = 0; i < rows.Count; i++)
            {
                SettingRowSpec spec = rows[i];
                if (spec == null || (!spec.IsHeader && spec.Definition == null)) continue;

                if (spec.IsHeader)
                {
                    TMP_Text header = headers[h++];
                    header.richText = false;
                    header.text = spec.HeaderLabel ?? "";
                    header.transform.SetSiblingIndex(sibling++);
                }
                else
                {
                    SettingRowView row;
                    if (spec.Control == SettingControl.Toggle) row = toggles[t++];
                    else if (spec.Control == SettingControl.Slider) row = sliders[s++];
                    else if (spec.Control == SettingControl.Dropdown) row = dropdowns[d++];
                    else row = inputs[n++];   // Input and IntInput

                    row.Bind(spec, OnRowChanged);
                    row.transform.SetSiblingIndex(sibling++);
                    rowsByKey[spec.Definition.Key] = row;
                    activeRows.Add(row);
                }
                built.Add(spec);
            }

            if (emptyMessage != null) emptyMessage.transform.SetAsLastSibling();
            hasBuilt = true;
        }

        public void RenderValue(string key, string raw, string display)
        {
            if (key != null && rowsByKey.TryGetValue(key, out SettingRowView row)) row.Render(raw, display);
        }

        public void SetEditable(string key, bool editable, bool showLock)
        {
            if (key != null && rowsByKey.TryGetValue(key, out SettingRowView row)) row.SetEditable(editable, showLock);
        }

        public void SetEmpty(string message)
        {
            if (message == null)
            {
                if (emptyMessage != null && emptyMessage.gameObject.activeSelf) emptyMessage.gameObject.SetActive(false);
                return;
            }
            if (emptyMessage == null)
            {
                if (content == null || groupHeaderPrefab == null) return;   // logged by ValidateReferences
                emptyMessage = Instantiate(groupHeaderPrefab, content, false);
                emptyMessage.name = "EmptyMessage";
                emptyMessage.richText = false;
            }
            emptyMessage.text = message;
            if (!emptyMessage.gameObject.activeSelf) emptyMessage.gameObject.SetActive(true);
            emptyMessage.transform.SetAsLastSibling();
        }

        // The rows' callback; Commit in SettingRowView already guards against a throwing presenter.
        private void OnRowChanged(string key, string value)
        {
            Action<string, string> callback = onUserChanged;
            if (callback != null) callback(key, value);
        }

        // Same headers, same definitions (by reference) and same controls as the last build.
        private bool MatchesLastBuild(IReadOnlyList<SettingRowSpec> rows)
        {
            if (rows.Count != built.Count) return false;
            for (int i = 0; i < rows.Count; i++)
            {
                SettingRowSpec next = rows[i];
                SettingRowSpec last = built[i];
                if (next == null || next.IsHeader != last.IsHeader) return false;
                if (next.IsHeader)
                {
                    if (!string.Equals(next.HeaderLabel, last.HeaderLabel, StringComparison.Ordinal)) return false;
                }
                else if (!ReferenceEquals(next.Definition, last.Definition) || next.Control != last.Control)
                {
                    return false;
                }
            }
            return true;
        }

        // Pools are created on first use: LobbyRoomView may drive the panel before this component's own Awake ran.
        private bool EnsurePools()
        {
            if (poolsReady) return true;
            if (content == null || groupHeaderPrefab == null || togglePrefab == null || sliderPrefab == null
                || dropdownPrefab == null || inputPrefab == null)
            {
                if (!missingLogged)
                {
                    missingLogged = true;
                    KitLog.Error("SettingsPanelView on '" + name + "' is missing its content or a row prefab; no settings are shown.");
                }
                return false;
            }

            headers = new RowPool<TMP_Text>(groupHeaderPrefab, content);
            toggles = new RowPool<ToggleSettingRow>(togglePrefab, content);
            sliders = new RowPool<SliderSettingRow>(sliderPrefab, content);
            dropdowns = new RowPool<DropdownSettingRow>(dropdownPrefab, content);
            inputs = new RowPool<InputSettingRow>(inputPrefab, content);
            poolsReady = true;
            return true;
        }
    }
}

// Roster list (05 §5.2) over RowPool<RosterRowView>: the pool grows with the member count (no fixed slot count).
// Parent-driven like the overlay widgets: it never creates a RosterPresenter. The owner calls Attach(identity,
// onKick) once per bind, then the presenter renders through ShowRows. Owners: LobbyRoomView in the room
// (showReadyColumn true) and SessionMenuView in game, whose SessionMenuPresenter owns the presenter (false).
// Design: 05 §5.2, §9.1-§9.3 (inspector fields), §10; 06 U21.

using System;
using System.Collections.Generic;
using UnityEngine;

namespace MultiplayerKit.UI
{
    public sealed class RosterView : KitView, IRosterView
    {
        [SerializeField] private RosterRowView rowPrefab;
        [SerializeField] private RectTransform content;
        [SerializeField] private bool showReadyColumn = true;

        private RowPool<RosterRowView> pool;
        private IPlayerIdentity identity;
        private Action<PlayerId> onKick;

        /// <summary>Inspector flag: true in the lobby room, false in the session menu (05 §9.1).</summary>
        public bool ShowReadyColumn => showReadyColumn;

        /// <summary>Identity for the avatars and the kick handler of every row.</summary>
        public void Attach(IPlayerIdentity playerIdentity, Action<PlayerId> kick)
        {
            identity = playerIdentity;
            onKick = kick;
        }

        protected override bool ValidateReferences()
        {
            return Require(rowPrefab, nameof(rowPrefab)) & Require(content, nameof(content));
        }

        // The owner drives this list; it has nothing of its own to bind.
        protected override void OnBind(KitServices services)
        {
        }

        protected override void OnUnbind()
        {
        }

        public void ShowRows(IReadOnlyList<RosterRowModel> rows)
        {
            if (pool == null)
            {
                // Created on first use: the owner may render before this component's own Awake ran.
                if (rowPrefab == null || content == null) return;   // logged by ValidateReferences
                pool = new RowPool<RosterRowView>(rowPrefab, content);
            }

            int count = rows != null ? rows.Count : 0;
            pool.Ensure(count);
            for (int i = 0; i < count; i++) pool[i].Bind(rows[i], identity, showReadyColumn, OnRowKick);
        }

        private void OnRowKick(PlayerId player)
        {
            Action<PlayerId> kick = onKick;
            if (kick != null) kick(player);
        }
    }
}

// Presenter of the lobby room's header and action bar (05 §5.1): lobby name, privacy, player count and the host's
// resume label; Start (host only, interactable from Core's CanStart, the blocked reason under it); the Ready toggle
// (clients, when requireReady is on); Invite; Leave (the host confirms first, a client leaves at once). The presenter
// never computes readiness: CanStart and StartBlockedReason are SessionController's (01 §6.3), so the button and
// the Start guard cannot disagree. Every room control is interactable only while State == LobbyRoom (05 §5.1);
// SetInteractable covers Leave and Ready, which the other setters do not. Plain class; talks only to Core.
// Design: 05 §5.1, §10; 02 §2c-§2e, §6b-§6c; 06 U20.

using System;
using System.Collections.Generic;

namespace MultiplayerKit.UI
{
    public interface ILobbyRoomView
    {
        /// <summary>resumeLabel null hides it.</summary>
        void SetHeader(string lobbyName, string privacyLabel, string playerCount, string resumeLabel);

        /// <summary>blockedReason null hides it.</summary>
        void SetStart(bool visible, bool interactable, string blockedReason);

        /// <summary>Drives the toggle without its callback, so rendering never echoes into SetReady.</summary>
        void SetReady(bool visible, bool isOn);

        void SetInviteInteractable(bool interactable);

        /// <summary>Leave and Ready; false outside LobbyRoom, where the busy overlay or the error popup covers the room.</summary>
        void SetInteractable(bool interactable);
    }

    public sealed class LobbyRoomPresenter
    {
        public const string FallbackLobbyName = "Co-op game";
        public const string ResumePrefix = "Resuming: ";
        public const string ReadyFailedText = "Couldn't change your ready state. Try again.";

        private const string OwnerGameSuffix = "'s game";
        private const string CloseTitle = "Close lobby?";
        private const string CloseBody = "Everyone in the lobby will be sent back to the menu.";
        private const string CloseLabel = "Close";

        private readonly SessionController session;
        private readonly ILobbyRoomView view;
        private readonly IKitDialogs dialogs;
        private bool bound;

        public LobbyRoomPresenter(KitServices services, ILobbyRoomView view, IKitDialogs dialogs)
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
            session.StateChanged += OnStateChanged;
            session.MembersChanged += OnMembersChanged;
            session.SettingChanged += OnSettingChanged;
            session.CanStartChanged += OnCanStartChanged;
            Render();
        }

        public void Unbind()
        {
            if (!bound) return;
            bound = false;
            session.StateChanged -= OnStateChanged;
            session.MembersChanged -= OnMembersChanged;
            session.SettingChanged -= OnSettingChanged;
            session.CanStartChanged -= OnCanStartChanged;
        }

        /// <summary>
        /// Session.StartGame. A blocked start shows Core's reason (the result Detail is the user-readable
        /// StartBlockedReason, 02 §2e.1); Busy is a race the interactable rule already guards and stays silent.
        /// </summary>
        public void OnStart()
        {
            if (!bound) return;
            KitResult result = session.StartGame();
            if (result.Ok || result.Error == KitError.Busy) return;
            dialogs.Toast(ToastSeverity.Warning, string.IsNullOrEmpty(result.Detail) ? SessionErrorMessages.For(result.Error) : result.Detail);
        }

        /// <summary>Client ready toggle. A refused change resyncs the toggle from Members.</summary>
        public void OnReadyChanged(bool ready)
        {
            if (!bound) return;
            if (session.IsHost || session.State != SessionState.LobbyRoom)
            {
                RenderReady();
                return;
            }

            KitResult result = session.SetReady(ready);
            if (result.Ok) return;   // MembersChanged confirms it
            RenderReady();
            if (result.Error != KitError.Busy) dialogs.Toast(ToastSeverity.Warning, ReadyFailedText);
        }

        public void OnInvite()
        {
            if (!bound) return;
            if (!session.OpenInviteOverlay()) dialogs.Toast(ToastSeverity.Warning, SessionMenuPresenter.OverlayUnavailableText);
        }

        /// <summary>Host: confirm, because the lobby closes for everyone. Client: leave at once (rejoining is cheap).</summary>
        public void OnLeave()
        {
            if (!bound) return;
            if (session.IsHost) dialogs.Confirm(CloseTitle, CloseBody, CloseLabel, ConfirmLeave);
            else session.Leave();
        }

        private void ConfirmLeave()
        {
            if (!bound) return;
            session.Leave();
        }

        private void OnStateChanged(SessionStateChange change)
        {
            if (bound) Render();
        }

        private void OnMembersChanged(IReadOnlyList<LobbyMember> members)
        {
            if (!bound) return;
            RenderHeader();
            RenderReady();
        }

        private void OnSettingChanged(string key)
        {
            if (!bound) return;
            if (key == CoreSettingKeys.LobbyName || key == CoreSettingKeys.Privacy || key == CoreSettingKeys.MaxPlayers) RenderHeader();
            else if (key == CoreSettingKeys.RequireReady) RenderReady();
        }

        private void OnCanStartChanged(bool canStart, string reason)
        {
            if (bound) RenderStart();
        }

        // Applied on Bind and on every state change (05 §2.3: correct on the first frame).
        private void Render()
        {
            bool inRoom = session.State == SessionState.LobbyRoom;
            RenderHeader();
            RenderStart();
            RenderReady();
            view.SetInviteInteractable(inRoom);
            view.SetInteractable(inRoom);
        }

        // Settings is replaced for every session, so it is read here each time, never cached.
        private void RenderHeader()
        {
            IReadOnlyHostSettings settings = session.Settings;
            string lobbyName = settings.GetString(CoreSettingKeys.LobbyName);
            if (string.IsNullOrEmpty(lobbyName)) lobbyName = OwnerGameName();

            string privacy = UiFormat.Privacy(settings.GetString(CoreSettingKeys.Privacy));
            string count = UiFormat.PlayerCount(MemberCount(), settings.GetInt(CoreSettingKeys.MaxPlayers));
            WorldSaveSlotInfo slot = session.ResumeSlot;
            string resume = session.IsHost && slot != null ? ResumePrefix + slot.DisplayName : null;
            view.SetHeader(lobbyName, privacy, count, resume);
        }

        // Start: host only. The reason shows only in the room; outside it the busy overlay covers the bar.
        private void RenderStart()
        {
            bool host = session.IsHost;
            bool inRoom = session.State == SessionState.LobbyRoom;
            bool canStart = session.CanStart;
            string reason = host && inRoom && !canStart ? session.StartBlockedReason : null;
            view.SetStart(host, host && inRoom && canStart, reason);
        }

        // Ready: clients only, while requireReady is on; isOn is the local member's IsReady.
        private void RenderReady()
        {
            bool visible = !session.IsHost && session.Settings.GetBool(CoreSettingKeys.RequireReady);
            LobbyMember local = FindMember(true);
            view.SetReady(visible, local != null && local.IsReady);
        }

        // "{owner}'s game" when the lobby name is empty; the owner's persona may not be resolved yet.
        private string OwnerGameName()
        {
            LobbyMember owner = FindMember(false);
            return owner != null && !string.IsNullOrEmpty(owner.DisplayName) ? owner.DisplayName + OwnerGameSuffix : FallbackLobbyName;
        }

        private int MemberCount()
        {
            IReadOnlyList<LobbyMember> members = session.Members;
            return members != null ? members.Count : 0;
        }

        // local true: the local member; false: the lobby owner.
        private LobbyMember FindMember(bool local)
        {
            IReadOnlyList<LobbyMember> members = session.Members;
            if (members == null) return null;
            for (int i = 0; i < members.Count; i++)
            {
                LobbyMember m = members[i];
                if (m != null && (local ? m.IsLocal : m.IsOwner)) return m;
            }
            return null;
        }
    }
}

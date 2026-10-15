// The overlay surface that screen presenters see (05 §3.1): toasts, the shared confirm dialog, and the lobby label
// for the password prompt. NullKitDialogs is the fallback when the KitOverlay prefab is missing (05 §10): toasts
// go to the log, and Confirm always takes the cancel path, so a kick, a save delete or "End game" is never
// confirmed without the user seeing it. Design: 05 §3.1, §10; 06 U4.

using System;

namespace MultiplayerKit.UI
{
    public interface IKitDialogs
    {
        void Toast(ToastSeverity severity, string text);

        /// <summary>One dialog at a time: a newer Confirm cancels the open one first (its onCancel runs).</summary>
        void Confirm(string title, string body, string confirmLabel, Action onConfirm, Action onCancel = null);

        /// <summary>Lobby name the password prompt shows if the join that follows needs a password (05 §3.2).</summary>
        void SetPendingJoinLabel(LobbyId lobby, string label);
    }

    /// <summary>Used by views when KitOverlay.Instance is null (see KitOverlay.Dialogs).</summary>
    public sealed class NullKitDialogs : IKitDialogs
    {
        private const int LoggedTextLength = 200;

        public static readonly NullKitDialogs Instance = new NullKitDialogs();

        private bool warned;
        private bool confirmErrorLogged;

        private NullKitDialogs()
        {
        }

        /// <summary>Logs at the matching level.</summary>
        public void Toast(ToastSeverity severity, string text)
        {
            WarnOnce();
            string line = "Toast without KitOverlay: " + KitLog.Truncate(text, LoggedTextLength);
            switch (severity)
            {
                case ToastSeverity.Error:
                    KitLog.Error(line);
                    break;
                case ToastSeverity.Warning:
                    KitLog.Warning(line);
                    break;
                default:
                    KitLog.Info(line);
                    break;
            }
        }

        /// <summary>Never confirms: invokes onCancel at once. Logs Error the first time.</summary>
        public void Confirm(string title, string body, string confirmLabel, Action onConfirm, Action onCancel = null)
        {
            WarnOnce();
            if (!confirmErrorLogged)
            {
                confirmErrorLogged = true;
                KitLog.Error("Confirm \"" + KitLog.Truncate(title) + "\" needs the KitOverlay prefab; it was answered with cancel.");
            }
            if (onCancel == null) return;

            try
            {
                onCancel();
            }
            catch (Exception e)
            {
                KitLog.Error("Confirm cancel callback threw.", e);
            }
        }

        /// <summary>No-op: without the overlay there is no password prompt to label.</summary>
        public void SetPendingJoinLabel(LobbyId lobby, string label)
        {
        }

        private void WarnOnce()
        {
            if (warned) return;
            warned = true;
            KitLog.Warning("No KitOverlay in the scene: toasts are only logged and confirmations are cancelled. Add the KitOverlay prefab (05 §9.1).");
        }
    }
}

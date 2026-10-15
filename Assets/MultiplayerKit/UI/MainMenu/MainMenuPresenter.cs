// Main menu presenter (05 §4): New Game, Continue (with the newest slot's subtitle), Load, Friends' Games, Browse
// Public (hidden, not disabled, without public lobbies) and Quit. It is the single owner of the "Steam is not
// running" UI (01 §6.2, 02 §1 step 4.3): while !Session.IsPlatformAvailable the networked buttons are
// non-interactable (Quit never is), and one Error toast is raised per app session through a static one-shot flag
// (reset at SubsystemRegistration, so play mode without a domain reload counts as a new app session); the
// bootstrap and Core raise nothing for it. Results: Busy stays silent. ContinueLatest toasts its own save
// failures (02 §2b.1-2), so for it only PlatformUnavailable is toasted here, and each failure shows one toast.
// Plain class; talks only to Core. Design: 05 §4, §5.4, §10; 02 §1 step 4.3, §2a.2, §2b; 06 U18.

using System;
using System.Runtime.CompilerServices;

// The UI tests (T15 assembly) reset the one-shot platform toast flag between cases through ResetStatics.
[assembly: InternalsVisibleTo("MultiplayerKit.UI.Tests")]

namespace MultiplayerKit.UI
{
    public interface IMainMenuView
    {
        /// <summary>subtitle, for example "World 3 · 2h 14m · yesterday 21:40"; ignored while hidden.</summary>
        void SetContinue(bool visible, string subtitle);

        void SetLoadVisible(bool visible);
        void SetBrowsePublicVisible(bool visible);

        /// <summary>New Game, Continue, Load, Friends' Games and Browse Public. Quit is never affected.</summary>
        void SetInteractable(bool interactable);
    }

    public sealed class MainMenuPresenter
    {
        public const string PlatformUnavailableText = "Steam is not running. Multiplayer is unavailable.";

        // One platform toast per app session, however often the menu scene is reloaded.
        private static bool platformToastShown;

        // Enter Play Mode without a domain reload keeps statics, so each play session must start with the flag clear.
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        internal static void ResetStatics()
        {
            platformToastShown = false;
        }

        private readonly KitServices services;
        private readonly SessionController session;
        private readonly IMainMenuView view;
        private readonly IKitDialogs dialogs;
        private readonly IMenuNavigator nav;
        private bool bound;

        public MainMenuPresenter(KitServices services, IMainMenuView view, IKitDialogs dialogs, IMenuNavigator nav)
        {
            this.services = services ?? throw new ArgumentNullException(nameof(services));
            session = services.Session;
            this.view = view ?? throw new ArgumentNullException(nameof(view));
            this.dialogs = dialogs ?? NullKitDialogs.Instance;
            this.nav = nav ?? throw new ArgumentNullException(nameof(nav));
        }

        public void Bind()
        {
            if (bound) return;
            bound = true;
            session.StateChanged += OnStateChanged;

            RenderContinue();   // re-read on every Bind: a session that just autosaved shows its new slot
            view.SetLoadVisible(WorldSaves());
            view.SetBrowsePublicVisible((session.Capabilities & GameCapabilities.PublicLobbies) != 0 && services.Browser.IsSupported);
            RenderInteractable();

            if (!session.IsPlatformAvailable && !platformToastShown)
            {
                platformToastShown = true;
                dialogs.Toast(ToastSeverity.Error, PlatformUnavailableText);
            }
        }

        public void Unbind()
        {
            if (!bound) return;
            bound = false;
            session.StateChanged -= OnStateChanged;
        }

        /// <summary>Session.HostNewGame. On success CreatingLobby follows and the busy overlay takes over.</summary>
        public void OnNewGame()
        {
            if (!bound) return;
            ShowHostFailure(session.HostNewGame(), false);
        }

        /// <summary>Session.ContinueLatest, which hosts the newest slot.</summary>
        public void OnContinue()
        {
            if (!bound) return;
            ShowHostFailure(session.ContinueLatest(), true);
        }

        public void OnLoad()
        {
            if (bound) nav.Show(MenuScreen.LoadGame);
        }

        // TEMPORARY: until the in-app friends-games browser screen is built, this button just opens the
        // Steam friends overlay (where you can use Steam's own "Join Game"). Swap back to
        // nav.Show(MenuScreen.FriendsGames) when that screen exists.
        public void OnFriendsGames()
        {
            if (!bound) return;
            if (!session.OpenFriendsOverlay())
                dialogs.Toast(ToastSeverity.Warning, PlatformUnavailableText);
        }

        public void OnBrowsePublic()
        {
            if (bound) nav.Show(MenuScreen.PublicBrowser);
        }

        /// <summary>Always available, also when Steam is not running.</summary>
        public void OnQuit()
        {
            nav.Quit();
        }

        private void OnStateChanged(SessionStateChange change)
        {
            if (bound) RenderInteractable();
        }

        // The state term keeps a double click on New Game from reaching Core as Busy (05 §4).
        private void RenderInteractable()
        {
            view.SetInteractable(session.State == SessionState.MainMenu && session.IsPlatformAvailable);
        }

        // Continue: visible iff world saves are on and GetLatest returns a slot. A read failure hides it and logs
        // Warning; the main menu raises no toast just for a missing cloud.
        private void RenderContinue()
        {
            if (!WorldSaves())
            {
                view.SetContinue(false, null);
                return;
            }

            KitResult<WorldSaveSlotInfo> latest;
            try
            {
                latest = services.Saves.GetLatest();
            }
            catch (Exception e)
            {
                latest = KitResult<WorldSaveSlotInfo>.Fail(KitError.SaveIoFailed, e.GetType().Name + ": " + e.Message);
            }

            if (latest.Ok && latest.Value != null)
            {
                view.SetContinue(true, UiFormat.SlotSubtitle(latest.Value));
                return;
            }
            if (!latest.Ok && latest.Error != KitError.SaveNotFound)
            {
                KitLog.Warning("Main menu: reading the latest save failed (" + latest + "); Continue is hidden.");
            }
            view.SetContinue(false, null);
        }

        private bool WorldSaves()
        {
            return (session.Capabilities & GameCapabilities.WorldSaves) != 0 && services.Saves != null;
        }

        // 05 §4 results: an Error toast with SessionErrorMessages.For(code); PlatformUnavailable reads "Steam is not running.".
        private void ShowHostFailure(KitResult result, bool coreToastsSaveErrors)
        {
            if (result.Ok || result.Error == KitError.Busy) return;
            if (coreToastsSaveErrors && result.Error != KitError.PlatformUnavailable) return;   // Core already showed it
            dialogs.Toast(ToastSeverity.Error, SessionErrorMessages.For(result.Error));
        }
    }
}

// Menu-scene navigation (05 §2.4): one screen visible at a time through CanvasGroup + SetActive (deactivating a
// screen unbinds its view, so hidden screens hold no subscriptions), the SessionState -> screen table,
// lastMenuScreen, and the back key. Lives on the MenuCanvas root (05 §9.2).
// Design: 05 §2.4, §4 (IMenuNavigator, Quit), §9.2 (inspector fields), §10 (Escape), §11 (first control selected); 06 U3.

using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MultiplayerKit.UI
{
    public enum MenuScreen { MainMenu, FriendsGames, PublicBrowser, LoadGame, LobbyRoom }

    /// <summary>Navigation seam handed to screen presenters; implemented by MenuRouter, faked in tests.</summary>
    public interface IMenuNavigator
    {
        /// <summary>Shows a menu screen. Ignored outside SessionState.MainMenu, where the session state decides.</summary>
        void Show(MenuScreen screen);

        /// <summary>Application.Quit, and leaves play mode in the editor.</summary>
        void Quit();
    }

    public sealed class MenuRouter : KitView, IMenuNavigator
    {
        private const int ScreenCount = 5;

        [SerializeField] private CanvasGroup mainMenu;
        [SerializeField] private CanvasGroup friendsGames;
        [SerializeField] private CanvasGroup publicBrowser;
        [SerializeField] private CanvasGroup loadGame;
        [SerializeField] private CanvasGroup lobbyRoom;
        [SerializeField] private KeyCode backKey = KeyCode.Escape;

        private SessionController session;
        private MenuScreen current = MenuScreen.MainMenu;
        private MenuScreen lastMenuScreen = MenuScreen.MainMenu;
        private SessionState leavingFrom = SessionState.MainMenu;

        public MenuScreen Current => current;

        // Before any service exists only the main menu is active, so hidden screens never bind.
        private void Awake()
        {
            ShowOnly(MenuScreen.MainMenu, false);
        }

        protected override bool ValidateReferences()
        {
            return Require(mainMenu, nameof(mainMenu)) & Require(friendsGames, nameof(friendsGames))
                & Require(publicBrowser, nameof(publicBrowser)) & Require(loadGame, nameof(loadGame))
                & Require(lobbyRoom, nameof(lobbyRoom));
        }

        protected override void OnBind(KitServices services)
        {
            session = services.Session;
            session.StateChanged += OnStateChanged;
            ApplyState(session.State);   // also covers a menu reloaded while State == Error (05 §10)
        }

        protected override void OnUnbind()
        {
            if (session != null) session.StateChanged -= OnStateChanged;
            session = null;
        }

        public void Show(MenuScreen screen)
        {
            if (session != null && session.State != SessionState.MainMenu)
            {
                KitLog.Debug("MenuRouter.Show(" + screen + ") ignored in state " + session.State + ".");
                return;
            }
            if (screen != MenuScreen.LobbyRoom) lastMenuScreen = screen;
            ShowOnly(screen, true);
        }

        public void Quit()
        {
            KitLog.Info("Quit requested from the menu.");
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#endif
            Application.Quit();
        }

        private void OnStateChanged(SessionStateChange change)
        {
            if (change.To == SessionState.Leaving) leavingFrom = change.From;

            // The user left a lobby: the list they came from is stale by now, so return to the main menu (05 §2.4).
            // A cancelled join or create keeps lastMenuScreen, and so does an error (OK returns to the browser).
            if (change.To == SessionState.MainMenu && change.From == SessionState.Leaving && IsLobbyState(leavingFrom))
            {
                lastMenuScreen = MenuScreen.MainMenu;
            }
            ApplyState(change.To);
        }

        // 05 §2.4 table. Every other state keeps the current screen; the busy overlay, the error popup or the
        // gameplay scene covers it.
        private void ApplyState(SessionState state)
        {
            if (state == SessionState.MainMenu) ShowOnly(lastMenuScreen, true);
            else if (state == SessionState.LobbyRoom) ShowOnly(MenuScreen.LobbyRoom, true);
        }

        private static bool IsLobbyState(SessionState state)
        {
            return state == SessionState.LobbyRoom || state == SessionState.StartingGame || state == SessionState.InGame;
        }

        // Back = main menu, only from a sub-screen in MainMenu and never while an overlay modal takes the key.
        private void Update()
        {
            if (!Input.GetKeyDown(backKey)) return;
            if (session == null || session.State != SessionState.MainMenu) return;
            if (current == MenuScreen.MainMenu || current == MenuScreen.LobbyRoom || KitOverlay.IsModalOpen) return;
            Show(MenuScreen.MainMenu);
        }

        private void ShowOnly(MenuScreen screen, bool selectFirstControl)
        {
            current = screen;

            // Hide the others first, so the shown screen binds after they unbound.
            for (int i = 0; i < ScreenCount; i++)
            {
                MenuScreen other = (MenuScreen)i;
                if (other != screen) SetScreen(GroupFor(other), false);
            }

            CanvasGroup shown = GroupFor(screen);
            SetScreen(shown, true);
            if (selectFirstControl) SelectFirstButton(shown);
        }

        private CanvasGroup GroupFor(MenuScreen screen)
        {
            switch (screen)
            {
                case MenuScreen.FriendsGames: return friendsGames;
                case MenuScreen.PublicBrowser: return publicBrowser;
                case MenuScreen.LoadGame: return loadGame;
                case MenuScreen.LobbyRoom: return lobbyRoom;
                default: return mainMenu;
            }
        }

        private static void SetScreen(CanvasGroup group, bool visible)
        {
            if (group == null) return;
            SetGroupVisible(group, visible);
            if (group.gameObject.activeSelf != visible) group.gameObject.SetActive(visible);
        }

        // 05 §11: keyboard focus starts on the first enabled button of the shown screen. Reads the button's own
        // interactable flag; the view has already rendered, because its OnEnable ran inside SetActive(true).
        private static void SelectFirstButton(CanvasGroup group)
        {
            EventSystem system = EventSystem.current;
            if (group == null || system == null) return;

            Button[] buttons = group.GetComponentsInChildren<Button>(false);
            for (int i = 0; i < buttons.Length; i++)
            {
                if (!buttons[i].interactable) continue;
                system.SetSelectedGameObject(buttons[i].gameObject);
                return;
            }
        }
    }
}

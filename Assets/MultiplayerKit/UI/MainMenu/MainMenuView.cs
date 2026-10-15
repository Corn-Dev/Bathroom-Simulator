// Main menu screen (05 §4, §9.2): New Game, Continue (+ subtitle), Load, Friends' Games, Browse Public and Quit.
// A thin view: button listeners forward to MainMenuPresenter, which decides visibility and interactability. Quit
// also works before the kit services are published, or if they never are, so the app can always be closed. When
// the networked buttons turn back on, keyboard focus left on Quit (or on nothing) moves to the first of them.
// Design: 05 §4, §5.4, §9.2 (hierarchy, inspector fields), §10, §11 (first control selected); 06 U17.

using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MultiplayerKit.UI
{
    public sealed class MainMenuView : KitView, IMainMenuView
    {
        [SerializeField] private Button newGameButton;
        [SerializeField] private Button continueButton;
        [SerializeField] private TMP_Text continueSubtitle;
        [SerializeField] private Button loadButton;
        [SerializeField] private Button friendsGamesButton;
        [SerializeField] private Button browsePublicButton;
        [SerializeField] private Button quitButton;
        [SerializeField] private MenuRouter router;

        private MainMenuPresenter presenter;

        private void Awake()
        {
            if (newGameButton != null) newGameButton.onClick.AddListener(OnNewGameClicked);
            if (continueButton != null) continueButton.onClick.AddListener(OnContinueClicked);
            if (loadButton != null) loadButton.onClick.AddListener(OnLoadClicked);
            if (friendsGamesButton != null) friendsGamesButton.onClick.AddListener(OnFriendsGamesClicked);
            if (browsePublicButton != null) browsePublicButton.onClick.AddListener(OnBrowsePublicClicked);
            if (quitButton != null) quitButton.onClick.AddListener(OnQuitClicked);

            // The subtitle carries the save's display name, which comes from the lobby name.
            if (continueSubtitle != null)
            {
                continueSubtitle.richText = false;
                continueSubtitle.overflowMode = TextOverflowModes.Ellipsis;
            }
        }

        protected override bool ValidateReferences()
        {
            return Require(newGameButton, nameof(newGameButton)) & Require(continueButton, nameof(continueButton))
                & Require(continueSubtitle, nameof(continueSubtitle)) & Require(loadButton, nameof(loadButton))
                & Require(friendsGamesButton, nameof(friendsGamesButton)) & Require(browsePublicButton, nameof(browsePublicButton))
                & Require(quitButton, nameof(quitButton)) & Require(router, nameof(router));
        }

        protected override void OnBind(KitServices services)
        {
            presenter = new MainMenuPresenter(services, this, KitOverlay.Dialogs, router);
            presenter.Bind();
        }

        protected override void OnUnbind()
        {
            if (presenter != null) presenter.Unbind();
            presenter = null;
        }

        // IMainMenuView

        public void SetContinue(bool visible, string subtitle)
        {
            if (continueButton != null) SetShown(continueButton.gameObject, visible);
            if (continueSubtitle == null) return;

            bool hasText = visible && !string.IsNullOrEmpty(subtitle);
            continueSubtitle.text = hasText ? subtitle : "";
            SetShown(continueSubtitle.gameObject, hasText);
        }

        public void SetLoadVisible(bool visible)
        {
            if (loadButton != null) SetShown(loadButton.gameObject, visible);
        }

        public void SetBrowsePublicVisible(bool visible)
        {
            if (browsePublicButton != null) SetShown(browsePublicButton.gameObject, visible);
        }

        public void SetInteractable(bool interactable)
        {
            bool turningOn = interactable && newGameButton != null && !newGameButton.interactable;

            SetInteractable(newGameButton, interactable);
            SetInteractable(continueButton, interactable);
            SetInteractable(loadButton, interactable);
            SetInteractable(friendsGamesButton, interactable);
            SetInteractable(browsePublicButton, interactable);

            if (turningOn) MoveFocusOffQuit();
        }

        // Clicks, forwarded to the presenter.

        private void OnNewGameClicked()
        {
            if (presenter != null) presenter.OnNewGame();
        }

        private void OnContinueClicked()
        {
            if (presenter != null) presenter.OnContinue();
        }

        private void OnLoadClicked()
        {
            if (presenter != null) presenter.OnLoad();
        }

        private void OnFriendsGamesClicked()
        {
            if (presenter != null) presenter.OnFriendsGames();
        }

        private void OnBrowsePublicClicked()
        {
            if (presenter != null) presenter.OnBrowsePublic();
        }

        private void OnQuitClicked()
        {
            if (presenter != null) presenter.OnQuit();
            else if (router != null) router.Quit();   // services never published: the app can still be closed
        }

        // MenuRouter selects the first enabled button in its own StateChanged handler, and Unity does not fix whether
        // that handler runs before or after the presenter's. Run first after a failed or cancelled create or join, it
        // finds the networked buttons still off and puts focus on Quit, one Submit from closing the game. So when they
        // turn back on, a selection on Quit or on nothing usable moves to the first shown networked button; any other
        // selection (an open overlay dialog, for example) is left alone.
        private void MoveFocusOffQuit()
        {
            EventSystem system = EventSystem.current;
            if (system == null) return;

            GameObject selected = system.currentSelectedGameObject;
            bool onQuit = quitButton != null && selected == quitButton.gameObject;
            if (selected != null && selected.activeInHierarchy && !onQuit) return;

            Button[] networked = { newGameButton, continueButton, loadButton, friendsGamesButton, browsePublicButton };
            for (int i = 0; i < networked.Length; i++)
            {
                Button button = networked[i];
                if (button == null || !button.interactable || !button.gameObject.activeInHierarchy) continue;
                system.SetSelectedGameObject(button.gameObject);
                return;
            }
        }

        private static void SetInteractable(Selectable control, bool interactable)
        {
            if (control != null) control.interactable = interactable;
        }

        private static void SetShown(GameObject target, bool shown)
        {
            if (target != null && target.activeSelf != shown) target.SetActive(shown);
        }
    }
}

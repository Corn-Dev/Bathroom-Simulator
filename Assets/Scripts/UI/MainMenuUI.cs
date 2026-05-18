using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class MainMenuUI : MonoBehaviour
{
    [Header("Flow")]
    [SerializeField] private bool useLobbyUi = false;

    [Header("Panels")]
    public GameObject mainMenuPanel;
    public GameObject playPanel;
    public GameObject lobbyPanel;
    public GameObject playerListPanel;
    // TODO: Add hostSettingsPanel — a panel with a max-players input, a friends-only
    // toggle, and a "Start" button that calls OnHostSettingsConfirmed(). Wire it in
    // the inspector and call ShowPanel(hostSettingsPanel) from OnHostLobbyRequested().

    [Header("Buttons")]
    public Button playButton;
    public Button settingsButton;
    public Button quitButton;
    public Button hostLobbyButton;
    public Button joinLobbyButton;
    public Button backFromPlayButton;
    public Button backFromLobbyButton;
    public Button startGameButton;

    [Header("Host Settings (placeholder values until settings panel exists)")]
    [SerializeField] private int defaultMaxPlayers = 8;
    [SerializeField] private bool defaultFriendsOnly = true;

    [Header("References")]
    public SteamLobby steamLobby;

    private LobbyPlayerList playerList;
    private LobbyUiPresenter lobbyUiPresenter;

    void Start()
    {
        if (playerListPanel != null)
        {
            playerList = playerListPanel.GetComponent<LobbyPlayerList>();
        }

        // Hook up listeners
        playButton.onClick.AddListener(OpenPlayPanel);
        settingsButton.onClick.AddListener(OpenSettings);
        quitButton.onClick.AddListener(QuitGame);

        hostLobbyButton.onClick.AddListener(HostLobby);
        joinLobbyButton.onClick.AddListener(JoinLobby);

        backFromPlayButton.onClick.AddListener(OnPlayBackButton);
        backFromLobbyButton.onClick.AddListener(OnLobbyBackButton);

        startGameButton.onClick.AddListener(OnStartGameButtonClicked);

        if (steamLobby != null)
        {
            steamLobby.OnLobbyEnteredEvent += OnLobbyEntered;
            steamLobby.OnHostLobbyRequested += OnHostLobbyRequested;
            if (useLobbyUi && playerList != null)
            {
                lobbyUiPresenter = new LobbyUiPresenter(playerList);
                lobbyUiPresenter.Bind(new SteamLobbyDiscoveryAdapter(steamLobby));
            }
        }

        startGameButton.enabled = false;

        // Show only main menu at start
        ShowPanel(mainMenuPanel);
    }

    private void OnLobbyEntered()
    {
        // Clients: NGO scene sync will load the game scene automatically.
        // Host: scene is loaded immediately in HandleLobbyEntered on SteamLobby.
        // Nothing to do here until lobby UI is implemented.
    }

    // Called when SteamLobby fires OnHostLobbyRequested (i.e. host button was clicked).
    // TODO: Replace the body of this method with ShowPanel(hostSettingsPanel) once the
    // host settings panel exists. For now, fall through to defaults immediately.
    private void OnHostLobbyRequested()
    {
        Debug.Log("Host lobby requested — host settings panel not yet implemented. Using defaults.");
        OnHostSettingsConfirmed(defaultMaxPlayers, defaultFriendsOnly);
    }

    // Called by the host settings panel's Start button (or by OnHostLobbyRequested
    // as a fallback until the panel exists).
    private void OnHostSettingsConfirmed(int maxPlayers, bool friendsOnly)
    {
        Debug.Log($"Starting game with maxPlayers={maxPlayers}, friendsOnly={friendsOnly}");
        steamLobby.StartGame(maxPlayers, friendsOnly);
    }

    private void UpdatePlayerList(List<(string playerName, Sprite avatar)> players)
    {
        if (playerList != null)
        {
            playerList.RefreshUI(players);
        }
    }

    private void ShowPanel(GameObject panel)
    {
        mainMenuPanel.SetActive(false);
        playPanel.SetActive(false);
        lobbyPanel.SetActive(false);

        panel.SetActive(true);
    }

    private void OpenPlayPanel() => ShowPanel(playPanel);

    private void OpenMainMenu() => ShowPanel(mainMenuPanel);

    private void OnPlayBackButton()
    {
        OpenMainMenu();
    }

    private void OnLobbyBackButton()
    {
        steamLobby.LeaveLobby();
        if (playerList != null)
        {
            playerList.ClearList();
        }
        OpenMainMenu();
    }

    private void OpenSettings() => Debug.Log("Settings clicked!");
    private void QuitGame() => Application.Quit();

    private void HostLobby()
    {
        Debug.Log("Host Lobby clicked!");
        steamLobby.HostLobby();
    }

    private void JoinLobby()
    {
        Debug.Log("Join Lobby clicked!");
        // Opens the Steam friends overlay. The player clicks "Join Game" on their
        // friend's profile from here, which triggers the join via Steam automatically.
        // TODO: Replace with a lobby browser UI showing friends' games and public lobbies.
        steamLobby.JoinFriendLobby();
    }

    private void OnStartGameButtonClicked()
    {
        // Only enabled for the host (see OnLobbyEntered).
        // Creates the Steam lobby with current settings; scene loads automatically
        // once HandleLobbyEntered fires on the host side.
        Debug.Log("Start Game clicked!");
        OnHostSettingsConfirmed(defaultMaxPlayers, defaultFriendsOnly);
    }

    private void OnDestroy()
    {
        if (steamLobby != null)
        {
            steamLobby.OnLobbyEnteredEvent -= OnLobbyEntered;
            steamLobby.OnHostLobbyRequested -= OnHostLobbyRequested;
        }
    }
}
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class MainMenuUI : MonoBehaviour
{
    [Header("Panels")]
    public GameObject mainMenuPanel;
    public GameObject playPanel;
    public GameObject lobbyPanel;
    public GameObject playerListPanel;

    [Header("Buttons")]
    public Button playButton;
    public Button settingsButton;
    public Button quitButton;
    public Button hostLobbyButton;
    public Button joinLobbyButton;
    public Button backFromPlayButton;
    public Button backFromLobbyButton;
    public Button startGameButton;

    [Header("References")]
    public SteamLobby steamLobby;

    private LobbyPlayerList playerList;

    void Start()
    {
        playerList = playerListPanel.GetComponent<LobbyPlayerList>();

        // Hook up listeners
        playButton.onClick.AddListener(OpenPlayPanel);
        settingsButton.onClick.AddListener(OpenSettings);
        quitButton.onClick.AddListener(QuitGame);

        hostLobbyButton.onClick.AddListener(HostLobby);
        joinLobbyButton.onClick.AddListener(JoinLobby);

        backFromPlayButton.onClick.AddListener(OnPlayBackButton);
        backFromLobbyButton.onClick.AddListener(OnLobbyBackButton);

        startGameButton.onClick.AddListener(StartGame);

        if (steamLobby != null)
        {
            steamLobby.OnLobbyPlayerListChanged += UpdatePlayerList;
            steamLobby.OnLobbyEnteredEvent += OnLobbyEntered;
        }

        startGameButton.enabled = false;

        // Show only main menu at start
        ShowPanel(mainMenuPanel);

    }

    private void OnLobbyEntered()
    {
        ShowPanel(lobbyPanel);
        if(steamLobby.IsHost())
        {
            startGameButton.enabled = true;
        }
    }

    private void UpdatePlayerList(List<(string playerName, Sprite avatar)> players)
    {
        playerList.RefreshUI(players);
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
        playerList.ClearList();
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
        steamLobby.JoinFriendLobby();
    }

    private void StartGame()
    {
        Debug.Log("Start Game clicked!");
        steamLobby.OnLobbyPlayerListChanged -= UpdatePlayerList;
        steamLobby.OnLobbyEnteredEvent -= OnLobbyEntered;
        steamLobby.StartGame();
    }
}
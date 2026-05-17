using System;
using Netcode.Transports;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class NgoSessionCoordinator : ISessionBootstrap
{
    private readonly NetworkManager networkManager;
    private readonly MultiplayerSessionStateMachine stateMachine;
    private readonly SteamNetworkingSocketsTransport transport;

    public event Action<MultiplayerSessionState> SessionStateChanged;

    public NgoSessionCoordinator(NetworkManager networkManager, MultiplayerSessionStateMachine stateMachine)
    {
        this.networkManager = networkManager;
        this.stateMachine = stateMachine;
        transport = networkManager != null ? networkManager.GetComponent<SteamNetworkingSocketsTransport>() : null;
    }

    public bool StartHost()
    {
        if (networkManager == null)
        {
            return false;
        }

        bool started = networkManager.StartHost();
        if (started)
        {
            SetState(MultiplayerSessionState.InSession);
        }

        return started;
    }

    public bool StartClient(ulong hostSteamId)
    {
        if (networkManager == null || transport == null)
        {
            return false;
        }

        transport.ConnectToSteamID = hostSteamId;
        bool started = networkManager.StartClient();
        if (started)
        {
            SetState(MultiplayerSessionState.InSession);
        }

        return started;
    }

    public void ShutdownSession()
    {
        if (networkManager == null)
        {
            return;
        }

        networkManager.Shutdown();
        SetState(MultiplayerSessionState.Idle);
    }

    public void LoadGameplayScene(string sceneName)
    {
        if (networkManager == null || networkManager.SceneManager == null)
        {
            return;
        }

        networkManager.SceneManager.LoadScene(sceneName, LoadSceneMode.Single);
        SetState(MultiplayerSessionState.InSession);
    }

    private void SetState(MultiplayerSessionState state)
    {
        stateMachine.ForceState(state);
        SessionStateChanged?.Invoke(state);
    }
}

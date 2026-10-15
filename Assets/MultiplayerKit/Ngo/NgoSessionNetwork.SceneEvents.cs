// NgoSessionNetwork, part 3 of 3 (03's "NgoSceneEvents"): NGO scene events and transport failure.
// Design: 03 §11.1 step 4, §11.2 step 7, §11.4, §12, §16, 06 N4. Subscribed by part 1 after a successful start,
// because the NetworkManager creates its NetworkSceneManager only then. The subscribed instance is kept, so
// Shutdown unsubscribes from that exact object even after NGO has replaced or dropped it.

using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine.SceneManagement;

namespace MultiplayerKit.Ngo
{
    public sealed partial class NgoSessionNetwork
    {
        private NetworkSceneManager subscribedScenes;

        // Host: load completion and per-client sync logging. Client: its own sync completion. Both: transport failure.
        private void SubscribeSessionEvents(bool asHost)
        {
            nm.OnTransportFailure += OnTransportFailure;
            NetworkSceneManager scenes = nm.SceneManager;
            if (scenes == null)
            {
                KitLog.Error("NGO started without a SceneManager; scene management must be enabled (03 §9).");
                return;
            }
            subscribedScenes = scenes;
            if (asHost) scenes.OnLoadEventCompleted += OnLoadEventCompleted;
            scenes.OnSceneEvent += OnSceneEvent;
        }

        // Removing a handler that was never added is a no-op, so this is safe in every state.
        private void UnsubscribeSessionEvents()
        {
            nm.OnTransportFailure -= OnTransportFailure;
            NetworkSceneManager scenes = subscribedScenes;
            subscribedScenes = null;
            if (scenes == null) return;
            scenes.OnLoadEventCompleted -= OnLoadEventCompleted;
            scenes.OnSceneEvent -= OnSceneEvent;
        }

        // Host (03 §11.1 step 4, §12): the networked load finished on the host and on every client connected when it
        // started (none at the initial start). Clients that timed out turn it into the synthesized load failure.
        private void OnLoadEventCompleted(string sceneName, LoadSceneMode loadSceneMode,
                                          List<ulong> clientsCompleted, List<ulong> clientsTimedOut)
        {
            if (shuttingDown || Role != NetworkRole.Host) return;
            if (clientsTimedOut != null && clientsTimedOut.Count > 0)
            {
                KitLog.Warning("Loading scene '" + sceneName + "' timed out for " + clientsTimedOut.Count + " client(s).");
                Raise(NetworkSceneLoadFailed, sceneName, KitError.SceneLoadFailed, nameof(NetworkSceneLoadFailed));
                return;
            }
            KitLog.Info("Scene '" + sceneName + "' loaded on the host.");
            Raise(NetworkSceneLoaded, sceneName, nameof(NetworkSceneLoaded));
        }

        // Client (03 §11.2 step 7): its own initial synchronization finished and NGO has made the host's active scene
        // active here; the controller checks that it is the gameplay scene.
        // Host (03 §11.4): a client finished synchronizing. NGO raises OnClientConnectedCallback for it right after,
        // which is where the game sends its first messages; this line lets the integrator check that order.
        private void OnSceneEvent(SceneEvent sceneEvent)
        {
            if (shuttingDown || sceneEvent == null || sceneEvent.SceneEventType != SceneEventType.SynchronizeComplete) return;
            if (Role == NetworkRole.Client)
            {
                if (sceneEvent.ClientId != nm.LocalClientId) return;
                string active = SceneManager.GetActiveScene().name;
                KitLog.Info("Scene synchronization complete; active scene '" + active + "'.");
                Raise(NetworkSceneLoaded, active, nameof(NetworkSceneLoaded));
                return;
            }
            if (Role == NetworkRole.Host && sceneEvent.ClientId != NetworkManager.ServerClientId)
            {
                KitLog.Info("Client " + sceneEvent.ClientId + " finished scene synchronization.");
            }
        }

        // 03 §16, host or client: NGO shuts itself down right after raising this.
        private void OnTransportFailure()
        {
            if (shuttingDown || Role == NetworkRole.None) return;
            RaiseLocalDisconnected(KitError.ConnectionLost, "transport failure");
        }
    }
}

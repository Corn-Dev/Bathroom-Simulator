// ISessionNetwork over the kit-owned NGO 1.7.1 NetworkManager and the Steam transport (decision 9), part 1 of 3:
// start/stop, scene loads, the clientId <-> PlayerId maps and the NGO connection callbacks. Design: 01 §4.6,
// 03 §9-§16, 06 N2. Part 2 (.Approval, N3) owns the approval callback and pendingByClient; part 3 (.SceneEvents,
// N4) owns the NGO scene events and the transport-failure handler. MultiplayerKitBootstrap (N5) constructs this
// class and assigns the approval callback. KitError <-> reason text always goes through
// ConnectionPayloadCodec.EncodeReason / DecodeReason. Main thread only.

using System;
using System.Collections.Generic;
using MultiplayerKit.Steam;
using Netcode.Transports;
using Steamworks;
using Unity.Netcode;
using UnityEngine.SceneManagement;

namespace MultiplayerKit.Ngo
{
    public sealed partial class NgoSessionNetwork : ISessionNetwork
    {
        // "No payload". Empty rather than null, so NGO never serializes a null array (03 §16 clears it on Shutdown).
        private static readonly byte[] NoPayload = new byte[0];

        private readonly NetworkManager nm;
        private readonly SteamNetworkingSocketsTransport transport;

        // 03 §14, host only. The host's own ServerClientId is mapped at self-approval (N3 step 1); remote clients move
        // here from pendingByClient (N3) on OnClientConnectedCallback.
        private readonly Dictionary<ulong, PlayerId> playerByClient = new Dictionary<ulong, PlayerId>();
        private readonly Dictionary<PlayerId, ulong> clientByPlayer = new Dictionary<PlayerId, ulong>();

        private PlayerId hostPlayer;            // host: the local PlayerId, set in StartHost before NGO starts
        private bool shuttingDown;              // set by Shutdown, reset by the next start (03 §16)
        private bool localDisconnectRaised;     // LocalDisconnected is raised at most once per session

        public NgoSessionNetwork(NetworkManager nm, SteamNetworkingSocketsTransport transport)
        {
            if (nm == null) throw new ArgumentNullException(nameof(nm));
            if (transport == null) throw new ArgumentNullException(nameof(transport));
            this.nm = nm;
            this.transport = transport;
        }

        public NetworkRole Role { get; private set; }

        /// <summary>From a successful StartHost/StartClient until Shutdown.</summary>
        public bool IsRunning => Role != NetworkRole.None;

        /// <summary>The kit-owned NetworkManager, for the game adapter to cast.</summary>
        public object NativeHandle => nm;

        /// <summary>Host: nm.ConnectedClientsIds.Count (server-only in NGO), including the host's own client.</summary>
        public int ConnectedCount => Role == NetworkRole.Host && nm.IsServer ? nm.ConnectedClientsIds.Count : 0;

        public event Action<string> NetworkSceneLoaded;
        public event Action<string, KitError> NetworkSceneLoadFailed;
        public event Action<ulong, PlayerId> ClientConnected;
        public event Action<ulong, PlayerId> ClientDisconnected;
        public event Action<KitError, string> LocalDisconnected;

        /// <summary>03 §11.1 step 2. The Steam transport listens on the local SteamID and needs no host config.</summary>
        public KitResult StartHost()
        {
            KitResult ready = CheckCanStart();
            if (!ready.Ok) return ready;
            PlayerId local = SteamIds.ToPlayer(SteamUser.GetSteamID());
            if (!local.IsValid) return KitResult.Fail(KitError.PlatformUnavailable, "no local Steam id");

            BeginSession();
            hostPlayer = local;
            // 03 §10.1: NGO runs the approval callback for the host's own client too, with this payload. The
            // self-approval (N3 step 1) never judges it, so it carries only the protocol and the PlayerId.
            nm.NetworkConfig.ConnectionData = ConnectionPayloadCodec.Encode(new ConnectionPayload(KitVersion.Protocol, "", local, ""));
            if (!TryStartNgo(true))
            {
                Shutdown();
                return KitResult.Fail(KitError.NetworkStartFailed, "NetworkManager.StartHost returned false");
            }

            Role = NetworkRole.Host;
            Subscribe(true);
            KitLog.Info("NGO host started on the Steam transport as player " + local + ".");
            return KitResult.Success;
        }

        /// <summary>03 §11.2 step 5: ConnectToSteamID, then the encoded approval payload, then nm.StartClient.</summary>
        public KitResult StartClient(PlayerId host, ConnectionPayload payload)
        {
            KitResult ready = CheckCanStart();
            if (!ready.Ok) return ready;
            if (!host.IsValid) return KitResult.Fail(KitError.InvalidArgument, "invalid host id");
            byte[] data;
            try
            {
                data = ConnectionPayloadCodec.Encode(payload);
            }
            catch (ArgumentException e)
            {
                return KitResult.Fail(KitError.InvalidArgument, e.Message);
            }

            BeginSession();
            transport.ConnectToSteamID = host.Value;
            nm.NetworkConfig.ConnectionData = data;
            if (!TryStartNgo(false))
            {
                Shutdown();
                return KitResult.Fail(KitError.NetworkStartFailed, "NetworkManager.StartClient returned false");
            }

            Role = NetworkRole.Client;
            Subscribe(false);
            KitLog.Info("NGO client connecting to host " + host + " over the Steam transport.");
            return KitResult.Success;
        }

        /// <summary>
        /// 03 §16. Idempotent. Unsubscribes first, so the events NGO raises during its own shutdown never reach the
        /// controller and no LocalDisconnected is raised. The approval callback stays assigned (set once by N5).
        /// </summary>
        public void Shutdown()
        {
            shuttingDown = true;
            Unsubscribe();
            bool wasRunning = Role != NetworkRole.None;
            if (nm != null)
            {
                try
                {
                    nm.Shutdown();
                }
                catch (Exception e)
                {
                    KitLog.Error("NetworkManager.Shutdown threw.", e);
                }
                if (nm.NetworkConfig != null) nm.NetworkConfig.ConnectionData = NoPayload;
            }
            ClearMaps();
            hostPlayer = PlayerId.None;
            Role = NetworkRole.None;
            if (wasRunning) KitLog.Info("NGO session shut down.");
        }

        /// <summary>
        /// 03 §12, host only. A status other than Started is reported through the return value (the controller fails
        /// the session on it); NetworkSceneLoadFailed is reserved for the asynchronous outcome (N4).
        /// </summary>
        public KitResult LoadNetworkScene(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName)) return KitResult.Fail(KitError.SceneLoadFailed, "no scene name");
            if (Role != NetworkRole.Host || !nm.IsServer)
            {
                KitLog.Error("LoadNetworkScene called on a non-server; only the host should call this.");
                return KitResult.Fail(KitError.SceneLoadFailed, "not host");
            }
            NetworkSceneManager scenes = nm.SceneManager;
            if (scenes == null)
            {
                KitLog.Error("NetworkManager.SceneManager is null; scene management must be enabled on the NetworkManager.");
                return KitResult.Fail(KitError.SceneLoadFailed, "scene management off");
            }

            KitLog.Info("Loading scene '" + sceneName + "' via the NGO SceneManager.");
            SceneEventProgressStatus status;
            try
            {
                status = scenes.LoadScene(sceneName, LoadSceneMode.Single);
            }
            catch (Exception e)
            {
                KitLog.Error("NetworkSceneManager.LoadScene threw for '" + sceneName + "'.", e);
                return KitResult.Fail(KitError.SceneLoadFailed, e.GetType().Name);
            }
            if (status != SceneEventProgressStatus.Started)
            {
                KitLog.Error("SceneManager.LoadScene returned " + status + ". Check that '" + sceneName
                    + "' is in Build Settings and that scene management is enabled.");
                return KitResult.Fail(KitError.SceneLoadFailed, status.ToString());
            }
            return KitResult.Success;
        }

        /// <summary>02 §6b.3: a no-op when the scene is already active, else a non-networked Single load.</summary>
        public void LoadLocalScene(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName))
            {
                KitLog.Warning("LoadLocalScene called without a scene name; ignored.");
                return;
            }
            if (string.Equals(SceneManager.GetActiveScene().name, sceneName, StringComparison.Ordinal))
            {
                KitLog.Debug("Scene '" + sceneName + "' is already active; not reloading it.");
                return;
            }
            KitLog.Info("Loading scene '" + sceneName + "' locally.");
            SceneManager.LoadScene(sceneName, LoadSceneMode.Single);
        }

        /// <summary>
        /// Host only; clients return false. Connected clients first, then approved clients that are still
        /// synchronizing, so a lookup from a raw NGO connect callback succeeds whatever the subscriber order.
        /// </summary>
        public bool TryGetPlayer(ulong clientId, out PlayerId player)
        {
            player = PlayerId.None;
            if (Role != NetworkRole.Host) return false;
            return playerByClient.TryGetValue(clientId, out player) || pendingByClient.TryGetValue(clientId, out player);
        }

        /// <summary>Host only; connected clients first, then approved clients that are still synchronizing.</summary>
        public bool TryGetClientId(PlayerId player, out ulong clientId)
        {
            clientId = 0;
            if (Role != NetworkRole.Host || !player.IsValid) return false;
            if (clientByPlayer.TryGetValue(player, out clientId)) return true;
            foreach (KeyValuePair<ulong, PlayerId> entry in pendingByClient)
            {
                if (entry.Value != player) continue;
                clientId = entry.Key;
                return true;
            }
            clientId = 0;
            return false;
        }

        /// <summary>03 §13 step 3: host only, never the host itself; no-op (Debug) if the player is not connected.</summary>
        public void DisconnectClient(PlayerId player, KitError reason)
        {
            if (Role != NetworkRole.Host || shuttingDown || !nm.IsServer || nm.ShutdownInProgress) return;
            if (!TryGetClientId(player, out ulong clientId) || clientId == NetworkManager.ServerClientId)
            {
                KitLog.Debug("DisconnectClient: player " + player + " is not connected.");
                return;
            }
            KitLog.Info("Disconnecting client " + clientId + " (player " + player + "): " + reason + ".");
            try
            {
                nm.DisconnectClient(clientId, ConnectionPayloadCodec.EncodeReason(reason));
            }
            catch (Exception e)
            {
                KitLog.Error("NetworkManager.DisconnectClient threw for client " + clientId + ".", e);
            }
        }

        // 03 §14: a remote client finished approval and scene sync. The host's own client is mapped at approval and
        // raises nothing (the adapter knows the host).
        private void OnClientConnected(ulong clientId)
        {
            if (shuttingDown || Role != NetworkRole.Host || clientId == NetworkManager.ServerClientId) return;
            if (!pendingByClient.TryGetValue(clientId, out PlayerId player))
            {
                if (playerByClient.ContainsKey(clientId)) return;   // repeated callback
                KitLog.Warning("NGO client " + clientId + " connected without a kit approval; disconnecting it.");
                try
                {
                    nm.DisconnectClient(clientId, ConnectionPayloadCodec.EncodeReason(KitError.ApprovalRejected));
                }
                catch (Exception e)
                {
                    KitLog.Error("NetworkManager.DisconnectClient threw for client " + clientId + ".", e);
                }
                return;
            }

            pendingByClient.Remove(clientId);
            playerByClient[clientId] = player;
            clientByPlayer[player] = clientId;
            KitLog.Info("Client " + clientId + " connected as player " + player + ".");
            Raise(ClientConnected, clientId, player, nameof(ClientConnected));
        }

        // Host: remove the mapping and raise ClientDisconnected (not an error). Client: the local connection ended,
        // either rejected at approval or disconnected later; the reason text is the decimal KitError (03 §10.3).
        private void OnClientDisconnected(ulong clientId)
        {
            if (shuttingDown) return;
            if (Role == NetworkRole.Client)
            {
                if (clientId != nm.LocalClientId && clientId != NetworkManager.ServerClientId) return;
                string raw = nm.DisconnectReason ?? "";
                RaiseLocalDisconnected(ConnectionPayloadCodec.DecodeReason(raw), raw);
                return;
            }
            if (Role != NetworkRole.Host || clientId == NetworkManager.ServerClientId) return;

            if (playerByClient.TryGetValue(clientId, out PlayerId player))
            {
                playerByClient.Remove(clientId);
                if (clientByPlayer.TryGetValue(player, out ulong mapped) && mapped == clientId) clientByPlayer.Remove(player);
                KitLog.Info("Client " + clientId + " (player " + player + ") disconnected.");
                Raise(ClientDisconnected, clientId, player, nameof(ClientDisconnected));
                return;
            }
            if (pendingByClient.Remove(clientId)) KitLog.Debug("Approved client " + clientId + " left before it finished connecting.");
        }

        // Shared by the disconnect callback and the transport-failure handler (N4).
        private void RaiseLocalDisconnected(KitError reason, string rawReason)
        {
            if (shuttingDown || localDisconnectRaised) return;
            localDisconnectRaised = true;
            KitLog.Info("NGO connection ended: " + reason + (string.IsNullOrEmpty(rawReason) ? "" : " ('" + KitLog.Truncate(rawReason) + "')") + ".");
            Raise(LocalDisconnected, reason, rawReason ?? "", nameof(LocalDisconnected));
        }

        private KitResult CheckCanStart()
        {
            if (nm == null) return KitResult.Fail(KitError.NetworkStartFailed, "the NetworkManager was destroyed");
            if (Role != NetworkRole.None || nm.IsListening || nm.ShutdownInProgress) return KitResult.Fail(KitError.NetworkStartFailed, "busy");
            if (!SteamPlatform.IsInitialized) return KitResult.Fail(KitError.PlatformUnavailable, "Steam is not initialized");
            return KitResult.Success;
        }

        private bool TryStartNgo(bool asHost)
        {
            try
            {
                return asHost ? nm.StartHost() : nm.StartClient();
            }
            catch (Exception e)
            {
                KitLog.Error("NetworkManager." + (asHost ? "StartHost" : "StartClient") + " threw.", e);
                return false;
            }
        }

        private void BeginSession()
        {
            shuttingDown = false;
            localDisconnectRaised = false;
            hostPlayer = PlayerId.None;
            ClearMaps();
        }

        private void ClearMaps()
        {
            playerByClient.Clear();
            clientByPlayer.Clear();
            pendingByClient.Clear();
        }

        // After a successful start (03 §11.1 step 2, §11.2 step 5). The connect callback is host-only.
        private void Subscribe(bool asHost)
        {
            if (asHost) nm.OnClientConnectedCallback += OnClientConnected;
            nm.OnClientDisconnectCallback += OnClientDisconnected;
            SubscribeSessionEvents(asHost);
        }

        // Removing a handler that was never added is a no-op, so this is safe in every state.
        private void Unsubscribe()
        {
            nm.OnClientConnectedCallback -= OnClientConnected;
            nm.OnClientDisconnectCallback -= OnClientDisconnected;
            UnsubscribeSessionEvents();
        }

        // One failing subscriber must neither keep the others from the event nor unwind into NGO.

        private static void Raise<T>(Action<T> handlers, T arg, string eventName)
        {
            if (handlers == null) return;
            foreach (Delegate handler in handlers.GetInvocationList())
            {
                try
                {
                    ((Action<T>)handler)(arg);
                }
                catch (Exception e)
                {
                    KitLog.Error("A handler of NgoSessionNetwork." + eventName + " threw.", e);
                }
            }
        }

        private static void Raise<T1, T2>(Action<T1, T2> handlers, T1 first, T2 second, string eventName)
        {
            if (handlers == null) return;
            foreach (Delegate handler in handlers.GetInvocationList())
            {
                try
                {
                    ((Action<T1, T2>)handler)(first, second);
                }
                catch (Exception e)
                {
                    KitLog.Error("A handler of NgoSessionNetwork." + eventName + " threw.", e);
                }
            }
        }
    }
}

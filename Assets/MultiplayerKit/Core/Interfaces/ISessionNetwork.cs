// The NGO session expressed without Netcode types: start/stop, scene loads, connection approval, the
// NGO clientId <-> PlayerId map and kick. Design: 01 §4.6; NGO implementation 03 §9-§13 (NgoSessionNetwork).
// The NetworkManager crosses into Core only as object (NativeHandle), for the game adapter to cast.
// Byte layout of ConnectionPayload is owned by ConnectionPayloadCodec (Core/Protocol).

using System;

namespace MultiplayerKit
{
    /// <summary>Approval payload a client sends with its NGO connection request (decision 5).</summary>
    public readonly struct ConnectionPayload
    {
        /// <summary>KitVersion.Protocol of the sender.</summary>
        public readonly int KitProtocol;

        /// <summary>adapter.GameVersion, max 32 chars. Never null.</summary>
        public readonly string GameVersion;

        public readonly PlayerId Player;

        /// <summary>Lowercase hex SHA-256 (PasswordHasher.Hash), "" when the lobby has no password. Never null.</summary>
        public readonly string PasswordHash;

        public ConnectionPayload(int kitProtocol, string gameVersion, PlayerId player, string passwordHash)
        {
            KitProtocol = kitProtocol;
            GameVersion = gameVersion ?? "";
            Player = player;
            PasswordHash = passwordHash ?? "";
        }
    }

    /// <summary>One NGO connection request, as seen by SessionController's approval rules (01 §6.3).</summary>
    public readonly struct ApprovalRequest
    {
        /// <summary>NGO client id.</summary>
        public readonly ulong ClientId;

        /// <summary>False if decoding the payload failed; Payload is then default.</summary>
        public readonly bool PayloadValid;

        public readonly ConnectionPayload Payload;

        public ApprovalRequest(ulong clientId, bool payloadValid, ConnectionPayload payload)
        {
            ClientId = clientId;
            PayloadValid = payloadValid;
            Payload = payload;
        }
    }

    /// <summary>Approval outcome. default(ApprovalDecision) is a rejection (fail closed).</summary>
    public readonly struct ApprovalDecision
    {
        public readonly bool Approved;

        /// <summary>Sent to the client as its disconnect reason (decimal KitError value). None when approved.</summary>
        public readonly KitError Reason;

        private ApprovalDecision(bool approved, KitError reason)
        {
            Approved = approved;
            Reason = reason;
        }

        public static ApprovalDecision Accept()
        {
            return new ApprovalDecision(true, KitError.None);
        }

        /// <summary>KitError.None is sent as ApprovalRejected so the client always gets a real reason.</summary>
        public static ApprovalDecision Reject(KitError reason)
        {
            return new ApprovalDecision(false, reason == KitError.None ? KitError.ApprovalRejected : reason);
        }
    }

    public enum NetworkRole { None, Host, Client }

    public interface ISessionNetwork
    {
        NetworkRole Role { get; }
        bool IsRunning { get; }

        /// <summary>The kit-owned NGO NetworkManager, as object; for the game adapter only.</summary>
        object NativeHandle { get; }

        /// <summary>Host: connected NGO clients including the host's own server client; 0 when not running.</summary>
        int ConnectedCount { get; }

        /// <summary>Host: approved but not yet connected (03 §10.2).</summary>
        int PendingApprovedCount { get; }

        /// <summary>
        /// Host: SessionController installs its approval rules before StartHost and keeps them for the session.
        /// With no handler installed every remote request is rejected (fail closed).
        /// </summary>
        Func<ApprovalRequest, ApprovalDecision> ApprovalHandler { set; }

        KitResult StartHost();
        KitResult StartClient(PlayerId host, ConnectionPayload payload);

        /// <summary>Idempotent; raises no LocalDisconnected.</summary>
        void Shutdown();

        /// <summary>Host only; NGO SceneManager, single (replacing) load.</summary>
        KitResult LoadNetworkScene(string sceneName);

        /// <summary>Non-networked return to the menu, after Shutdown.</summary>
        void LoadLocalScene(string sceneName);

        /// <summary>From the approval payload.</summary>
        bool TryGetPlayer(ulong clientId, out PlayerId player);

        bool TryGetClientId(PlayerId player, out ulong clientId);

        /// <summary>Host only; no-op if the player is not connected.</summary>
        void DisconnectClient(PlayerId player, KitError reason);

        /// <summary>Host: load completed for all clients. Client: own synchronization complete.</summary>
        event Action<string> NetworkSceneLoaded;

        event Action<string, KitError> NetworkSceneLoadFailed;

        /// <summary>Host only, after approval: NGO client id and its PlayerId.</summary>
        event Action<ulong, PlayerId> ClientConnected;

        /// <summary>Host only.</summary>
        event Action<ulong, PlayerId> ClientDisconnected;

        /// <summary>Client: lost the host or was rejected (reason decoded), with the raw reason text. Host: transport failure.</summary>
        event Action<KitError, string> LocalDisconnected;
    }
}

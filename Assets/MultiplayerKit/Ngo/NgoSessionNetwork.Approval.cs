// NgoSessionNetwork, part 2 of 3: NGO connection approval, the authoritative gate of decisions 5 and 6.
// Design: 03 §10.2 (order), §10.1 (host payload), §14 (pendingByClient), 01 §4.6 / §6.3, 06 N3.
// Order: 1 host self-approval, 2 decode, 3 duplicate PlayerId, 4 SessionController's rules through ApprovalHandler
// (no handler: reject, fail closed), 5 fill the response, 6 record the approved client. Any exception rejects.
// Rejection text is ConnectionPayloadCodec.EncodeReason; None (a default ApprovalDecision) is sent as ApprovalRejected.

using System;
using System.Collections.Generic;
using Unity.Netcode;

namespace MultiplayerKit.Ngo
{
    public sealed partial class NgoSessionNetwork
    {
        // Approved remote clients that have not reached OnClientConnectedCallback yet (03 §10.2 step 6). Moved into
        // the live maps by OnClientConnected, dropped by OnClientDisconnected (part 1).
        private readonly Dictionary<ulong, PlayerId> pendingByClient = new Dictionary<ulong, PlayerId>();

        private Func<ApprovalRequest, ApprovalDecision> approvalHandler;

        /// <summary>SessionController's rules (01 §6.3), installed before StartHost. Null rejects every remote client.</summary>
        public Func<ApprovalRequest, ApprovalDecision> ApprovalHandler
        {
            set { approvalHandler = value; }
        }

        /// <summary>
        /// Host: approved clients that ConnectedCount does not include yet. NGO 1.7.1 adds an approved client to
        /// ConnectedClientsIds when it processes the approval, long before OnClientConnectedCallback (which waits for
        /// scene sync), so only pending entries NGO does not list yet are counted. ConnectedCount + PendingApprovedCount
        /// therefore counts every approved client exactly once (the LobbyFull rule).
        /// </summary>
        public int PendingApprovedCount
        {
            get
            {
                if (Role != NetworkRole.Host || pendingByClient.Count == 0) return 0;
                if (!nm.IsServer) return pendingByClient.Count;
                IReadOnlyList<ulong> listed = nm.ConnectedClientsIds;
                int count = 0;
                foreach (ulong clientId in pendingByClient.Keys)
                {
                    if (!Contains(listed, clientId)) count++;
                }
                return count;
            }
        }

        /// <summary>
        /// The NetworkManager's approval callback, assigned once by MultiplayerKitBootstrap.Start right after this
        /// object is constructed. Synchronous: the response is never Pending (03 §10.2).
        /// </summary>
        internal void OnApprovalRequested(NetworkManager.ConnectionApprovalRequest request,
                                          NetworkManager.ConnectionApprovalResponse response)
        {
            ulong clientId = request.ClientNetworkId;
            PlayerId claimed = PlayerId.None;
            try
            {
                // 1. The host's own client, approved by NGO inside StartHost: no payload is judged.
                if (clientId == NetworkManager.ServerClientId)
                {
                    ApproveHost(response);
                    return;
                }

                // 2. Decode. An invalid payload still goes to the handler, whose first rule rejects it.
                bool payloadValid = ConnectionPayloadCodec.TryDecode(request.Payload, out ConnectionPayload payload);
                if (payloadValid) claimed = payload.Player;

                // 3. The same account twice, or a spoof of a member that is already connected or approved.
                if (payloadValid && IsPlayerTaken(payload.Player, clientId))
                {
                    Reject(response, KitError.ApprovalRejected);
                    LogRejected(clientId, claimed, KitError.ApprovalRejected, "duplicate PlayerId");
                    return;
                }

                // 4. SessionController's rules. No handler, or a session that is ending: fail closed.
                Func<ApprovalRequest, ApprovalDecision> handler = approvalHandler;
                if (handler == null || shuttingDown || Role != NetworkRole.Host)
                {
                    Reject(response, KitError.ApprovalRejected);
                    LogRejected(clientId, claimed, KitError.ApprovalRejected, "no approval handler");
                    return;
                }
                ApprovalDecision decision = handler(new ApprovalRequest(clientId, payloadValid, payload));

                // 5. Fill the response. The controller already logged its own rejections at Info.
                if (!decision.Approved)
                {
                    Reject(response, decision.Reason);
                    KitLog.Debug("Approval handler rejected client " + clientId + ": " + decision.Reason + ".");
                    return;
                }
                if (!payloadValid || !payload.Player.IsValid)
                {
                    Reject(response, KitError.ApprovalRejected);
                    LogRejected(clientId, claimed, KitError.ApprovalRejected, "approved without a valid PlayerId");
                    return;
                }
                response.Approved = true;
                response.CreatePlayerObject = nm.NetworkConfig.PlayerPrefab != null;
                response.Pending = false;

                // 6. Until OnClientConnectedCallback moves it into the live maps.
                pendingByClient[clientId] = payload.Player;
                KitLog.Debug("Approved client " + clientId + " as player " + payload.Player + ".");
            }
            catch (Exception e)
            {
                KitLog.Error("Connection approval threw for client " + clientId + "; rejecting it.", e);
                Reject(response, KitError.ApprovalRejected);
            }
        }

        // Step 1: approve and map ServerClientId to the local player StartHost recorded. Not mapped when NGO was
        // started as host outside StartHost (no recorded player).
        private void ApproveHost(NetworkManager.ConnectionApprovalResponse response)
        {
            response.Approved = true;
            response.CreatePlayerObject = nm.NetworkConfig.PlayerPrefab != null;
            response.Pending = false;
            if (!hostPlayer.IsValid) return;
            playerByClient[NetworkManager.ServerClientId] = hostPlayer;
            clientByPlayer[hostPlayer] = NetworkManager.ServerClientId;
        }

        // Step 3: player is mapped to a different client, connected (including the host) or approved and syncing.
        private bool IsPlayerTaken(PlayerId player, ulong requestingClient)
        {
            if (!player.IsValid) return false;
            if (clientByPlayer.TryGetValue(player, out ulong connected) && connected != requestingClient) return true;
            foreach (KeyValuePair<ulong, PlayerId> entry in pendingByClient)
            {
                if (entry.Key != requestingClient && entry.Value == player) return true;
            }
            return false;
        }

        private static void Reject(NetworkManager.ConnectionApprovalResponse response, KitError reason)
        {
            response.Approved = false;
            response.CreatePlayerObject = false;
            response.Pending = false;
            response.Reason = ConnectionPayloadCodec.EncodeReason(reason == KitError.None ? KitError.ApprovalRejected : reason);
        }

        // 03 §10.2 step 6 log line, for the rejections this class decides itself.
        private static void LogRejected(ulong clientId, PlayerId player, KitError reason, string why)
        {
            KitLog.Info("approval rejected client " + clientId + " player " + player + ": " + reason + " (" + why + ")");
        }

        private static bool Contains(IReadOnlyList<ulong> list, ulong value)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] == value) return true;
            }
            return false;
        }
    }
}

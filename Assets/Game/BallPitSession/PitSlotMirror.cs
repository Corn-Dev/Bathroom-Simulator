// COUPLED RULE: mirrors NetPit.OnClientConnected / NetPit.OnClientDisconnected (NetPit.cs, not copied, decision 10):
// a remote client gets the lowest player slot 1..3 not used by a tracked client, keyed by NGO clientId; no free slot
// means NetPit disconnects it. If NetPit's slot rule ever changes, change this class with it (HANDOFF.md risk list).
// A wrong mirror can only mis-assign tank ownership, never break the ledger or replication. Host only.
// Design: 04 §5.3, 02 §4 step 10; 06 G6. Subscribed right after NetPit.StartHost, so NGO runs NetPit's handler first.

using System;
using System.Collections.Generic;
using BallPitFinal;
using MultiplayerKit;
using Unity.Netcode;
using UnityEngine;

namespace BallPitSession
{
    public sealed class PitSlotMirror
    {
        private readonly HopperOwnership ownership;
        private IHopperOps ops; // not readonly: RebindOps swaps it after an in-session pit rebuild
        private readonly ISessionNetwork network;      // clientId -> PlayerId from the approval payload
        private readonly ulong localClientId;          // the host's own NGO client, never given a slot
        private readonly Dictionary<ulong, int> slotOf = new Dictionary<ulong, int>();
        private NetworkManager nm;

        public PitSlotMirror(HopperOwnership ownership, IHopperOps ops, ISessionNetwork network, ulong localClientId)
        {
            this.ownership = ownership ?? throw new ArgumentNullException(nameof(ownership));
            this.ops = ops ?? throw new ArgumentNullException(nameof(ops));
            this.network = network;
            this.localClientId = localClientId;
        }

        /// <summary>Clients currently holding a slot.</summary>
        public int TrackedCount => slotOf.Count;

        public bool TryGetSlot(ulong clientId, out int slot) => slotOf.TryGetValue(clientId, out slot);

        /// <summary>Deliverable D: re-point at the new authority's hopper ops after an in-session pit rebuild. Null is ignored.</summary>
        public void RebindOps(IHopperOps newOps) { if (newOps != null) ops = newOps; }

        /// <summary>Subscribes to the same two NGO callbacks NetPit uses. Call right after NetPit.StartHost.</summary>
        public void Attach(NetworkManager networkManager)
        {
            if (nm != null || networkManager == null) return;
            nm = networkManager;
            nm.OnClientConnectedCallback += HandleClientConnected;
            nm.OnClientDisconnectCallback += HandleClientDisconnected;
        }

        /// <summary>Unsubscribes (session end, while the NetworkManager is still valid) and forgets every slot.</summary>
        public void Detach()
        {
            if (nm != null)
            {
                nm.OnClientConnectedCallback -= HandleClientConnected;
                nm.OnClientDisconnectCallback -= HandleClientDisconnected;
                nm = null;
            }
            slotOf.Clear();
        }

        /// <summary>NetPit's rule: ignore the host and known clients; lowest free slot 1..3; none free = not tracked.</summary>
        public void HandleClientConnected(ulong clientId)
        {
            if (clientId == localClientId || slotOf.ContainsKey(clientId)) return;
            int slot = -1;
            for (int p = 1; p < PitAuthority.MaxPlayers && slot < 0; p++)
            {
                bool used = false;
                foreach (int s in slotOf.Values) if (s == p) { used = true; break; }
                if (!used) slot = p;
            }
            if (slot < 0) return; // the pit is full: NetPit disconnects this client

            slotOf[clientId] = slot;
            PlayerId player = PlayerId.None;
            if (network == null || !network.TryGetPlayer(clientId, out player) || !player.IsValid)
            {
                Debug.LogWarning($"[BallPitSession] no PlayerId for client {clientId}; slot {slot} gets an empty tank");
                player = PlayerId.None;
            }
            ownership.OnSlotJoined(slot, player, ops);
        }

        /// <summary>Frees the client's slot and parks its tank under its player.</summary>
        public void HandleClientDisconnected(ulong clientId)
        {
            if (!slotOf.TryGetValue(clientId, out int slot)) return;
            slotOf.Remove(clientId);
            ownership.OnSlotLeft(slot, DateTime.UtcNow.Ticks, ops);
        }
    }
}

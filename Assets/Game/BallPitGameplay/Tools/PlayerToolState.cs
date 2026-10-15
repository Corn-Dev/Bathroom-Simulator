// PlayerToolState: a small per-player NetworkBehaviour on the player prefab that replicates the carried stack count and
// the active tool, so remotes see what each player holds (brief §3, §4.4). Owner-written NetworkVariables — NetPit is
// NOT used for this. The HUD and view read CarriedCount / CurrentTool. Added to Player.prefab by the owner.
using Unity.Netcode;
using UnityEngine;

namespace BallPitGameplay
{
    [DisallowMultipleComponent]
    public sealed class PlayerToolState : NetworkBehaviour
    {
        private readonly NetworkVariable<int> carriedCount = new NetworkVariable<int>(0,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        private readonly NetworkVariable<byte> currentTool = new NetworkVariable<byte>(0,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        /// <summary>Balls the player is carrying (held + pocketed), replicated.</summary>
        public int CarriedCount => carriedCount.Value;
        /// <summary>The active tool slot (0 = hands), replicated.</summary>
        public byte CurrentTool => currentTool.Value;

        /// <summary>Raised on every peer when either value changes.</summary>
        public event System.Action Changed;

        public override void OnNetworkSpawn()
        {
            carriedCount.OnValueChanged += OnIntChanged;
            currentTool.OnValueChanged += OnByteChanged;
        }

        public override void OnNetworkDespawn()
        {
            carriedCount.OnValueChanged -= OnIntChanged;
            currentTool.OnValueChanged -= OnByteChanged;
        }

        /// <summary>Owner only: write the carried stack count.</summary>
        public void SetCarried(int count)
        {
            if (!IsOwner) return;
            int v = Mathf.Max(0, count);
            if (carriedCount.Value != v) carriedCount.Value = v;
        }

        /// <summary>Owner only: write the active tool slot.</summary>
        public void SetTool(byte tool)
        {
            if (!IsOwner) return;
            if (currentTool.Value != tool) currentTool.Value = tool;
        }

        private void OnIntChanged(int previous, int current) => Changed?.Invoke();
        private void OnByteChanged(byte previous, byte current) => Changed?.Invoke();
    }
}

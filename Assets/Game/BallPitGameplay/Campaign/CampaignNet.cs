// CampaignNet: the thin NGO object that replicates WHICH campaign entry is live (Deliverable D; DESIGN-D §5, plan item 6).
// Modelled on GameplayNet: host-spawned, one NetworkVariable (Everyone read / Server write), a Unity-fake-null-safe static
// Instance. RPC-free. Unlike GameplayNet it is spawned ONCE per session by CampaignDirector (not per world), so a late joiner
// receives the current index from NGO's spawn sync before the kit's post-sync OnSessionLive. Registered in the kit
// NetworkPrefabsList as BallPitCampaignNet.prefab (created by Tools/Ball Pit/Create Campaign Net Prefab).
using Unity.Netcode;

namespace BallPitGameplay
{
    public sealed class CampaignNet : NetworkBehaviour
    {
        private readonly NetworkVariable<int> entryIndex = new NetworkVariable<int>(0,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private static CampaignNet instance;

        /// <summary>The live instance while a networked campaign session runs; null otherwise. Unity fake-null safe.</summary>
        public static CampaignNet Instance => instance != null ? instance : null;

        /// <summary>The replicated campaign entry index. Only meaningful after spawn (null Instance until then).</summary>
        public int Index => entryIndex.Value;

        public override void OnNetworkSpawn()
        {
            instance = this;
            entryIndex.OnValueChanged += OnChanged;
            if (IsServer)
            {
                CampaignDirector d = CampaignDirector.Instance;
                entryIndex.Value = d != null ? d.CurrentIndex : 0;
            }
            CampaignDirector.Instance?.AttachNet(this);
        }

        public override void OnNetworkDespawn()
        {
            entryIndex.OnValueChanged -= OnChanged;
            CampaignDirector.Instance?.DetachNet(this);
            if (instance == this) instance = null;
        }

        /// <summary>Host only: publish the live entry index.</summary>
        public void SetIndex(int index)
        {
            if (IsServer && IsSpawned) entryIndex.Value = index;
        }

        private void OnChanged(int previous, int current) => CampaignDirector.Instance?.OnReplicatedIndexChanged(previous, current);
    }
}

// CampaignSaveParticipant: persists which campaign entry the run is on (Deliverable D; plan item 7). Pattern =
// EconomySaveParticipant, but it is registered ONCE per session by CampaignDirector.EnsureFirstPit (not per world), so the
// blob is handed over by the first build's restore. The siteId in the save's FILL already selects the right pit prefab; this
// blob lets the director reconcile its index. A corrupt blob is a Warning: the siteId-derived index stands.
using System.IO;
using BallPitSession;

namespace BallPitGameplay
{
    public sealed class CampaignSaveParticipant : IBallPitSaveParticipant
    {
        private const byte Version = 1;

        private readonly CampaignDirector director;

        public CampaignSaveParticipant(CampaignDirector director) { this.director = director; }

        public string Tag => "campaign";

        public byte[] Capture()
        {
            if (director == null) return System.Array.Empty<byte>();
            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms);
            w.Write(Version);
            w.Write(director.CurrentIndex);
            return ms.ToArray();
        }

        public void Restore(byte[] data)
        {
            if (director == null || data == null || data.Length == 0) return;
            int index;
            try
            {
                using var ms = new MemoryStream(data);
                using var r = new BinaryReader(ms);
                byte version = r.ReadByte();
                if (version < 1) return;
                index = r.ReadInt32();
            }
            catch (System.Exception e)
            {
                UnityEngine.Debug.LogWarning("[CampaignSaveParticipant] restore failed; keeping the siteId-derived campaign index: " + e.Message);
                return;
            }
            director.OnIndexRestored(index);
        }
    }
}

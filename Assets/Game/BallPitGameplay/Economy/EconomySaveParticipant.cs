// EconomySaveParticipant: persists the economy's integrity flags in the world save (brief §4.3; design D3). Money and
// upgrade LEVELS already persist via adapter.GameState (MoneyPot + Upgrades), so this blob only carries the extra
// counters the later achievements framework reads: devToolsUsed, peakPlayers, upgradeLevelsBought, rejectsTotal, and
// the site-completed flag. Pattern = HopperOwnership: an IBallPitSaveParticipant with a stable tag; Capture/Restore run
// host-side between steps / before NetPit.StartHost. A compact, versioned byte[] so adding a field never breaks old saves.
using System.IO;
using BallPitSession;

namespace BallPitGameplay
{
    public sealed class EconomySaveParticipant : IBallPitSaveParticipant
    {
        private const byte Version = 1;

        private readonly GameplayCore core;

        public EconomySaveParticipant(GameplayCore core) { this.core = core; }

        public string Tag => "economy";

        public byte[] Capture()
        {
            if (core == null) return System.Array.Empty<byte>();
            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms);
            w.Write(Version);
            w.Write(core.DevToolsUsed);
            w.Write(core.PeakPlayers);
            w.Write(core.UpgradeLevelsBought);
            w.Write(core.RejectsTotal);
            w.Write(core.SiteCompleted);
            return ms.ToArray();
        }

        public void Restore(byte[] data)
        {
            if (core == null || data == null || data.Length == 0) return;
            try
            {
                using var ms = new MemoryStream(data);
                using var r = new BinaryReader(ms);
                byte version = r.ReadByte();
                if (version < 1) return;
                core.DevToolsUsed = r.ReadBoolean();
                core.PeakPlayers = r.ReadInt32();
                core.UpgradeLevelsBought = r.ReadInt32();
                core.RejectsTotal = r.ReadInt32();
                core.SiteCompleted = r.ReadBoolean();
            }
            catch (System.Exception e)
            {
                UnityEngine.Debug.LogWarning("[EconomySaveParticipant] restore failed; integrity flags reset: " + e.Message);
            }
        }
    }
}

// Gameplay state of the world save (section GAME) and the hook future systems use to store their own data in it.
// Design: 04 §3.2 GAME, §4.4, §6.3 step 6; 06 G11. The money pot, purchased upgrades and container contents have no
// gameplay system yet: these DTOs and IBallPitSaveParticipant are the delivery's only placeholders (06 §1). Host
// authoritative; replicating them to clients is the future systems' own concern, not NetPit's.

using System.Collections.Generic;

namespace BallPitSession
{
    /// <summary>A purchased upgrade. Id ^[a-z0-9_.]{1,64}$, unique in the list; level >= 0.</summary>
    public readonly struct UpgradeEntry
    {
        public readonly string Id;
        public readonly int Level;

        public UpgradeEntry(string id, int level)
        {
            Id = id;
            Level = level;
        }
    }

    /// <summary>
    /// The balls one sorting container holds. Id ^[a-z0-9_.]{1,64}$, unique in the list; color below the pit's color
    /// count; stored >= 0.
    /// </summary>
    public readonly struct ContainerEntry
    {
        public readonly string Id;
        public readonly byte Color;
        public readonly int Stored;

        public ContainerEntry(string id, byte color, int stored)
        {
            Id = id;
            Color = color;
            Stored = stored;
        }
    }

    /// <summary>
    /// Host-authoritative gameplay state, owned by BallPitGameAdapter (GameState). Future money, shop and container
    /// systems read and write it directly; anything else plugs in through IBallPitSaveParticipant. A save must satisfy
    /// the 04 §3.2 GAME rules (limits below, MoneyPot >= 0, ids and tags by IsValidKey, unique per list); the writer
    /// drops entries that break them (logged at Error) so a save always loads.
    /// </summary>
    public sealed class BallPitGameState
    {
        public const int MaxUpgrades = 1024, MaxContainers = 256, MaxExtensions = 64;
        public const int MaxExtensionLength = 16777216; // bytes per extension blob
        public const int MaxIdLength = 64, MaxTagLength = 32;

        public long MoneyPot;
        public readonly List<UpgradeEntry> Upgrades = new List<UpgradeEntry>();
        public readonly List<ContainerEntry> Containers = new List<ContainerEntry>();

        /// <summary>
        /// GAME blobs with no registered participant, or whose participant's Restore threw. They are written back
        /// verbatim on the next save, so loading a save in a build that lacks a system does not delete its data.
        /// </summary>
        public readonly List<(string Tag, byte[] Data)> UnclaimedExtensions = new List<(string Tag, byte[] Data)>();

        /// <summary>Deep copy: the entries are immutable values, blob bytes are copied.</summary>
        public BallPitGameState Clone()
        {
            var copy = new BallPitGameState { MoneyPot = MoneyPot };
            copy.Upgrades.AddRange(Upgrades);
            copy.Containers.AddRange(Containers);
            for (int i = 0; i < UnclaimedExtensions.Count; i++)
            {
                (string tag, byte[] data) = UnclaimedExtensions[i];
                copy.UnclaimedExtensions.Add((tag, data == null ? null : (byte[])data.Clone()));
            }
            return copy;
        }

        /// <summary>The id and tag rule: 1..maxLength characters of [a-z0-9_.] (MaxIdLength or MaxTagLength).</summary>
        public static bool IsValidKey(string key, int maxLength)
        {
            if (string.IsNullOrEmpty(key) || key.Length > maxLength) return false;
            for (int i = 0; i < key.Length; i++)
                if (!IsKeyChar(key[i])) return false;
            return true;
        }

        public static bool IsKeyChar(char c) => (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_' || c == '.';
    }

    /// <summary>
    /// A future system (shop, containers, quests) that stores its own blob in the world save. Register it with
    /// BallPitGameAdapter.RegisterSaveParticipant before the session goes live, so the restore can hand it its data.
    /// </summary>
    public interface IBallPitSaveParticipant
    {
        /// <summary>^[a-z0-9_.]{1,32}$, unique among participants. Keep it stable: saved blobs are matched by tag.</summary>
        string Tag { get; }

        /// <summary>Host, main thread, between two physics steps. Null writes an empty blob; at most 16 MiB.</summary>
        byte[] Capture();

        /// <summary>Host, during the restore, before NetPit.StartHost. data is never null (it may be empty).</summary>
        void Restore(byte[] data);
    }
}

// IGameplayReadModel: the read-only facade the placeholder UI and the container fill views read from (brief §4.1,
// §4.6). It hides whether the live source is the host core (offline / host) or the replicated GameplayNet (client), so
// the HUD, shop and results never reach into the kit, NGO, or the pit. Everything is a plain read; nothing mutates.
using System.Collections.Generic;

namespace BallPitGameplay
{
    /// <summary>One upgrade row for the shop, resolved for the local peer.</summary>
    public readonly struct UpgradeView
    {
        public readonly string Id;
        public readonly string Name;
        public readonly string Description;
        public readonly int Level;
        public readonly int MaxLevel;
        public readonly long NextCost;
        public readonly bool CanBuy;
        public readonly string Reason;   // null when buyable, else why not

        public UpgradeView(string id, string name, string description, int level, int maxLevel, long nextCost, bool canBuy, string reason)
        {
            Id = id; Name = name; Description = description; Level = level; MaxLevel = maxLevel;
            NextCost = nextCost; CanBuy = canBuy; Reason = reason;
        }
    }

    public interface IGameplayReadModel
    {
        /// <summary>True while a live gameplay context exists (a world is built).</summary>
        bool IsLive { get; }

        long Money { get; }
        bool Won { get; }

        /// <summary>Pit colour count (clamped 1..16 by the store).</summary>
        int ColorCount { get; }
        /// <summary>Balls of a colour still in play (not sold, not lost).</summary>
        int InPlay(int color);
        /// <summary>Balls of a colour already sold (Stored).</summary>
        int Sold(int color);
        /// <summary>Total balls of a colour.</summary>
        int Total(int color);
        /// <summary>Sold / Total for a colour, 0..1 (0 when the colour has no balls).</summary>
        float SoldFraction(int color);
        /// <summary>The display name of a colour (from the active palette).</summary>
        string ColorName(int color);

        /// <summary>The local player's carry count (balls in hand + pocketed) and current tool name.</summary>
        int CarryCount { get; }
        string CurrentToolName { get; }

        /// <summary>The shop rows for the local peer (level, cost, prereqs, buyability).</summary>
        IReadOnlyList<UpgradeView> Upgrades { get; }

        /// <summary>The recent sale/purchase feed, oldest first (bounded).</summary>
        IReadOnlyList<string> Feed { get; }
    }
}

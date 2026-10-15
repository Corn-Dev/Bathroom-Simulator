// IToolCatalog: the seam the hotbar selects from (brief §5). The ordered set of tools a player can equip. Bite (a)
// exposes exactly one (A's ArmsTool), but the interface is N-wide so Phase 1-4 tools (rake, broom, dustpan, powered and
// two-stage tools) register here later with no rework. Deliberately NOT tied to "a tool belongs to a player" or to any
// fixed count — holders beyond players (carts, drones, belts) are future, and nothing here designs against them.
using System.Collections.Generic;

namespace BallPitGameplay
{
    public interface IToolCatalog
    {
        /// <summary>The ordered tools the hotbar can select (index 0 = slot 1 / hands this bite).</summary>
        IReadOnlyList<IPlayerTool> Tools { get; }
    }
}

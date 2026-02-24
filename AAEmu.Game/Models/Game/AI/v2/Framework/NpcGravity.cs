using AAEmu.Game.Models.Game.NPChar;

namespace AAEmu.Game.Models.Game.AI.v2.Framework;

/// <summary>
/// NPC gravity system — DISABLED.
/// NPC Z positions are now trusted from A* pathfinding waypoints and spawner data.
/// The height correction chain was fighting with A* which already delivers correct Z
/// from BAI node positions. Kept as a stub so callers don't need to change.
/// </summary>
public static class NpcGravity
{
    /// <summary>
    /// Gravity is disabled — A* pathfinding and spawner data provide correct Z.
    /// </summary>
    /// <returns>Always false (no position adjustment)</returns>
    public static bool ApplyGravity(Npc npc, TimeSpan delta)
    {
        return false;
    }
}

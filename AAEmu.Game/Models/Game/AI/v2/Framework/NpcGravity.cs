using AAEmu.Game.Models.Game.NPChar;

namespace AAEmu.Game.Models.Game.AI.v2.Framework;

/// <summary>
/// NPC gravity — currently disabled in favour of NavMesh-driven Z correction.
///
/// Z is managed exclusively by:
///   • MoveTowards() → GetHeight() → NavMesh (terrain + brush collision meshes)
///     Every movement step queries the navmesh and snaps Z to the surface polygon.
///   • Spawner position (initial Z at spawn time).
///
/// An explicit gravity loop on top of NavMesh-driven movement caused conflicting
/// corrections that made NPCs oscillate (bounce between two surfaces), appear to fly
/// (snapped to wrong upper polygon), or walk at wrong floor heights inside buildings.
/// The navmesh contains the full geometry needed to guide NPCs correctly without help.
/// </summary>
public static class NpcGravity
{
    /// <summary>
    /// No-op — Z is handled entirely by MoveTowards/GetHeight and the navmesh.
    /// </summary>
    public static bool ApplyGravity(Npc npc, TimeSpan delta) => false;
}

using AAEmu.Game.Models.Game.NPChar;

namespace AAEmu.Game.Models.Game.Units;

/// <summary>
/// World-owned NPCs are heightmap-snapped when SCUnitState is first written so plaza
/// spawns sit on the floor. Temporary portal units keep their authored Z: they are
/// placed on a book cast point or a worldgate pad/aimpoint, and the heightmap under
/// that pad is the ground the pad stands on — snapping buries the portal mesh.
/// </summary>
public static class UnitStateGroundRules
{
    public static bool ShouldSnapWorldNpcToHeightmap(Npc npc)
    {
        if (npc is null || npc.IsZoneMirror)
            return false;

        return npc is not Portal;
    }
}

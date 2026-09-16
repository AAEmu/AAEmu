using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Models.Game.Skills;

/// <summary>
/// Ground snap for the move_to_ground special effect. The floor itself comes from <see cref="TerrainFloor"/>,
/// the same heightmap + clearance policy the spawn and teleport paths use; this class only decides whether
/// there is a floor to move the unit onto and what Z that is.
/// </summary>
public static class GroundSnapRules
{
    /// <summary>
    /// Landing height, or nothing. <see cref="TerrainFloor.SampleHeightmap"/> answers 0 when heightmaps are
    /// off or the cell is missing, and <see cref="TerrainFloor.ChooseUnitFloorZ"/> treats that as "no floor"
    /// as well; moving a unit to Z 0 on that answer would drop it into the sea, so the effect does nothing.
    /// </summary>
    /// <param name="rawZ">The Z the unit holds now — the snap is measured against it.</param>
    /// <param name="sampledGround">Heightmap sample at the unit's XY.</param>
    public static bool TryGetFloorZ(
        float rawZ,
        float sampledGround,
        bool overWater,
        float waterSurfaceZ,
        out float z)
    {
        if (sampledGround <= 0f)
        {
            z = rawZ;
            return false;
        }

        z = TerrainFloor.ChooseUnitFloorZ(rawZ, sampledGround, overWater, waterSurfaceZ);
        return true;
    }
}

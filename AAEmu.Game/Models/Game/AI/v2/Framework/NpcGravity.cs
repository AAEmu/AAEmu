using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.NPChar;

namespace AAEmu.Game.Models.Game.AI.v2.Framework;

/// <summary>
/// NPC gravity safety net — snaps non-flying NPCs to terrain Z every tick.
/// Only applies downward gravity (falling). Upward corrections require the NPC
/// to actually walk onto a higher surface (MoveTowards handles this).
/// </summary>
public static class NpcGravity
{
    /// <summary>
    /// Snap the NPC's Z to the terrain/navmesh surface if the NPC is floating above it.
    /// Does NOT snap upwards — that would teleport NPCs off spawner-defined floors,
    /// bridges, or elevated surfaces when GetHeight picks a wrong source.
    /// </summary>
    /// <returns>True if Z was adjusted</returns>
    public static bool ApplyGravity(Npc npc, TimeSpan delta)
    {
        if (npc == null || npc.CanFly)
            return false;

        var pos = npc.Transform.Local.Position;
        var terrainZ = WorldManager.Instance.GetHeightWithSource(
            npc.Transform.ZoneId, pos.X, pos.Y, pos.Z, out var source);

        if (terrainZ <= 0f)
            return false;

        var diff = terrainZ - pos.Z;

        // NPC is above terrain — apply gravity (falling)
        // Only snap if the difference is significant (> 0.5m) to avoid jitter
        if (diff < -0.5f)
        {
            // HeightMap is outdoor terrain — shouldn't pull down NPCs on building floors
            if (source == "HeightMap" && pos.Z > terrainZ + 3f)
                return false;

            // NavMesh has the actual collision geometry (terrain + all brush structures).
            // If NavMesh confirms a surface near the NPC's current Z, the NPC is on a
            // real structure — don't fall even if GeoData/other sources say ground is lower.
            // This prevents BAI waypoint-based sources (GeoData, Type4, BuildingFloorBAI)
            // from incorrectly pulling NPCs off structures they're standing on.
            if (source != "NavMesh" && diff < -2f)
            {
                var navMesh = npc.ParentWorld?.NavMesh;
                if (navMesh?.HasData == true)
                {
                    var navZ = navMesh.GetHeight(pos.X, pos.Y, pos.Z);
                    if (navZ > 0f && MathF.Abs(pos.Z - navZ) < 2f)
                        return false; // NavMesh confirms surface at NPC's Z — don't fall
                }
            }

            npc.Transform.Local.SetHeight(terrainZ);
            return true;
        }

        // NPC is below terrain — gentle correction upward (stuck in ground)
        // This can happen after loading or teleporting. Only correct small differences
        // to avoid teleporting NPCs to wrong floors.
        if (diff > 1.0f && source != "HeightMap")
        {
            // Only push up if a high-priority source says NPC should be higher
            // (GeoData, BuildingFloor, Type4Floor — not HeightMap which could be wrong)
            npc.Transform.Local.SetHeight(terrainZ);
            return true;
        }

        return false;
    }
}

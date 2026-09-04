using AAEmu.Game.Models.Game.DoodadObj.Funcs;
using AAEmu.Game.Models.Game.DoodadObj.Static;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.NPChar;

namespace AAEmu.Game.Models.Game.DoodadObj;

/// <summary>
/// School-of-fish doodads are <see cref="DoodadGroupId.SportFishing"/> (group 65).
/// Only the chummed phase carries <see cref="DoodadFuncFishSchool"/>; idle yields no spawner.
/// </summary>
public static class FishSchoolLookup
{
    public static uint SchoolGroupId => (uint)DoodadGroupId.SportFishing;

    public static bool IsSchool(Doodad doodad) =>
        doodad?.Template != null && doodad.Template.GroupId == SchoolGroupId;

    /// <summary>
    /// Radar must only list a school that is still in its world. The boot snapshot kept
    /// deleted placements, which is why the finder showed pins over empty water.
    /// </summary>
    public static bool IsPresent(Doodad doodad)
    {
        if (!IsSchool(doodad) || doodad.IsDeleted || !doodad.IsVisible)
            return false;

        var world = doodad.ParentWorld;
        return world != null && world.GetDoodad(doodad.ObjId) == doodad;
    }

    public static uint ReadActiveSpawnerId(
        Doodad doodad,
        Func<uint, string, DoodadPhaseFuncTemplate> getPhaseTemplate)
    {
        if (!IsSchool(doodad) || getPhaseTemplate == null || doodad.CurrentPhaseFuncs == null)
            return 0;

        foreach (var func in doodad.CurrentPhaseFuncs)
        {
            if (func == null)
                continue;
            if (getPhaseTemplate(func.FuncId, func.FuncType) is DoodadFuncFishSchool school)
                return school.NpcSpawnerId;
        }

        return 0;
    }

    /// <summary>
    /// Nearest chummed school to the bobber (or other origin), inside <paramref name="rangeMeters"/>.
    /// Entries with <c>SpawnerId == 0</c> are idle and skipped.
    /// </summary>
    public static uint ResolveNearestSpawnerId(
        IEnumerable<(float X, float Y, uint SpawnerId)> schools,
        float originX,
        float originY,
        float rangeMeters)
    {
        if (schools == null || rangeMeters < 0f)
            return 0;

        uint best = 0;
        var bestD2 = rangeMeters * rangeMeters;
        foreach (var (x, y, spawnerId) in schools)
        {
            if (spawnerId == 0)
                continue;
            var dx = x - originX;
            var dy = y - originY;
            var d2 = dx * dx + dy * dy;
            if (d2 <= bestD2)
            {
                bestD2 = d2;
                best = spawnerId;
            }
        }

        return best;
    }

    public static NpcSpawnerNpc SelectWeighted(IReadOnlyList<NpcSpawnerNpc> npcs, double rollUnitInterval)
    {
        if (npcs == null || npcs.Count == 0)
            return null;
        if (npcs.Count == 1)
            return npcs[0];

        var totalWeight = 0f;
        foreach (var entry in npcs)
            totalWeight += entry.Weight;
        if (totalWeight <= 0f)
            return npcs[0];

        var roll = Math.Clamp(rollUnitInterval, 0d, 1d) * totalWeight;
        var current = 0d;
        foreach (var entry in npcs)
        {
            current += entry.Weight;
            if (roll < current)
                return entry;
        }

        return npcs[0];
    }
}

using System.Numerics;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.World.Zones;

namespace AAEmu.Game.Core.Managers.World;

public interface IZoneManager : ILoadable
{
    event Action<ushort, ZoneConflictType, ZoneConflictType> ZoneConflictStateChanged;

    ZoneConflict[] GetConflicts();
    Zone GetZoneById(uint zoneId);
    Zone GetZoneByKey(uint zoneKey);
    ZoneGroup GetZoneGroupById(uint zoneId);
    List<uint> GetZoneKeysInZoneGroupById(uint zoneGroupId);
    uint GetTargetIdByZoneId(uint zoneId);
    Vector2 GetZoneOriginCell(uint zoneId);
    Vector3 ConvertToWorldCoordinates(uint zoneId, Vector3 point);
    Vector3 ConvertToLocalCoordinates(uint zoneId, Vector3 point);
    bool DoodadHasMatchingClimate(Doodad doodad);
    List<Climate> GetClimatesByZone(Zone zone);

    /// <summary>Arms the wall-clock cycle for conflict zones driven by <c>conflict_zone_realtime_schedules</c>.</summary>
    void StartConflictCycles();

    /// <summary>Records an NPC death for conflict-zone participation when the template is listed for its zone.</summary>
    void RegisterNpcKill(Npc npc);

    /// <summary>Records a finished quest for conflict-zone participation when it is listed for the character's zone.</summary>
    void RegisterQuestCompletion(Character character, uint questId);
}

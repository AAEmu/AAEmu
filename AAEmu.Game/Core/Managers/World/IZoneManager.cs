using System.Numerics;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.World.Zones;

namespace AAEmu.Game.Core.Managers.World;

public interface IZoneManager : ILoadable
{
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
}

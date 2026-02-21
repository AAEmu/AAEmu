using AAEmu.Game.Models.Game.World.Zones;

namespace AAEmu.Game.Core.Managers.World;

public interface IZoneManager
{
    void Load();
    ZoneConflict[] GetConflicts();
    Zone GetZoneById(uint zoneId);
    Zone GetZoneByKey(uint zoneKey);
    ZoneGroup GetZoneGroupById(uint zoneId);
    List<uint> GetZoneKeysInZoneGroupById(uint zoneGroupId);
    uint GetTargetIdByZoneId(uint zoneId);
}

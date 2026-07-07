using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.StaticValues;

using Portal = AAEmu.Game.Models.Game.Portal;

namespace AAEmu.Game.Core.Managers;

public interface IPortalManager : ILoadable
{
    List<Portal> GetRecallBySubZoneId(uint subZoneId);
    Portal GetRecallById(uint returnPointId);
    Portal GetRespawnBySubZoneId(uint subZoneId);
    Portal GetRespawnById(uint id);
    Portal GetWorldGatesBySubZoneId(uint subZoneId);
    Portal GetWorldGatesById(uint id);
    uint GetDistrictReturnPoint(uint districtId);
    uint GetDistrictReturnPoint(uint districtId, FactionsEnum factionId);
    void OpenPortal(Character owner, SkillObjectPortalInfo portalEffectObj);
    Portal GetClosestReturnPortal(Character character);
}

using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Effects;
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
    Portal GetReturnPoint(uint returnPointId);
    IReadOnlyList<Portal> GetLevelReturnPoints();
    uint GetDistrictReturnPoint(uint districtId);
    uint GetDistrictReturnPoint(uint districtId, FactionsEnum factionId);
    uint GetDistrictIdByReturnPoint(uint returnPointId, FactionsEnum factionId);
    void OpenPortal(Character owner, SkillObjectUnk1 portalEffectObj, OpenPortalEffect effect);

    /// <summary>
    /// Spawns a temporary enter/exit portal pair for a guild worldgate selection (no book reagents).
    /// Enter sits at <paramref name="enterPosition"/>; exit at <paramref name="portalInfo"/>.
    /// </summary>
    bool OpenExpeditionPortal(Character owner, Portal portalInfo, System.Numerics.Vector3 enterPosition);

    Portal GetClosestReturnPortal(Character character);
}

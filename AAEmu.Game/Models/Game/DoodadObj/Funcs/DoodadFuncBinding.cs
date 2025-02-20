using System.Linq;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs;

/// <summary>
/// Sets recall point of caster
/// </summary>
public class DoodadFuncBinding : DoodadFuncTemplate
{
    public uint DistrictId { get; set; }

    public override void Use(BaseUnit caster, Doodad owner, uint skillId, int nextPhase = 0)
    {
        if (caster is not Character character) { return; }

        var returnPointId = PortalManager.Instance.GetDistrictReturnPoint(DistrictId, character.Faction.Id);

        Logger.Trace($"DoodadFuncBinding: DistrictId {DistrictId} ==> ReturnPointId {returnPointId}, SubZonesId {character.SubZoneId}");
        character.SendDebugMessage($"DoodadFuncBinding: DistrictId {DistrictId} ==> ReturnPointId {returnPointId}, SubZonesId {character.SubZoneId}");

        if (returnPointId == 0) { return; }

        var portal = PortalManager.Instance.GetRecallById(returnPointId);

        if (portal != null)
        {
            character.ReturnDistrictId = DistrictId;
            var portals = character.Portals.DistrictPortals.Values.ToArray();
            character.SendPacket(new SCCharacterReturnDistrictsPacket(portals, portal.Id));
            Logger.Trace($"DoodadFuncBinding: ReturnPointId {returnPointId} ==> Portal.Id {portal.Id}");
            character.SendDebugMessage($"DoodadFuncBinding: ReturnPointId {returnPointId} ==> Portal.Id {portal.Id}");
        }
        else
        {
            Logger.Warn($"DoodadFuncBinding: Recall point {DistrictId} not found!");
            character.SendDebugMessage($"DoodadFuncBinding: Recall point {DistrictId} not found!");
        }
        owner.ToNextPhase = true;
    }
}

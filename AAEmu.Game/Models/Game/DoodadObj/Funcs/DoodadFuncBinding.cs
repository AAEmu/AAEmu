using System.Linq;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs
{
    /// <summary>
    /// Sets recall point of caster
    /// </summary>
    public class DoodadFuncBinding : DoodadFuncTemplate
    {
        public uint DistrictId { get; set; }

        public override void Use(Unit caster, Doodad owner, uint skillId, int nextPhase = 0)
        {
            if (caster is not Character character) { return; }

            var returnPointId = PortalManager.Instance.GetDistrictReturnPoint(DistrictId, character.Faction.Id);

            _log.Trace("DoodadFuncBinding: DistrictId {0} ==> ReturnPointId {1}, SubZonesId {2}", DistrictId, returnPointId, character.SubZoneId);
            character.SendMessage("DoodadFuncBinding: DistrictId {0} ==> ReturnPointId {1}, SubZonesId {2}", DistrictId, returnPointId, character.SubZoneId);
            
            if (returnPointId == 0) { return; }

            var portal = PortalManager.Instance.GetRecallById(returnPointId);

            if (portal != null)
            {
                character.ReturnDictrictId = DistrictId;
                var portals = character.Portals.DistrictPortals.Values.ToArray();
                character.SendPacket(new SCCharacterReturnDistrictsPacket(portals, portal.Id));
                _log.Trace($"DoodadFuncBinding: ReturnPointId {returnPointId} ==> Portal.Id {portal.Id}");
                character.SendMessage($"DoodadFuncBinding: ReturnPointId {returnPointId} ==> Portal.Id {portal.Id}");
            }
            else
            {
                _log.Warn($"DoodadFuncBinding: Recall point {DistrictId} not found!");
                character.SendMessage($"DoodadFuncBinding: Recall point {DistrictId} not found!");
            }
            owner.ToNextPhase = true;
        }
    }
}

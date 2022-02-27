using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs
{
    public class DoodadFuncBinding : DoodadFuncTemplate
    {
        // doodad_funcs
        public uint DistrictId { get; set; }

        public override void Use(Unit caster, Doodad owner, uint skillId, int nextPhase = 0)
        {
            if (caster is not Character character) { return; }

            var returnPointId = PortalManager.Instance.GetDistrictReturnPoint(DistrictId, character.Faction.Id);
            
            if (returnPointId == 0) { return; }

            var portal = PortalManager.Instance.GetPortalById(returnPointId);
            _log.Trace("DoodadFuncBinding: DistrictId {0} ==> ReturnPointId {1}, SubZonesId {2}", DistrictId, returnPointId, character.SubZoneId);
            character.SendMessage("DoodadFuncBinding: DistrictId {0} ==> ReturnPointId {1}, SubZonesId {2}", DistrictId, returnPointId, character.SubZoneId);

            if (portal != null)
            {
                character.ReturnDictrictId = DistrictId;
                var portals = new Portal[character.Portals.DistrictPortals.Count];
                character.Portals.DistrictPortals.Values.CopyTo(portals, 0);
                character.SendPacket(new SCCharacterReturnDistrictsPacket(portals, (int)portal.Id));
                _log.Trace("DoodadFuncBinding: ReturnPointId {0} ==> Portal.Id {1}", returnPointId, portal.Id);
                character.SendMessage("DoodadFuncBinding: ReturnPointId {0} ==> Portal.Id {1}", returnPointId, portal.Id);
            }
            else
            {
                _log.Trace("DoodadFuncBinding: Recall point {0} not found!", DistrictId);
                character.SendMessage("DoodadFuncBinding: Recall point {0} not found!", DistrictId);
            }
        }
    }
}

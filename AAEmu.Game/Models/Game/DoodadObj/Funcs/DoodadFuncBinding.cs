using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs
{
    public class DoodadFuncBinding : DoodadFuncTemplate
    {
        public uint DistrictId { get; set; }

        public override void Use(Unit caster, Doodad owner, uint skillId, int nextPhase = 0)
        {
            if (caster is Character character)
            {
                var ReturnPointId = PortalManager.Instance.GetDistrictReturnPoint(DistrictId, character.Faction.Id);
                if (ReturnPointId != 0)
                {
                    var portal = PortalManager.Instance.GetPortalById(ReturnPointId);
                    _log.Debug("DoodadFuncBinding: DistrictId {0} ==> ReturnPointId {1}", DistrictId, ReturnPointId);
                    character.SendMessage("DoodadFuncBinding: DistrictId {0} ==> ReturnPointId {1}", DistrictId, ReturnPointId);

                    if (portal == null)
                    {
                        _log.Debug("DoodadFuncBinding: Recall point {0} not found!", DistrictId);
                        character.SendMessage("DoodadFuncBinding: Recall point {0} not found!", DistrictId);
                        return;
                    }
                    character.ReturnDictrictId = DistrictId;
                    var portals = new Portal[character.Portals.DistrictPortals.Count];
                    character.Portals.DistrictPortals.Values.CopyTo(portals, 0);
                    character.SendPacket(new SCCharacterReturnDistrictsPacket(portals, (int)ReturnPointId));
                }
            }
        }
    }
}

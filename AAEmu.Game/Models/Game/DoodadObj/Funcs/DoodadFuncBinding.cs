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
            _log.Debug("DoodadFuncBinding: Save {0} recall point", DistrictId);

            if (caster is Character character)
            {

                var portal = PortalManager.Instance.GetPortalById(DistrictId + 1);

                //character.Portals.AddPrivatePortal(portal.X, portal.Y, portal.Z, portal.ZRot, portal.ZoneId, portal.Name);
                character.ReturnDictrictId = DistrictId + 1;
                character.SendMessage("[Portal] {0} has added the entry \"{1}\" to your portal book", portal?.Name, character.Name);
                var portals = new Portal[character.Portals.DistrictPortals.Count];
                character.Portals.DistrictPortals.Values.CopyTo(portals, 0);
                character.SendPacket(new SCCharacterReturnDistrictsPacket(portals, (int)character.ReturnDictrictId)); // INFO - What is returnDistrictId?

                //PortalManager.Instance.SetFavoritePortal(portal);
            }
        }
    }
}

using AAEmu.Game.Core.Managers;
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
            // Sets the recall point to DistrictId
            if (caster is Character character)
            {
                var portal = PortalManager.Instance.GetPortalByDoodadId(owner.TemplateId);

                if (portal != null)
                {
                    character.ReturnDictrictId = portal.SubZoneId;
                    character.Portals.Send(); // Updates return point
                }
                else
                {
                    _log.Error("Player tried to bind memory tome with no recall data!");
                }
            }

            _log.Debug("DoodadFuncBinding");
        }
    }
}

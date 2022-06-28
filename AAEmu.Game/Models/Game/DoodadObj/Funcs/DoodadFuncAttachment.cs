using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj.Static;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs
{
    public class DoodadFuncAttachment : DoodadFuncTemplate
    {
        // doodad_funcs
        public AttachPointKind AttachPointId { get; set; }
        public int Space { get; set; }
        public BondKind BondKindId { get; set; }

        public override void Use(Unit caster, Doodad owner, uint skillId, int nextPhase = 0)
        {
            _log.Trace("DoodadFuncAttachment");
            if (caster is Character character)
            {
                if (BondKindId > BondKind.BondInvalid)
                {
                    var spot = owner.Seat.LoadPassenger(character, owner.ObjId, Space); // ask for a free meta number for landing
                    if (spot == -1)
                    {
                        return; // we leave if there is no place
                    }

                    character.Bonding = new BondDoodad(owner, AttachPointId, BondKindId, Space, spot);
                    character.BroadcastPacket(new SCBondDoodadPacket(caster.ObjId, character.Bonding), true);
                    character.Transform.StickyParent = owner.Transform;
                }
                // Ships // TODO Check how sit on the ship
                else
                {
                    SlaveManager.Instance.BindSlave(character, owner.ParentObjId, AttachPointId, AttachUnitReason.NewMaster);
                }
            }
        }
    }
}

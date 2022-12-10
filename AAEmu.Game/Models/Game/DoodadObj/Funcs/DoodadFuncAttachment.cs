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
        public uint AnimActionId { get; set; }

        public override void Use(BaseUnit caster, Doodad owner, uint skillId, int nextPhase = 0)
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

                    // Chairs, beds etc.
                    // spot = 0 sit left, = 1 sit right on the bench, spot = -1 нет свободного места
                    // Space = 1-means that there is one place (a chair), Space = 2-means that there are two places to sit (a bench on transport)
                    character.Bonding = new BondDoodad(owner, AttachPointId, Space, spot, AnimActionId);
                    character.BroadcastPacket(new SCAttachToDoodadPacket(caster.ObjId, character.Bonding), true);
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

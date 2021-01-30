using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs
{
    public class DoodadFuncAttachment : DoodadFuncTemplate
    {
        public byte AttachPointId { get; set; }
        public int Space { get; set; }
        public byte BondKindId { get; set; }
        public uint AnimActionId { get; set; } // added in 1.7

        public override void Use(Unit caster, Doodad owner, uint skillId, int nextPhase = 0)
        {
            _log.Debug("DoodadFuncAttachment");
            if (caster is Character character)
            {
                if (BondKindId > 1)
                {
                    // Chairs, beds etc.
                    var Spot = 0;// spot = 0 sit left, = 1 sit right on the bench
                    character.Bonding = new BondDoodad(owner, AttachPointId, Space, Spot, AnimActionId);
                    character.BroadcastPacket(new SCBondDoodadPacket(caster.ObjId, character.Bonding), true);
                }
                // Ships
                else
                {
                    SlaveManager.Instance.BindSlave(character, owner.ParentObjId, AttachPointId, (byte) (BondKindId + 6));
                }
            }
        }
    }
}

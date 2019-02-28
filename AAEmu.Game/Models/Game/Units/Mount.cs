using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Mate;
using AAEmu.Game.Models.Game.NPChar;

namespace AAEmu.Game.Models.Game.Units
{
    public sealed class Mount : Unit
    {
        public ushort TlId { get; set; }
        public uint TemplateId { get; set; }
        public NpcTemplate Template { get; set; }

        public MateTemplate MateTemplate { get; set; }
        public uint OwnerObjId { get; set; }
        public uint Att1 { get; set; }
        public byte Reason1 { get; set; }
        public uint Att2 { get; set; }
        public byte Reason2 { get; set; }

        public override float Scale => Template.Scale;

        public Mount()
        {
            ModelParams = new UnitCustomModelParams();
            Att1 = 0u;
            Reason1 = 0;
            Att2 = 0u;
            Reason2 = 0;
        }

        public override void AddVisibleObject(Character character)
        {
            character.SendPacket(new SCUnitStatePacket(this));
            character.SendPacket(new SCMateStatePacket(ObjId));
            character.SendPacket(new SCUnitPointsPacket(ObjId, Hp, Mp));
            if (Att1 > 0)
            {
                var owner = WorldManager.Instance.GetCharacterByObjId(Att1);
                if (owner != null)
                    character.SendPacket(new SCUnitAttachedPacket(owner.ObjId, 1, Reason1, ObjId));
            }
            if (Att2 > 0)
            {
                var passenger = WorldManager.Instance.GetCharacterByObjId(Att1);
                if (passenger != null)
                    character.SendPacket(new SCUnitAttachedPacket(passenger.ObjId, 2, Reason2, ObjId));
            }
        }

        public override void RemoveVisibleObject(Character character)
        {
            if (character.CurrentTarget != null && character.CurrentTarget == this)
            {
                character.CurrentTarget = null;
                character.SendPacket(new SCTargetChangedPacket(character.ObjId, 0));
            }

            character.SendPacket(new SCUnitsRemovedPacket(new[] { ObjId }));
        }
    }
}

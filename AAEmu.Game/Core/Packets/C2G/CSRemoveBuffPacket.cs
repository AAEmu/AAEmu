using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSRemoveBuffPacket : GamePacket
    {
        public CSRemoveBuffPacket() : base(CSOffsets.CSRemoveBuffPacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            var objId = stream.ReadBc();
            var buffId = stream.ReadUInt32();
            var reason = stream.ReadByte();

            if (Connection.ActiveChar.ObjId != objId)
            {
                return;
            }

            var effect = Connection.ActiveChar.Buffs.GetEffectByIndex(buffId);
            if (effect == null)
            {
                return;
            }

            var template = effect.Template;
            if (template is {Kind: BuffKind.Good})
            {
                effect.Exit();
            }

            if (effect.Template.Kind == BuffKind.Good)
            {
                effect.Exit();
            }
        }
    }
}

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCNotifyResurrectionPacket : GamePacket
    {
        private readonly SkillCaster _skillCaster;
        
        public SCNotifyResurrectionPacket(SkillCaster skillCaster) : base(0x040, 1) // TODO 1.0 opcode: 0x03c
        {
            _skillCaster = skillCaster;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_skillCaster);
            return stream;
        }
    }
}

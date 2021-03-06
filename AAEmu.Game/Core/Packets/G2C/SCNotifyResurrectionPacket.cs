using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCNotifyResurrectionPacket : GamePacket
    {
        private readonly SkillCaster _skillCaster;
        
        public SCNotifyResurrectionPacket(SkillCaster skillCaster) : base(SCOffsets.SCNotifyResurrectionPacket, 5)
        {
            _skillCaster = skillCaster;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_skillCaster);
            stream.Write(0); // inPlaceRemainCnt, added in 2.0

            return stream;
        }
    }
}

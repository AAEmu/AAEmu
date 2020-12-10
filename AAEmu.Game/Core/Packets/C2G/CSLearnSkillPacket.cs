using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSLearnSkillPacket : GamePacket
    {
        public CSLearnSkillPacket() : base(CSOffsets.CSLearnSkillPacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var skillId = stream.ReadUInt32();

            Connection.ActiveChar.Skills.AddSkill(skillId);
        }
    }
}

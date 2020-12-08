using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSResetSkillsPacket : GamePacket
    {
        public CSResetSkillsPacket() : base(CSOffsets.CSResetSkillsPacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var abilityId = stream.ReadByte();
            var ausp = stream.ReadBoolean();

            Connection.ActiveChar.Skills.Reset((AbilityType) abilityId);
        }
    }
}

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCSkillLearnedPacket(Skill skill) : GamePacket(SCOffsets.SCSkillLearnedPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(skill.Id);
        stream.Write(skill.Level);
        return stream;
    }
}

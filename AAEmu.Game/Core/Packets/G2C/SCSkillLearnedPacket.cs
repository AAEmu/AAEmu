using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.Game.Core.Packets.G2C;

/// <remarks>
/// 10.0.2.13 reads a single i32 the serializer names "type" — the skill id — which is what this writes.
/// </remarks>
public class SCSkillLearnedPacket(Skill skill) : GamePacket(SCOffsets.SCSkillLearnedPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(skill.Id);
        // 10.0.2.13 stops at the skill id; the 1.2 level field is not read and shifted everything
        // the client parsed afterwards.
        return stream;
    }
}

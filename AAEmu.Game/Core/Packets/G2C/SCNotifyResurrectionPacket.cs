using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCNotifyResurrectionPacket(SkillCaster skillCaster) : GamePacket(SCOffsets.SCNotifyResurrectionPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(skillCaster);
        return stream;
    }
}

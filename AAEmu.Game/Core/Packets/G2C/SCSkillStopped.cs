using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCSkillStoppedPacket(uint unitObjId, uint skillId) : GamePacket(SCOffsets.SCSkillStoppedPacket, 5)
{
    public override PacketLogLevel LogLevel => PacketLogLevel.Trace;

    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(unitObjId);
        stream.Write(skillId);
        return stream;
    }
}

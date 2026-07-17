using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCSkillEndedPacket(ushort tlId) : GamePacket(SCOffsets.SCSkillEndedPacket, 5)
{
    public override PacketLogLevel LogLevel => PacketLogLevel.Trace;

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(tlId);
        return stream;
    }

    public override string Verbose()
    {
        return $" - {tlId}";
    }
}

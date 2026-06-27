using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCBotSuspectReportedPacket(string reporter, string suspect) : GamePacket(SCOffsets.SCBotSuspectReportedPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(reporter);
        stream.Write(suspect);
        return stream;
    }
}

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCTrialAudienceLeftPacket(uint bc, string audienceName)
    : GamePacket(SCOffsets.SCTrialAudienceLeftPacket, 1)
{
    public override PacketLogLevel LogLevel => PacketLogLevel.Trace;

    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(bc);
        stream.Write(audienceName);
        return stream;
    }
}

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCMailReceiverOpenedPacket(long mainId, DateTime openDate)
    : GamePacket(SCOffsets.SCMailReceiverOpenedPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(mainId);
        stream.Write(openDate);
        return stream;
    }
}

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCTradeOkUpdatePacket(bool myOk, bool otherOk) : GamePacket(SCOffsets.SCTradeOkUpdatePacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(myOk);
        stream.Write(otherOk);
        return stream;
    }
}

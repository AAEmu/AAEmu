using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCICSBuyResultPacket(bool success, byte buyMode, string receiverName, int chargeAaPoint)
    : GamePacket(SCOffsets.SCICSBuyResultPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(success);
        stream.Write(buyMode);
        stream.Write(receiverName);
        stream.Write(chargeAaPoint);
        return stream;
    }
}

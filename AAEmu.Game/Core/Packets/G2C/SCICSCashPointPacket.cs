using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCICSCashPointPacket(int point) : GamePacket(SCOffsets.SCICSCashPointPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(point);
        return stream;
    }
}

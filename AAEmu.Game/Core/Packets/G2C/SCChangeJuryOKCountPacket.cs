using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
namespace AAEmu.Game.Core.Packets.G2C;

public class SCChangeJuryOKCountPacket(int count, int total) : GamePacket(SCOffsets.SCChangeJuryOkCountPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(count);
        stream.Write(total);
        return stream;
    }
}

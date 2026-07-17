using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCBmPointPacket(long bmPoint) : GamePacket(SCOffsets.SCBmPointPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(bmPoint);
        return stream;
    }
}

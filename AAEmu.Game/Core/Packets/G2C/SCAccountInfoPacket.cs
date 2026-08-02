using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCAccountInfoPacket(int payMethod, int payLocation, DateTime payStart, DateTime payEnd)
    : GamePacket(SCOffsets.SCAccountInfoPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(payMethod);
        stream.Write(payLocation);
        stream.Write(payStart);
        stream.Write(payEnd);
        stream.Write((long)0); // realPayTime (+152, 8 bytes)
        stream.Write((uint)0);
        return stream;
    }
}

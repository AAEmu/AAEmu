using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCAccountWarnedPacket(byte source, string msg) : GamePacket(SCOffsets.SCAccountWarnedPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(source);
        stream.Write(msg);
        return stream;
    }
}

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCMailRemovedPacket(bool isSent, long mailId) : GamePacket(SCOffsets.SCMailRemovedPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(isSent);
        stream.Write(mailId);
        return stream;
    }
}

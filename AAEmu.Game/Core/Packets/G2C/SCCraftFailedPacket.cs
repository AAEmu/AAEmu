using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCCraftFailedPacket(uint id, uint craftId, int count) : GamePacket(SCOffsets.SCCraftFailedPacket, 5)
{
    // TODO needs fixing

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(id);
        stream.Write(count);
        for (var i = 0; i < count; i++)
        {
            stream.Write(craftId);
        }

        return stream;
    }
}

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCCraftFailedPacket(int type, IReadOnlyList<int> types) : GamePacket(SCOffsets.SCCraftFailedPacket, 1)
{
    private const int MaxTypes = 20;

    public override PacketStream Write(PacketStream stream)
    {
        var count = Math.Min(types.Count, MaxTypes);

        stream.Write(type);
        stream.Write((uint)count);
        for (var i = 0; i < count; i++)
        {
            stream.Write(types[i]);
        }

        return stream;
    }
}

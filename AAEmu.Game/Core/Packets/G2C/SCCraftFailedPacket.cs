using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCCraftFailedPacket : GamePacket
{
    private readonly uint _id;
    private readonly uint _craftId;
    private readonly int _count;

    // TODO needs fixing
    public SCCraftFailedPacket(uint id, uint craftId, int count) : base(SCOffsets.SCCraftFailedPacket, 5)
    {
        _id = id;
        _craftId = craftId;
        _count = count;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(_id);
        stream.Write(_count);
        for (var i = 0; i < _count; i++)
        {
            stream.Write(_craftId);
        }

        return stream;
    }
}

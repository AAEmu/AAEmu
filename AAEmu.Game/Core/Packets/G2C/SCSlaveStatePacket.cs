using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCSlaveStatePacket(uint objId, ushort tlId, string creatorName, uint ownerId, uint dbId)
    : GamePacket(SCOffsets.SCSlaveStatePacket, 5)
{
    private readonly int _skillCount = 0;
    private readonly int _tagCount = 0;

    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(objId);
        stream.Write(tlId);
        stream.Write(0ul); // type
        stream.Write(_skillCount);
        stream.Write(_tagCount);
        stream.Write(creatorName);
        stream.Write(ownerId);
        stream.Write(dbId);
        return stream;
    }
}

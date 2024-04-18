using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCSlaveCreatedPacket : GamePacket
{
    private readonly uint _ownerObjId;
    private readonly ushort _tlId;
    private readonly uint _slaveObjId;
    private readonly ulong _itemId;
    private readonly string _creatorName;

    public SCSlaveCreatedPacket(uint ownerObjId, ushort tlId, uint slaveObjId, ulong itemId, string creatorName)
        : base(SCOffsets.SCSlaveCreatedPacket, 5)
    {
        _ownerObjId = ownerObjId;
        _tlId = tlId;
        _slaveObjId = slaveObjId;
        _itemId = itemId;
        _creatorName = creatorName;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(_ownerObjId);
        stream.Write(_tlId);
        stream.WriteBc(_slaveObjId);
        //stream.Write(_hideSpawnEffect); // no in 3+
        stream.Write(0ul);
        stream.Write(_creatorName);
        return stream;
    }
}

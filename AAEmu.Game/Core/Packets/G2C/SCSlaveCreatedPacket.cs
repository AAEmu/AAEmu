using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCSlaveCreatedPacket(
    uint ownerObjId,
    ushort tlId,
    uint slaveObjId,
    bool hideSpawnEffect,
    long unkId,
    string creatorName)
    : GamePacket(SCOffsets.SCSlaveCreatedPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(ownerObjId);
        stream.Write(tlId);
        stream.WriteBc(slaveObjId);
        stream.Write(hideSpawnEffect);
        stream.Write(unkId);
        stream.Write(creatorName);
        return stream;
    }
}

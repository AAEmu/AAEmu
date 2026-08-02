using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// bc(owner), tl s16, bc(slave), u64, creatorName[128].
/// stray byte that shifted every field after it.
/// </summary>
public class SCSlaveCreatedPacket(
    uint ownerObjId,
    ushort tlId,
    uint slaveObjId,
    long unkId,
    string creatorName)
    : GamePacket(SCOffsets.SCSlaveCreatedPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(ownerObjId);
        stream.Write(tlId);
        stream.WriteBc(slaveObjId);
        stream.Write(unkId);
        stream.Write(creatorName);
        return stream;
    }
}

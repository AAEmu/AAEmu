using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCHouseSoldPacket(
    ushort tl,
    uint previousOwnerId,
    uint newOwnerId,
    uint newOwnerAcc,
    string ownerName,
    string houseName)
    : GamePacket(SCOffsets.SCHouseSoldPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write((short)tl);
        stream.Write((ulong)previousOwnerId);
        stream.Write((ulong)newOwnerId);
        stream.Write((long)newOwnerAcc);
        stream.Write(ownerName ?? string.Empty);
        stream.Write(houseName ?? string.Empty);
        return stream;
    }
}

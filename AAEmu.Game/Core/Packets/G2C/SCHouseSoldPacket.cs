using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <remarks>
/// 10.0.2.13 body, named by its own serializer: i16 tl, two u64s it calls "type" — the outgoing and
/// incoming owner — then i64 newOwnerAcc and string ownerName. 1.2 sent all three ids as u32 and appended a
/// house name the client never reads, so the owner name was parsed out of the wrong bytes.
/// </remarks>
public class SCHouseSoldPacket(ushort tl, uint previousOwnerId, uint newOwnerId, uint newOwnerAcc, string ownerName)
    : GamePacket(SCOffsets.SCHouseSoldPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write((short)tl);
        stream.Write((ulong)previousOwnerId);
        stream.Write((ulong)newOwnerId);
        stream.Write((long)newOwnerAcc);
        stream.Write(ownerName);
        return stream;
    }
}

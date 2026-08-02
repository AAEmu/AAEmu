using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <remarks>
/// 10.0.2.13 body, named by its own serializer: i32 family, then two u64s it calls "type" — the outgoing
/// and incoming owner. 1.2 sent both ids as u32, so the client read the family, then half of the old owner,
/// and everything after that was shifted.
/// </remarks>
public class SCFamilyOwnerChangedPacket(uint familyId, uint newOwnerId, uint oldOwnerId = 0)
    : GamePacket(SCOffsets.SCFamilyOwnerChangedPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write((int)familyId);
        stream.Write((ulong)oldOwnerId);
        stream.Write((ulong)newOwnerId);
        return stream;
    }
}

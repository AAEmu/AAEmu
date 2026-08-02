using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <remarks>
/// 10.0.2.13 body, named by its own serializer: i32 family, u64 member, bool online, i8 level, i8
/// heirLevel. 1.2 sent the member id as u32 and stopped at the flag, so the two levels came out of the next
/// packet's bytes.
/// </remarks>
public class SCFamilyMemberOnlinePacket(uint familyId, uint memberId, bool online, byte level = 0, byte heirLevel = 0)
    : GamePacket(SCOffsets.SCFamilyMemberOnlinePacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write((int)familyId);
        stream.Write((ulong)memberId);
        stream.Write(online);
        stream.Write(level);
        stream.Write(heirLevel);
        return stream;
    }
}

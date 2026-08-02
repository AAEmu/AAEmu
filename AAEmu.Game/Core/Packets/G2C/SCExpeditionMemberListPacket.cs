using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Expeditions;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCExpeditionMemberListPacket(uint id, List<ExpeditionMember> members)
    : GamePacket(SCOffsets.SCExpeditionMemberListPacket, 1)
{
    private const int MaxMembersPerPacket = 20;

    public override PacketStream Write(PacketStream stream)
    {
        if (members.Count > MaxMembersPerPacket)
            throw new ArgumentOutOfRangeException(nameof(members), members.Count, "Native expedition member packets contain at most 20 entries.");

        stream.Write((byte)members.Count);
        stream.Write(id); // expedition id
        foreach (var member in members)
            stream.Write(member);
        return stream;
    }
}

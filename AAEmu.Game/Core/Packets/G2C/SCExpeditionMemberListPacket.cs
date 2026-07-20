using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Expeditions;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCExpeditionMemberListPacket(uint total, uint id, List<ExpeditionMember> members)
    : GamePacket(SCOffsets.SCExpeditionMemberListPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(total);
        stream.Write((byte)members.Count); // TODO max length 20
        stream.Write(id); // expedition id
        foreach (var member in members)
            stream.Write(member);
        return stream;
    }
}

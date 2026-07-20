using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Expeditions;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCExpeditionMemberStatusChangedPacket(ExpeditionMember expeditionMember, byte flag)
    : GamePacket(SCOffsets.SCExpeditionMemberStatusChangedPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(expeditionMember);
        stream.Write(flag);
        return stream;
    }
}

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Expeditions;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCExpeditionRolePolicyChangedPacket(ExpeditionRolePolicy rolePolicy, bool success)
    : GamePacket(SCOffsets.SCExpeditionRolePolicyChangedPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(rolePolicy);
        stream.Write(success);
        return stream;
    }
}

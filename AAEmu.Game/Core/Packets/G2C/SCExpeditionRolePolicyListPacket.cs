using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Expeditions;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCExpeditionRolePolicyListPacket(List<ExpeditionRolePolicy> rolePolicies)
    : GamePacket(SCOffsets.SCExpeditionRolePolicyListPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write((byte)rolePolicies.Count);
        foreach (var rolePolicy in rolePolicies) // TODO max length 20
            stream.Write(rolePolicy);
        return stream;
    }
}

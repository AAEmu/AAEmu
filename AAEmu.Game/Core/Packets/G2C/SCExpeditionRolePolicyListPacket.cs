using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Expeditions;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCExpeditionRolePolicyListPacket(List<ExpeditionRolePolicy> rolePolicies)
    : GamePacket(SCOffsets.SCExpeditionRolePolicyListPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        if (rolePolicies.Count > 20)
            throw new ArgumentOutOfRangeException(nameof(rolePolicies), rolePolicies.Count,
                "Native expedition role-policy list packets contain at most 20 entries.");

        stream.Write((byte)rolePolicies.Count);
        foreach (var rolePolicy in rolePolicies)
            stream.Write(rolePolicy);
        return stream;
    }
}

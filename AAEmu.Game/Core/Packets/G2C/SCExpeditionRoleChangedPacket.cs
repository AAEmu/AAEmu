using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Announces one member's role change (NOT a role-policy definition). Wire: characterId (u64, compared
/// client-side against the local player to pick "Your role changed" vs "Member $1's role changed"),
/// member name (string, the "$1" substitution), new role id (u32) - the role's display name is looked
/// up client-side from the role-policy list already cached via SCExpeditionRolePolicyListPacket.
/// </summary>
public class SCExpeditionRoleChangedPacket(uint characterId, string name, byte role)
    : GamePacket(SCOffsets.SCExpeditionRoleChangedPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write((ulong)characterId);
        stream.Write(name);
        stream.Write((uint)role);
        return stream;
    }
}

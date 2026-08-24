using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// SC 0x314 broadcast: u64 worldCharKey, u8 role — mirrors a member's role change to the whole squad.
/// </summary>
public class SCChangeSquadMemberRolePacket(ulong worldCharKey, byte role)
    : GamePacket(SCOffsets.SCChangeSquadMemberRoleBcast, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(worldCharKey);
        stream.Write(role);
        return stream;
    }
}

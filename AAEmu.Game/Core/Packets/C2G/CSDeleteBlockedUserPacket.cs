using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// </summary>
public class CSDeleteBlockedUserPacket() : GamePacket(CSOffsets.CSDeleteBlockedUserPacket, 1)
{
    public ulong BlockedCharacterId { get; private set; }

    public override void Read(PacketStream stream)
    {
        BlockedCharacterId = stream.ReadUInt64();
    }

    public override void Execute()
    {
        Connection.ActiveChar.Blocked.RemoveBlockedUser(BlockedCharacterId);
    }
}

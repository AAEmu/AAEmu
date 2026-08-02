using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>An account attribute was granted or its count changed.</summary>
/// <remarks>
/// i8 AccountAttributeKind, u32 extraKind, i8 worldId, u32 count, i64 startDate, i64 endData.
/// </remarks>
public class SCAccountAttributeUpdatedPacket(
    AccountAttributeKind kind,
    uint extraKind,
    byte worldId,
    uint count,
    DateTime startDate,
    DateTime endDate)
    : GamePacket(SCOffsets.SCAccountAttributeUpdatedPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write((byte)kind);
        stream.Write(extraKind);
        stream.Write(worldId);
        stream.Write(count);
        stream.Write(startDate);
        stream.Write(endDate);
        return stream;
    }
}

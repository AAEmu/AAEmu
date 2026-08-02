using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <remarks>
/// 10.0.2.13 body, named by its own serializer: i16 tl, u64 moneyAmount, u64 (the buyer id, which the
/// serializer calls "type"), string sellToName. 1.2 sent the money and buyer id as u32 and appended a house
/// name the client never reads, so the name string was parsed out of the wrong bytes.
/// </remarks>
public class SCHouseSetForSalePacket(
    ushort tl,
    uint moneyAmount,
    uint sellToPlayerId,
    string sellToName)
    : GamePacket(SCOffsets.SCHouseSetForSalePacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write((short)tl);
        stream.Write((ulong)moneyAmount);
        stream.Write((ulong)sellToPlayerId);
        stream.Write(sellToName);
        return stream;
    }
}

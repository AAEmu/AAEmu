using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Acknowledges one manual random shop refresh for one shop type.
/// </summary>
/// <remarks>
/// Field order, widths and names come from the 10.0.2.13 client's serializer, which passes each
/// value's name alongside the value:
/// s16 ErrorMessage. Only the success (no-error) acknowledgement is sent; the failure codes for
/// this family are not decoded in the corpus.
/// </remarks>
public class SCRandomShopInfoRefreshPacket(ushort errorMessage) : GamePacket(SCOffsets.SCRandomShopInfoRefreshPacket, 1)
{
    public ushort ErrorMessage { get; } = errorMessage;

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(ErrorMessage);
        return stream;
    }
}

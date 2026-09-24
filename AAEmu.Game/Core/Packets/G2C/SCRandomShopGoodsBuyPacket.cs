using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Acknowledges one or more random shop buys: which requested offers the server sold.
/// </summary>
/// <remarks>
/// Field order, widths and names come from the 10.0.2.13 serializer, which passes each value's
/// name alongside the value: s16 ErrorMessage · u32 type · then the "buyCart" block - whose
/// begin/end tags carry no wire bytes - u32 Size and per requested offer a u32 "type" (the same
/// element layout the buy request's "buyGoods" list uses; both sides of the wire share one
/// serializer). The elements echo the display map keys the client asked for; the client marks
/// each echoed offer bought.
/// </remarks>
public class SCRandomShopGoodsBuyPacket(
    short errorMessage,
    uint type,
    IReadOnlyList<uint> boughtGoods) : GamePacket(SCOffsets.SCRandomShopGoodsBuyPacket, 1)
{
    public short ErrorMessage { get; } = errorMessage;

    /// <summary>Record key; echoed from the buy request.</summary>
    public uint Type { get; } = type;

    public IReadOnlyList<uint> BoughtGoods { get; } = boughtGoods;

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(ErrorMessage);
        stream.Write(Type);

        // buyCart block: begin/end tags are zero-wire, so no tag bytes here.
        stream.Write((uint)BoughtGoods.Count);
        foreach (var good in BoughtGoods)
            stream.Write(good);

        return stream;
    }
}

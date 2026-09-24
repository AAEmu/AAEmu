using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Pushes the spent random-shop refresh counters for one record.
/// </summary>
/// <remarks>
/// Field order, widths and names come from the 10.0.2.13 serializer, which passes each value's
/// name alongside the value: s16 ErrorMessage · u8 freeCnt · u8 chargeCnt · u32 type - eight
/// bytes, written in that order (the error word sits first even though the counters live at the
/// lower object offsets). freeCnt/chargeCnt are the SPENT counters: the client enables its
/// refresh button while either sits below the pack max (x2ui/store/buy.lua).
/// </remarks>
public class SCRandomShopBaseInfoUpdatePacket(ushort errorMessage, byte freeCnt, byte chargeCnt, uint @type)
    : GamePacket(SCOffsets.SCRandomShopBaseInfoUpdatePacket, 1)
{
    public ushort ErrorMessage { get; } = errorMessage;

    /// <summary>Free refreshes spent this period.</summary>
    public byte FreeCnt { get; } = freeCnt;

    /// <summary>Paid refreshes spent this period.</summary>
    public byte ChargeCnt { get; } = chargeCnt;

    public uint Type { get; } = @type;

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(ErrorMessage);
        stream.Write(FreeCnt);
        stream.Write(ChargeCnt);
        stream.Write(Type);
        return stream;
    }
}

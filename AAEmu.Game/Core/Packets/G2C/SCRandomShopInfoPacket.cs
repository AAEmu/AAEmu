using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models;
using AAEmu.Game.Models.Game.Merchant;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// The random shop window reply: the per-viewer record header plus the offer map.
/// </summary>
/// <remarks>
/// Field order, widths and names come from the 10.0.2.13 serializer, which passes each value's
/// name alongside the value. The "shopDisplayInfo" section is a block whose begin/end tags carry
/// no wire bytes (the block helpers are presence constants on the reader), so the frame is flat:
/// s16 ErrorMessage · u32 type · then the display header
/// u32 type · u8 freeCnt · u8 chargeCnt · u64 dbid · u64 recordTime · then the "displayGoods"
/// map: u32 Size, and per pair u32 key + element
/// (u8 type · u32 cost · u32 type · u8 order · u8 buyAmount · u32 type).
/// The element's first byte and its two trailing u32s are named only "type" by the serializer -
/// three distinct fields the corpus does not further pin - so they are written as zero and
/// flagged; cost, order and buyAmount are the pinned fields (order is the offer slot, buyAmount
/// the remaining count, which the buy acknowledgement drives to zero). freeCnt/chargeCnt are the
/// SPENT counters: the client enables its refresh button while either sits below the pack max
/// (x2ui/store/buy.lua). recordTime is unix seconds, the in-tree convention for u64 times
/// (SCICSExchangeRatioPacket).
/// </remarks>
public class SCRandomShopInfoPacket(
    short errorMessage,
    uint type,
    uint displayType,
    byte freeCnt,
    byte chargeCnt,
    ulong dbid,
    DateTime recordTime,
    IReadOnlyList<RandomShopOffer> displayGoods) : GamePacket(SCOffsets.SCRandomShopInfoPacket, 1)
{
    public short ErrorMessage { get; } = errorMessage;

    /// <summary>Record key; echoed from the client's open request.</summary>
    public uint Type { get; } = type;

    /// <summary>Display record identity carried in the header.</summary>
    public uint DisplayType { get; } = displayType;

    /// <summary>Refreshes of this kind spent this period.</summary>
    public byte FreeCnt { get; } = freeCnt;

    /// <summary>Paid refreshes of this kind spent this period.</summary>
    public byte ChargeCnt { get; } = chargeCnt;

    public ulong Dbid { get; } = dbid;

    public DateTime RecordTime { get; } = recordTime;

    public IReadOnlyList<RandomShopOffer> DisplayGoods { get; } = displayGoods;

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(ErrorMessage);
        stream.Write(Type);

        // shopDisplayInfo block: begin/end tags are zero-wire, so no tag bytes here.
        stream.Write(DisplayType);
        stream.Write(FreeCnt);
        stream.Write(ChargeCnt);
        stream.Write(Dbid);
        stream.Write(new DateTimeOffset(ServerCalendar.AsUtc(RecordTime)).ToUnixTimeSeconds());

        stream.Write((uint)DisplayGoods.Count);
        foreach (var offer in DisplayGoods)
        {
            stream.Write((uint)offer.Slot);            // map key
            stream.Write((byte)0);                     // element type byte - undecoded
            stream.Write((uint)offer.Cost);
            stream.Write(offer.ItemId);                // the item this offer sells
            stream.Write((byte)offer.Slot);            // order
            stream.Write((byte)(offer.Sold ? 0 : 1));  // buyAmount = remaining
            stream.Write(offer.GoodId);                // the content good row
        }

        return stream;
    }
}

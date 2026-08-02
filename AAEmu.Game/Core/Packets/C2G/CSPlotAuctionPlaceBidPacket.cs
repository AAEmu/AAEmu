using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// TODO: the body is parsed but nothing acts on it yet.
/// </summary>
/// <remarks>
/// Field order, widths and names come from the 10.0.2.13 client's serializer, which passes each
/// value's name alongside the value:
/// </remarks>
public class CSPlotAuctionPlaceBidPacket() : GamePacket(CSOffsets.CSPlotAuctionPlaceBidPacket, 1)
{
    public uint ActivityId { get; private set; }
    public uint AuctionConfigId { get; private set; }
    public uint BidAmount { get; private set; }

    public override void Read(PacketStream stream)
    {
        ActivityId = stream.ReadUInt32();
        AuctionConfigId = stream.ReadUInt32();
        BidAmount = stream.ReadUInt32();
    }
}

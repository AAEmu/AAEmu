using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.PlotAuctions;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// The answer to CSPlotAuctionQueryInfoPacket: the activity's whole bid-info map.
/// </summary>
/// <remarks>
/// Field order pinned from the 10.0.2.13 client two independent ways: the extracted schema
/// (— u32 activityId@16, then a nested serializer call
/// At object offset 24) and the serializer's own field reads, which take a
/// u32 "Size" and then per pair a u32 key followed by the value struct
/// (plotId, myBidAmount, myRanking, currentWinningBid, totalBidders, basePrice — six s32).
/// The catalog line that shows activityId alone stopped at the scalar; the map is the payload
/// the client's X2Player:GetPlotAuctionInfoList cache is built from.
/// </remarks>
public class SCPlotAuctionInfoPacket(uint activityId, IReadOnlyList<PlotAuctionBidInfoRow> rows)
    : GamePacket(SCOffsets.SCPlotAuctionInfoPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(activityId);
        stream.Write((uint)rows.Count);
        foreach (var row in rows)
        {
            // pair key, then the value struct — exactly the client serializer's order; the
            // struct repeats plotId as its own first field.
            stream.Write(row.PlotId);
            stream.Write(row.PlotId);
            stream.Write((int)row.MyBidAmount);
            stream.Write((int)row.MyRanking);
            stream.Write((int)row.CurrentWinningBid);
            stream.Write((int)row.TotalBidders);
            stream.Write((int)row.BasePrice);
        }

        return stream;
    }
}

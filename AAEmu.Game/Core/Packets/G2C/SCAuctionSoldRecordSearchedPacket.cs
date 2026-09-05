using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Auction;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCAuctionSoldRecordSearchedPacket(
    uint itemTemplateId,
    byte grade,
    bool askMarketPriceUi,
    IReadOnlyList<AuctionSoldRecord> days)
    : GamePacket(SCOffsets.SCAuctionSoldRecordSearchedPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(itemTemplateId);
        stream.Write(grade);
        stream.Write(askMarketPriceUi);

        var rows = days ?? [];
        for (var i = 0; i < AuctionHouseRules.SoldRecordDays; i++)
        {
            if (i < rows.Count)
                stream.Write(rows[i]);
            else
                stream.Write(new AuctionSoldRecord { Day = i, ItemTemplateId = itemTemplateId, Grade = grade });
        }

        return stream;
    }
}

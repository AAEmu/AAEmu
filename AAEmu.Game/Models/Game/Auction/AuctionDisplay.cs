using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.Auction;

public class AuctionDisplay : PacketMarshaler
{
    public AuctionLot Lot { get; set; } = new();

    public override void Read(PacketStream stream)
    {
        stream.Read(Lot);
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(Lot);

        return stream;
    }
}

using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.Auction;

public class AuctionDisplay : PacketMarshaler
{
    public AuctionLot Lot { get; set; }

    public AuctionDisplay()
    {
        Lot = new AuctionLot(); // Инициализация Lot
    }

    public override void Read(PacketStream stream)
    {
        Lot.Read(stream);
    }

    public override PacketStream Write(PacketStream stream)
    {
        Lot.Write(stream);

        return stream;
    }
}

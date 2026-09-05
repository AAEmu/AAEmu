using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Auction;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCAuctionMessagePacket(AuctionMessageKind kind, uint itemTemplateId, long money)
    : GamePacket(SCOffsets.SCAuctionMessagePacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write((byte)kind);
        stream.Write(itemTemplateId);
        stream.Write(money);
        return stream;
    }
}

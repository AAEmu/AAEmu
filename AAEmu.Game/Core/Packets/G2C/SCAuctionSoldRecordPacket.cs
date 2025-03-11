using System.Collections.Generic;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Auction;

namespace AAEmu.Game.Core.Packets.G2C;

class SCAuctionSoldRecordPacket : GamePacket
{
    private readonly uint _itemTemplateId;
    private readonly byte _itemGrade;
    private readonly List<AuctionSold> _solds;

    public SCAuctionSoldRecordPacket(uint itemTemplateId, byte itemGrade, List<AuctionSold> solds) : base(SCOffsets.SCAuctionSoldRecordPacket, 5)
    {
        _itemTemplateId = itemTemplateId;
        _itemGrade = itemGrade;
        _solds = solds;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(_itemTemplateId); // itemTemplateId
        stream.Write(_itemGrade); // itemGrade

        foreach (var sold in _solds) // TODO не более 14
        {
            sold.Write(stream);
        }

        return stream;
    }
}

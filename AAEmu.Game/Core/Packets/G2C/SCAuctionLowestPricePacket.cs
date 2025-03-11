using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCAuctionLowestPricePacket : GamePacket
{
    private readonly uint _itemTemplateId;
    private readonly byte _itemGrade;
    private readonly int _moneyAmount;

    public SCAuctionLowestPricePacket(uint itemTemplateId, byte itemGrade, int moneyAmount) : base(SCOffsets.SCAuctionLowestPricePacket, 5)
    {
        _itemTemplateId = itemTemplateId;
        _itemGrade = itemGrade;
        _moneyAmount = moneyAmount;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(_itemTemplateId);
        stream.Write(_itemGrade);
        stream.Write(_moneyAmount);

        return stream;
    }
}

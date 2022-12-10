using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.CashShop;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCICSGoodListPacket : GamePacket
    {
        private readonly bool _pageEnd;
        private readonly ushort _totalPage;
        private readonly CashShopItem _item;
        
        public SCICSGoodListPacket(bool pageEnd, ushort totalPage, CashShopItem item) : base(SCOffsets.SCICSGoodListPacket, 1)
        {
            _pageEnd = pageEnd;
            _totalPage = totalPage;
            _item = item;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_pageEnd);
            stream.Write(_totalPage);
            stream.Write(_item);
            return stream;
        }
    }
}

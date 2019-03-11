using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSICSBuyGoodPacket : GamePacket
    {
        public CSICSBuyGoodPacket() : base(0x11d, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var numBuys = stream.ReadByte();
            for (var i = 0; i < numBuys; i++)
            {
                var cashShopId = stream.ReadInt32();
                var mainTab = stream.ReadByte();
                var subTab = stream.ReadByte();
                var detailIndex = stream.ReadByte();
            }

            var receiverName = stream.ReadString();

            _log.Warn("ICSBuyGood");

            Connection.SendPacket(new SCICSBuyResultPacket(true, 1, "Test", 5555));
        }
    }
}

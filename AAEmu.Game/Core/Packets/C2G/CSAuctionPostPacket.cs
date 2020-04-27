using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSAuctionPostPacket : GamePacket
    {
        public CSAuctionPostPacket() : base(0x0b7, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var npcObjId = stream.ReadBc();
            var itemId = stream.ReadUInt32();
            var startPrice = stream.ReadUInt32();
            var buyoutPrice = stream.ReadUInt32();
            var duration = stream.ReadByte();

            AuctionManager.Instance.AddAuctionItem(Connection.ActiveChar, itemId, startPrice, buyoutPrice, duration);
        }
    }
}

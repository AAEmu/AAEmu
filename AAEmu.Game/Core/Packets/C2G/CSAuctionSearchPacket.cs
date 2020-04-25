using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Auction.Templates;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSAuctionSearchPacket : GamePacket
    {
        public CSAuctionSearchPacket() : base(0x0b8, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var _searchTemplate = new AuctionSearchTemplate();
            var npcObjId = stream.ReadBytes(6);
            _searchTemplate.ItemName = stream.ReadString();
            _searchTemplate.ExactMatch = stream.ReadBoolean();
            _searchTemplate.Grade = stream.ReadByte();
            _searchTemplate.CategoryA = stream.ReadByte();
            _searchTemplate.CategoryB = stream.ReadByte();
            _searchTemplate.CategoryC = stream.ReadByte();
            _searchTemplate.Page = stream.ReadUInt32();
            _searchTemplate.Type = stream.ReadUInt32();
            _searchTemplate.Filter = stream.ReadUInt32();
            _searchTemplate.WorldID = stream.ReadByte();
            _searchTemplate.MinItemLevel = stream.ReadByte();
            _searchTemplate.MaxItemLevel = stream.ReadByte();
            _searchTemplate.SortKind = stream.ReadByte();
            _searchTemplate.SortOrder = stream.ReadByte();

            var foundItems = AuctionManager.Instance.GetAuctionItems(_searchTemplate);
            Connection.SendPacket(new SCAuctionSearchedPacket(foundItems));

        }
    }
}

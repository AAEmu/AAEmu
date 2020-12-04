﻿using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Auction.Templates;
using NLog;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSAuctionSearchPacket : GamePacket
    {

        public CSAuctionSearchPacket() : base(0x0b8, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var _sTemplate = new AuctionSearchTemplate();
            var objId = stream.ReadBytes(6);
            _sTemplate.Player = Connection.ActiveChar;
            _sTemplate.ItemName = stream.ReadString();
            _sTemplate.ExactMatch = stream.ReadBoolean();
            _sTemplate.Grade = stream.ReadByte();
            _sTemplate.CategoryA = stream.ReadByte();
            _sTemplate.CategoryB = stream.ReadByte();
            _sTemplate.CategoryC = stream.ReadByte();
            _sTemplate.Page = stream.ReadUInt32();
            _sTemplate.PlayerId = stream.ReadUInt32();
            _sTemplate.Filter = stream.ReadUInt32();
            _sTemplate.WorldID = stream.ReadByte();
            _sTemplate.MinItemLevel = stream.ReadByte();
            _sTemplate.MaxItemLevel = stream.ReadByte();
            _sTemplate.SortKind = stream.ReadByte();
            _sTemplate.SortOrder = stream.ReadByte();

            var foundItems = AuctionManager.Instance.GetAuctionItems(_sTemplate);
            
            Connection.SendPacket(new SCAuctionSearchedPacket(foundItems, _sTemplate.Page));
        }
    }
}

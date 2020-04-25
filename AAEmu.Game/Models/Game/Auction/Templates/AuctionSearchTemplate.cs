using System;
using System.Collections.Generic;
using System.Text;

namespace AAEmu.Game.Models.Game.Auction.Templates
{
    class AuctionSearchTemplate
    {
        public string ItemName { get; set; }
        public bool ExactMatch { get; set; }
        public byte Grade { get; set; }
        public byte CategoryA { get; set; }
        public byte CategoryB { get; set; }
        public byte CategoryC { get; set; }
        public uint Page { get; set; }
        public uint Type { get; set; }
        public uint Filter { get; set; }
        public uint WorldID { get; set; }
        public byte MinItemLevel { get; set; }
        public byte MaxItemLevel { get; set; }
        public byte SortKind { get; set; }
        public byte SortOrder { get; set; }

    }
}

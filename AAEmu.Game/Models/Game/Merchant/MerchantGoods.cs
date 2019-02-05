using System.Collections.Generic;

namespace AAEmu.Game.Models.Game.Merchant
{   
    public class MerchantGoods
    {
        public uint Id { get; set; }
        public Dictionary<uint, MerchantItem> Items { get; set; }

        public MerchantGoods(uint id)
        {
            Id = id;
            Items = new Dictionary<uint, MerchantItem>();
        }
    }
}

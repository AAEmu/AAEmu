using System.Collections.Generic;

namespace AAEmu.Game.Models.Game.Merchant
{   
    public class MerchantGoods
    {
        public uint Id { get; set; }
        public List<MerchantGoodsItem> Items { get; set; }

        public MerchantGoods(uint id)
        {
            Id = id;
            Items = new List<MerchantGoodsItem>();
        }

        // NOTE: If there is ever a case where one itemTemplate is sold at multiple grades, then this code needs a rework
        public bool SellsItem(uint itemTemplateId)
        {
            foreach (var i in Items)
                if (i.ItemTemplateId == itemTemplateId)
                    return true;
            return false;
        }

        public void AddItemToStock(uint itemTemplateId, byte itemGrade)
        {
            if (SellsItem(itemTemplateId))
                return;
            var newItem = new MerchantGoodsItem();
            newItem.ItemTemplateId = itemTemplateId;
            newItem.Grade = itemGrade;

            Items.Add(newItem);
        }
    }

    public class MerchantGoodsItem
    {
        public uint ItemTemplateId;
        public byte Grade;
    }
}

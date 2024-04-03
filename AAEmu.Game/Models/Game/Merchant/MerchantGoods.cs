using System.Collections.Generic;

namespace AAEmu.Game.Models.Game.Merchant;

public class MerchantGoods
{
    public uint Id { get; set; } // npcId
    public Dictionary<uint, List<MerchantGoodsItem>> Items { get; set; } // npcId, list <MerchantGoodsItem>

    public MerchantGoods(uint npcId)
    {
        Id = npcId;
        Items = new Dictionary<uint, List<MerchantGoodsItem>>();
    }

    // NOTE: If there is ever a case where one itemTemplate is sold at multiple grades, then this code needs a rework
    public bool SellsItem(uint itemTemplateId)
    {
        foreach (var items in Items.Values)
            foreach (var i in items)
            if (i.ItemId == itemTemplateId)
                return true;
        return false;
    }

    //public void AddItemToStock(uint itemTemplateId, byte itemGrade)
    //{
    //    if (SellsItem(itemTemplateId))
    //        return;
    //    var newItem = new MerchantGoodsItem();
    //    newItem.ItemTemplateId = itemTemplateId;
    //    newItem.Grade = itemGrade;

    //    Items.Add(newItem);
    //}
}

public class MerchantGoodsItem
{
    public uint ItemId;
    public byte Grade;
    public byte KindId;
}

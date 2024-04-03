using System.Collections.Generic;

namespace AAEmu.Game.Models.Game.Merchant;

public class MerchantPacks
{
    public uint PackId { get; set; }
    public uint ItemId { get; set; }
    public uint GradeId { get; set; }
    public uint KindId { get; set; }

    public List<MerchantGoodsItem> Items { get; set; } // npcId, list <MerchantGoodsItem>

    public MerchantPacks(uint packId)
    {
        PackId = packId;
        Items = new List<MerchantGoodsItem>();
    }

    // NOTE: If there is ever a case where one itemTemplate is sold at multiple grades, then this code needs a rework
    public bool SellsItem(uint itemTemplateId)
    {
        foreach (var i in Items)
            if (i.ItemId == itemTemplateId)
                return true;
        return false;
    }

    public void AddItemToStock(uint itemTemplateId, byte itemGrade)
    {
        if (SellsItem(itemTemplateId))
            return;
        var newItem = new MerchantGoodsItem();
        newItem.ItemId = itemTemplateId;
        newItem.Grade = itemGrade;

        Items.Add(newItem);
    }
}

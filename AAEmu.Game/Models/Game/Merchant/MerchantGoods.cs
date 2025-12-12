namespace AAEmu.Game.Models.Game.Merchant;

public class MerchantGoods(uint id)
{
    public uint Id { get; set; } = id;
    public List<MerchantGoodsItem> Items { get; set; } = [];

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
        var newItem = new MerchantGoodsItem { ItemTemplateId = itemTemplateId, Grade = itemGrade };

        Items.Add(newItem);
    }
}

public class MerchantGoodsItem
{
    public uint ItemTemplateId;
    public byte Grade;
}

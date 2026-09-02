using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Templates;

namespace AAEmu.Game.Models.Game.DoodadObj;

public readonly record struct DoodadPhysicalGoods(uint ItemTemplateId, ulong FreshnessTime)
{
    public static bool TryResolve(Doodad doodad, IItemManager itemManager, out DoodadPhysicalGoods goods)
    {
        var item = doodad.ItemId != 0 ? itemManager.GetItemByItemId(doodad.ItemId) : null;
        var template = item?.Template ?? itemManager.GetTemplate(doodad.ItemTemplateId);
        if (template is not BackpackTemplate
            {
                BackpackType: BackpackType.TradePack or BackpackType.TradeGoods
            } backpackTemplate)
        {
            goods = default;
            return false;
        }

        ulong freshnessTime = 0;
        if (item is Backpack backpack && backpack.TryGetFreshness(out var freshnessStartTime, out _))
            freshnessTime = checked((ulong)new DateTimeOffset(freshnessStartTime).ToUnixTimeSeconds());

        goods = new DoodadPhysicalGoods(backpackTemplate.Id, freshnessTime);
        return true;
    }
}

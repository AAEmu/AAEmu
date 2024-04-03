using System.Collections.Generic;

namespace AAEmu.Game.Models.Game.Merchant;

public class Merchants
{
    public uint NpcId { get; set; }
    public uint ItemId { get; set; }
    public uint GradeId { get; set; }
    public uint KindId { get; set; }

    // NOTE: If there is ever a case where one itemTemplate is sold at multiple grades, then this code needs a rework
    public static bool SellsItem(uint itemTemplateId, List<Merchants> items)
    {
        foreach (var i in items)
            if (i.ItemId == itemTemplateId)
                return true;
        return false;
    }
}

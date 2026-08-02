using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Items.Templates;

namespace AAEmu.Game.Models.Game.Items;

public class ItemBag : Item
{
    public ItemBag()
    {
    }

    public ItemBag(ulong id, ItemTemplate template, int count) : base(id, template, count)
    {
    }

    public override bool CanDestroy()
    {
        return ItemManager.Instance.GetItemBagContainer(Id) is not { Items.Count: > 0 };
    }
}

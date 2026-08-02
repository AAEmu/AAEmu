using AAEmu.Game.Models.Game.Items.Templates;

namespace AAEmu.Game.Models.Game.Items.Containers;

/// <summary>A persistent coffer subcontainer owned by one ItemBag instance.</summary>
public class ItemBagContainer(uint ownerId, bool createWithNewId) : CofferContainer(ownerId, createWithNewId)
{
    private ulong _parentItemId;

    public ulong ParentItemId
    {
        get => _parentItemId;
        set
        {
            if (_parentItemId == value)
                return;

            _parentItemId = value;
            IsDirty = true;
        }
    }

    public void Configure(ItemBagTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);
        ContainerSize = template.Capacity;
        IsPrivateCoffer = true;
        AllowedItemCategoryIds = template.AllowedItemCategoryIds;
    }

    public void ReassignOwner(ulong ownerId)
    {
        OwnerId = checked((uint)ownerId);
        foreach (var item in Items)
            item.OwnerId = ownerId;
    }
}

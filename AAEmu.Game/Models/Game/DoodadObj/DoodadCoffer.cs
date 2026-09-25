using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Housing;
using AAEmu.Game.Models.Game.Items.Containers;

namespace AAEmu.Game.Models.Game.DoodadObj;

public class DoodadCoffer : Doodad
{
    /// <summary>The mannequin protocol carries no slot field; its appearance is the primary coffer slot.</summary>
    public const byte ManikinDisplaySlot = 0;

    public int Capacity { get; set; }
    public bool IsPrivate { get; set; }
    public bool IsManikin { get; set; }
    public HashSet<int> AllowedItemCategoryIds { get; set; } = [];
    public CofferContainer ItemContainer { get; set; }
    public Character OpenedBy { get; set; }
    public ulong OpenedItemBagId { get; set; }

    public void InitializeCoffer(uint playerId)
    {
        ConfigureItemContainer(ItemManager.Instance.NewCofferContainer(playerId));
    }

    public void ConfigureItemContainer(CofferContainer itemContainer)
    {
        ItemContainer = itemContainer;
        ItemContainer.ContainerSize = Capacity;
        ItemContainer.IsPrivateCoffer = IsPrivate;
        ItemContainer.AllowedItemCategoryIds = AllowedItemCategoryIds;
        ItemContainer.Doodad = this;
    }

    public override bool AllowRemoval()
    {
        return (ItemContainer == null || ItemContainer.Items.Count <= 0) && base.AllowRemoval();
    }

    public override void Delete()
    {
        if (IsPersistent)
            ItemContainer?.Delete();
        base.Delete();
    }

    public override bool AllowedToInteract(Character character)
    {
        var permission = (HousingPermission)Data;
        var privateOwnerOnly = ItemContainer?.CofferType == ChestType.Otherworldly;

        return HousingPermissionRules.CanAccess(character, OwnerId, permission, privateOwnerOnly) &&
               base.AllowedToInteract(character);
    }

    public override ulong GetItemContainerId()
    {
        return ItemContainer?.ContainerId ?? 0;
    }
}

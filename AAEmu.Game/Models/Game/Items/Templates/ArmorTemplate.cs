namespace AAEmu.Game.Models.Game.Items.Templates;

public class ArmorTemplate : EquipItemTemplate
{
    public override Type ClassType => typeof(Armor);

    public Wearable WearableTemplate { get; set; }

    public bool UseAsStat { get; set; }

    public int SkinKindId { get; set; }

    public int RechargeRestrictItemId { get; set; }

    public string NoVisualErrorMessage { get; set; }

    public int ItemId { get; set; }

    public bool InvisibleAsset { get; set; }

    public bool EquipOnlyHasArmorVisual { get; set; }

    public int EnhancedItemMaterialId { get; set; }

    public int AssetId { get; set; }

    public int Asset2Id { get; set; }
    public WearableKind KindTemplate { get; set; }
    public WearableSlot SlotTemplate { get; set; }
    public bool BaseEnchantable { get; set; }
    public bool BaseEquipment { get; set; }
}
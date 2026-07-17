namespace AAEmu.Game.Models.Game.Items.Templates;

public class WeaponTemplate : EquipItemTemplate
{
    public override Type ClassType => typeof(Weapon);

    public bool BaseEnchantable { get; set; }

    public float WornScale { get; set; }

    public bool UseAsStat { get; set; }

    public int SkinKindId { get; set; }

    public int RechargeRestrictItemId { get; set; }

    public int FixedVisualEffectId { get; set; }

    public int EnhancedItemMaterialId { get; set; }

    public float DrawnScale { get; set; }

    public int AssetId { get; set; }
    public Holdable HoldableTemplate { get; set; }
    public bool BaseEquipment { get; set; }
}
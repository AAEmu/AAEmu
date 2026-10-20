namespace AAEmu.Game.Models.Game.Items.Templates;

public class WeaponTemplate : EquipItemTemplate
{
    public override Type ClassType => typeof(Weapon);

    public bool BaseEnchantable { get; set; }
    public Holdable HoldableTemplate { get; set; }
    public bool BaseEquipment { get; set; }
    /// <summary>
    /// <c>item_weapons.useAsStat</c>. False marks play-only / cosmetic instruments
    /// (tooltip caution: disables Songcraft while equipped); those fail equip_ranged=1.
    /// </summary>
    public bool UseAsStat { get; set; } = true;
}
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

/// <summary>
/// The 1.2-era tempering action. 10.0.2.13 has no <c>special_effects</c> rows of this type left -
/// tempering runs through <see cref="ItemRefurbishment"/> now - so this only exists for older data
/// sets, where it sets the item's scale straight to what the effect's values ask for.
/// </summary>
public class ItemCapScale : SpecialEffectAction
{
    protected override SpecialType SpecialEffectActionType => SpecialType.ItemCapScale;

    public override void Execute(BaseUnit caster,
        SkillCaster casterObj,
        BaseUnit target,
        SkillCastTarget targetObj,
        CastAction castObj,
        Skill skill,
        SkillObject skillObject,
        DateTime time,
        int value1,
        int value2,
        int value3,
        int value4)
    {
        if (caster is not Character owner)
            return;

        if (targetObj is not SkillCastItemTarget skillTargetItem)
            return;

        if (owner.Inventory.GetItemById(skillTargetItem.Id) is not EquipItem equipItem)
            return;

        // value1/value2 are the range the old per-skill item_cap_scales lookup used to carry.
        var scaleMin = Math.Max(0, value1);
        var scaleMax = Math.Max(scaleMin + 1, value2);
        var rolled = Random.Shared.Next(scaleMin, scaleMax);

        var maxScale = equipItem.Template?.MaxEnchantScaleId ?? 0;
        equipItem.EnchantScale = (ushort)Math.Min(maxScale, rolled);
        equipItem.IsDirty = true;

        owner.SendPacket(new SCItemDetailUpdatedPacket(equipItem));

        Logger.Debug("ItemCapScale: {0} set item {1} to scale {2}", owner.Name, equipItem.Id, equipItem.EnchantScale);
    }
}

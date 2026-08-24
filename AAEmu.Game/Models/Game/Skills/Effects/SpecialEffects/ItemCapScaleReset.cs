using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

/// <summary>
/// Counterpart of <see cref="ItemCapScale"/>: clears an item's tempering. Like its sibling this has
/// no rows in 10.0.2.13 data and is kept for older data sets.
/// </summary>
public class ItemCapScaleReset : SpecialEffectAction
{
    protected override SpecialType SpecialEffectActionType => SpecialType.ItemCapScaleReset;

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

        equipItem.EnchantScale = 0;
        equipItem.IsDirty = true;

        owner.SendPacket(new SCItemDetailUpdatedPacket(equipItem));

        Logger.Debug("ItemCapScaleReset: {0} cleared tempering on item {1}", owner.Name, equipItem.Id);
    }
}

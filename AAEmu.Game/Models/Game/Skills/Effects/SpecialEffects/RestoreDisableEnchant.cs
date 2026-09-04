using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

/// <summary>
/// Unlocks an item that a failed awakening or a failed high-scale temper left disabled, putting it
/// back in reach of the enchant window.
/// </summary>
public class RestoreDisableEnchant : SpecialEffectAction
{
    protected override SpecialType SpecialEffectActionType => SpecialType.RestoreDisableEnchant;

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
        {
            Logger.Error("RestoreDisableEnchant: caster {0} is not a character", caster?.Id);
            return;
        }

        if (targetObj is not SkillCastItemTarget itemTarget)
        {
            Logger.Warn("RestoreDisableEnchant: target {0} is not an item", targetObj);
            skill.Cancelled = true;
            return;
        }

        if (owner.Inventory.GetItemById(itemTarget.Id) is not EquipItem equipItem)
        {
            Logger.Warn("RestoreDisableEnchant: item {0} not found or not equipment", itemTarget.Id);
            skill.Cancelled = true;
            return;
        }

        if (!equipItem.EnchantDisabled)
        {
            // Nothing to undo - don't burn the restore item over it.
            owner.SendErrorMessage(ErrorMessageType.ItemCannotUse);
            skill.Cancelled = true;
            return;
        }

        equipItem.EnchantDisabled = false;
        equipItem.IsDirty = true;

        owner.SendPacket(new SCItemDetailUpdatedPacket(equipItem));
        owner.SendPacket(new SCRestoreDisableEnchantPacket(equipItem,
            (byte)ItemGradeEnchantResult.RestoreDisable, 0));

        Logger.Debug("RestoreDisableEnchant: {0} unlocked item {1}", owner.Name, equipItem.Id);
    }
}

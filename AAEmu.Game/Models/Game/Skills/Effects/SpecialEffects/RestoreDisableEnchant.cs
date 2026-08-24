using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

/// <summary>
/// Restores an item that was disabled by a failed grade enchant.
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
        if (caster is not Character character || character is null)
            return;

        if (targetObj is not SkillCastItemTarget itemTarget || itemTarget is null)
            return;

        var item = character.Inventory.GetItemById(itemTarget.Id);
        if (item == null)
            return;

        if (!item.HasFlag(ItemFlag.Disabled))
        {
            Logger.Debug("RestoreDisableEnchant: item {0} is not disabled", item.Id);
            return;
        }

        item.RemoveFlag(ItemFlag.Disabled);

        character.SendPacket(new SCItemTaskSuccessPacket(ItemTaskType.EnchantPhysical, [new ItemUpdate(item)], []));
        character.SendPacket(new SCRestoreDisableEnchantPacket(item, item.Grade, item.Grade));
    }
}

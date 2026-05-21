using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

public class GainItem : SpecialEffectAction
{
    public override void Execute(BaseUnit caster,
        SkillCaster casterObj,
        BaseUnit target,
        SkillCastTarget targetObj,
        CastAction castObj,
        Skill skill,
        SkillObject skillObject,
        DateTime time,
        int value1, // Item TemplateId
        int value2, // Item Count
        int value3,
        int value4
    )
    {
        if (caster is not Character character)
            return;

        Logger.Debug("Special effects: GainItem value1 {0}, value2 {1}, value3 {2}, value4 {3}", value1, value2, value3, value4);

        if (value1 <= 0)
        {
            Logger.Warn("Special effects: GainItem ignored invalid item template id {0}", value1);
            return;
        }

        var itemId = (uint)value1;
        var itemCount = value2 > 0 ? value2 : 1;
        var taskType = ItemTaskType.SkillEffectGainItem;

        // Keep the historical behaviour for regular inventory items: they are still
        // created directly in the bag. Only auto-equip backpacks/tradepacks must go
        // through Inventory.TryAddNewItem(), otherwise ItemContainer rejects them
        // because tradepacks cannot be stored in the normal inventory bag.
        var acquired = ItemManager.Instance.IsAutoEquipTradePack(itemId)
            ? character.Inventory.TryAddNewItem(taskType, itemId, itemCount, 0)
            : character.Inventory.Bag.AcquireDefaultItem(taskType, itemId, itemCount, 0);

        if (!acquired)
            Logger.Warn("Special effects: GainItem failed to give item {0} x{1} to character {2}", itemId, itemCount, character.Id);
    }
}

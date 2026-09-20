using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Crafts;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

/// <summary>
/// The board's "Craft requested items" cast: fills the order named in the extras, consumes the
/// crafter's labor, and mails the product and the fee.
/// </summary>
public class ProcessCraftOrder : SpecialEffectAction
{
    public override void Execute(BaseUnit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
        CastAction castObj, Skill skill, SkillObject skillObject, DateTime time,
        int value1, int value2, int value3, int value4)
    {
        if (caster is not Character character)
            return;

        var orderId = 0ul;
        if (skillObject is SkillObjectExtraValues extras)
            CraftOrderProcessRules.TryReadOrderId(extras, out orderId);

        if (orderId == 0)
            CraftOrderManager.Instance.TryTakeProcessOrder(character.Id, out orderId);

        if (orderId == 0)
        {
            Logger.Warn("Special effects: ProcessCraftOrder cast by {0} with no order queued", character.Name);
            return;
        }

        if (!CraftOrderManager.Instance.TryProcess(character, orderId, out var reason))
            Logger.Info("Special effects: ProcessCraftOrder refused for {0}: {1}", character.Name, reason);
    }
}

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Crafts;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

/// <summary>
/// My List Complete: the owner spends Instant tickets and the additional fee, the product is
/// mailed to them, and the listing leaves the board.
/// </summary>
public class ProcessCraftOrderInstant : SpecialEffectAction
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
            Logger.Warn("Special effects: ProcessCraftOrderInstant cast by {0} with no order queued", character.Name);
            return;
        }

        if (!CraftOrderManager.Instance.TryProcessInstant(character, orderId, out var reason))
            Logger.Info("Special effects: ProcessCraftOrderInstant refused for {0}: {1}", character.Name, reason);
    }
}

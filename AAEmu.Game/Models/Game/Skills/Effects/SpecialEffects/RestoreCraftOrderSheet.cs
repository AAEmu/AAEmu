using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Crafts;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

/// <summary>
/// The restore tab's "break the sheet" cast: the sheet the tab queued on 0x14A leaves the bag
/// and the materials that made it come back. The cast itself is a unit caster.
/// </summary>
public class RestoreCraftOrderSheet : SpecialEffectAction
{
    public override void Execute(BaseUnit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
        CastAction castObj, Skill skill, SkillObject skillObject, DateTime time,
        int value1, int value2, int value3, int value4)
    {
        if (caster is not Character character)
            return;

        var sheetId = 0ul;
        if (!CraftOrderManager.Instance.TryTakeRestoreSheet(character.Id, out sheetId) || sheetId == 0)
        {
            if (casterObj is SkillItem itemCaster)
                sheetId = itemCaster.ItemId;
            else if (skillObject is SkillObjectExtraValues extras)
                CraftOrderProcessRules.TryReadOrderId(extras, out sheetId);
        }

        if (sheetId == 0)
        {
            Logger.Warn("Special effects: RestoreCraftOrderSheet cast by {0} with no sheet queued", character.Name);
            return;
        }

        if (!CraftOrderManager.Instance.TryRestoreSheet(character, sheetId, out var reason))
            Logger.Info("Special effects: RestoreCraftOrderSheet refused for {0}: {1}", character.Name, reason);
    }
}

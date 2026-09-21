using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

/// <summary>
/// The folio's "make a request sheet" cast: consumes the materials the named craft needs and hands
/// the player the sheet item that stands for it.
///
/// The craft and the count are not in this effect's data — the cast itself carries them, and
/// <c>CSStartSkillPacket</c> queues them here.
/// </summary>
public class MakeCraftOrderSheet : SpecialEffectAction
{
    public override void Execute(BaseUnit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
        CastAction castObj, Skill skill, SkillObject skillObject, DateTime time,
        int value1, int value2, int value3, int value4)
    {
        if (caster is not Character character)
            return;

        if (!CraftOrderManager.Instance.TryTakeSheetCraft(character.Id, out var craftId, out var count))
        {
            // The cast is still accepted — the sheet simply cannot be made without the folio's choice.
            Logger.Warn("Special effects: MakeCraftOrderSheet cast by {0} with no craft queued", character.Name);
            return;
        }

        if (!CraftOrderManager.Instance.TryCraftSheet(character, craftId, count, out var reason))
            Logger.Info("Special effects: MakeCraftOrderSheet refused for {0}: {1}", character.Name, reason);
    }
}

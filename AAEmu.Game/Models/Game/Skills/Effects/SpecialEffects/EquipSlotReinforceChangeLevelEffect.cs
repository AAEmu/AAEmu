using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

/// <summary>
/// The step the artifact window's Replace button starts: the player spends a serendipity stone to re-roll the
/// effect one tier of a slot holds. The window sends the slot and the tier with the cast (see
/// <c>CSStartSkillPacket</c>), which queues them; this effect is what spends the stone and rolls when the cast
/// lands, not the moment the button was pressed.
/// </summary>
public class EquipSlotReinforceChangeLevelEffect : SpecialEffectAction
{
    protected override SpecialType SpecialEffectActionType { get; set; } =
        SpecialType.EquipSlotReinforceChangeLevelEffect;

    public override void Execute(BaseUnit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
        CastAction castObj, Skill skill, SkillObject skillObject, DateTime time, int value1, int value2, int value3,
        int value4)
    {
        if (caster is not Character character)
            return;

        if (!character.EquipSlotReinforces.ConsumeQueuedEffectReplace(out var slotTypeId, out var triggerLevel))
        {
            Logger.Warn("Equip slot reinforce: {0} replace landed with no slot queued", character.Name);
            return;
        }

        var change = character.EquipSlotReinforces.ReplaceTierEffect(slotTypeId, triggerLevel);
        Logger.Info("Equip slot reinforce: {0} replace landed for slot {1} level {2} -> {3}",
            character.Name, slotTypeId, triggerLevel, change);
    }
}

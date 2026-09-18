using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

/// <summary>
/// The reinforcement step a player starts from the artifact window. The window's Confirm button sends its
/// choice along with the cast (see <c>CSStartSkillPacket</c>), which queues it; this effect is what spends it,
/// so the bar moves and the material is taken when the cast lands rather than the moment the button was
/// pressed.
/// <para>
/// What the window sent is the slot it is on and the <c>equip_slot_reinforce_materials</c> row the player
/// picked; the row is read against the content tables and has to belong to that slot.
/// </para>
/// </summary>
public class EquipSlotReinforceAddExp : SpecialEffectAction
{
    protected override SpecialType SpecialEffectActionType { get; set; } = SpecialType.EquipSlotReinforceAddExp;

    public override void Execute(BaseUnit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
        CastAction castObj, Skill skill, SkillObject skillObject, DateTime time, int value1, int value2, int value3,
        int value4)
    {
        if (caster is not Character character)
            return;

        var change = character.EquipSlotReinforces.ConsumeQueuedWindowFeed();
        Logger.Info("Equip slot reinforce: {0} cast landed -> {1}", character.Name, change);
    }
}

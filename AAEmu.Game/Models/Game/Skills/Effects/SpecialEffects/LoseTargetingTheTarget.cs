using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

/// <summary>
/// Drops the target of the unit the effect lands on, and the target of every unit that can no longer
/// hold it. 미러 워프 (Mirror Warp) 23936/23937 and its 불꽃 / 바위 variants 39321-39324 land on the
/// caster itself; the 42 trigger rows hang off buffs such as 대상 지정 불가 and 사신, where the set of
/// units that must drop <em>their</em> target is decided by the buff's own targeting restriction.
/// </summary>
/// <remarks>
/// content, 10.0.2.13 game_decrypted: 65 <c>special_effects</c> rows of type 146. Six are skill effects
/// (the self-cast Mirror Warp skills, <c>target_type_id</c> 0 with a single-target area) and clear only
/// the caster's own target. The remaining 42 are buff-trigger rows; 26 of them sit on a buff that bars
/// a targeting relation, which is the reversed selection <see cref="LoseTargetingRules"/> makes, and 16
/// sit on a buff that bars nobody, so the reversed selection is empty for them. <c>value1</c> is 4 on
/// 59 rows and 0 on the other six, <c>value2</c> is 1 on three, and no shipped row correlates either
/// with the buff's targeting columns, so neither is read.
/// </remarks>
public class LoseTargetingTheTarget : SpecialEffectAction
{
    protected override SpecialType SpecialEffectActionType => SpecialType.LoseTargetingTheTarget;

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
        if (target is not Unit affectedUnit)
            return;

        // Enumerated before anything is cleared, because the reversed set is defined by the targets
        // that are in place at the moment the effect lands.
        var holders = LoseTargetingRules.SelectHolders(affectedUnit, affectedUnit.ParentWorld?.GetAllUnits());

        ClearTarget(affectedUnit);

        foreach (var holder in holders)
        {
            // Re-checked: the direct clear above runs first, and a holder that was only selected
            // because it pointed at the affected unit must not be told to drop a target it no longer has.
            if (holder.CurrentTarget == null || holder.CurrentTarget.ObjId != affectedUnit.ObjId)
                continue;

            ClearTarget(holder);
        }
    }

    private static void ClearTarget(Unit unit)
    {
        if (WorldIntegration.ZoneAuthority)
        {
            // A zero target bc is the native clear-target sentinel. Wait for the Zone response before
            // changing the World mirror or notifying clients, exactly as LoseTarget does.
            WorldIntegration.RelayTargetChangedToZone?.Invoke(
                unit.ObjId,
                0,
                true);
            return;
        }

        unit.CurrentTarget = null;
        var packet = new SCTargetChangedPacket(unit.ObjId, 0);
        if (unit is Character character)
            character.SendPacket(packet);
        else
            unit.BroadcastPacket(packet, true);
    }
}

using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

/// <summary>
/// Clears the current target of the unit the effect lands on, the way <see cref="LoseTarget"/> does. 미러 워프
/// (Mirror Warp) 23936/23937 and its 불꽃 / 바위 variants 39321-39324 carry it.
/// </summary>
/// <remarks>
/// content, 10.0.2.13 game_decrypted: 65 <c>special_effects</c> rows of type 146, 48 reachable (6
/// <c>skill_effects</c>, 42 <c>buff_triggers</c>). <c>value1</c> is 4 on 59 rows and 0 on the other six, and
/// <c>value2</c> is 1 on three; neither changes which unit loses its target and no shipped row separates
/// them from "clear it", so neither is read.
/// <para>
/// Every shipped row acts on the unit the effect landed on: the Mirror Warp skills are <c>target_type_id</c>
/// 0 with a single-target area, so the effect's target is the warper itself, and the 42 trigger rows hang off
/// buffs such as 대상 지정 불가 and 사신, so the target is that buff's owner. The reading "everybody who had
/// this unit targeted drops it" is not modelled anywhere in the server (there is no reverse index from a
/// unit to the units targeting it), so it is not implemented.
/// </para>
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

        if (WorldIntegration.ZoneAuthority)
        {
            // A zero target bc is the native clear-target sentinel. Wait for the Zone response before
            // changing the World mirror or notifying clients, exactly as LoseTarget does.
            WorldIntegration.RelayTargetChangedToZone?.Invoke(
                affectedUnit.ObjId,
                0,
                true);
            return;
        }

        affectedUnit.CurrentTarget = null;
        var packet = new SCTargetChangedPacket(affectedUnit.ObjId, 0);
        if (affectedUnit is Character character)
            character.SendPacket(packet);
        else
            affectedUnit.BroadcastPacket(packet, true);
    }
}

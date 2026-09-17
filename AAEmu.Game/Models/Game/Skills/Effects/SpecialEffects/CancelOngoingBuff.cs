using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

/// <summary>
/// Takes the buffs a row names off the target through <c>Buffs</c>: a buff tag removes every member of
/// that family that is on the unit, a buff id removes one instance.
/// </summary>
public class CancelOngoingBuff : SpecialEffectAction
{
    protected override SpecialType SpecialEffectActionType => SpecialType.CancelOngoingBuff;

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
        if (target == null)
            return;

        var request = OngoingBuffCancelRules.Resolve(value1, value2);
        if (request.IsNoOp)
            return;

        if (request.BuffTagId > 0)
        {
            // Buffs.RemoveBuffs(tag, count) dereferences the tag's buff list, which is null for a tag the
            // content does not define (Buffs.CheckBuffTag guards for the same reason). Nothing to cancel
            // for an undefined family, so the guard is not a silent drop of a real removal.
            var tagged = SkillManager.Instance.GetBuffsByTagId(request.BuffTagId);
            if (tagged is { Count: > 0 })
                target.Buffs.RemoveBuffs(request.BuffTagId, int.MaxValue);
        }

        if (request.BuffId > 0)
            target.Buffs.RemoveBuff(request.BuffId);
    }
}

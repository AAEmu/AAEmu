using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

public sealed class ReduceBuffTime : SpecialEffectAction
{
    protected override SpecialType SpecialEffectActionType => SpecialType.ReduceBuffTime;

    public override void Execute(BaseUnit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
        CastAction castObj, Skill skill, SkillObject skillObject, DateTime time, int buffId, int buffTagId,
        int milliseconds, int value4)
    {
        if (target == null)
            return;

        IEnumerable<Buff> buffs;
        if (buffId > 0)
        {
            var buff = target.Buffs.GetEffectFromBuffId((uint)buffId);
            buffs = buff == null ? [] : [buff];
        }
        else if (buffTagId > 0)
        {
            var ids = global::AAEmu.Game.Core.Managers.SkillManager.Instance.GetBuffsByTagId((uint)buffTagId) ?? [];
            buffs = ids.Select(target.Buffs.GetEffectFromBuffId).Where(x => x != null);
        }
        else
        {
            return;
        }

        foreach (var buff in buffs)
        {
            var remaining = buff.GetTimeLeft() + milliseconds;
            if (remaining <= 0)
            {
                buff.Exit();
                continue;
            }

            buff.Duration = checked((int)Math.Min(int.MaxValue, buff.GetTimeElapsed() + remaining));
            buff.EndTime = buff.StartTime.AddMilliseconds(buff.Duration);
            buff.UpdateEffect();
        }
    }
}

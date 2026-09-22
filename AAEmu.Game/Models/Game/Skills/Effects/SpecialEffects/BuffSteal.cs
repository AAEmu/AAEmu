using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

/// <summary>
/// Transfers effects between the plot source and target. The ordinary 소드락질 (Leech) rows take beneficial
/// effects from the target; 돌려주기 (Reversal) marks both value1 and value2 to return the source's harmful
/// effects to the target. The transferred instance keeps the time it had left.
/// </summary>
public class BuffSteal : SpecialEffectAction
{
    protected override SpecialType SpecialEffectActionType => SpecialType.BuffSteal;

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
        if (caster == null || target == null || caster.ObjId == target.ObjId)
            return;

        var count = value3;
        var requiredTagId = (uint)Math.Max(0, value4);
        if (count < 1)
            return;

        var mode = BuffStealRules.ResolveMode(value1, value2);
        var donor = mode.ReverseDirection ? caster : target;
        var recipient = mode.ReverseDirection ? target : caster;

        // Passives and system buffs are part of what the unit is, not something a leech can take; the
        // shipped rows name tags that only hold ordinary buffs. GetAllBuffs is the Buffs API that classifies
        // by kind, so the rule can tell 이로운 효과 (beneficial) from a debuff.
        var good = new List<Buff>();
        var bad = new List<Buff>();
        donor.Buffs.GetAllBuffs(good, bad, [], includeAllPassives: false);
        var held = mode.Kind == BuffKind.Bad ? bad : good;

        var candidates = held
            .Where(buff => buff.Template != null)
            .Select(buff => new BuffStealRules.StealCandidate(
                (int)buff.Index,
                buff.Template.BuffId,
                buff.Template.Kind,
                buff.Passive,
                buff.Template.System,
                SkillManager.Instance.GetBuffTags(buff.Template.BuffId) ?? []))
            .ToList();

        foreach (var candidate in BuffStealRules.Select(candidates, count, requiredTagId, mode.Kind))
        {
            var source = donor.Buffs.GetEffectByIndex((uint)candidate.Index);
            if (source?.Template == null)
                continue;

            // Permanent instances answer -1; handing that to AddBuff would write a negative duration, so a
            // permanent buff is re-applied from its template instead (duration 0 = permanent there too).
            var remaining = source.GetTimeLeft();
            var forcedDuration = remaining < 0 ? 0 : (int)remaining;

            var transferred = new Buff(recipient, caster, casterObj, source.Template, skill, DateTime.UtcNow)
            {
                AbLevel = source.AbLevel,
                Charge = source.Charge,
                Stack = source.Stack
            };

            // Dispel before the list removal, the pair Buffs.RemoveBuff performs: without it the victim and
            // everyone watching keep the icon of a buff that is now on somebody else.
            source.Template.Dispel(source.Caster, source.Owner, source);
            donor.Buffs.RemoveEffect(source);
            recipient.Buffs.AddBuff(transferred, forcedDuration: forcedDuration);
        }
    }
}

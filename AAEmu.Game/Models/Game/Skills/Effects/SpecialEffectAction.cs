using AAEmu.Game.Models.Game.Units;

using NLog;

namespace AAEmu.Game.Models.Game.Skills.Effects;

public abstract class SpecialEffectAction
{
    protected static Logger Logger { get; } = LogManager.GetCurrentClassLogger();
    protected virtual SpecialType SpecialEffectActionType { get; set; }

    public abstract void Execute(BaseUnit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
        CastAction castObj, Skill skill, SkillObject skillObject, DateTime time, int value1, int value2, int value3, int value4);

    /// <summary>
    /// v10 special effects carry seven values. Existing actions keep their four-value contract while newer
    /// actions can override this overload when the trailing native fields are meaningful.
    /// </summary>
    public virtual void Execute(BaseUnit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
        CastAction castObj, Skill skill, SkillObject skillObject, DateTime time, int value1, int value2, int value3,
        int value4, int value5, int value6, int value7)
    {
        Execute(caster, casterObj, target, targetObj, castObj, skill, skillObject, time, value1, value2, value3,
            value4);
    }
}

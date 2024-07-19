using System;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Tasks.Skills;

public class UseAutoAttackSkillTask : SkillTask
{
    private readonly Skill _skill;
    private readonly Character _caster;

    public UseAutoAttackSkillTask(Skill skill, Character caster) : base(skill)
    {
        _skill = skill;
        _caster = caster;
        Cancelled = false;
        // _caster.SendMessage($"[UseAutoAttackSkillTask] Created");
    }

    public override void Execute()
    {
        var target = _caster.CurrentTarget as Unit;
        if (_caster.Hp <= 0 || target == null || target.Hp <= 0 || target.ObjId == _caster.ObjId || Cancelled)
        {
            Cancelled = true;
            _caster.IsAutoAttack = false;
            _caster.AutoAttackTask = null;
            // _caster.SendMessage($"[UseAutoAttackSkillTask] Cancelled");
            Cancel();
        }

        if (Cancelled)
            return;

        if (target == null)
            return;

        if (_caster.CanAttack(target))
        {

            var casterCaster = new SkillCasterUnit(_caster.ObjId);
            var targetCaster = new SkillCastUnitTarget(_caster.CurrentTarget.ObjId);
            var skillObject = SkillObject.GetByType(SkillObjectType.None);

            // _caster.SendMessage($"[UseAutoAttackSkillTask] Using {_skill.Template.Id} on {target.ObjId}");
            _skill.Use(_caster, casterCaster, targetCaster, skillObject, true, out _);

            var newDelay = TimeSpan.FromMilliseconds(GetAttackDelay());
            if (newDelay != RepeatInterval)
            {
                _caster.SendMessage($"[AutoAttack] Delay changed {RepeatInterval.TotalMilliseconds} -> {newDelay.TotalMilliseconds}");
                RepeatInterval = newDelay;
            }
        }
        else
        {
            _caster.SendMessage($"[UseAutoAttackSkillTask] Cannot attack with {_skill.Template.Id} on {target.ObjId}");
        }
    }

    /// <summary>
    /// Calculate the delay between Auto-Attacks
    /// </summary>
    /// <returns></returns>
    public double GetAttackDelay()
    {
        // This isn't 100% accurate, but it feels "close enough"
        // TODO: Implement weapon speed delays
        var castTime = _skill.Template.CastingTime * _caster.CastTimeMul;
        var coolDownTime = _skill.Template.CooldownTime * (_caster.GlobalCooldownMul / 100.0);
        var additionalDelay = 1000.0 * (_caster.GlobalCooldownMul / 100.0);
        return castTime + coolDownTime + additionalDelay;
    }
}

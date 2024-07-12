﻿using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Tasks.Skills;

public class UseAutoAttackSkillTask : SkillTask
{
    private readonly Skill _skill;
    private readonly Character _caster;
    private readonly SkillCastTarget _targetCaster;
    private readonly SkillObject _skillObject;

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
        }
        else
        {
            _caster.SendMessage($"[UseAutoAttackSkillTask] Cannot attack with {_skill.Template.Id} on {target.ObjId}");
        }
    }
}

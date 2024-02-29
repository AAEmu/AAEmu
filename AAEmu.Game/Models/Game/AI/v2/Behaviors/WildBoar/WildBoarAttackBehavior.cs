using System;
using System.Linq;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.AI.v2.Params.WildBoar;
using AAEmu.Game.Models.Game.Models;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.AI.v2.Behaviors.WildBoar;

public class WildBoarAttackBehavior : BaseCombatBehavior
{
    // onCombatStartSkill = { 15625 }, 
    // onSpurtSkill = {
    //     { skillType = 14038, healthCondition = 70 },
    // },

    private WildBoarAiParams _aiParams;
    //private float _prevHealth;
    private float _currHealth;
    //private float _healthPercentage;
    private bool _enter;

    public override void Enter()
    {
        Ai.Param = Ai.Owner.Template.AiParams;

        if (!UpdateTarget() || ShouldReturn)
        {
            Ai.GoToReturn();
            return;
        }

        // On Combat Start Skill
        var startCombatSkillId = _aiParams.OnCombatStartSkills.FirstOrDefault();
        Ai.Owner.IsInBattle = true;
        if (startCombatSkillId == 0)
            return;

        Ai.Owner.StopMovement();
        var skillTemplate = SkillManager.Instance.GetSkillTemplate(startCombatSkillId);
        var skill = new Skill(skillTemplate);
        UseSkill(skill, Ai.Owner.CurrentTarget);

        //_healthPercentage = Ai.Owner.Hp / (float)Ai.Owner.MaxHp * 100;
        Ai.Owner.CurrentGameStance = GameStanceType.Combat;
        if (Ai.Owner is { } npc)
        {
            npc.Events.OnCombatStarted(this, new OnCombatStartedArgs { Owner = npc, Target = npc });
        }
        _enter = true;
    }

    public override void Tick(TimeSpan delta)
    {
        if (!_enter)
            return; // not initialized yet Enter()

        Ai.Param ??= new WildBoarAiParams("");

        if (Ai.Param is not WildBoarAiParams aiParams)
            return;

        _aiParams = aiParams;

        if (_aiParams == null)
            return;

        //_prevHealth = _healthPercentage;
        _currHealth = Ai.Owner.Hp / (float)Ai.Owner.MaxHp * 100;
        //_healthPercentage = _currHealth;

        if (!UpdateTarget() || ShouldReturn)
        {
            Ai.GoToReturn();
            return;
        }

        if (CanStrafe && !IsUsingSkill)
            MoveInRange(Ai.Owner.CurrentTarget, delta);

        if (!CanUseSkill)
            return;

        // Spurt or base?
        var targetDist = Ai.Owner.GetDistanceTo(Ai.Owner.CurrentTarget);

        if (_aiParams.OnSpurtSkills == null)
            return;

        var numOfSkills = _aiParams.OnSpurtSkills.Count;

        if (numOfSkills == 0)
        {
            PickSkillAndUseIt(SkillUseConditionKind.InCombat, Ai.Owner.CurrentTarget, targetDist);
            return;
        }

        for (var i = 0; i < numOfSkills; i++)
        {
            var skillData = _aiParams.OnSpurtSkills[i];
            if (_currHealth < skillData.HealthCondition/* && skillData.HealthCondition <= _prevHealth*/)
            {
                var skillTemplate = SkillManager.Instance.GetSkillTemplate(skillData.SkillType);
                var skill = new Skill(skillTemplate);
                if (targetDist >= skill.Template.MinRange && targetDist <= skill.Template.MaxRange)
                {
                    SetMaxWeaponRange(skill, Ai.Owner.CurrentTarget); // set the maximum distance to attack with the skill
                    var result = UseSkill(skill, Ai.Owner.CurrentTarget);
                    if (result == SkillResult.CooldownTime)
                    {
                        PickSkillAndUseIt(SkillUseConditionKind.InCombat, Ai.Owner.CurrentTarget, targetDist);
                    }
                }
            }
            else
            {
                PickSkillAndUseIt(SkillUseConditionKind.InCombat, Ai.Owner.CurrentTarget, targetDist);
            }
        }
    }

    public override void Exit()
    {
        _enter = false;
    }
}

using System;
using System.Linq;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.AI.v2.Params.WildBoar;
using AAEmu.Game.Models.Game.Models;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.Units.Movements;

namespace AAEmu.Game.Models.Game.AI.v2.Behaviors.WildBoar;

public class WildBoarAttackBehavior : BaseCombatBehavior
{
    // onCombatStartSkill = { 15625 }, 
    // onSpurtSkill = {
    //     { skillType = 14038, healthCondition = 70 },
    // },

    private WildBoarAiParams _aiParams;
    private float _currHealth;
    private bool _enter;
    private bool _combatStartSkill;

    public override void Enter()
    {
        Ai.Param = Ai.Owner.Template.AiParams;
        Ai.Owner.CurrentGameStance = GameStanceType.Combat;
        Ai.Owner.CurrentAlertness = MoveTypeAlertness.Combat;
        Ai.Owner.BroadcastPacket(new SCUnitModelPostureChangedPacket(Ai.Owner, Ai.Owner.AnimActionId, false), false);
        
        if (Ai.Owner is { } npc)
        {
            npc.Events.OnCombatStarted(this, new OnCombatStartedArgs { Owner = npc, Target = npc });
        }
        Ai.Owner.IsInBattle = true;
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

        _currHealth = Ai.Owner.Hp / (float)Ai.Owner.MaxHp * 100;

        if (!UpdateTarget() || ShouldReturn)
        {
            Ai.OnNoAggroTarget();
            return;
        }

        if (CanStrafe && !IsUsingSkill)
            MoveInRange(Ai.Owner.CurrentTarget, delta);

        if (!CanUseSkill)
            return;

        if (!_combatStartSkill)
        {
            // On Combat Start Skill. Execute once
            var startCombatSkillId = _aiParams.OnCombatStartSkills.FirstOrDefault();
            if (startCombatSkillId != 0)
            {
                Ai.Owner.StopMovement();
                var skillTemplate = SkillManager.Instance.GetSkillTemplate(startCombatSkillId);
                var skill = new Skill(skillTemplate);
                UseSkill(skill, Ai.Owner.CurrentTarget);
                _combatStartSkill = true;
            }
        }

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
                    SetWeaponRange(skill, Ai.Owner.CurrentTarget); // set the maximum distance to attack with the skill
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

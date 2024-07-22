using System;
using System.Collections.Generic;
using System.Linq;

using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.AI.v2.Params.Archer;
using AAEmu.Game.Models.Game.Models;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Models.Game.AI.v2.Behaviors.Archer;

public class ArcherAttackBehavior : BaseCombatBehavior
{
    public string Phase { get; set; }
    public int MakeAGapCount { get; set; }
    private bool _enter;

    public override void Enter()
    {
        /*
           -- "entity.AI.phase" list
           --   base
           --   tryingMeleeSkill
           --   usedMeleeSkill
           --   tryingRangedDefSkill
           --   usedRangedDefSkill
           --   tryingMakeAGapSkill
           --   needMakeAGap
         */
        Phase = "base";
        MakeAGapCount = 0;
        Ai.Owner.InterruptSkills();
        Ai.Owner.CurrentGameStance = GameStanceType.Combat;
        Ai.Owner.IsInBattle = true;
        if (Ai.Owner is { } npc)
        {
            npc.Events.OnCombatStarted(this, new OnCombatStartedArgs { Owner = npc, Target = npc });
        }
        Ai.Param = Ai.Owner.Template.AiParams;
        _enter = true;
    }

    public override void Tick(TimeSpan delta)
    {
        if (!_enter)
            return; // not initialized yet Enter()

        Ai.Param ??= new ArcherAiParams("");

        if (Ai.Param is not ArcherAiParams aiParams)
            return;

        if (!UpdateTarget() || ShouldReturn)
        {
            Ai.OnNoAggroTarget();
            return;
        }

        if (Phase == "needMakeAGap")
        {
            var idlePosition = Ai.IdlePosition;
            var npcPosition = Ai.Owner.Transform.World.Position;
            var abuserPosition = Ai.Owner.CurrentTarget.Transform.World.Position;

            Ai.Owner.MoveTowards(idlePosition, Ai.Owner.BaseMoveSpeed * (100 / 1000.0f));

            var dist = MathUtil.CalculateDistance(npcPosition, idlePosition, true);
            var dist2 = MathUtil.CalculateDistance(npcPosition, abuserPosition, true);
            if (dist < 1.0f || dist2 > aiParams.PreferedCombatDist)
            {
                Ai.Owner.StopMovement();
                MakeAGapCount++;
                Phase = "tryingRangedDefSkill";
            }
            else
            {
                return;
            }
        }
        else
        if (CanStrafe && !IsUsingSkill)
            MoveInRange(Ai.Owner.CurrentTarget, delta);

        if (!CanUseSkill)
            return;

        #region Pick a skill

        // TODO: Get skill list
        _maxWeaponRange = aiParams.PreferedCombatDist;
        var targetDist = Ai.Owner.GetDistanceTo(Ai.Owner.CurrentTarget);
        var selectedSkill = PickSkill(RequestAvailableSkills(aiParams, targetDist));

        var skillTemplate = SkillManager.Instance.GetSkillTemplate(selectedSkill);
        if (skillTemplate == null)
            return;

        UseSkill(new Skill(skillTemplate), Ai.Owner.CurrentTarget, skillTemplate.CastingTime); // TODO выбрать правильный delay

        #endregion
    }

    public override void Exit()
    {
        _enter = false;
    }

    private void OnUseSkillDone()
    {
        switch (Phase)
        {
            case "tryingMeleeSkill":
                Phase = "usedMeleeSkill";
                break;
            case "tryingRangedDefSkill":
                Phase = "usedRangedDefSkill";
                break;
            case "tryingMakeAGapSkill":
                Phase = "needMakeAGap";
                break;
            case "needMakeAGap":
                Phase = "needMakeAGap";
                break;
            default:
                Phase = "base";
                break;
        }
    }

    // OnRequestSkillInfo
    private List<uint> RequestAvailableSkills(ArcherAiParams aiParams, float trgDist)
    {
        var inMeleeAttackRange = trgDist <= aiParams.MeleeAttackRange;

        var baseList = aiParams.CombatSkills;
        var skillList = new List<uint>();

        if (Phase == "usedMeleeSkill")
        {
            var needMakeAGap = inMeleeAttackRange && MakeAGapCount < aiParams.MaxMakeAGapCount;
            if (needMakeAGap)
            {
                // skillList = entity.AI.param.combatSkills.makeAGap;
                foreach (var acs in baseList)
                {
                    skillList.AddRange(acs.MakeAGap.Where(skillId => !Ai.Owner.Cooldowns.CheckCooldown(skillId)));
                }
                Phase = "tryingMakeAGapSkill";
            }
            else
            {
                Phase = "base";
                // self:OnRequestSkillInfo(entity);    -- call again with phase change ("base")
                return RequestAvailableSkills(aiParams, trgDist);
            }
        }
        else if (Phase == "usedRangedDefSkill")
        {
            //skillList = entity.AI.param.combatSkills.rangedStrong;
            foreach (var acs in baseList)
            {
                skillList.AddRange(acs.RangedStrong.Where(skillId => !Ai.Owner.Cooldowns.CheckCooldown(skillId)));
            }
        }

        if (skillList.Count == 0)
        {
            if (inMeleeAttackRange)
            {
                // skillList = entity.AI.param.combatSkills.melee;
                foreach (var acs in baseList)
                {
                    skillList.AddRange(acs.Melee.Where(skillId => !Ai.Owner.Cooldowns.CheckCooldown(skillId)));
                }
                Phase = "tryingMeleeSkill";
            }
            else
            {
                // skillList = entity.AI.param.combatSkills.rangedDef;
                foreach (var acs in baseList)
                {
                    skillList.AddRange(acs.RangedDef.Where(skillId => !Ai.Owner.Cooldowns.CheckCooldown(skillId)));
                }
                Phase = "tryingRangedDefSkill";
            }
        }

        OnUseSkillDone();

        return skillList;
    }

    private uint PickSkill(List<uint> skills)
    {
        if (skills.Count > 0)
            return skills[Rand.Next(0, skills.Count)];

        if (!Ai.Owner.Cooldowns.CheckCooldown((uint)Ai.Owner.Template.BaseSkillId))
            return (uint)Ai.Owner.Template.BaseSkillId;

        return 0; // no melee Skill
    }
}

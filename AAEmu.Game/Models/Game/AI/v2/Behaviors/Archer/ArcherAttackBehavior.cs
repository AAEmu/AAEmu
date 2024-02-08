using System;
using System.Collections.Generic;
using System.Linq;

using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.AI.v2.Params.Archer;
using AAEmu.Game.Models.Game.Models;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.AI.v2.Behaviors.Archer;

public class ArcherAttackBehavior : BaseCombatBehavior
{
    public string Phase { get; set; }
    public int MakeAGapCount { get; set; }

    public override void Enter()
    {
        MakeAGapCount = 0;
        Ai.Owner.InterruptSkills();
        Ai.Owner.CurrentGameStance = GameStanceType.Combat;
        if (Ai.Owner is { } npc)
        {
            npc.Events.OnCombatStarted(this, new OnCombatStartedArgs { Owner = npc, Target = npc });
        }
    }

    public override void Tick(TimeSpan delta)
    {
        var aiParams = Ai.Owner.Template.AiParams as ArcherAiParams;
        if (aiParams == null)
            return;

        if (!UpdateTarget() || ShouldReturn)
        {
            Ai.GoToReturn();
            return;
        }

        if (CanStrafe && !IsUsingSkill)
            MoveInRange(Ai.Owner.CurrentTarget, delta);

        if (!CanUseSkill)
            return;

        #region Pick a skill

        Ai.Owner.StopMovement();
        Ai.Owner.IsInBattle = true;

        // TODO: Get skill list
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
    }

    private void OnUseSkillDone(bool inMeleeAttackRange)
    {
        Phase = inMeleeAttackRange ? "tryingMeleeSkill" : "tryingRangedDefSkill";

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
                MakeAGapCount++;
                break;
            default:
                Phase = "base";
                break;
        }
    }

    private List<uint> RequestAvailableSkills(ArcherAiParams aiParams, float trgDist)
    {
        var inMeleeAttackRange = trgDist <= aiParams.MeleeAttackRange;

        OnUseSkillDone(inMeleeAttackRange);

        var baseList = aiParams.CombatSkills;
        var skillList = new List<uint>();

        switch (Phase)
        {
            case "usedMeleeSkill":
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
                        if (!Ai.Owner.Cooldowns.CheckCooldown((uint)Ai.Owner.Template.BaseSkillId))
                            skillList.Add((uint)Ai.Owner.Template.BaseSkillId);
                    }

                    break;
                }
            case "usedRangedDefSkill":
                {
                    //skillList = entity.AI.param.combatSkills.rangedStrong;
                    foreach (var acs in baseList)
                    {
                        skillList.AddRange(acs.RangedStrong.Where(skillId => !Ai.Owner.Cooldowns.CheckCooldown(skillId)));
                    }

                    break;
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

        return skillList;
    }

    private uint PickSkill(List<uint> skills)
    {
        if (skills.Count > 0)
            return skills[Rand.Next(0, skills.Count)];

        if (!Ai.Owner.Cooldowns.CheckCooldown((uint)Ai.Owner.Template.BaseSkillId))
            return (uint)Ai.Owner.Template.BaseSkillId;

        return 2; // melee Skill
    }
}

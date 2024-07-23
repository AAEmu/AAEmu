using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;

using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.AI.v2.Controls;
using AAEmu.Game.Models.Game.AI.v2.Params.Almighty;
using AAEmu.Game.Models.Game.Models;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Models.Game.AI.v2.Behaviors.Common;

public class FollowPathBehavior : BaseCombatBehavior
{
    public AlmightyNpcAiParams _aiParams;
    private bool _enter;

    public override void Enter()
    {
        Ai.Owner.InterruptSkills();
        _skillQueue = new Queue<AiSkill>();
        Ai.Owner.CurrentGameStance = GameStanceType.Fly;

        _combatStartTime = DateTime.UtcNow;

        if (Ai.Owner is { IsInBattle: false } npc)
        {
            npc.Events.OnCombatStarted(this, new OnCombatStartedArgs { Owner = npc, Target = npc });
        }
        Ai.Param = Ai.Owner.Template.AiParams;

        Ai.Owner.IsInPatrol = true;
        Ai.Owner.Simulation.MoveToPathEnabled = true;
        Ai.Owner.Simulation.GoToPath(Ai.Owner, true);

        _enter = true;
    }

    public override void Tick(TimeSpan delta)
    {
        if (!_enter)
            return; // not initialized yet Enter()

        //if (Ai.Param is not AlmightyNpcAiParams aiParams)
        //   return;

        //_aiParams = aiParams;

        if (!UpdateTarget())
            Ai.Owner.SetTarget(Ai.Owner);

        if (CheckAggression())
            return;

        if (CheckAlert())
            return;
        
        //var targetDist = Ai.Owner.GetDistanceTo(Ai.Owner.CurrentTarget);
        //PickSkillAndUseIt(SkillUseConditionKind.InIdle, Ai.Owner, targetDist);

        // If still aggro, go back to combat
        if (Ai.Owner.IsInBattle && !Ai.Owner.AggroTable.IsEmpty)
        {
            Ai.GoToCombat();
            return;
        }

        // Queue empty? refill!
        if (Ai.AiPathPointsRemaining.Count <= 0 && Ai.AiPathPoints.Count > 0 && Ai.AiPathLooping)
        {
            Ai.AiPathLooping = false;
            foreach (var aiPathPoint in Ai.AiPathPoints)
            {
                Ai.AiPathPointsRemaining.Enqueue(aiPathPoint);
            }
        }

        // Are we there yet?
        if (Ai.Owner.Simulation.TargetPosition != Vector3.Zero && MathUtil.CalculateDistance(Ai.Owner.Simulation.TargetPosition, Ai.Owner.Transform.World.Position, true) < Ai.Owner.Template.Scale)
        {
            Ai.Owner.Simulation.TargetPosition = Vector3.Zero;
        }

        // No current target? Set it!
        if (Ai.Owner.Simulation.TargetPosition == Vector3.Zero && Ai.AiPathPointsRemaining.Count > 0)
        {
            var nextPos = Ai.AiPathPointsRemaining.Dequeue();
            switch (nextPos.Action)
            {
                case AiPathPointAction.None:
                    break;
                case AiPathPointAction.DisableLoop:
                    Ai.AiPathLooping = false;
                    break;
                case AiPathPointAction.EnableLoop:
                    Ai.AiPathLooping = true;
                    break;
                case AiPathPointAction.Speed:
                    if (float.TryParse(nextPos.Param, CultureInfo.InvariantCulture, out var newSpeed))
                        Ai.AiPathSpeed = newSpeed;
                    break;
                case AiPathPointAction.StanceFlags:
                    if (byte.TryParse(nextPos.Param, out var newStance))
                        Ai.AiPathStanceFlags = newStance;
                    break;
                case AiPathPointAction.ReturnToCommandSet:
                    Ai.GoToRunCommandSet();
                    return;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            // Set next move point if it's not zero
            if (!nextPos.Position.Equals(Vector3.Zero))
            {
                Ai.Owner.Simulation.TargetPosition = nextPos.Position;
                // Move the idle "home" location long with the path, so it doesn't immediately trigger a return to home state when going into combat
                Ai.IdlePosition = nextPos.Position;
            }
        }

        // We know where to go? Then go that direction
        if (Ai.Owner.Simulation.TargetPosition != Vector3.Zero)
        {
            Ai.Owner.MoveTowards(Ai.Owner.Simulation.TargetPosition, Ai.AiPathSpeed * Ai.Owner.BaseMoveSpeed * (delta.Milliseconds / 1000.0f), Ai.AiPathStanceFlags);
        }

        if (Ai.AiPathPointsRemaining.Count <= 0 && Ai.AiPathLooping == false)
        {
            Ai.GoToIdle();
        }
/*
        CheckPipeName();
        if (!CanUseSkill)
            return;

        _strafeDuringDelay = false;

        #region Pick a skill

        var delay = 150;
        // Will delay for 150 Milliseconds to eliminate the hanging of the skill
        if (!Ai.Owner.CheckInterval(delay))
        {
            Logger.Trace($"Skill: CooldownTime [{delay}]!");
        }
        else
        {
            if (_skillQueue.Count == 0)
            {
                RefreshSkillQueue(_aiParams.AiPathSkillLists);
                RefreshSkillQueue(_aiParams.AiSkillLists);
            }

            var selectedSkill = _skillQueue.Dequeue();
            if (selectedSkill == null)
                return;
            var skillTemplate = SkillManager.Instance.GetSkillTemplate(selectedSkill.SkillId);
            if (skillTemplate == null)
                return;

            UseSkill(new Skill(skillTemplate), Ai.Owner.CurrentTarget, selectedSkill.Delay);

            _strafeDuringDelay = selectedSkill.Strafe;
        }

        #endregion

        var healthRatio = (float)Ai.Owner.Hp / Ai.Owner.MaxHp * 100;
        if (!(healthRatio <= 80f))
            return;
        */

        /*
        Ai.Owner.IsInPatrol = false;
        Ai.Owner.Simulation.MoveToPathEnabled = false;
        Ai.Owner.StopMovement();
        Ai.GoToDefaultBehavior();
        */
    }

    public override void Exit()
    {
        _enter = false;
    }

    private bool RefreshSkillQueue(List<AiSkillList> skillLists)
    {
        var targetDist = Ai.Owner.GetDistanceTo(Ai.Owner.CurrentTarget);
        var aiSkillLists = RequestAvailableAiSkillList(skillLists);
        if (aiSkillLists.Count > 0)
        {
            // select a set of skills by dice
            var selectedSkillList = aiSkillLists.RandomElementByWeight(s => s.Dice);
            if (selectedSkillList != null)
            {
                _aiParams.RestorationOnReturn = selectedSkillList.Restoration;
                _aiParams.GoReturnState = selectedSkillList.GoReturn;

                Logger.Info($"RefreshSkillQueue: Dice Check: Ai.Owner={Ai.Owner.ObjId}:{Ai.Owner.TemplateId}, healthRange=[{selectedSkillList.HealthRangeMin}.{selectedSkillList.HealthRangeMax}], timeElapsed={(DateTime.UtcNow - _combatStartTime).TotalSeconds}, timeRange=[{selectedSkillList.TimeRangeStart}.{selectedSkillList.TimeRangeEnd}], skills Count={selectedSkillList.SkillLists.Count}, Dice={selectedSkillList.Dice}");

                // add startAiSkill first to the queue if it is available
                if (selectedSkillList.StartAiSkills.Count > 0)
                {
                    foreach (var skill in selectedSkillList.StartAiSkills)
                    {
                        if (Ai.Owner.Cooldowns.CheckCooldown(skill.SkillId))
                        {
                            continue;
                        }
                        Logger.Info($"RefreshSkillQueue: Ai.Owner={Ai.Owner.ObjId}:{Ai.Owner.TemplateId}, StartAiSkill={skill.SkillId}");
                        _skillQueue.Enqueue(skill);
                    }
                }

                var availableSkillList = RequestAvailableSkillList(selectedSkillList.SkillLists);

                // then add from skillLists
                var skillList = availableSkillList.RandomElementByWeight(s => s.Dice);
                if (skillList != null)
                {
                    Logger.Info($"RefreshSkillQueue: Dice Check: Ai.Owner={Ai.Owner.ObjId}:{Ai.Owner.TemplateId}, healthRange=[{skillList.HealthRangeMin}.{skillList.HealthRangeMax}], timeElapsed={(DateTime.UtcNow - _combatStartTime).TotalSeconds}, timeRange=[{skillList.TimeRangeStart}.{skillList.TimeRangeEnd}], skills Count={skillList.Skills.Count}, Dice={skillList.Dice}");

                    foreach (var skill in skillList.Skills)
                    {
                        if (Ai.Owner.Cooldowns.CheckCooldown(skill.SkillId))
                        {
                            continue;
                        }
                        var template = SkillManager.Instance.GetSkillTemplate(skill.SkillId);
                        if (template == null) { continue; }
                        if (targetDist >= template.MinRange && targetDist <= template.MaxRange || template.TargetType == SkillTargetType.Self)
                        {
                            Logger.Info($"RefreshSkillQueue: Ai.Owner={Ai.Owner.ObjId}:{Ai.Owner.TemplateId}, trgDist={targetDist}, rangeDist=[{template.MinRange}.{template.MaxRange}], skill={skill.SkillId}");
                            _skillQueue.Enqueue(skill);
                        }
                        Logger.Info($"RefreshSkillQueue: Ai.Owner={Ai.Owner.ObjId}:{Ai.Owner.TemplateId}, skill={skill.SkillId}");
                    }
                }
            }

            return _skillQueue.Count > 0;
        }

        if (Ai.Owner.Template.BaseSkillId == 0) { return false; }

        var item = new AiSkill();
        item.SkillId = (uint)Ai.Owner.Template.BaseSkillId;
        item.Strafe = Ai.Owner.Template.BaseSkillStrafe;
        item.Delay = Ai.Owner.Template.BaseSkillDelay;
        Logger.Info($"RefreshSkillQueue: Use BaseSkill: Ai.Owner={Ai.Owner.ObjId}:{Ai.Owner.TemplateId}, skill={item.SkillId}");
        _skillQueue.Enqueue(item);

        return true;

    }

    private List<AiSkillList> RequestAvailableAiSkillList(List<AiSkillList> aiSkillLists)
    {
        var healthRatio = (int)((float)Ai.Owner.Hp / Ai.Owner.MaxHp * 100);

        var baseList = aiSkillLists.AsEnumerable();
        var timeElapsed = (DateTime.UtcNow - _combatStartTime).TotalSeconds;

        var availableSkillLists = new List<AiSkillList>();
        foreach (var s in baseList)
        {
            // first, let's select the allowed skills based on life value
            if ((s.HealthRangeMin == 0 && s.HealthRangeMax == 0) || (s.HealthRangeMin < healthRatio && healthRatio <= s.HealthRangeMax))
            {
                Logger.Info($"RequestAvailableSkillList: HealthCheck passed successfully for Ai.Owner={Ai.Owner.ObjId}:{Ai.Owner.TemplateId}, health={healthRatio}, healthRange=[{s.HealthRangeMin}.{s.HealthRangeMax}], timeElapsed={timeElapsed}, timeRange=[{s.TimeRangeStart}.{s.TimeRangeEnd}], SkillLists Count={s.SkillLists.Count}, Dice={s.Dice}");

                // then, select the allowed skills by time
                if ((s.TimeRangeStart >= 0 && s.TimeRangeEnd > 0) || (s.TimeRangeStart > 0 && s.TimeRangeEnd >= 0))
                {
                    if (s.TimeRangeStart <= timeElapsed && s.TimeRangeEnd == 0)
                    {
                        Logger.Info($"RequestAvailableSkillList: TimeCheck passed successfully for Ai.Owner={Ai.Owner.ObjId}:{Ai.Owner.TemplateId}, health={healthRatio}, healthRange=[{s.HealthRangeMin}.{s.HealthRangeMax}], timeElapsed={timeElapsed}, timeRange=[{s.TimeRangeStart}.{s.TimeRangeEnd}], SkillLists Count={s.SkillLists.Count}, Dice= {s.Dice}");

                        availableSkillLists.Add(s);
                    }
                    else if (s.TimeRangeStart <= timeElapsed && timeElapsed <= s.TimeRangeEnd)
                    {
                        Logger.Info($"RequestAvailableSkillList: TimeCheck passed successfully for Ai.Owner={Ai.Owner.ObjId}:{Ai.Owner.TemplateId}, health={healthRatio}, healthRange=[{s.HealthRangeMin}.{s.HealthRangeMax}], timeElapsed={timeElapsed}, timeRange=[{s.TimeRangeStart}.{s.TimeRangeEnd}], SkillLists Count={s.SkillLists.Count}, Dice= {s.Dice}");

                        availableSkillLists.Add(s);
                    }
                }
                else if (s.TimeRangeStart == 0 && s.TimeRangeEnd == 0)
                {
                    Logger.Info($"RequestAvailableSkillList: TimeCheck passed successfully for Ai.Owner={Ai.Owner.ObjId}:{Ai.Owner.TemplateId}, health={healthRatio}, healthRange=[{s.HealthRangeMin}.{s.HealthRangeMax}], timeElapsed={timeElapsed}, timeRange=[{s.TimeRangeStart}.{s.TimeRangeEnd}], SkillLists Count={s.SkillLists.Count}, Dice= {s.Dice}");

                    availableSkillLists.Add(s);
                }
            }
        }

        return availableSkillLists;
    }

    private List<SkillList> RequestAvailableSkillList(List<SkillList> skillLists)
    {
        var healthRatio = (int)((float)Ai.Owner.Hp / Ai.Owner.MaxHp * 100);

        var baseList = skillLists.AsEnumerable();
        var timeElapsed = (DateTime.UtcNow - _combatStartTime).TotalSeconds;

        var availableSkillLists = new List<SkillList>();
        foreach (var s in baseList)
        {
            // first, let's select the allowed skills based on life value
            if ((s.HealthRangeMin == 0 && s.HealthRangeMax == 0) || (s.HealthRangeMin < healthRatio && healthRatio <= s.HealthRangeMax))
            {
                Logger.Info($"RequestAvailableSkillList: HealthCheck passed successfully for Ai.Owner={Ai.Owner.ObjId}:{Ai.Owner.TemplateId}, health={healthRatio}, healthRange=[{s.HealthRangeMin}.{s.HealthRangeMax}], timeElapsed={timeElapsed}, timeRange=[{s.TimeRangeStart}.{s.TimeRangeEnd}], skills Count={s.Skills.Count}, Dice={s.Dice}");

                // then, select the allowed skills by time
                if ((s.TimeRangeStart >= 0 && s.TimeRangeEnd > 0) || (s.TimeRangeStart > 0 && s.TimeRangeEnd >= 0))
                {
                    if (s.TimeRangeStart <= timeElapsed && s.TimeRangeEnd == 0)
                    {
                        Logger.Info($"RequestAvailableSkillList: TimeCheck passed successfully for Ai.Owner={Ai.Owner.ObjId}:{Ai.Owner.TemplateId}, health={healthRatio}, healthRange=[{s.HealthRangeMin}.{s.HealthRangeMax}], timeElapsed={timeElapsed}, timeRange=[{s.TimeRangeStart}.{s.TimeRangeEnd}], skills Count={s.Skills.Count}, Dice= {s.Dice}");

                        availableSkillLists.Add(s);
                    }
                    else if (s.TimeRangeStart <= timeElapsed && timeElapsed <= s.TimeRangeEnd)
                    {
                        Logger.Info($"RequestAvailableSkillList: TimeCheck passed successfully for Ai.Owner={Ai.Owner.ObjId}:{Ai.Owner.TemplateId}, health={healthRatio}, healthRange=[{s.HealthRangeMin}.{s.HealthRangeMax}], timeElapsed={timeElapsed}, timeRange=[{s.TimeRangeStart}.{s.TimeRangeEnd}], skills Count={s.Skills.Count}, Dice= {s.Dice}");

                        availableSkillLists.Add(s);
                    }
                }
                else if (s.TimeRangeStart == 0 && s.TimeRangeEnd == 0)
                {
                    Logger.Info($"RequestAvailableSkillList: TimeCheck passed successfully for Ai.Owner={Ai.Owner.ObjId}:{Ai.Owner.TemplateId}, health={healthRatio}, healthRange=[{s.HealthRangeMin}.{s.HealthRangeMax}], timeElapsed={timeElapsed}, timeRange=[{s.TimeRangeStart}.{s.TimeRangeEnd}], skills Count={s.Skills.Count}, Dice= {s.Dice}");

                    availableSkillLists.Add(s);
                }
            }
        }

        return availableSkillLists;
    }
}

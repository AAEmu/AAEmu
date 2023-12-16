using System;
using System.Collections.Generic;
using System.Linq;

using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.AI.v2.Params.Almighty;
using AAEmu.Game.Models.Game.AI.V2.Params;
using AAEmu.Game.Models.Game.Models;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.AI.v2.Behaviors.Common;

public class AlmightyAttackBehavior : BaseCombatBehavior
{
    private AlmightyNpcAiParams _aiParams;
    private Queue<AiSkill> _skillQueue;
    private DateTime _combatStartTime;

    public override void Enter()
    {
        Ai.Owner.InterruptSkills();
        _aiParams = Ai.Owner.Template.AiParams as AlmightyNpcAiParams;
        _skillQueue = new Queue<AiSkill>();
        Ai.Owner.CurrentGameStance = GameStanceType.Combat;
        //if (_aiParams != null)
        //{
        //    _combatStartTime = DateTime.UtcNow - TimeSpan.FromMilliseconds(_aiParams.AlertDuration * 1000); // отложим время начала боя на задержку перед боем
        //}
        //else
        //{
        //    _combatStartTime = DateTime.UtcNow;
        //}
        _combatStartTime = DateTime.UtcNow;

        if (Ai.Owner is { IsInBattle: false } npc)
        {
            npc.Events.OnCombatStarted(this, new OnCombatStartedArgs { Owner = npc, Target = npc });
        }
    }

    public override void Tick(TimeSpan delta)
    {
        if (_aiParams == null)
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

        _strafeDuringDelay = false;

        #region Pick a skill

        var targetDist = Ai.Owner.GetDistanceTo(Ai.Owner.CurrentTarget);

        if (_skillQueue.Count == 0)
        {
            if (!RefreshSkillQueue(targetDist))
                return;
        }

        var selectedSkill = _skillQueue.Dequeue();
        if (selectedSkill == null)
            return;
        var skillTemplate = SkillManager.Instance.GetSkillTemplate(selectedSkill.SkillId);
        if (skillTemplate != null)
        {
            if (targetDist >= skillTemplate.MinRange && targetDist <= skillTemplate.MaxRange)
            {
                Ai.Owner.StopMovement();
                UseSkill(new Skill(skillTemplate), Ai.Owner.CurrentTarget, selectedSkill.Delay);
                _strafeDuringDelay = selectedSkill.Strafe;
                return;
            }
        }
        // If skill list is empty, get Base skill
        #endregion

        // PickSkillAndUseIt(SkillUseConditionKind.InCombat, Ai.Owner);

    }

    public override void Exit()
    {
    }

    private bool RefreshSkillQueue(float trgDist)
    {
        var availableSkills = RequestAvailableSkillList(trgDist);

        if (availableSkills.Count > 0)
        {
            // выбираем набор скиллов по дайс
            var selectedSkillList = availableSkills.RandomElementByWeight(s => s.Dice);
            if (selectedSkillList != null)
            {
                Logger.Info($"RefreshSkillQueue: Dice Check: Ai.Owner={Ai.Owner.ObjId}:{Ai.Owner.TemplateId}, healthRange=[{selectedSkillList.HealthRangeMin}.{selectedSkillList.HealthRangeMax}], timeElapsed={(DateTime.UtcNow - _combatStartTime).TotalSeconds}, timeRange=[{selectedSkillList.TimeRangeStart}.{selectedSkillList.TimeRangeEnd}], skills Count={selectedSkillList.Skills.Count}, Dice={selectedSkillList.Dice}");
                foreach (var skill in selectedSkillList.Skills)
                {
                    Logger.Info($"RefreshSkillQueue: Ai.Owner={Ai.Owner.ObjId}:{Ai.Owner.TemplateId}, skill={skill.SkillId}");
                    _skillQueue.Enqueue(skill);
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

    private List<AiSkillList> RequestAvailableSkillList(float trgDist)
    {
        var healthRatio = (int)((float)Ai.Owner.Hp / Ai.Owner.MaxHp * 100);

        var baseList = _aiParams.AiSkillLists.AsEnumerable();
        var timeElapsed = (DateTime.UtcNow - _combatStartTime).TotalSeconds;

        //if (_aiParams.AlertDuration > timeElapsed)
        //{
        //    return new List<AiSkillList>(); // пропускаем время в которое нельзя нападать
        //}

        //baseList = baseList.Where(s => ((s.TimeRangeStart >= 0 && s.TimeRangeEnd > 0) || (s.TimeRangeStart > 0 && s.TimeRangeEnd >= 0))
        //                               && ((s.TimeRangeStart <= timeElapsed && timeElapsed <= s.TimeRangeEnd) ||
        //                                   (s.TimeRangeStart <= timeElapsed && s.TimeRangeEnd == 0)));
        //baseList = baseList.Where(s => s.HealthRangeMin <= healthRatio && healthRatio <= s.HealthRangeMax);

        var aiSkillLists = new List<AiSkillList>();
        //var rnd = Rand.Next(1, 1000);
        foreach (var s in baseList)
        {
            // сначала подберем разрешенные скиллы по величине жизни
            if ((s.HealthRangeMin == 0 && s.HealthRangeMax == 0) || (s.HealthRangeMin <= healthRatio && healthRatio <= s.HealthRangeMax))
            {
                Logger.Info($"RequestAvailableSkillList: HealthCheck passed successfully for Ai.Owner={Ai.Owner.ObjId}:{Ai.Owner.TemplateId}, health={healthRatio}, healthRange=[{s.HealthRangeMin}.{s.HealthRangeMax}], timeElapsed={timeElapsed}, timeRange=[{s.TimeRangeStart}.{s.TimeRangeEnd}], skills Count={s.Skills.Count}, Dice={s.Dice}");
                // затем, подберем разрешенные скиллы по времени
                if ((s.TimeRangeStart >= 0 && s.TimeRangeEnd > 0) || (s.TimeRangeStart > 0 && s.TimeRangeEnd >= 0))
                {
                    if (s.TimeRangeStart <= timeElapsed && s.TimeRangeEnd == 0)
                    {
                        Logger.Info($"RequestAvailableSkillList: TimeCheck passed successfully for Ai.Owner={Ai.Owner.ObjId}:{Ai.Owner.TemplateId}, health={healthRatio}, healthRange=[{s.HealthRangeMin}.{s.HealthRangeMax}], timeElapsed={timeElapsed}, timeRange=[{s.TimeRangeStart}.{s.TimeRangeEnd}], skills Count={s.Skills.Count}, Dice= {s.Dice}");
                        aiSkillLists.Add(s);
                        foreach (var skill in s.Skills)
                        {
                            Logger.Info($"RequestAvailableSkillList: Ai.Owner={Ai.Owner.ObjId}:{Ai.Owner.TemplateId}, skill={skill.SkillId}");
                        }
                    }
                    else if (s.TimeRangeStart <= timeElapsed && timeElapsed <= s.TimeRangeEnd)
                    {
                        Logger.Info($"RequestAvailableSkillList: TimeCheck passed successfully for Ai.Owner={Ai.Owner.ObjId}:{Ai.Owner.TemplateId}, health={healthRatio}, healthRange=[{s.HealthRangeMin}.{s.HealthRangeMax}], timeElapsed={timeElapsed}, timeRange=[{s.TimeRangeStart}.{s.TimeRangeEnd}], skills Count={s.Skills.Count}, Dice= {s.Dice}");
                        aiSkillLists.Add(s);
                        foreach (var skill in s.Skills)
                        {
                            Logger.Info($"RequestAvailableSkillList: Ai.Owner={Ai.Owner.ObjId}:{Ai.Owner.TemplateId}, skill={skill.SkillId}");
                        }
                    }
                }
            }
        }

        // проверяем на кулдаун скилла и оставляем первый подходящий
        //baseList = baseList.Where(s => s.Skills.All(skill => !Ai.Owner.Cooldowns.CheckCooldown(skill.SkillId)));
        var aiSkillLists2 = new List<AiSkillList>();
        var check = false;
        foreach (var s in aiSkillLists)
        {
            foreach (var skill in s.Skills)
            {
                if (Ai.Owner.Cooldowns.CheckCooldown(skill.SkillId)) { continue; }
                //var tmpAiSkill = s.CloneJson();
                //var tmpSkillList = new List<AiSkill> { skill };
                //tmpAiSkill.Skills = tmpSkillList;
                //aiSkillLists2.Add(tmpAiSkill);
                Logger.Info($"RequestAvailableSkillList2: Ai.Owner={Ai.Owner.ObjId}:{Ai.Owner.TemplateId}, CheckCooldown passed successfully for skill={skill.SkillId}");
                check = true;
            }

            if (check)
            {
                aiSkillLists2.Add(s);
            }
        }

        // далее проверим расстояние до цели и дальность скилла
        //baseList = baseList.Where(s => s.Skills.All(skill =>
        //{
        //    var template = SkillManager.Instance.GetSkillTemplate(skill.SkillId);
        //    return (template != null && (trgDist >= template.MinRange && trgDist <= template.MaxRange || template.TargetType == SkillTargetType.Self));
        //}));
        var aiSkillLists3 = new List<AiSkillList>();
        check = false;
        foreach (var s in aiSkillLists2)
        {
            foreach (var skill in s.Skills)
            {
                var template = SkillManager.Instance.GetSkillTemplate(skill.SkillId);
                if (template == null) { continue; }
                if (trgDist >= template.MinRange && trgDist <= template.MaxRange || template.TargetType == SkillTargetType.Self)
                {
                    check = true;
                    Logger.Info($"RequestAvailableSkillList3: Ai.Owner={Ai.Owner.ObjId}:{Ai.Owner.TemplateId}, trgDist={trgDist}, rangeDist=[{template.MinRange}.{template.MaxRange}], skill={skill.SkillId}");
                }
                else
                {
                    Logger.Info($"RequestAvailableSkillList3: Ai.Owner={Ai.Owner.ObjId}:{Ai.Owner.TemplateId}, trgDist={trgDist}, rangeDist=[{template.MinRange}.{template.MaxRange}], skill=none");
                }
            }

            if (check)
            {
                aiSkillLists3.Add(s);
            }
        }

        return aiSkillLists3.ToList();
    }
}

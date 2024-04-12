using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;

using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.AI.AStar;
using AAEmu.Game.Models.Game.AI.v2.Framework;
using AAEmu.Game.Models.Game.AI.v2.Params;
using AAEmu.Game.Models.Game.AI.v2.Params.Almighty;
using AAEmu.Game.Models.Game.Items.Templates;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.SkillControllers;
using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Models.Game.AI.v2.Behaviors;

public abstract class BaseCombatBehavior : Behavior
{
    protected bool _strafeDuringDelay;
    protected string _pipeName;
    protected uint _phaseType;
    protected DateTime _combatStartTime;
    protected Queue<AiSkill> _skillQueue;
    private bool _startingSkillAlreadyUsed;

    protected const int Delay = 350;

    public void MoveInRange(BaseUnit target, TimeSpan delta)
    {
        if (Ai?.Owner == null)
            return;

        if (Ai.Owner.Buffs.HasEffectsMatchingCondition(e =>
                e.Template.Stun
                || e.Template.Sleep
                || e.Template.Root
                || e.Template.Knockdown
                || e.Template.Fastened)
            || Ai.Owner.IsDead)
        {
            return;
        }

        if (Ai.Owner.Buffs.CheckBuffs(SkillManager.Instance.GetBuffsByTagId((uint)SkillConstants.Shackle)) ||
            Ai.Owner.Buffs.CheckBuffs(SkillManager.Instance.GetBuffsByTagId((uint)SkillConstants.Snare)))
        {
            return;
        }

        if ((Ai.Owner.ActiveSkillController?.State ?? SkillController.SCState.Ended) == SkillController.SCState.Running)
            return;

        //Ai.Owner.Template.AttackStartRangeScale * 4,
        //var range = 2f;// Ai.Owner.Template.AttackStartRangeScale * 6;
        var range = Ai.Owner.Template.AttackStartRangeScale;
        if (Ai.Owner.Template.UseRangeMod)
        {
            if (_maxWeaponRange != 0)
                range *= _maxWeaponRange;
        }

        if (Ai.Owner.Template.BaseSkillId == 2 && Ai.Owner.Template.Skills.Count == 0 && range == 4)
        {
            range -= 1f; // Fix that ID=7927, Plateau Earth Elemental can hit with a melee attack
        }
        var speed = Ai.Owner.BaseMoveSpeed * (delta.Milliseconds / 1000.0f);
        var distanceToTarget = Ai.Owner.GetDistanceTo(target, true);

        if (AppConfiguration.Instance.World.GeoDataMode && Ai.Owner.Transform.WorldId > 0)
        {
            // TODO найдем путь к abuser, только если координаты цели изменились
            if (target != null && Ai.PathNode?.pos2 != null && Ai.PathNode != null)
            {
                if (!Ai.PathNode.pos2.Equals(new Point(target.Transform.World.Position.X, target.Transform.World.Position.Y, target.Transform.World.Position.Z)))
                {
                    var stopWatch = new Stopwatch();
                    stopWatch.Start();
                    // TODO найдем путь к abuser
                    Ai.Owner.FindPath((Unit)target);
                    stopWatch.Stop();
                    // Toss warning if it took a long time
                    if (stopWatch.Elapsed.Ticks >= TimeSpan.TicksPerMillisecond)
                        Logger.Warn($"FindPath took {stopWatch.Elapsed} for Ai.Owner.ObjId:{Ai.Owner.ObjId}, Owner.TemplateId {Ai.Owner.TemplateId}");
                    // запомним новые координаты цели
                    Ai.PathNode.pos2 = new Point(target.Transform.World.Position.X, target.Transform.World.Position.Y, target.Transform.World.Position.Z);
                }
            }

            if (target != null && Ai.PathNode != null)
            {
                if (Ai.PathNode.findPath.Count > 0 && !Ai.PathNode.findPath[0].Equals(Point.Zero))
                {
                    // TODO взять точку к которой движемся
                    var position = new Vector3(Ai.PathNode.Position.X, Ai.PathNode.Position.Y, Ai.PathNode.Position.Z);
                    distanceToTarget = MathUtil.CalculateDistance(Ai.Owner.Transform.World.Position, position, true);
                    if (distanceToTarget > range)
                    {
                        Ai.Owner.MoveTowards(position, speed);
                    }
                    else
                    {
                        // TODO взять следующую точку к которой движемся
                        Ai.PathNode.Current++;
                        if (Ai.PathNode.Current >= Ai.PathNode.findPath.Count)
                        {
                            Ai.Owner.StopMovement();
                            Ai.PathNode.findPath = new List<Point>();
                            return;
                        }

                        Ai.PathNode.Position = Ai.PathNode.findPath[(int)Ai.PathNode.Current];
                    }
                }
                else
                {
                    if (distanceToTarget > range)
                        Ai.Owner.MoveTowards(target.Transform.World.Position, speed);
                    else
                        Ai.Owner.StopMovement();
                }
            }
            else
            {
                if (distanceToTarget > range && target != null)
                    Ai.Owner.MoveTowards(target.Transform.World.Position, speed);
                else
                    Ai.Owner.StopMovement();
            }
        }
        else
        {
            if (distanceToTarget > range && target != null)
                Ai.Owner.MoveTowards(target.Transform.World.Position, speed);
            else
                Ai.Owner.StopMovement();
        }
    }

    protected bool CanStrafe
    {
        get
        {
            return DateTime.UtcNow > _delayEnd || _strafeDuringDelay;
        }
    }

    protected bool IsUsingSkill
    {
        get
        {
            return Ai.Owner.SkillTask != null || Ai.Owner.ActivePlotState != null;
        }
    }

    protected bool CanUseSkill
    {
        get
        {
            if (IsUsingSkill)
                return false;
            if ((Ai.Owner?.ActiveSkillController?.State ?? SkillController.SCState.Ended) == SkillController.SCState.Running)
                return false;
            if (Ai.Owner != null && Ai.Owner.Buffs.HasEffectsMatchingCondition(e => e.Template.Stun || e.Template.Sleep || e.Template.Silence))
                return false;
            return Ai.Owner != null && DateTime.UtcNow >= _delayEnd && !Ai.Owner.IsGlobalCooldowned;
        }
    }

    // TODO: Absolute return dist
    protected bool ShouldReturn
    {
        get
        {
            var returnDistance = 50f;
            var absoluteReturnDistance = 200f;

            if (Ai.Owner.Template.ReturnDistance > 0)
            {
                returnDistance = Ai.Owner.Template.ReturnDistance;
            }
            if (Ai.Owner.Template.AbsoluteReturnDistance > 0)
            {
                absoluteReturnDistance = Ai.Owner.Template.AbsoluteReturnDistance;
            }

            if (Ai.Owner.CurrentTarget == null)
                return true; // нет цели, возвращаемся

            var distanceToTarget = MathUtil.CalculateDistance(Ai.Owner.Transform.World.Position, Ai.Owner.CurrentTarget.Transform.World.Position, true);
            var distanceToIdlePosition = MathUtil.CalculateDistance(Ai.Owner.Transform.World.Position, Ai.IdlePosition.Local.Position, true);

            var res = distanceToTarget > returnDistance || distanceToIdlePosition > returnDistance;
            if (res)
                res = distanceToIdlePosition <= absoluteReturnDistance; // если больше, то нужен телепорт на место спавна
            return res;
        }
    }

    public bool UpdateTarget()
    {
        // We might want to optimize this somehow.
        var aggroList = Ai.Owner.AggroTable.Values;
        var abusers = aggroList.OrderByDescending(o => o.TotalAggro).Select(o => o.Owner).ToList();

        foreach (var abuser in abusers)
        {
            //Ai.Owner.LookTowards(abuser.Transform.World.Position); // Prevents archers from escaping (they spin around all the time)

            if (AppConfiguration.Instance.World.GeoDataMode && Ai.Owner.Transform.WorldId > 0)
            {
                // включена геодата и не основной мир
                // geodata enabled and not the main world
                if (Ai.Owner.UnitIsVisible(abuser) && !abuser.IsDead)
                {
                    if (Ai.Owner.CurrentAggroTarget != abuser.ObjId && !Ai.AlreadyTargetted)
                    {
                        // TODO найдем путь к abuser
                        Ai.Owner.FindPath(abuser);
                    }
                    Ai.Owner.CurrentAggroTarget = abuser.ObjId;
                    Ai.Owner.SetTarget(abuser);
                    UpdateAggroHelp(abuser);

                    return true;
                }
            }
            else
            {
                if (Ai.Owner.UnitIsVisible(abuser) && !abuser.IsDead)
                {
                    // check that such an Npc is in the database, there are cases that it is in the game, but not in the database
                    var currentTarget = abuser.ObjId > 0 ? WorldManager.Instance.GetUnit(abuser.ObjId) : null;
                    if (currentTarget == null)
                        continue;

                    Ai.Owner.CurrentAggroTarget = abuser.ObjId;
                    Ai.Owner.SetTarget(abuser);
                    UpdateAggroHelp(abuser);
                    return true;
                }
            }
            Ai.Owner.ClearAggroOfUnit(abuser);
        }

        Ai.Owner.SetTarget(null);
        return false;
    }

    protected void CheckPipeName()
    {
        if (_pipeName == "phase_dragon_ground" || _phaseType == 1) // "PHASE_DRAGON_GROUND = 1;"
        {
            // try to find Z first in GeoData, and then in HeightMaps, if not found, leave Z as it is
            var updZ = WorldManager.Instance.GetHeight(Ai.Owner.Transform.ZoneId, Ai.Owner.Transform.Local.Position.X, Ai.Owner.Transform.Local.Position.Y);
            Ai.Owner.Transform.Local.SetHeight(updZ);
        }
        else if (_pipeName == "phase_dragon_fly_hovering" || _phaseType == 2) // "PHASE_DRAGON_HOVERING = 2;"
        {
            Ai.Owner.Transform.Local.SetHeight(Ai.Owner.Transform.Local.Position.Z + 15f);
            Ai.Owner.StopMovement();
        }
        else if (_pipeName == "phase_dragon_fly_path")
        {
            Ai.GoToFollowPath();
        }
    }

    protected bool RefreshSkillQueue(List<AiSkillList> skillLists, AiParams aiParams)
    {
        var targetDist = Ai.Owner.GetDistanceTo(Ai.Owner.CurrentTarget);
        var aiSkillLists = RequestAvailableAiSkillList(skillLists);
        if (aiSkillLists.Count > 0)
        {
            // select a set of skills by dice
            var selectedSkillList = aiSkillLists.RandomElementByWeight(s => s.Dice);
            if (selectedSkillList != null)
            {
                _pipeName = selectedSkillList.PipeName;
                _phaseType = selectedSkillList.PhaseType;
                aiParams.RestorationOnReturn = selectedSkillList.Restoration;
                aiParams.GoReturnState = selectedSkillList.GoReturn;

                Logger.Info($"RefreshSkillQueue: Dice Check: Ai.Owner={Ai.Owner.ObjId}:{Ai.Owner.TemplateId}, healthRange=[{selectedSkillList.HealthRangeMin}.{selectedSkillList.HealthRangeMax}], timeElapsed={(DateTime.UtcNow - _combatStartTime).TotalSeconds}, timeRange=[{selectedSkillList.TimeRangeStart}.{selectedSkillList.TimeRangeEnd}], skills Count={selectedSkillList.SkillLists.Count}, Dice={selectedSkillList.Dice}");

                // add startAiSkill first to the queue if it is available
                if (selectedSkillList.StartAiSkills.Count > 0 && !_startingSkillAlreadyUsed)
                {
                    foreach (var skill in selectedSkillList.StartAiSkills)
                    {
                        if (Ai.Owner.Cooldowns.CheckCooldown(skill.SkillId))
                        {
                            continue;
                        }
                        Logger.Info($"RefreshSkillQueue: Ai.Owner={Ai.Owner.ObjId}:{Ai.Owner.TemplateId}, StartAiSkill={skill.SkillId}");
                        _skillQueue.Enqueue(skill);
                        _startingSkillAlreadyUsed = true;
                    }
                }

                var availableSkillList = RequestAvailableSkillList(selectedSkillList.SkillLists);

                // then add from skillLists
                var skillList = availableSkillList.RandomElementByWeight(s => s.Dice);
                if (skillList == null)
                    return _skillQueue.Count > 0;
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

    public SkillResult PickSkillAndUseIt(SkillUseConditionKind kind, BaseUnit target, float targetDist)
    {
        var res = SkillResult.InvalidSkill;
        // Attack behavior probably only uses base skill ?
        var skills = new List<NpcSkill>();
        if (Ai.Owner.Template.Skills.TryGetValue(kind, out var templateSkill))
        {
            skills = templateSkill;
        }
        if (skills.Count > 0)
        {
            skills = skills
                .Where(s => !Ai.Owner.Cooldowns.CheckCooldown(s.SkillId))
                .Where(s =>
                {
                    var template = SkillManager.Instance.GetSkillTemplate(s.SkillId);
                    return template != null && (targetDist >= template.MinRange && targetDist <= template.MaxRange || template.TargetType == SkillTargetType.Self);
                }).ToList();
        }

        if (targetDist == 0 && kind == SkillUseConditionKind.InIdle)
        {
            // This SkillTargetType.Self & SkillUseConditionKind.InIdle
            if (skills.Count <= 0)
            {
                return res;
            }
            var skillSelfId = skills[Rand.Next(skills.Count)].SkillId;
            var skillTemplateSelf = SkillManager.Instance.GetSkillTemplate(skillSelfId);
            var skillSelf = new Skill(skillTemplateSelf);

            var delay1 = (int)(Ai.Owner.Template.BaseSkillDelay * 1000);
            if (Ai.Owner.Template.BaseSkillDelay == 0)
            {
                const uint Delay1 = 10000u;
                const uint Delay2 = 13000u;
                delay1 = (int)Rand.Next(Delay1, Delay2);
            }

            if (this.CheckInterval(delay1))
            {
                Logger.Trace("PickSkillAndUseIt:UseSelfSkill Owner.ObjId {0}, Owner.TemplateId {1}, SkillId {2}", Ai.Owner.ObjId, Ai.Owner.TemplateId, skillTemplateSelf.Id);
                res = UseSkill(skillSelf, target);
            }
            return res;
        }

        // This SkillUseConditionKind.InCombat
        var pickedSkillId = (uint)Ai.Owner.Template.BaseSkillId;
        if (skills.Count > 0)
        {
            pickedSkillId = skills[Rand.Next(skills.Count)].SkillId;
        }

        // Hackfix for Melee attack. Needs to look at the held weapon (if any) or default to 3m
        if (pickedSkillId == 2 && targetDist > 4.0f)
        {
            return SkillResult.TooFarRange;
        }
        var skillTemplate = SkillManager.Instance.GetSkillTemplate(pickedSkillId);
        var skill = new Skill(skillTemplate);

        SetMaxWeaponRange(skill, target); // установим максимальную дистанцию для атаки скиллом

        var delay2 = (int)(Ai.Owner.Template.BaseSkillDelay * 1000);
        if (Ai.Owner.Template.BaseSkillDelay == 0)
        {
            const uint Delay1 = 1500u;
            const uint Delay2 = 1550u;
            delay2 = (int)Rand.Next(Delay1, Delay2);
        }

        if (this.CheckInterval(delay2))
        {
            Logger.Trace("PickSkillAndUseIt:UseSkill Owner.ObjId {0}, Owner.TemplateId {1}, SkillId {2} on Target {3}", Ai.Owner.ObjId, Ai.Owner.TemplateId, skillTemplate.Id, target.ObjId);
            res = UseSkill(skill, target);
        }

        return res;
    }

    /// <summary>
    /// Use a skill
    /// </summary>
    /// <param name="skill">Skill object to use</param>
    /// <param name="target">Target Unit</param>
    /// <param name="delay">Delay (in seconds) after this skill is used before the next one is allowed</param>
    /// <returns>Skill result of the used skill</returns>
    public SkillResult UseSkill(Skill skill, BaseUnit target, float delay = 0)
    {
        if (target == null)
        {
            return SkillResult.NoTarget;
        }

        if (skill == null)
        {
            return SkillResult.Failure;
        }

        if (Ai.Owner.Cooldowns.CheckCooldown(skill.Id))
        {
            return SkillResult.CooldownTime;
        }

        var targetDist = Ai.Owner.GetDistanceTo(target);
        if (targetDist < skill.Template.MinRange)
        {
            return SkillResult.TooCloseRange;
        }

        if (targetDist > skill.Template.MaxRange)
        {
            return SkillResult.TooFarRange;
        }

        _nextTimeToDelay = delay;
        var skillCaster = SkillCaster.GetByType(SkillCasterType.Unit);
        skillCaster.ObjId = Ai.Owner.ObjId;

        SkillCastTarget skillCastTarget;
        switch (skill.Template.TargetType)
        {
            case SkillTargetType.Pos:
                var pos = Ai.Owner.Transform.World.Position;
                skillCastTarget = new SkillCastPositionTarget()
                {
                    ObjId = Ai.Owner.ObjId,
                    PosX = pos.X,
                    PosY = pos.Y,
                    PosZ = pos.Z,
                    PosRot = Ai.Owner.Transform.World.ToRollPitchYawDegrees().Z // (float)MathUtil.ConvertDirectionToDegree(pos.RotationZ) //Is this rotation right?
                };
                break;
            default:
                skillCastTarget = SkillCastTarget.GetByType(SkillCastTargetType.Unit);
                skillCastTarget.ObjId = target.ObjId;
                break;
        }

        var skillObject = SkillObject.GetByType(SkillObjectType.None);

        skill.Callback = OnSkillEnded;
        var result = skill.Use(Ai.Owner, skillCaster, skillCastTarget, skillObject);
        // fix the eastward turn when using SelfSkill
        if (skill.Template.TargetType != SkillTargetType.Self && result == SkillResult.Success)
            Ai.Owner.LookTowards(target.Transform.World.Position);
        return result;
    }

    public virtual void OnSkillEnded()
    {
        try
        {
            _delayEnd = DateTime.UtcNow.AddSeconds(_nextTimeToDelay);
        }
        catch
        {
            // Do nothing
        }
    }

    public void OnEnemySeen(Unit target)
    {
        Ai.Owner.AddUnitAggro(AggroKind.Damage, target, 1);
        //Ai.GoToCombat();
        //Ai.OnAggroTargetChanged();
        Ai.GoToAlert();
    }

    public void CheckAggression()
    {
        if (!Ai.Owner.Template.Aggression) { return; }
        var nearbyUnits = WorldManager.GetAround<Unit>(Ai.Owner, CheckSightRangeScale(10f));

        foreach (var unit in nearbyUnits)
        {
            if (unit.IsDead || unit.Hp <= 0)
                continue; // not counting dead Npc

            // Need to check for stealth detection here
            if (Ai.Owner.Template.SightFovScale >= 2.0f || MathUtil.IsFront(Ai.Owner, unit))
            {
                if (Ai.Owner.CanAttack(unit))
                {
                    OnEnemySeen(unit);
                    break;
                }
            }
            else
            {
                var rangeOfUnit = MathUtil.CalculateDistance(Ai.Owner, unit, true);
                if (rangeOfUnit < 3 * Ai.Owner.Template.SightRangeScale)
                {
                    if (Ai.Owner.CanAttack(unit))
                    {
                        OnEnemySeen(unit);
                        break;
                    }
                }
            }
        }
    }

    public void UpdateAggroHelp(Unit abuser, int radius = 20)
    {
        var npcs = WorldManager.GetAround<Npc>(Ai.Owner, CheckSightRangeScale(radius));
        if (npcs == null)
            return;

        foreach (var npc in npcs
                     .Where(npc => npc.Template.Aggression && !npc.IsInBattle && npc.Template.AcceptAggroLink)
                     .Where(npc => npc.GetDistanceTo(npc.Ai.Owner) <= npc.Template.AggroLinkHelpDist))
        {
            if (Ai.Owner.IsInBattle)
                return; // already in battle, let's not change the target

            npc.Ai.Owner.AddUnitAggro(AggroKind.Damage, abuser, 1);
            npc.Ai.OnAggroTargetChanged();
        }
    }

    public void SetMaxWeaponRange(Skill skill, BaseUnit target)
    {
        var unit = (Unit)target;
        // Check if target is within range
        var skillRange = Ai.Owner.ApplySkillModifiers(skill, SkillAttribute.Range, skill.Template.MaxRange);

        var minRangeCheck = skill.Template.MinRange * 1.0;
        var maxRangeCheck = skillRange;

        // HACKFIX : Used mostly for boats, since the actual position of the doodad is the boat's origin, and not where it is displayed
        // TODO: Do a check based on model size or bounding box instead

        // If weapon is used to calculate range, use that
        if (skill.Template.WeaponSlotForRangeId > 0)
        {
            var minWeaponRange = 0.0f; // Fist default
            var maxWeaponRange = 3.0f; // Fist default
            if (unit.Equipment.GetItemBySlot(skill.Template.WeaponSlotForRangeId)?.Template is WeaponTemplate weaponTemplate)
            {
                minWeaponRange = weaponTemplate.HoldableTemplate.MinRange;
                maxWeaponRange = weaponTemplate.HoldableTemplate.MaxRange;
            }

            minRangeCheck = minWeaponRange;
            maxRangeCheck = maxWeaponRange;
        }

        _maxWeaponRange = (float)maxRangeCheck;
    }
}

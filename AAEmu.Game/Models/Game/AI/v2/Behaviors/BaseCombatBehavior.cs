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
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.SkillControllers;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.StaticValues;

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

        var speed = Ai.GetRealMovementSpeed(Ai.Owner.BaseMoveSpeed);
        var moveFlags = Ai.GetRealMovementFlags(speed);
        speed *= (delta.Milliseconds / 1000.0);

        if (Ai.Owner.Buffs.CheckBuffs(SkillManager.Instance.GetBuffsByTagId((uint)TagsEnum.Fish)))
        {
            // Sports fish movement logic
            // Halve the speed for the sports fish movement logic
            speed *= 0.5;

            // Buff 1021: Move left (i.e. as far left as possible)
            if (Ai.Owner.Buffs.CheckBuffs(SkillManager.Instance.GetBuffsByTagId((uint)TagsEnum.Left)))
            {
                var currentPosition = Ai.Owner.Transform.World.Position;
                // Define a target far to the left 
                var leftTarget = new Vector3(currentPosition.X - 10000, currentPosition.Y, currentPosition.Z);
                Ai.Owner.LookTowards(leftTarget);
                Ai.Owner.MoveTowards(leftTarget, (float)speed, moveFlags);
                return;
            }

            // Buff 1020: Move right
            if (Ai.Owner.Buffs.CheckBuffs(SkillManager.Instance.GetBuffsByTagId((uint)TagsEnum.Right)))
            {
                var currentPosition = Ai.Owner.Transform.World.Position;
                var rightTarget = new Vector3(currentPosition.X + 10000, currentPosition.Y, currentPosition.Z);
                Ai.Owner.LookTowards(rightTarget);
                Ai.Owner.MoveTowards(rightTarget, (float)speed, moveFlags);
                return;
            }

            // Buff 1023: Run away from the player's location (i.e. the target)
            if (Ai.Owner.Buffs.CheckBuffs(SkillManager.Instance.GetBuffsByTagId((uint)TagsEnum.Back)))
            {
                var currentPosition = Ai.Owner.Transform.World.Position;
                var playerPosition = target.Transform.World.Position;
                // Calculate the opposite direction from the player
                var runDirection = currentPosition - playerPosition;
                if (runDirection.Length() == 0)
                {
                    // Fallback in case we're exactly on top of the player: default to right
                    runDirection = new Vector3(1, 0, 0);
                }
                runDirection = Vector3.Normalize(runDirection);
                // Create a target far away in that direction
                var runTarget = currentPosition + runDirection * 10000;
                Ai.Owner.LookTowards(runTarget);
                Ai.Owner.MoveTowards(runTarget, (float)speed, moveFlags);
                return;
            }

            // Buff 1022: Descend (move downward)
            if (Ai.Owner.Buffs.CheckBuffs(SkillManager.Instance.GetBuffsByTagId((uint)TagsEnum.Descend)))
            {
                var currentPosition = Ai.Owner.Transform.World.Position;
                var descendTarget = new Vector3(currentPosition.X, currentPosition.Y - 10000, currentPosition.Z);
                Ai.Owner.LookTowards(descendTarget);
                Ai.Owner.MoveTowards(descendTarget, (float)speed, moveFlags);
                return;
            }

            // If no specific fish buff is active, remain still.
            Ai.Owner.StopMovement();
            return;
        }
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
                        Ai.Owner.MoveTowards(position, (float)speed, moveFlags);
                    }
                    else
                    {
                        // TODO взять следующую точку к которой движемся
                        Ai.PathNode.Current++;
                        if (Ai.PathNode.Current >= Ai.PathNode.findPath.Count)
                        {
                            Ai.Owner.StopMovement();
                            Ai.PathNode.findPath = [];
                            return;
                        }

                        Ai.PathNode.Position = Ai.PathNode.findPath[(int)Ai.PathNode.Current];
                    }
                }
                else
                {
                    if (distanceToTarget > range)
                        Ai.Owner.MoveTowards(target.Transform.World.Position, (float)speed, moveFlags);
                    else
                        Ai.Owner.StopMovement();
                }
            }
            else
            {
                if (distanceToTarget > range && target != null)
                    Ai.Owner.MoveTowards(target.Transform.World.Position, (float)speed, moveFlags);
                else
                    Ai.Owner.StopMovement();
            }
        }
        else
        {
            if (distanceToTarget > range && target != null)
                Ai.Owner.MoveTowards(target.Transform.World.Position, (float)speed, moveFlags);
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
            var distanceToIdlePosition = MathUtil.CalculateDistance(Ai.Owner.Transform.World.Position, Ai.IdlePosition, true);

            var res = distanceToTarget > returnDistance || distanceToIdlePosition > returnDistance;
            if (res)
                res = distanceToIdlePosition <= absoluteReturnDistance; // если больше, то нужен телепорт на место спавна
            return res;
        }
    }

    /// <summary>
    /// Updates Aggro target to the one with the most aggro
    /// </summary>
    /// <returns></returns>
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
                    if (Ai.Owner.CurrentAggroTarget != abuser && !Ai.AlreadyTargeted)
                    {
                        // TODO найдем путь к abuser
                        Ai.Owner.FindPath(abuser);
                    }
                    Ai.Owner.CurrentAggroTarget = abuser;
                    Ai.Owner.SetTarget(abuser);
                    UpdateAggroHelp(abuser);

                    return true;
                }
            }
            else
            {
                if (Ai.Owner.UnitIsVisible(abuser) && !abuser.IsDead)
                {
                    // check that such a Npc is in the database, there are cases that it is in the game, but not in the database
                    var currentTarget = abuser.ObjId > 0 ? WorldManager.Instance.GetUnit(abuser.ObjId) : null;
                    if (currentTarget == null)
                        continue;

                    Ai.Owner.CurrentAggroTarget = abuser;
                    Ai.Owner.SetTarget(abuser);
                    UpdateAggroHelp(abuser);
                    return true;
                }
            }
            Ai.Owner.ClearAggroOfUnit(abuser);
        }

        // Only remove CurrentTarget is either no unit selected, or if target is already dead
        if (Ai.Owner.CurrentTarget is not Unit currentTargetUnit)
        {
            Ai.Owner.CurrentAggroTarget = null;
            Ai.Owner.SetTarget(null);
        }
        else if ((currentTargetUnit.Hp <= 0) || (currentTargetUnit.IsDead))
        {
            Ai.Owner.CurrentAggroTarget = null;
            Ai.Owner.SetTarget(null);
        }

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
}

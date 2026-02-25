using System.Diagnostics;
using System.Numerics;

using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.AI.v2.Framework;
using AAEmu.Game.Models.Game.AI.v2.Params;
using AAEmu.Game.Models.Game.AI.v2.Params.Almighty;
using AAEmu.Game.Models.Game.Char;
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
    private DateTime _lastPathRecalcTime;

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
        speed *= delta.Milliseconds / 1000.0;

        // Fish overrides
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

        if (AppConfiguration.Instance.World.GeoDataMode && target != null)
        {
            if (distanceToTarget <= range)
            {
                Ai.Owner.StopMovement();
                if (Ai.PathNode?.FoundPath?.Count > 0)
                    Ai.PathNode.FoundPath = [];
            }
            else
            {
                var npcPos = Ai.Owner.Transform.World.Position;
                var targetPos = target.Transform.World.Position;

                // NavMesh Raycast checks if path crosses collision walls (brush meshes
                // + forbidden area walls). Falls back to ForbiddenArea polygon check
                // when NavMesh has no poly data near the NPC (tile not built yet, edge of map).
                var navMesh = Ai.Owner.ParentWorld?.NavMesh;
                bool pathClear;
                if (navMesh?.HasData == true && navMesh.TryRaycast(npcPos, targetPos, out var raycastClear))
                {
                    // NavMesh answered reliably — use its result
                    pathClear = raycastClear;
                }
                else
                {
                    // ForbiddenArea fallback disabled — navmesh is the authoritative source.
                    // ForbiddenArea polygons often cover building interiors where NPCs should navigate.
                    pathClear = true;

                    if (!Ai.Owner.CanFly)
                    {
                        var heightDiff = MathF.Abs(targetPos.Z - npcPos.Z);
                        var dist2D = MathUtil.CalculateDistance(npcPos, targetPos, true);
                        if (heightDiff > 3f && (dist2D < 1f || heightDiff / dist2D > 0.5f))
                            pathClear = false;
                    }
                }

                if (pathClear)
                {
                    // Clear line of sight — no walls between NPC and target
                    Ai.Owner.MoveTowards(targetPos, (float)speed, moveFlags, range);
                    if (Ai.PathNode?.FoundPath?.Count > 0)
                        Ai.PathNode.FoundPath = [];
                }
                else if (Ai.PathNode != null)
                {
                    // Blocked by wall — use A* pathfinding to navigate around
                    var targetMoved = (Ai.PathNode.EndPointPos - targetPos).Length() > 3f;
                    var noPath = Ai.PathNode.FoundPath.Count == 0;
                    var cooldownElapsed = DateTime.UtcNow > _lastPathRecalcTime.AddSeconds(2);

                    if ((targetMoved || noPath) && cooldownElapsed)
                    {
                        var stopWatch = new Stopwatch();
                        stopWatch.Start();
                        Ai.Owner.FindPath((Unit)target);
                        stopWatch.Stop();
                        _lastPathRecalcTime = DateTime.UtcNow;
                        if (stopWatch.Elapsed.Ticks >= TimeSpan.TicksPerMillisecond)
                            Logger.Warn($"FindPath took {stopWatch.Elapsed} for Ai.Owner.ObjId:{Ai.Owner.ObjId}, Owner.TemplateId {Ai.Owner.TemplateId} @ {Ai.Owner.Transform}");
                    }

                    // Follow A* waypoints
                    if (Ai.PathNode.FoundPath.Count > 0 && !Ai.PathNode.FoundPath.Peek().Equals(Vector3.Zero))
                    {
                        var nextPathPoint = Ai.PathNode.FoundPath.Peek();
                        var distToPoint = MathUtil.CalculateDistance(npcPos, nextPathPoint, true);
                        if (distToPoint > 1.0f)
                        {
                            Ai.Owner.MoveTowards(nextPathPoint, (float)speed, moveFlags);
                        }
                        else
                        {
                            Ai.PathNode.CurrentTargetPos = Ai.PathNode.FoundPath.Dequeue();
                        }
                    }
                    else
                    {
                        // No A* path yet — move straight toward target (best effort).
                        // Return is handled by ShouldReturn distance check in attack behaviors,
                        // not by pathfinding failure.
                        Ai.Owner.MoveTowards(targetPos, (float)speed, moveFlags, range);
                    }
                }
                else
                {
                    // No PathNode available — move straight (best effort)
                    Ai.Owner.MoveTowards(targetPos, (float)speed, moveFlags, range);
                }
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
            return Ai.Owner != null && DateTime.UtcNow >= _delayEnd && !Ai.Owner.IsGlobalCooldownDone;
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
                return true; // no target, returning

            var distanceToTarget = MathUtil.CalculateDistance(Ai.Owner.Transform.World.Position, Ai.Owner.CurrentTarget.Transform.World.Position, true);
            var distanceToIdlePosition = MathUtil.CalculateDistance(Ai.Owner.Transform.World.Position, Ai.IdlePosition, true);

            var res = distanceToTarget > returnDistance || distanceToIdlePosition > returnDistance;
            if (res)
                res = distanceToIdlePosition <= absoluteReturnDistance; // if it's greater, then we need a teleport to the spawn point
            return res;
        }
    }

    /// <summary>
    /// Updates Aggro target to the one with the most aggro
    /// </summary>
    /// <returns></returns>
    public bool UpdateTarget()
    {
        // Check if owner still exists
        if (Ai?.Owner == null)
        {
            return false;
        }
        // We might want to optimize this somehow.
        var aggroList = Ai.Owner.AggroTable.Values;
        var abusers = aggroList.OrderByDescending(o => o.TotalAggro).Select(o => o.Owner).ToList();

        foreach (var abuser in abusers)
        {
            //Ai.Owner.LookTowards(abuser.Transform.World.Position); // Prevents archers from escaping (they spin around all the time)

            if (AppConfiguration.Instance.World.GeoDataMode)
            {
                if (Ai.Owner.UnitIsVisible(abuser) && !abuser.IsDead)
                {
                    if (Ai.Owner.CurrentAggroTarget != abuser && !Ai.AlreadyTargeted)
                    {
                        // TODO: find the path to abuser
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
                    var currentTarget = abuser.ObjId > 0 ? Ai.Owner.ParentWorld.GetUnit(abuser.ObjId) : null;
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
        else if (currentTargetUnit.Hp <= 0 || currentTargetUnit.IsDead)
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
            // try to find Z first in WorldManager (includes building floors), then leave Z as it is
            var updZ = WorldManager.Instance.GetHeight(Ai.Owner.Transform.ZoneId,
                Ai.Owner.Transform.World.Position.X, Ai.Owner.Transform.World.Position.Y, Ai.Owner.Transform.World.Position.Z);
            if (updZ > 0f)
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

        var item = new AiSkill
        {
            SkillId = (uint)Ai.Owner.Template.BaseSkillId, Strafe = Ai.Owner.Template.BaseSkillStrafe, Delay = Ai.Owner.Template.BaseSkillDelay
        };
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

    /// <summary>
    /// Returns nearby players within range, in front, and not greeted recently.
    /// </summary>
    public static List<Character> GetPlayersInRange(
        Unit owner,
        float range,
        double fovScale,
        Dictionary<uint, DateTime> greeted,
        TimeSpan cooldown)
    {
        var now = DateTime.UtcNow;

        return WorldManager
            .GetAround<Character>(owner, range, true)
            .Where(p => MathUtil.IsFront(owner, p, fovScale))
            .Where(p => !greeted.TryGetValue(p.ObjId, out var last) || now - last >= cooldown)
            .ToList();
    }
}

using System.Numerics;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.AI.v2.Framework;
using AAEmu.Game.Models.Game.AI.v2.Params.Flytrap;
using AAEmu.Game.Models.Game.AI.V2.Params.Flytrap;
using AAEmu.Game.Models.Game.Models;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.Units.Movements;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Models.Game.AI.v2.Behaviors.Flytrap;

public class FlytrapAttackBehavior : Behavior
{
    private FlytrapAiParams _aiParams;
    private bool _enter;

    public override void Enter()
    {
        Ai.Owner.InterruptSkills();
        Ai.Owner.CurrentGameStance = GameStanceType.Combat;
        Ai.Owner.CurrentAlertness = MoveTypeAlertness.Combat;
        Ai.Owner.BroadcastPacket(new SCUnitModelPostureChangedPacket(Ai.Owner, Ai.Owner.AnimActionId, false), false);

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

        Ai.Param ??= new FlytrapAiParams("");

        if (Ai.Param is not FlytrapAiParams aiParams)
            return;

        _aiParams = aiParams;

        if (!UpdateTarget())
        {
            Ai.OnNoAggroTarget();
            return;
        }

        if (Ai.Owner.CurrentTarget == null)
            return;

        if (Ai.Owner.Gimmick?.CurrentTarget != null)
            MoveInRange(Ai.Owner.Gimmick.CurrentTarget, delta);

        Ai.Owner.IsInBattle = true;
        var targetDist = Ai.Owner.GetDistanceTo(Ai.Owner.CurrentTarget);

        // Consume FlytrapAiParams.CombatSkills before falling back to PickSkillAndUseIt — this
        // is the retail-correct data source for Halcyona War Auto-Cannons (13648/13662) and any
        // other Flytrap/Turret NPC whose attack rotation is defined in npc_ai_params Lua rather
        // than np_skills. Without this loop, Cannons aimed but never fired (PickSkillAndUseIt
        // reads only Template.Skills which is populated from np_skills; cannons have 0 rows).
        // We prefer Ranged when out of melee, then Melee when in range — same order as
        // ArcherAttackBehavior. The first cooldown-clear skill whose range fits wins.
        var firedFromCombatSkills = TryUseCombatSkill(aiParams.CombatSkills, Ai.Owner.CurrentTarget, targetDist);
        if (!firedFromCombatSkills)
            PickSkillAndUseIt(SkillUseConditionKind.InCombat, Ai.Owner.CurrentTarget, targetDist);

        Update();
    }

    /// <summary>
    /// Iterates the Lua-defined combat skill lists (Ranged first, then Melee inside meleeAttackRange),
    /// filters by cooldown + range, and casts the first match. Returns true if any skill was attempted.
    /// </summary>
    private bool TryUseCombatSkill(FlytrapCombatSkill combatSkills, BaseUnit target, float targetDist)
    {
        if (combatSkills == null)
            return false;
        if (TryPickAndCast(combatSkills.Ranged, target, targetDist))
            return true;
        if (targetDist <= _aiParams?.MeleeAttackRange + 0.001f
            && TryPickAndCast(combatSkills.Melee, target, targetDist))
            return true;
        return false;
    }

    private bool TryPickAndCast(List<uint> skillIds, BaseUnit target, float targetDist)
    {
        if (skillIds == null || skillIds.Count == 0)
            return false;
        foreach (var skillId in skillIds)
        {
            if (skillId == 0)
                continue;
            if (Ai.Owner.Cooldowns.CheckCooldown(skillId))
                continue;
            var template = SkillManager.Instance.GetSkillTemplate(skillId);
            if (template == null)
                continue;
            if (!(targetDist >= template.MinRange && targetDist <= template.MaxRange || template.TargetType == SkillTargetType.Self))
                continue;
            var skill = new Skill(template);
            UseSkill(skill, target);
            return true;
        }
        return false;
    }

    public override void Exit()
    {
        _enter = false;
    }

    #region Gimmick
    private void MoveInRange(BaseUnit target, TimeSpan delta)
    {
        if (Ai?.Owner?.Gimmick == null)
            return;

        var gimmick = Ai.Owner.Gimmick;
        var gimmickPosition = Ai.Owner.Gimmick.Transform.World.Position;
        if (gimmick.Target == Vector3.Zero)
        {
            gimmick.Target = target.Transform.World.Position;
        }
        var finalPoint = gimmick.Target;

        var range = 0.1f;
        var moveDistance = gimmick.BaseMoveSpeed * (delta.Milliseconds / 1000.0f) + 1f;
        var moveDistanceZ = gimmick.Template.Gravity * (delta.Milliseconds / 1000.0f);
        var distanceToTarget = MathUtil.CalculateDistance(gimmickPosition, gimmick.Target, true);

        if (AppConfiguration.Instance.World.GeoDataMode)
        {
            // we will find the path to the abuser only if the target coordinates have changed
            if (Ai.PathNode?.FoundPath?.Count == 0 && target != null && Ai.PathNode?.EndPointPos != null)
            {
                //if (!Ai.PathNode.pos2.Equals(new Point(target.Transform.World.Position.X, target.Transform.World.Position.Y, target.Transform.World.Position.Z)))
                if (Math.Abs((Ai.PathNode.EndPointPos - target.Transform.World.Position).Length()) <= Ai.Owner.ModelSize)
                {
                    // let's find the path to the abuser
                    Ai.Owner.FindPath((Unit)target);
                    // remember the new target coordinates
                    Ai.PathNode.EndPointPos = new Vector3(target.Transform.World.Position.X, target.Transform.World.Position.Y, target.Transform.World.Position.Z);
                    gimmick.Target = target.Transform.World.Position;
                }
            }
            // moving along the route points
            if (Ai.PathNode?.FoundPath?.Count > 0 && !Ai.PathNode.FoundPath.Peek().Equals(Vector3.Zero))
            {
                // take the point to which we are moving
                var routePoint = new Vector3(Ai.PathNode.Position.X, Ai.PathNode.Position.Y, Ai.PathNode.Position.Z);
                // recalculate the distance, since the path is divided into points
                var distanceToPoint = MathUtil.CalculateDistance(gimmickPosition, routePoint, true);
                if (distanceToPoint > range)
                {
                    gimmick.MoveTowards(routePoint, moveDistance, moveDistanceZ);
                }
                else
                {
                    // take the next point to which we are moving
                    if (Ai.PathNode.FoundPath.Count <= 0)
                    {
                        Ai.Owner.StopMovement();
                        Ai.PathNode.FoundPath = [];
                        return;
                    }

                    Ai.PathNode.CurrentTargetPos = Ai.PathNode.FoundPath.Dequeue();
                }
            }
            else // we move straight to the final point
            {
                if (distanceToTarget > range)
                    gimmick.MoveTowards(finalPoint, moveDistance, moveDistanceZ);
                else
                    gimmick.StopMovement();
            }
        }
        else // we move straight to the final point
        {
            if (distanceToTarget > range)
                gimmick.MoveTowards(finalPoint, moveDistance, moveDistanceZ);
            else
                gimmick.StopMovement();
        }
    }

    private bool UpdateTarget()
    {
        // We might want to optimize this somehow...
        var aggroList = Ai.Owner.AggroTable.Values;
        var abusers = aggroList.OrderByDescending(o => o.TotalAggro).Select(o => o.Owner).ToList();

        foreach (var abuser in abusers)
        {
            // Aggro can land on units the cannon must not return fire on (a friendly tester punched
            // it, splash damage from a hostile AoE clipped a Friendly, etc.). Gate the lock-on by
            // CanAttack so the cannon ignores friendlies even if they appear in the aggro list.
            // Without this the Halcyona Auto-Cannons end up tracking and "firing" at the player who
            // poked them first, regardless of faction.
            if (!Ai.Owner.CanAttack(abuser))
            {
                Ai.Owner.ClearAggroOfUnit(abuser);
                continue;
            }

            Ai.Owner.LookTowards(abuser.Transform.World.Position);
            if (Ai.AlreadyTargeted)
                return true;

            if (AppConfiguration.Instance.World.GeoDataMode)
            {
                // включена геодата и не основной мир
                // geodata enabled and not the main world
                if (Ai.Owner.UnitIsVisible(abuser) && !abuser.IsDead)
                {
                    Ai.Owner.CurrentAggroTarget = abuser;
                    Ai.Owner.SetTarget(abuser);
                    UpdateAggroHelp(abuser);
                    Ai.Owner.FindPath(abuser);
                    return true;
                }
            }
            else
            {
                if (Ai.Owner.UnitIsVisible(abuser) && !abuser.IsDead)
                {
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
            Ai.Owner.SetTarget(null);
        else if (currentTargetUnit.Hp <= 0 || currentTargetUnit.IsDead)
            Ai.Owner.SetTarget(null);

        return false;
    }
    #endregion

    public void Update()
    {
        var abuser = (Unit)Ai.Owner.CurrentTarget;
        var abuserPos = Ai.Owner.CurrentTarget.Transform.World.Position;
        var currentPos = Ai.Owner.Transform.World.Position;
        var idlePos = Ai.IdlePosition;
        // Check out of idle pos
        if (Ai.Param.AlwaysTeleportOnReturn && MathUtil.DistanceSqVectors(currentPos, idlePos) > 3 * 3)
        {
            // NpcTeleportTo(entity.AI.idlePos);
            Ai.Owner.ClearAggroOfUnit(abuser);
            Ai.OnNoAggroTarget();
            return;
        }

        // Check that some target was gone out from attack end distance
        if (MathUtil.DistanceSqVectors(abuserPos, idlePos) > _aiParams.AttackEndDistance * _aiParams.AttackEndDistance)
        {
            // entity.unit:NpcRemoveAggroOutOfRange(entity.AI.param.attackEndDistance);
            Ai.Owner.ClearAggroOfUnit(abuser);
            Ai.OnNoAggroTarget();
        }
    }
}

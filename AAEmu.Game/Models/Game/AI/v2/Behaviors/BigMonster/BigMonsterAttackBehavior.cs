using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.AI.v2.Params.BigMonster;
using AAEmu.Game.Models.Game.AI.V2.Params.BigMonster;
using AAEmu.Game.Models.Game.Models;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.Units.Movements;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Models.Game.AI.v2.Behaviors.BigMonster;

public class BigMonsterAttackBehavior : BaseCombatBehavior
{
    private bool _enter;
    private DateTime _lastFaceTargetTime = DateTime.MinValue;
    private float _maxSkillRange;

    /// <summary>
    /// Minimum MinRange on a skill template that marks it as a "long-range-only" skill
    /// (e.g. skill 13851 with MinRange=50). These skills have their own SkillController
    /// (e.g. LeapSkillController) that handles movement; they should NOT appear in the
    /// fallback filter when the player is within melee range.
    /// </summary>
    private const float LongRangeSkillMinRangeThreshold = 40f;

    /// <summary>
    /// Maximum effective AI skill delay in seconds. Prevents the boss from appearing
    /// idle for excessively long periods between attacks. The original DB values
    /// (e.g. 9s, 10s) are capped to this value making the boss feel more aggressive.
    /// </summary>
    private const float MaxSkillDelay = 5f;

    public override void Enter()
    {
        Ai.Owner.InterruptSkills();
        Ai.Owner.CurrentGameStance = GameStanceType.Combat;
        Ai.Owner.CurrentAlertness = MoveTypeAlertness.Combat;
        Ai.Owner.BroadcastPacket(new SCUnitModelPostureChangedPacket(Ai.Owner, Ai.Owner.AnimActionId, false), false);

        // Reset skill delay from previous combat so skills can fire immediately on re-engage.
        _delayEnd = DateTime.MinValue;

        Ai.Owner.IsInBattle = true;
        if (Ai.Owner is { } npc)
        {
            npc.Events.OnCombatStarted(this, new OnCombatStartedArgs { Owner = npc, Target = npc });
        }
        Ai.Param = Ai.Owner.Template.AiParams;

        // Compute max skill range from combat skills for engagement distance.
        // Aquatic bosses use this as the effective leash distance (they don't chase).
        _maxSkillRange = 50f; // default minimum
        if (Ai.Param is BigMonsterAiParams bmp)
        {
            foreach (var cs in bmp.CombatSkills)
            {
                var st = SkillManager.Instance.GetSkillTemplate(cs.SkillType);
                if (st != null && st.MaxRange > _maxSkillRange)
                    _maxSkillRange = st.MaxRange;
            }
        }

        _enter = true;
    }

    public override void Tick(TimeSpan delta)
    {
        if (!_enter)
            return; // not initialized yet Enter()

        Ai.Param ??= new BigMonsterAiParams("");

        if (Ai.Param is not BigMonsterAiParams aiParams)
            return;

        var targetUpdated = UpdateTarget();

        // For aquatic bosses that don't chase: disengage when target goes beyond max skill range.
        // They stay at spawn and fight from position, so the standard ShouldReturn (based on
        // returnDistance=800) is too generous — the boss would hold aggro but never reach the target.
        bool shouldReturn;
        if (Ai.Owner is Npc { IsAquatic: true } && targetUpdated && Ai.Owner.CurrentTarget != null)
        {
            var distToTarget = Ai.Owner.GetDistanceTo(Ai.Owner.CurrentTarget, false);
            shouldReturn = distToTarget > _maxSkillRange + 30f;
        }
        else
        {
            shouldReturn = targetUpdated && ShouldReturn;
        }

        if (!targetUpdated || shouldReturn)
        {
            Ai.OnNoAggroTarget();
            return;
        }

        // Normal movement: aquatic bosses stay at their spawn position and only face
        // the target periodically (every ~2.5s). This prevents the boss from chasing
        // (exiting water) and matches retail behavior where the boss executes skill
        // sequences, only turning to face the target between attacks.
        // Non-aquatic NPCs use normal chase behavior.
        if (CanStrafe && !IsUsingSkill)
        {
            if (Ai.Owner is Npc { IsAquatic: true })
            {
                if (DateTime.UtcNow > _lastFaceTargetTime.AddSeconds(2.5) && Ai.Owner.CurrentTarget != null)
                {
                    _lastFaceTargetTime = DateTime.UtcNow;
                    Ai.Owner.LookTowards(Ai.Owner.CurrentTarget.Transform.World.Position);
                }
            }
            else
            {
                MoveInRange(Ai.Owner.CurrentTarget, delta);
            }
        }

        if (!CanUseSkill)
            return;

        _strafeDuringDelay = false;

        #region Pick a skill

        var targetDist = Ai.Owner.GetDistanceTo(Ai.Owner.CurrentTarget);
        var availableSkills = RequestAvailableSkills(aiParams, targetDist);
        var selectedSkill = PickSkill(availableSkills);
        if (selectedSkill == null)
        {
            // If skill list is empty, get Base skill
            var baseResult = PickSkillAndUseIt(SkillUseConditionKind.InCombat, Ai.Owner.CurrentTarget, targetDist);

            // If base skill also failed due to range, try to reposition
            if (baseResult == SkillResult.TooCloseRange && aiParams.PreferedCombatDist > 0)
            {
                // Back off to preferred combat distance
                var npcPos = Ai.Owner.Transform.World.Position;
                var targetPos = Ai.Owner.CurrentTarget.Transform.World.Position;
                var dir = npcPos - targetPos;
                if (dir.LengthSquared() > 0.01f)
                {
                    dir = System.Numerics.Vector3.Normalize(dir);
                    var retreatTarget = targetPos + dir * aiParams.PreferedCombatDist;
                    var retreatSpeed = Ai.GetRealMovementSpeed(Ai.Owner.BaseMoveSpeed);
                    var moveFlags = Ai.GetRealMovementFlags(retreatSpeed);
                    retreatSpeed *= delta.TotalMilliseconds / 1000.0;
                    Ai.Owner.MoveTowards(retreatTarget, (float)retreatSpeed, moveFlags);
                }
            }
            return;
        }

        var skillTemplate = SkillManager.Instance.GetSkillTemplate(selectedSkill.SkillType);
        if (skillTemplate == null)
            return;

        // When the skill has a SkillController (e.g. LeapSkillController for skill 13851),
        // the controller already provides "busy time" during execution. Stacking the full
        // AI skillDelay on top creates excessive idle windows (e.g. 6s controller + 6s delay
        // = 12s of apparent inactivity). Cap the delay to 1s for controlled skills.
        var effectiveDelay = Math.Min(selectedSkill.SkillDelay, MaxSkillDelay);
        if (skillTemplate.SkillControllerId != 0)
            effectiveDelay = Math.Min(effectiveDelay, 1f);

        UseSkill(new Skill(skillTemplate), Ai.Owner.CurrentTarget, effectiveDelay);
        _strafeDuringDelay = selectedSkill.StrafeDuringDelay;

        #endregion
    }

    public override void Exit()
    {
        // Clear combat state here
        _enter = false;
    }

    private List<BigMonsterCombatSkill> RequestAvailableSkills(BigMonsterAiParams aiParams, float trgDist)
    {
        var healthRatio = (int)((float)Ai.Owner.Hp / Ai.Owner.MaxHp * 100);

        var baseList = aiParams.CombatSkills.AsEnumerable();

        baseList = baseList.Where(s => s.HealthRangeMin <= healthRatio && healthRatio <= s.HealthRangeMax);
        baseList = baseList.Where(s => !Ai.Owner.Cooldowns.CheckCooldown(s.SkillType));

        // Range filtering: Self-target skills (AoE centered on caster) skip MinRange
        // but still respect MaxRange — they only affect targets within their AoE radius.
        // Without the MaxRange check, the boss wastes Self-target AoE skills when the
        // target is far beyond the skill's effective range, causing it to appear idle.
        var rangeFiltered = baseList.Where(s =>
        {
            var template = SkillManager.Instance.GetSkillTemplate(s.SkillType);
            if (template == null) return false;
            if (template.TargetType == SkillTargetType.Self)
                return trgDist <= template.MaxRange; // Self: skip MinRange, check MaxRange
            return trgDist >= template.MinRange && trgDist <= template.MaxRange;
        }).ToList();

        if (rangeFiltered.Count > 0)
            return rangeFiltered;

        // Fallback: when player is too close (below all MinRanges), drop MinRange requirement
        // so boss NPCs can still attack at melee range instead of getting stuck.
        // However, exclude "long-range-only" skills (MinRange >= threshold) — these are
        // designed for specific engagement distances (e.g. dive-chase skill 13851 with
        // MinRange=50) and have their own SkillControllers handling movement.
        // Including them in the fallback causes massive spam of TooCloseRange failures.
        var maxRangeOnly = baseList.Where(s =>
        {
            var template = SkillManager.Instance.GetSkillTemplate(s.SkillType);
            return template != null
                   && trgDist <= template.MaxRange
                   && template.MinRange < LongRangeSkillMinRangeThreshold;
        }).ToList();

        return maxRangeOnly;
    }

    private BigMonsterCombatSkill PickSkill(List<BigMonsterCombatSkill> skills)
    {
        if (skills.Count > 0)
            return skills[Random.Shared.Next(0, skills.Count)];

        if (!Ai.Owner.Cooldowns.CheckCooldown((uint)Ai.Owner.Template.BaseSkillId))
            return new BigMonsterCombatSkill
            {
                SkillType = (uint)Ai.Owner.Template.BaseSkillId,
                SkillDelay = Ai.Owner.Template.BaseSkillDelay,
                StrafeDuringDelay = Ai.Owner.Template.BaseSkillStrafe
            };

        return null;
    }
}

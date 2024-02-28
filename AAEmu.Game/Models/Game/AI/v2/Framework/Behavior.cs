using System;
using System.Collections.Generic;
using System.Linq;

using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Items.Templates;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Utils;

using NLog;

namespace AAEmu.Game.Models.Game.AI.v2.Framework;

/// <summary>
/// Represents an AI state. Called as such because of naming in the game's files.
/// </summary>
public abstract class Behavior
{
    protected static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    protected DateTime _delayEnd;
    protected float _nextTimeToDelay;
    protected float _maxWeaponRange;

    public NpcAi Ai { get; set; }
    public abstract void Enter();
    public abstract void Tick(TimeSpan delta);
    public abstract void Exit();

    public Behavior AddTransition(TransitionEvent on, BehaviorKind kind)
    {
        return AddTransition(new Transition(on, kind));
    }

    public Behavior AddTransition(Transition transition)
    {
        return Ai.AddTransition(this, transition);
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
                Logger.Debug("PickSkillAndUseIt:UseSelfSkill Owner.ObjId {0}, Owner.TemplateId {1}, SkillId {2}", Ai.Owner.ObjId, Ai.Owner.TemplateId, skillTemplateSelf.Id);
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
            Logger.Debug("PickSkillAndUseIt:UseSkill Owner.ObjId {0}, Owner.TemplateId {1}, SkillId {2} on Target {3}", Ai.Owner.ObjId, Ai.Owner.TemplateId, skillTemplate.Id, target.ObjId);
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

    // Check if can pick a new skill (delay, already casting)

    public float CheckSightRangeScale(float value)
    {
        var sightRangeScale = value * Ai.Owner.Template.SightRangeScale;
        if (sightRangeScale < value)
        {
            sightRangeScale = value;
        }

        return sightRangeScale;
    }

    public void OnEnemySeen(Unit target)
    {
        Ai.Owner.AddUnitAggro(AggroKind.Damage, target, 1);
        Ai.GoToCombat();
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
            npc.Ai.GoToCombat();
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

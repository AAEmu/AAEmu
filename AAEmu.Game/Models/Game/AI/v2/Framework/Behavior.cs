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

    public float CheckSightRangeScale(float value)
    {
        var sightRangeScale = value * Ai.Owner.Template.SightRangeScale;
        if (sightRangeScale < value)
        {
            sightRangeScale = value;
        }

        return sightRangeScale;
    }

    /// <summary>
    /// Trigger when AI is about to attack target and goes to combat mode
    /// </summary>
    /// <param name="target"></param>
    public void OnEnemySeen(Unit target)
    {
        Ai.Owner.AddUnitAggro(AggroKind.Damage, target, 1);
        Ai.GoToCombat();
    }

    public bool CheckAggression()
    {
        if (!Ai.Owner.Template.Aggression)
        {
            return false;
        }

        var res = false;
        var nearbyUnits = WorldManager.GetAround<Unit>(Ai.Owner, Ai.Owner.Template.AttackStartRangeScale * 10f);

        // Sort by distance
        var unitsWithDistance = new List<(Unit, float)>();
        foreach (var nearbyUnit in nearbyUnits)
        {
            var rangeOfUnit = MathUtil.CalculateDistance(Ai.Owner, nearbyUnit, true);
            unitsWithDistance.Add((nearbyUnit, rangeOfUnit));
        }
        unitsWithDistance.Sort((p, q) => p.Item2.CompareTo(q.Item2));
        
        foreach (var (unit, rangeOfUnit) in unitsWithDistance)
        {
            if (unit.IsDead || unit.Hp <= 0)
                continue; // not counting dead Npc

            // Need to check for stealth detection here
            if (MathUtil.IsFront(Ai.Owner, unit, Ai.Owner.Template.SightFovScale))
            {
                if (Ai.Owner.CanAttack(unit) && (rangeOfUnit < 1f || Ai.Owner.CanSeeTarget(unit)))
                {
                    OnEnemySeen(unit);
                    res = true;
                    break;
                }
            }
            else
            {
                // If you're breathing down their neck, they will also start attacking you if they can
                if (rangeOfUnit < 1.5f * Ai.Owner.Template.SightRangeScale)
                {
                    if (Ai.Owner.CanAttack(unit) && (rangeOfUnit < 0.5f || Ai.Owner.CanSeeTarget(unit)))
                    {
                        OnEnemySeen(unit);
                        res = true;
                        break;
                    }
                }
            }
        }

        return res;
    }

    public void OnEnemyAlert(Unit target)
    {
        // if (target is Character player)
        // {
        //     var degree = MathUtil.ClampDegAngle(MathUtil.CalculateAngleFrom(Ai.Owner, player));
        //     player.SendMessage($"ObjId {Ai.Owner.ObjId} has seen you at a angle of {degree:F0}°");
        // }

        // TODO: Tweak these values, or grab them from DB somewhere?
        Ai._alertEndTime = DateTime.UtcNow.AddSeconds(5);
        Ai._nextAlertCheckTime = DateTime.UtcNow.AddSeconds(7);
        // Ai.Owner.CurrentAggroTarget = target;
        Ai.Owner.SetTarget(target);

        Ai.GoToAlert();
    }

    public bool CheckAlert()
    {
        if (Ai._nextAlertCheckTime > DateTime.UtcNow)
            return false;

        // Don't do alerts if already in combat
        if (Ai.Owner.IsInBattle)
            return false;

        var res = false;
        var nearbyUnits = WorldManager.GetAround<Unit>(Ai.Owner, Ai.Owner.Template.SightRangeScale * 15f);
        
        // Sort by distance
        var unitsWithDistance = new List<(Unit, float)>();
        foreach (var nearbyUnit in nearbyUnits)
        {
            var rangeOfUnit = MathUtil.CalculateDistance(Ai.Owner, nearbyUnit, true);
            unitsWithDistance.Add((nearbyUnit, rangeOfUnit));
        }
        unitsWithDistance.Sort((p, q) => p.Item2.CompareTo(q.Item2));
        
        foreach (var (unit, rangeOfUnit) in unitsWithDistance)
        {
            if (unit.IsDead || unit.Hp <= 0)
                continue; // not counting dead Npc

            // Need to check for stealth detection here
            if (MathUtil.IsFront(Ai.Owner, unit, Ai.Owner.Template.SightFovScale))
            {
                if (Ai.Owner.CanAttack(unit) && (rangeOfUnit < 1f || Ai.Owner.CanSeeTarget(unit)))
                {
                    OnEnemyAlert(unit);
                    res = true;
                    break;
                }
            }
            else
            {
                // If you're breathing down their neck, they will also notice you.
                // Not sure if this is retail behavior
                if (rangeOfUnit < 2f * Ai.Owner.Template.SightRangeScale)
                {
                    if (Ai.Owner.CanAttack(unit) && (rangeOfUnit < 0.5f || Ai.Owner.CanSeeTarget(unit)))
                    {
                        OnEnemyAlert(unit);
                        res = true;
                        break;
                    }
                }
            }
        }

        return res;
    }

    /// <summary>
    /// Check if this NPC can get help, and if so, make them aggro the abuser
    /// </summary>
    /// <param name="abuser">The attacking Unit</param>
    /// <param name="radius">Maximum range to check for help, this is not the range of the NPCs that will help, but rather possibly help. Maximum range defined in the DB is 100m</param>
    public void UpdateAggroHelp(Unit abuser, int radius = 20)
    {
        var npcs = WorldManager.GetAround<Npc>(Ai.Owner,  Ai.Owner.Template.AttackStartRangeScale * radius);
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

using System.Collections.Generic;
using AAEmu.Commons.Exceptions;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Faction;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.StaticValues;
using AAEmu.Game.Utils;

using NLog;

namespace AAEmu.Game.Models.Game.Skills.Plots;

public class PlotCondition
{
    protected static Logger Logger { get; } = LogManager.GetCurrentClassLogger();
    public uint Id { get; set; }
    public bool NotCondition { get; set; }
    public PlotConditionType Kind { get; set; }
    public int Param1 { get; set; }
    public int Param2 { get; set; }
    public int Param3 { get; set; }

    /// <summary>
    /// Checks if this PlotCondition is true
    /// </summary>
    /// <param name="caster"></param>
    /// <param name="casterCaster"></param>
    /// <param name="target"></param>
    /// <param name="targetCaster"></param>
    /// <param name="skillObject"></param>
    /// <param name="skill"></param>
    /// <returns></returns>
    public bool Check(BaseUnit caster, SkillCaster casterCaster, BaseUnit target, SkillCastTarget targetCaster, SkillObject skillObject, Skill skill)
    {
        var res = Kind switch
        {
            PlotConditionType.Level => ConditionLevel(caster, casterCaster, target, targetCaster, skillObject, Param1, Param2, Param3),
            PlotConditionType.Relation => ConditionRelation(caster, casterCaster, target, targetCaster, skillObject, Param1, Param2, Param3),
            PlotConditionType.Direction => ConditionDirection(caster, casterCaster, target, targetCaster, skillObject, Param1, Param2, Param3),
            // 4 does not exist or is unused
            PlotConditionType.BuffTag => ConditionBuffTag(caster, casterCaster, target, targetCaster, skillObject, Param1, Param2, Param3),
            PlotConditionType.WeaponEquipStatus => ConditionWeaponEquipStatus(caster, casterCaster, target, targetCaster, skillObject, Param1, Param2, Param3),
            PlotConditionType.Chance => ConditionChance(caster, casterCaster, target, targetCaster, skillObject, Param1, Param2, Param3),
            PlotConditionType.Dead => ConditionDead(caster, casterCaster, target, targetCaster, skillObject, Param1, Param2, Param3),
            PlotConditionType.CombatDiceResult => ConditionCombatDiceResult(caster, casterCaster, target, targetCaster, skillObject, Param1, Param2, Param3, skill), // Every CombatDiceResult is a NotCondition -> false makes it true.
            PlotConditionType.InstrumentType => ConditionInstrumentType(caster, casterCaster, target, targetCaster, skillObject, Param1, Param2, Param3),
            PlotConditionType.Range => ConditionRange(caster, casterCaster, target, targetCaster, skillObject, Param1, Param2, Param3),
            PlotConditionType.Variable => ConditionVariable(caster, casterCaster, target, targetCaster, skillObject, Param1, Param2, Param3),
            PlotConditionType.UnitAttrib => ConditionUnitAttrib(caster, casterCaster, target, targetCaster, skillObject, Param1, Param2, Param3),
            PlotConditionType.Actability => ConditionActability(caster, casterCaster, target, targetCaster, skillObject, Param1, Param2, Param3),
            PlotConditionType.Stealth => ConditionStealth(caster, casterCaster, target, targetCaster, skillObject, Param1, Param2, Param3),
            PlotConditionType.Visible => ConditionVisible(caster, casterCaster, target, targetCaster, skillObject, Param1, Param2, Param3),
            PlotConditionType.ABLevel => ConditionAbLevel(caster, casterCaster, target, targetCaster, skillObject, Param1, Param2, Param3),
            _ => true
        };

        Logger.Trace($"PlotCondition : {Kind} | Params : {Param1}, {Param2}, {Param3} | Result : {(NotCondition ? "NOT" : "")} {res}");

        return NotCondition ? !res : res;
    }

    // 1
    private static bool ConditionLevel(BaseUnit caster, SkillCaster casterCaster, BaseUnit target,
        SkillCastTarget targetCaster, SkillObject skillObject, int minLevel, int maxLevel, int unused3)
    {
        if (caster is not Unit casterUnit)
        {
            Logger.Warn($"PlotCondition Level check without caster being a Unit");
            return false;
        }
        return casterUnit.Level >= minLevel && casterUnit.Level <= maxLevel;
    }

    // 2
    private static bool ConditionRelation(BaseUnit caster, SkillCaster casterCaster, BaseUnit target,
        SkillCastTarget targetCaster, SkillObject skillObject, int relationType, int unused2, int unused3)
    {
        if (caster is Character player)
            player.SendDebugMessage($"ConditionRelation Caster {relationType}");
        if (target is Character targetPlayer)
            targetPlayer.SendDebugMessage($"ConditionRelation Target {relationType}");
        Logger.Warn($"ConditionRelation {relationType} {caster} -> {target}");
        // Param1 is either 1, 4 or 5
        switch (relationType)
        {
            case 1: // Friendly?
                return caster.Faction.GetRelationState(target.Faction) == RelationState.Friendly;
            case 4: // Hostile?
                return caster.Faction.GetRelationState(target.Faction) == RelationState.Hostile;
            case 5: // 
                break;
        }
        return true;
    }

    // 3
    private static bool ConditionDirection(BaseUnit caster, SkillCaster casterCaster, BaseUnit target,
        SkillCastTarget targetCaster, SkillObject skillObject, int unused1, int unused2, int unused3)
    {
        return MathUtil.IsFront(caster, target);
    }

    // 4 does not exist

    // 5
    private static bool ConditionBuffTag(BaseUnit caster, SkillCaster casterCaster, BaseUnit target,
        SkillCastTarget targetCaster, SkillObject skillObject, int tagId, int unused2, int unused3)
    {
        // if (eventCondition.TargetId == PlotEffectTarget.Source)
        //     return caster.Effects.CheckBuffs(SkillManager.Instance.GetBuffsByTagId((uint)tagId));
        // else if (eventCondition.TargetId == PlotEffectTarget.Target)
        //     return target.Effects.CheckBuffs(SkillManager.Instance.GetBuffsByTagId((uint)tagId));
        return target.Buffs.CheckBuffs(SkillManager.Instance.GetBuffsByTagId((uint)tagId));
    }

    // 6
    private static bool ConditionWeaponEquipStatus(BaseUnit caster, SkillCaster casterCaster, BaseUnit target,
        SkillCastTarget targetCaster, SkillObject skillObject, int weaponEquipStatus, int unused2, int unused3)
    {
        // Weapon equip status can be :
        // 1 = 1handed
        // 2 = 2handed
        // 3 = duel-wielded
        var wieldKind = (WeaponWieldKind)weaponEquipStatus;
        if (caster is Character character)
        {
            return character.GetWeaponWieldKind() == wieldKind;
        }
        return false;
    }

    // 7
    private static bool ConditionChance(BaseUnit caster, SkillCaster casterCaster, BaseUnit target,
        SkillCastTarget targetCaster, SkillObject skillObject, int chance, int unknown2, int unused3)
    {
        if (caster is not Unit casterUnit)
        {
            Logger.Warn($"PlotCondition Chance check without caster being a Unit");
            return false;
        }

        // NOTE: Param2 is only used once, and its value is "1"
        // It's used for fishing skill 18711, so it could mean the roll is affected by vocation skill level rates
        // That event sets a variable to 11 and trigger FinishChanneling if true
        // Nowhere in the skill does it seem to check for this value (only for 0 or 1)

        var roll = Rand.Next(0, 100);
        casterUnit.ConditionChance = roll <= chance;
        return roll <= chance;
    }

    // 8
    private static bool ConditionDead(BaseUnit caster, SkillCaster casterCaster, BaseUnit target,
        SkillCastTarget targetCaster, SkillObject skillObject, int unused1, int unused2, int unused3)
    {
        if (target is Unit unitTarget)
            return unitTarget.Hp == 0;
        return false;
    }

    // 9
    private static bool ConditionCombatDiceResult(BaseUnit caster, SkillCaster casterCaster, BaseUnit target,
        SkillCastTarget targetCaster, SkillObject skillObject, int unknown1, int unused2, int unused3, Skill skill)
    {
        // NOTE: unknown1 kind of looks like it could be a bit mask of some sorts, but no idea what it actually is
        if (target is Unit targetUnit)
        {
            // Super hacky way to do combat dice....
            var hitType = skill.RollCombatDice(caster, targetUnit);
            if (!skill.HitTypes.TryAdd(targetUnit.ObjId, hitType))
                skill.HitTypes[targetUnit.ObjId] = hitType;

            return hitType == SkillHitType.MeleeDodge
                || hitType == SkillHitType.MeleeParry
                || hitType == SkillHitType.MeleeBlock
                || hitType == SkillHitType.MeleeMiss
                || hitType == SkillHitType.RangedDodge
                || hitType == SkillHitType.RangedParry
                || hitType == SkillHitType.RangedBlock
                || hitType == SkillHitType.RangedMiss
                || hitType == SkillHitType.Immune;
        }
        return true; // Almost Every CombatDiceResult is a NotCondition -> false makes it true.
    }

    // 10
    private static bool ConditionInstrumentType(BaseUnit caster, SkillCaster casterCaster, BaseUnit target,
        SkillCastTarget targetCaster, SkillObject skillObject, int instrumentTypeId, int unused2, int unused3)
    {
        // Param1 is either 21, 22 or 23
        if (caster is Character character)
        {
            var item = character.Inventory.Equipment.GetItemBySlot((int)EquipmentItemSlot.Musical);
            if (item == null)
                return false;
            if (item.Template is WeaponTemplate template)
            {
                if (instrumentTypeId == template.HoldableTemplate.SlotTypeId)
                    return true;
            }
        }
        return false;
    }

    // 11
    private static bool ConditionRange(BaseUnit caster, SkillCaster casterCaster, BaseUnit target,
        SkillCastTarget targetCaster, SkillObject skillObject, int minRange, int maxRange, int unused3)
    {
        // Param1 = Min range
        // Param2 = Max range
        // var range = MathUtil.CalculateDistance(caster.Transform.World.Position, target.Transform.World.Position);
        // range -= 2;//Temp fix because the calculation is off
        // range = Math.Max(0f, range);
        var range = caster.GetDistanceTo(target);
        return range >= minRange && range <= maxRange;
    }

    // 12
    private static bool ConditionVariable(BaseUnit caster, SkillCaster casterCaster, BaseUnit target,
        SkillCastTarget targetCaster, SkillObject skillObject, int variableIndex, int operation, int compareValue)
    {
        if (caster is not Unit casterUnit)
        {
            Logger.Warn($"PlotCondition Variable check without caster being a Unit");
            return false;
        }

        var variableValue = casterUnit.ActivePlotState.Variables[variableIndex];
        // There is a high chance this is not implemented correctly ...
        // If refactoring. See SpecialEffect -> SetVariable as well
        
        return CompareWithOperator(variableValue, operation, compareValue);
    }

    // 13
    private static bool ConditionUnitAttrib(BaseUnit caster, SkillCaster casterCaster, BaseUnit target,
        SkillCastTarget targetCaster, SkillObject skillObject, int attributeType, int operation, int compareValue)
    {
        // All 3 params used. No idea.
        if (caster is not Unit casterUnit)
        {
            Logger.Warn($"PlotCondition UnitAttrib check without caster being a Unit");
            return false;
        }

        if (!int.TryParse(casterUnit.GetAttribute((UnitAttribute)attributeType), out var attributeValue))
            attributeValue = 0;

        return CompareWithOperator(attributeValue, operation, compareValue);
    }

    // 14
    private static bool ConditionActability(BaseUnit caster, SkillCaster casterCaster, BaseUnit target,
        SkillCastTarget targetCaster, SkillObject skillObject, int actabilityId, int operation, int compareValue)
    {
        if (caster is not Character player)
        {
            // Not a player
            return false;
        }

        var actAbility = player.Actability.Actabilities.GetValueOrDefault((uint)actabilityId);
        var actAbilityPoints = actAbility?.Point ?? 0;
        return CompareWithOperator(actAbilityPoints, operation, compareValue);
    }

    // 15
    private static bool ConditionStealth(BaseUnit caster, SkillCaster casterCaster, BaseUnit target,
        SkillCastTarget targetCaster, SkillObject skillObject, int unused1, int unused2, int unused3)
    {
        // Unsure if player or target, plot logic suggests it is the target
        // only used for Flamebolt for some reason.
        // Is also always a "NotCondition" so will default to false (result will be True) (only on non-stealth targets)
        return target?.Buffs.CheckBuffTag((uint)TagsEnum.Stealth) ?? false;
    }

    // 16
    private static bool ConditionVisible(BaseUnit caster, SkillCaster casterCaster, BaseUnit target,
        SkillCastTarget targetCaster, SkillObject skillObject, int unused1, int unused2, int unused3)
    {
        if (target != null)
        {
            return target.Buffs.CheckBuffTag((uint)TagsEnum.Stealth) == false && target.IsVisible;    
        }
        return false;
    }
    private static bool ConditionAbLevel(BaseUnit caster, SkillCaster casterCaster, BaseUnit target,
        SkillCastTarget targetCaster, SkillObject skillObject, int abilityType, int minimumLevel, int maximumLevel)
    {
        if (caster is Character character)
        {
            var ability = character.Abilities.Abilities[(AbilityType)abilityType];
            int abLevel = ExperienceManager.Instance.GetLevelFromExp(ability.Exp, out _);
            return abLevel >= minimumLevel && abLevel <= maximumLevel;
        }
        
        // Should this ever not be a character using this condition?
        return false;
    }

    /// <summary>
    /// Helper function for condition checks with comparators
    /// </summary>
    /// <param name="value"></param>
    /// <param name="operation"></param>
    /// <param name="compareValue"></param>
    /// <returns></returns>
    /// <exception cref="GameException">Invalid operator</exception>
    private static bool CompareWithOperator(int value, int operation, int compareValue)
    {
        switch (operation) // operator
        {
            case 1: // ==
                return value == compareValue;
            case 2: // > x
                return value > compareValue;
            case 3: // >= x
                return value >= compareValue;
            case 4: // < x
                return value < compareValue;
            case 5: // <= x
                return value <= compareValue;
            default:
                throw new GameException($"CompareWithOperator: Unknown Comparison Operation {operation}");
        }
    }
}

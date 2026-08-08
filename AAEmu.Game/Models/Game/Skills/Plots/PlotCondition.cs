using AAEmu.Commons.Exceptions;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.GameData;
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
    /// plot_conditions.or_unit_reqs — whether the unit_reqs rows owned by this condition pass when ANY of
    /// them holds (94 rows) or only when ALL do (1511 rows).
    /// </summary>
    public bool OrUnitReqs { get; set; }

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
            PlotConditionType.Range => ConditionRange(caster, casterCaster, target, targetCaster, skillObject, Param1, Param2, Param3, skill),
            PlotConditionType.Variable => ConditionVariable(caster, casterCaster, target, targetCaster, skillObject, Param1, Param2, Param3, skill),
            PlotConditionType.UnitAttrib => ConditionUnitAttrib(caster, casterCaster, target, targetCaster, skillObject, Param1, Param2, Param3),
            PlotConditionType.Actability => ConditionActability(caster, casterCaster, target, targetCaster, skillObject, Param1, Param2, Param3),
            PlotConditionType.Stealth => ConditionStealth(caster, casterCaster, target, targetCaster, skillObject, Param1, Param2, Param3),
            PlotConditionType.Visible => ConditionVisible(caster, casterCaster, target, targetCaster, skillObject, Param1, Param2, Param3),
            PlotConditionType.ABLevel => ConditionAbLevel(caster, casterCaster, target, targetCaster, skillObject, Param1, Param2, Param3),
            PlotConditionType.CombatResource => ConditionCombatResource(caster, Param1, Param2, Param3),
            PlotConditionType.UnitReqs => ConditionUnitReqs(caster, target),
            _ => UnhandledKind()
        };

        Logger.Trace($"PlotCondition : {Kind} | Params : {Param1}, {Param2}, {Param3} | Result : {(NotCondition ? "NOT" : "")} {res}");

        return NotCondition ? !res : res;
    }

    // 20
    /// <summary>
    /// unit_reqs: the condition's checks live in unit_reqs rows owned by it, not in its own params.
    /// </summary>
    /// <remarks>
    /// The most used gate in the shipped plots after buff_tag and dead — 1605 conditions, of which 1504 own
    /// unit_reqs rows. It is the "may this unit do this here" test: stance, equipped weapon type, buff, zone,
    /// gear score, combat resource. While it was unimplemented all 1605 passed, so every plot branching on it
    /// took its first edge unconditionally.
    /// </remarks>
    private bool ConditionUnitReqs(BaseUnit caster, BaseUnit target)
    {
        return UnitRequirementsGameData.Instance.CanPassPlotCondition(this, caster, target);
    }

    /// <summary>
    /// Fallback for condition kinds this server does not implement yet. Stays permissive so an
    /// unknown kind cannot silently block a plot, but says so once per kind instead of passing
    /// invisibly - kinds 18/21 are still unimplemented and used to look like working gates.
    /// </summary>
    private bool UnhandledKind()
    {
        if (_warnedKinds.Add(Kind))
            Logger.Warn($"PlotCondition kind {(int)Kind} ({Kind}) is not implemented - treated as true. Plots gated on it take their first branch unconditionally.");
        return true;
    }

    private static readonly HashSet<PlotConditionType> _warnedKinds = [];

    // 19
    /// <summary>
    /// combat_resource: is the caster's amount of combat resource <paramref name="combatResourceId"/>
    /// within [<paramref name="min"/>, <paramref name="max"/>]?
    /// </summary>
    /// <remarks>
    /// This is the v10 combo-point gate. Malediction is the clearest case: Ghastly Pack (plot 5477)
    /// carries three of these on resource 15 - (1,4), (5,9) and (10,10) - which are exactly the
    /// base / "5 Malice Charges" / "10 Malice Charges" tiers the tooltip advertises. While kind 19
    /// was unimplemented all three passed, so the plot always took its first branch, never reached
    /// the higher tiers, and never ran the plot_effects that spend the charges.
    /// 233 conditions across the shipped data are gated this way.
    /// </remarks>
    private static bool ConditionCombatResource(BaseUnit caster, int min, int max, int combatResourceId)
    {
        if (caster is not Unit casterUnit)
        {
            Logger.Warn($"PlotCondition CombatResource check without caster being a Unit");
            return false;
        }

        var amount = casterUnit.GetCombatResource(combatResourceId);
        return amount >= min && amount <= max;
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

    // 6 — compact.sqlite3 enum_weapon_equip_statuses:
    // 1 onehand, 2 twohand, 3 dual_wield, 4 bow, 5 gun.
    // 4/5 are ranged holdables (const_holdable_types bow / shot_gun), not WeaponWieldKind.
    // Shotgun auto-attack plot 5796 event 52244 ("총 장착 여부 판정") uses param1=5; treating 5 as
    // WeaponWieldKind always failed and stopped the plot after the start node (no damage, no ManaCost).
    private static bool ConditionWeaponEquipStatus(BaseUnit caster, SkillCaster casterCaster, BaseUnit target,
        SkillCastTarget targetCaster, SkillObject skillObject, int weaponEquipStatus, int unused2, int unused3)
    {
        if (caster is not Character character)
            return false;

        return weaponEquipStatus switch
        {
            1 => character.GetWeaponWieldKind() == WeaponWieldKind.OneHanded,
            2 => character.GetWeaponWieldKind() == WeaponWieldKind.TwoHanded,
            3 => character.GetWeaponWieldKind() == WeaponWieldKind.DuelWielded,
            4 => HasRangedHoldable(character, "bow"),
            5 => HasRangedHoldable(character, "shot_gun"),
            _ => false
        };
    }

    private static bool HasRangedHoldable(Character character, string holdableName)
    {
        var holdableId = ItemManager.Instance.GetConstHoldableId(holdableName);
        if (holdableId == 0)
            return false;
        var ranged = character.Inventory.Equipment.GetItemBySlot((int)EquipmentItemSlot.Ranged);
        return ranged?.Template is WeaponTemplate weapon &&
               weapon.HoldableTemplate.Id == holdableId;
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

        var roll = Random.Shared.Next(0, 100);
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
    /// <summary>
    /// Range gate. Its max is widened to the radius of the area search that selected this target, when
    /// that search reached further than the gate allows.
    /// </summary>
    /// <remarks>
    /// The shipped data disagrees with itself: Backdraft (44200) selects with aoe_shapes 19754 (r 9.7) and
    /// then re-checks with Range 0..9, so a unit between 9.0 and 9.7m is found, counted into the target
    /// list, and only then dropped — while the client, which draws the telegraph from the shape, shows it
    /// comfortably inside the cone. Left alone, the outer 0.7m of every such cone is decorative.
    ///
    /// This is a deliberate deviation from the raw data, and it is scoped to that contradiction: the gate
    /// only ever grows, only for a target that an area search actually selected, and only up to that
    /// search's own radius (blank shape rows, which fall back to a 40m guess, are never recorded). A
    /// target that was never area-selected, or one further out than the selection reached, is judged by
    /// the unmodified value.
    /// </remarks>
    private static bool ConditionRange(BaseUnit caster, SkillCaster casterCaster, BaseUnit target,
        SkillCastTarget targetCaster, SkillObject skillObject, int minRange, int maxRange, int unused3,
        Skill skill = null)
    {
        // Param1 = Min range
        // Param2 = Max range
        var range = caster.GetDistanceTo(target);

        var effectiveMax = (float)maxRange;
        var plotState = skill?.ActivePlotState ?? (caster as Unit)?.ActivePlotState;
        if (target != null && plotState != null &&
            plotState.AreaSelectionRadius.TryGetValue(target.ObjId, out var selectionRadius) &&
            selectionRadius > effectiveMax)
        {
            effectiveMax = selectionRadius;
        }

        return range >= minRange && range <= effectiveMax;
    }

    // 12
    private static bool ConditionVariable(BaseUnit caster, SkillCaster casterCaster, BaseUnit target,
        SkillCastTarget targetCaster, SkillObject skillObject, int variableIndex, int operation, int compareValue,
        Skill skill = null)
    {
        // Prefer this skill's plot state — caster.ActivePlotState is overwritten by concurrent plot_only combos.
        var plotState = skill?.ActivePlotState ?? (caster as Unit)?.ActivePlotState;
        if (plotState == null)
        {
            Logger.Warn("PlotCondition Variable check without ActivePlotState");
            return false;
        }

        // enum_plot_variable_kinds: 1..10 = a..j, 11 = zero, 12 = targets. 11 and 12 are engine
        // pseudo variables that content never writes — special_effects of type set_variable only
        // ever target indices 1..5 and 10. Variables is int[12] (0..11), so index 12 fell straight
        // through the range guard below and returned false, which hard-wired every "did this event's
        // area search hit anything?" gate to failure. 268 plot_conditions rows use param1=12, among
        // them 20527 on event 48423 of Crashing Wave's plot 3523.
        const int variableZero = 11;
        const int variableTargets = 12;
        if (variableIndex == variableTargets)
            return CompareWithOperator(plotState.LastEffectedTargetCount, operation, compareValue);
        if (variableIndex == variableZero)
            return CompareWithOperator(0, operation, compareValue);

        if (variableIndex < 0 || variableIndex >= plotState.Variables.Length)
            return false;

        var variableValue = plotState.Variables[variableIndex];
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

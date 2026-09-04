namespace AAEmu.Game.Models.Game.Formulas;

public enum FormulaKind
{
    CastingCancelPercent = 2,
    CastingDelayTime = 3,
    DetectStealthRangeFront = 4,
    NpcPerceptionRangeMod = 5,
    MoneyForRecoverExp = 6,
    LaborPowerForRecoverExp = 7,
    PenaltyExp = 8,
    RecoverableExp = 9,
    DetectStealthRangeBack = 10,
    DamageMultiplierByHeight = 11,
    DamageMultiplierByRange = 12,
    ArmorGradeBuffABLevel = 13,
    NPCFlyPassDist = 14,
    NPCFlyPassHeight = 15,
    NPCFlyDiveDist = 16,
    NPCFlyDiveHeight = 17,
    NPCFlyQuickTurnDist = 18,
    ExpByLaborPower = 19,
    DamageBonusMultiplierByLevel = 20,
    DamagePenaltyMultiplierByLevel = 21,
    GradeEnchantCost = 22,
    DamageReduceRadioByBattleResist = 23,
    FacetsForBullsEye = 24,
    FlexibilityRatio = 25,
    FlexibilityBonus = 26,
    ExpBySkillEffect = 27,
    // this is for versions after 1.2
    PhysicsCollisionDamage = 28,
    SlaveEquipmentGradeEnchantCost = 29,
    GearScoreWeaponArmorAcc = 30,
    GearScoreSocket = 31,
    GearScoreEnchantingGem = 32,
    ConquestBonusScore = 33,
    ConquestPenaltyScore = 34,
    AAPointMaxLimitByLevel = 35,
    LeadershipPointDecreaseForNationChange = 36,
    MateEquipmentEchantCost = 37,
    ItemSocketingCost = 38,
    UnbindEquipBindItem = 39,
    ResetSkillCost = 40,
    SwapAbilityCost = 41,
    SwapAbilitySetCost = 42,
    EloRatingCalculation = 43,
    BlessUthstinConsumeItemNum = 44,
    BlessUthstinExtendMaxStat = 45,

    /// <summary>
    /// Coin charged per tempering step. Variables: item_level, scale_cost (the target rung's cost),
    /// equip_slot_enchant_cost (the slot's own factor) and enchant_scale_cost_mul.
    /// </summary>
    EnchantScaleCost = 59,
    // 10.0.2.13 gear-score formulas (formulas table ids)
    GearScoreArmor = 56,
    GearScoreAccessory = 57,

    /// <summary>
    /// Coin charged per synthesis attempt. Variables: item_evolving_value (the price already totalled
    /// across the grades the experience travels, each at its own gold_mul), item_level, and
    /// item_evolving_cost_mul - which is unit attribute 223 on the caster, a per-mille discount, not
    /// anything off the pool.
    /// </summary>
    ItemEvolvingCost = 64,
};

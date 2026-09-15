namespace AAEmu.Game.Models.Game.Units;

/// <remarks>
/// Ids with shipped <c>unit_modifiers</c> rows that this branch still leaves without a consumer, so the next
/// reader does not have to re-derive it:
/// <list type="bullet">
/// <item><description>
/// <see cref="SwimSpeedMul"/> (66, 156 rows: 126 Buff, 11 Item, 11 Npc, 3 BuffUnitModifier, 5
/// ExpeditionBuffGrade): movement is simulated by the zone, not by this process. The world only has an
/// estimated move rate for the NPC/slave route simulation, which reads <see cref="MoveSpeedMul"/> (10) and has
/// no swim case, so there is nothing here to scale. The buff is already relayed to the zone, which owns the
/// actual swim speed.
/// </description></item>
/// <item><description>
/// <c>ignore_shield_bonus</c> (205): two rows, both on <c>(attr_test)</c> placeholders (buff 28506, item
/// 50774), and no shipped definition of the "shield bonus" the name refers to.
/// </description></item>
/// <item><description>
/// <c>ignore_shield_bonus_mul</c> (206): six rows — the 초승돌: 격파 gems (items 39823 at 13, 40938 at 23,
/// 39825 at 43), two <c>(attr_test)</c> placeholders at 100 (buff 28507, item 50774) and one row with
/// <c>enable='f'</c> (buff 11194). The server models a shield as the charge <see cref="IgnoreShieldChance"/>
/// bypasses, with no bonus amount for a "bonus" multiplier to scale, so wiring it would mean inventing the
/// quantity it multiplies.
/// </description></item>
/// </list>
/// </remarks>
public enum UnitAttribute : uint // 10.0.2.13 adds unit_attribute_id 256-261 (>255), widened from byte
{
    Str = 0,
    Dex = 1,
    Sta = 2,
    Int = 3,
    Spi = 4,
    Fai = 5,
    MaxHealth = 6,
    MaxMana = 7,
    Armor = 8,
    MoveSpeedMul = 10,
    HealthRegen = 11,
    ManaRegen = 12,
    Facets = 13,
    MeleeCritical = 16,
    MeleeCriticalBonus = 17,
    MeleeAntiMiss = 18,
    MeleeDodge = 20,
    MeleeBlock = 21,
    MeleeParry = 22,
    RangedAntiMiss = 23,
    RangedCritical = 25,
    RangedCriticalBonus = 26,
    SpellAntiMiss = 28,
    SpellCritical = 30,
    SpellCriticalBonus = 31,
    MeleeDpsInc = 33,
    RangedDpsInc = 34,
    SpellDpsInc = 35,
    MainhandSpeed = 36,
    MainhandDamageMin = 37,
    MainhandDamageMax = 38,
    OffhandSpeed = 41,
    OffhandDamageMin = 42,
    OffhandDamageMax = 43,
    RangedSpeed = 46,
    RangedDamageMin = 47,
    RangedDamageMax = 48,
    MeleeDamageMul = 51,
    RangedDamageMul = 52,
    SpellDamageMul = 53,
    MeleeSpeedMul = 54,
    RangedSpeedMul = 55,
    IncomingHealMul = 56,
    IgnoreArmor = 57,
    IncomingDamageMul = 58,
    RangedDodge = 59,
    RangedBlock = 60,
    AggroRangeMul = 61,
    IncomingAggroMul = 62,
    Hovering = 63,
    MagicResist = 64,
    MagicStability = 65,
    SwimSpeedMul = 66,
    PersistentHealthRegen = 67,
    PersistentManaRegen = 68,
    ArmorType = 69,
    ArmorTypeCoverage = 70,
    CastingTimeMul = 71,
    TurnSpeed = 72,
    GravityMul = 73,
    GlobalCooldownMul = 74,
    TwohandSpeedMul = 75,
    MeleeCriticalMul = 77,
    MeleeAntiMissMul = 78,
    MeleeDodgeMul = 79,
    MeleeBlockMul = 80,
    MeleeParryMul = 81,
    RangedCriticalMul = 82,
    RangedAntiMissMul = 83,
    RangedDodgeMul = 84,
    RangedBlockMul = 85,
    SpellCriticalMul = 86,
    SpellDps = 87,
    SpellAntiMissMul = 88,
    CastingTolerance = 89,
    AggroMul = 90,
    LungCapacity = 91,
    FallDamageMul = 92,
    LadderSpeedMul = 93,
    DetectStealthRangeMul = 94,
    ExpMul = 95,
    MainhandDps = 96,
    OffhandDps = 97,
    RangedDps = 98,
    ActabilityArchemy = 99,
    ActabilityArchitecture = 100,
    ActabilityCook = 101,
    ActabilityHandicraft = 102,
    ActabilityLivestock = 103,
    ActabilityFarm = 104,
    ActabilityFish = 105,
    ActabilityLumber = 106,
    ActabilityCollection = 107,
    ActabilityMachinery = 108,
    ActabilityMetal = 109,
    ActabilityPrint = 110,
    ActabilityMine = 111,
    ActabilityStonemason = 112,
    ActabilitySewing = 113,
    ActabilitySkin = 114,
    ActabilityWeapon = 115,
    ActabilityCarpentry = 116,
    ActabilityTheft = 117,
    ActabilityBusiness = 118,
    AttackAnimSpeedMul = 119,
    HealMul = 120,
    BackattackMeleeDamageMul = 121,
    BackattackRangedDamageMul = 122,
    BackattackSpellDamageMul = 123,
    FrictionMul = 124,
    HonorPointLoseMul = 125,
    HonorPointGainBattleField = 126,
    HonorPointGainBattleFieldMul = 127,
    HonorPointGainNpcKill = 128,
    HonorPointGainNpcKillMul = 129,
    HonorPointGainTrial = 130,
    HonorPointGainTrialMul = 131,
    HonorPointGainWar = 132,
    HonorPointGainWarMul = 133,
    HonorPointGainQuest = 134,
    HonorPointGainQuestMul = 135,
    LivingPointGain = 136,
    LivingPointGainMul = 137,
    ActabilityComposition = 138,
    UnderwaterSwimSpeedMul = 139,
    DropRateMul = 140,
    LootGoldMul = 141,
    IncomingMeleeDamageAdd = 142,
    IncomingMeleeDamageMul = 143,
    IncomingRangedDamageAdd = 144,
    IncomingRangedDamageMul = 145,
    IncomingSpellDamageAdd = 146,
    IncomingSpellDamageMul = 147,
    IncomingSiegeDamageAdd = 148,
    IncomingSiegeDamageMul = 149,
    SpellDamageCritical = 150,
    SpellDamageCriticalMul = 151,
    SpellDamageCriticalBonus = 152,
    RangedParry = 153,
    RangedParryMul = 154,
    DeathDurabilityLossRatioMul = 155,
    PenaltyExpMul = 156,
    RecoverableExpMul = 157,
    // Language actability attrs 158-165 are unused here.
    /*
    ACTABILITY_LANG_NUIAN = 0x9E,
    ACTABILITY_LANG_ELF = 0x9F,
    ACTABILITY_LANG_HARIHARAN = 0xA0,
    ACTABILITY_LANG_FERRE = 0xA1,
    ACTABILITY_LANG_WESTCOMMON = 0xA2,
    ACTABILITY_LANG_EASTCOMMON = 0xA3,
    ACTABILITY_LANG_DWARF = 0xA4,
    ACTABILITY_LANG_WARBORN = 0xA5,*/
    HealDps = 173,
    HealCritical = 174,
    HealDpsInc = 175,
    HealCriticalBonus = 176,
    Block = 177,
    Dodge = 178,
    BlockMul = 179,
    DodgeMul = 180,
    BullsEye = 181,
    BattleResist = 182,
    Flexibility = 183,
    MagicPenetration = 184,
    HealCriticalMul = 185,
    ExpByLaborPowerMul = 186,
    /// <summary>
    /// Percent points of collision damage the hull takes, 100 = unmodified. Rowboats carry +19900
    /// (they shatter), the Growling sailing ship +50, and the dock's Moored buff -99.
    /// This is one value for the whole hull — the per-face split lives in slave_collision_damages.
    /// </summary>
    PhysicsCollisionDamageMul = 187,
    /// <summary>
    /// Extra kilograms on a hull (sails, figure, engine, masts, toys). The zone adds this to
    /// <c>ship_models.mass</c> when it folds thrust; it is not a collision multiplier despite
    /// sitting next to those attributes.
    /// </summary>
    Mass = 188,
    /// <summary>
    /// Percent points of collision armour, 100 = unmodified, divides the damage. Monster figurehead
    /// grades give +10/20/35/50, the dock's Moored buff +2900.
    /// </summary>
    PhysicsCollisionArmorMul = 194,

    /// <summary>
    /// The attacker's per-mille chance to bypass the victim's damage absorption ("방패 관통률").
    /// <c>unit_attribute_limits</c> row 26 floors it at 0. See <c>ShieldIgnoreRules</c>.
    /// </summary>
    IgnoreShieldChance = 204,
    /// <summary>
    /// Signed delta on every combat resource ceiling. Buff 22278 (정복) stores 1 for "광란의 중첩 개수가
    /// 1개 증가합니다". See <c>CombatResourceRules</c>.
    /// </summary>
    MaxCombatResource = 215,
    /// <summary>
    /// Attack speed as a per-mille rate, the newer family of <see cref="GlobalCooldownMul"/> (74),
    /// <see cref="MeleeSpeedMul"/> (54), <see cref="RangedSpeedMul"/> (55) and
    /// <see cref="AttackAnimSpeedMul"/> (119). <c>unit_attribute_limits</c> row 46 bounds it to -666..2000.
    /// See <c>SpeedMultiplierRules</c>.
    /// </summary>
    AttackSpeedMul = 218,

    /// <summary>
    /// Siege damage the caster adds, the siege counterpart of <see cref="SpellDps"/>: the effect composes it
    /// into the same DPS term. The twelve 검은 가시 감옥 stages (buffs 29998-30009) walk it -400…+700.
    /// </summary>
    SiegeDps = 260,
    /// <summary>
    /// Siege damage the caster deals as a per-mille delta, the counterpart of
    /// <see cref="MeleeDamageMul"/>. See <c>SiegeDamageRules</c>.
    /// </summary>
    SiegeDamageMul = 261,

    // The five ids above (204, 215, 218, 260, 261) are also declared by the regenerated enum in PR #1589
    // (fix/c1-unit-attribute-enum). Same ids and same names there, so whichever lands first is a no-op for
    // the other; nothing here renumbers an existing member.

    /// <summary>
    /// Per-mille discount on what a synthesis attempt costs, which is why
    /// <c>unit_attribute_limits</c> pens it into -1000..0. Jake's Blessing grants the full -1000 and
    /// makes synthesis free; nothing shipped raises the price.
    /// </summary>
    ItemEvolvingCostMul = 223,
}

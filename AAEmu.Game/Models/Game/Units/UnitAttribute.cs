namespace AAEmu.Game.Models.Game.Units;

/// <summary>
/// One member per <c>enum_unit_attribute</c> row of the 10.0.2.13 content DB (255 rows, ids 0-289),
/// plus the eleven ids this server has always carried that the 10.0.2 table dropped.
/// </summary>
/// <remarks>
/// Names are the table's <c>name</c> column in PascalCase: split on '_', upper-case the first letter
/// of each part and leave the rest alone (<c>melee_dps_inc_anti_npc</c> becomes
/// <see cref="MeleeDpsIncAntiNpc"/>). Two members deliberately differ: 187 keeps its long-standing
/// name (the table calls it <c>physics_collision_front_damage_mul</c>) and 239 spells out the table's
/// "engin". The id-to-name pairs are checked in at
/// <c>AAEmu.UnitTests/Game/Models/Game/Units/UnitAttributeContentSnapshot.cs</c> and asserted against
/// this enum by <c>UnitAttributeContentTests</c>, so a member that does not come from a real row at
/// that exact id fails the test run.
///
/// The 10.0.2.13 client ids reach 289 and the zone sends 256-261, which is why this is uint-backed
/// rather than a byte.
///
/// Id 14 is used by two <c>unit_modifiers</c> rows (buffs 185/186) but has no row in
/// <c>enum_unit_attribute</c>, so it has no name to derive and stays unnamed here; the loaders report
/// it once per start instead of dropping it silently.
///
/// Ids with shipped <c>unit_modifiers</c> rows that this server still leaves without a consumer, so the
/// next reader does not have to re-derive it:
/// <list type="bullet">
/// <item><description>
/// <see cref="SwimSpeedMul"/> (66, 156 rows: 126 Buff, 11 Item, 11 Npc, 3 BuffUnitModifier, 5
/// ExpeditionBuffGrade): movement is simulated by the zone, not by this process. The world only has an
/// estimated move rate for the NPC/slave route simulation, which reads <see cref="MoveSpeedMul"/> (10) and
/// has no swim case, so there is nothing here to scale. The buff is already relayed to the zone, which owns
/// the actual swim speed.
/// </description></item>
/// <item><description>
/// <c>ignore_shield_bonus</c> (205): two rows, both on <c>(attr_test)</c> placeholders (buff 28506, item
/// 50774), and no shipped definition of the "shield bonus" the name refers to.
/// </description></item>
/// <item><description>
/// <c>ignore_shield_bonus_mul</c> (206): six rows - the 초승돌: 격파 gems (items 39823 at 13, 40938 at 23,
/// 39825 at 43), two <c>(attr_test)</c> placeholders at 100 (buff 28507, item 50774) and one row with
/// <c>enable='f'</c> (buff 11194). The server models a shield as the charge <see cref="IgnoreShieldChance"/>
/// bypasses, with no bonus amount for a "bonus" multiplier to scale, so wiring it would mean inventing the
/// quantity it multiplies.
/// </description></item>
/// </list>
/// </remarks>
public enum UnitAttribute : uint
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
    AggroRangeMul = 61,
    IncomingAggroMul = 62,
    Hovering = 63,
    MagicResist = 64,
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
    MeleeParryMul = 81,
    RangedCriticalMul = 82,
    RangedAntiMissMul = 83,
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
    // The eight language ladders. 10.0.2 names them in enum_unit_attribute and
    // actability_groups.unit_attr_id points at them.
    ActabilityLangNuian = 158,
    ActabilityLangElf = 159,
    ActabilityLangHariharan = 160,
    ActabilityLangFerre = 161,
    ActabilityLangWestcommon = 162,
    ActabilityLangEastcommon = 163,
    ActabilityLangDwarf = 164,
    ActabilityLangWarborn = 165,
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
    /// <c>enum_unit_attribute</c> names the row <c>physics_collision_front_damage_mul</c>;
    /// the member keeps the name the rest of the server already uses.
    PhysicsCollisionDamageMul = 187,
    /// <summary>
    /// Extra kilograms on a hull (sails, figure, engine, masts, toys). The zone adds this to
    /// <c>ship_models.mass</c> when it folds thrust; it is not a collision multiplier despite
    /// sitting next to those attributes.
    /// </summary>
    Mass = 188,
    // Hull driving-model values: mass multiplier, steering and reverse velocity.
    MassMul = 189,
    SteeringSpeed = 190,
    SteeringSpeedMul = 191,
    ReverseVelocity = 192,
    ReverseVelocityMul = 193,
    /// <summary>
    /// Percent points of collision armour, 100 = unmodified, divides the damage. Monster figurehead
    /// grades give +10/20/35/50, the dock's Moored buff +2900.
    /// </summary>
    PhysicsCollisionArmorMul = 194,
    ActabilityExploration = 195,

    /// <summary>
    /// Per-mille melee damage against NPC victims, 1000 = unmodified. The attacker's own output, so the
    /// victim kind picks between this trio (196-198) and the anti-PC trio (244-246).
    /// 93 rows of <c>unit_modifiers</c> carry each of the anti-NPC ids.
    /// </summary>
    MeleeDamageMulAntiNpc = 196,
    RangedDamageMulAntiNpc = 197,
    SpellDamageMulAntiNpc = 198,
    IncomingDamageMulAntiNpc = 199,
    IncomingMeleeDamageAddAntiNpc = 200,
    IncomingRangedDamageAddAntiNpc = 201,
    IncomingSpellDamageAddAntiNpc = 202,
    ImpactMass = 203,
    IgnoreShieldChance = 204,
    IgnoreShieldBonus = 205,
    IgnoreShieldBonusMul = 206,
    MeleeDynamicNormalizable = 207,
    RangedDynamicNormalizable = 208,
    MagicDynamicNormalizable = 209,
    HealDynamicNormalizable = 210,
    DefenceDynamicNormalizable = 211,
    MusicDynamicNormalizable = 212,
    MaxCombatResource = 215,
    CombatResourceRegen = 216,
    CombatResourceRegenInCombat = 217,
    AttackSpeedMul = 218,
    SubmergeDepth = 219,
    ExpByKillMonsterMul = 220,
    BaseCombatResource = 221,

    /// <summary>
    /// Per-mille heal output, 1000 = unmodified. 216 buff rows carry it. Unlike <see cref="HealMul"/>
    /// (120) it has no victim-kind split, and unlike <see cref="IncomingHealMul"/> (56) it belongs to
    /// the healer, not the healed.
    /// </summary>
    HealDamageMul = 222,
    /// <summary>
    /// Per-mille discount on what a synthesis attempt costs, which is why
    /// <c>unit_attribute_limits</c> pens it into -1000..0. Jake's Blessing grants the full -1000 and
    /// makes synthesis free; nothing shipped raises the price.
    /// </summary>
    ItemEvolvingCostMul = 223,
    // Per-face collision multipliers. 187 above is the whole-hull value the slave code
    // folds first; these split it per hull face.
    PhysicsCollisionSideDamageMul = 224,
    PhysicsCollisionRearDamageMul = 225,
    PhysicsCollisionTopDamageMul = 226,
    PhysicsCollisionBottomDamageMul = 227,
    ButlerHarvestGrowthTimeMul = 228,
    ButlerHarvestBonusRatioMul = 229,
    // Slave vehicle tuning (ship acceleration and halt, turret axes, engine torque).
    SlaveShipAcceleration = 230,
    SlaveShipReverseAcceleration = 231,
    SlaveShipHaltRate = 232,
    SlaveVehicleTurretPitchAngle = 233,
    SlaveVehicleTurretYawAngle = 234,
    SlaveVehicleTurretPitchSpeed = 235,
    SlaveVehicleTurretYawSpeed = 236,
    SlaveVehicleMaxClimbAngle = 237,
    SlaveVehicleMaxSpeed = 238,
    // enum_unit_attribute spells this slave_vehicle_engin_power.
    SlaveVehicleEnginePower = 239,
    SlaveVehicleBrakeTorque = 240,
    SlaveVehicleBallastMass = 241,
    ButlerTradeDeliveryTimeMul = 242,
    ButlerTradeProductionCostMul = 243,

    /// <summary>
    /// Per-mille melee damage against player victims, 1000 = unmodified. 7 rows of
    /// <c>unit_modifiers</c> carry each of the anti-PC ids, the largest being buff 27590
    /// ("pvp 기술 피해 버프") at 2000.
    /// </summary>
    MeleeDamageMulAntiPc = 244,
    RangedDamageMulAntiPc = 245,
    SpellDamageMulAntiPc = 246,
    SlaveVehicleSteerMul = 247,
    ExpByCompleteQuestMul = 248,
    MeleeDpsIncAntiNpc = 249,
    RangedDpsIncAntiNpc = 250,
    SpellDpsIncAntiNpc = 251,
    HealDamageMulAntiNpc = 252,
    HealDpsIncAntiNpc = 253,
    HealDpsIncOnlyHeal = 254,
    HealMulOnlyHeal = 255,
    // Enchant and socketing cost multipliers, then the siege damage lines.
    ElementEnchantCostMul = 256,
    GradeEnchantCostMul = 257,
    ItemSocketingCostMul = 258,
    EnchantScaleCostMul = 259,
    SiegeDps = 260,
    SiegeDamageMul = 261,
    // Labour-power advantage and extra harvest gain per actability group.
    LpAdvantageByArchemyActGroup = 262,
    LpAdvantageByArchitectureActGroup = 263,
    LpAdvantageByCookActGroup = 264,
    LpAdvantageByHandicraftActGroup = 265,
    LpAdvantageByLivestockActGroup = 266,
    LpAdvantageByFarmActGroup = 267,
    LpAdvantageByFishActGroup = 268,
    LpAdvantageByLumberActGroup = 269,
    LpAdvantageByCollectionActGroup = 270,
    LpAdvantageByMachineryActGroup = 271,
    LpAdvantageByMetalActGroup = 272,
    LpAdvantageByPrintActGroup = 273,
    LpAdvantageByMineActGroup = 274,
    LpAdvantageByStonemasonActGroup = 275,
    LpAdvantageBySewingActGroup = 276,
    LpAdvantageBySkinActGroup = 277,
    LpAdvantageByWeaponActGroup = 278,
    LpAdvantageByCarpentryActGroup = 279,
    LpAdvantageByTheftActGroup = 280,
    LpAdvantageByBusinessActGroup = 281,
    LpAdvantageByCompositionActGroup = 282,
    LpAdvantageByExplorationActGroup = 283,
    ExtraGainByLivestockActGroup = 284,
    ExtraGainByFarmActGroup = 285,
    ExtraGainByFishActGroup = 286,
    ExtraGainByLumberActGroup = 287,
    ExtraGainByCollectionActGroup = 288,
    ExtraGainByMineActGroup = 289,

    // ---------------------------------------------------------------------
    // Ids below are not in the 10.0.2.13 enum_unit_attribute table. They stay
    // declared so the values cannot be reused for something else unnoticed.
    // ---------------------------------------------------------------------

    /// <summary>
    /// 10.0.2's <c>enum_unit_attribute</c> has no row for 21, but <c>unit_attribute_limits</c> row 8
    /// still bounds it (0..2000000000), so the id is live content and stays a normal member.
    /// </summary>
    MeleeBlock = 21,

    /// <summary>
    /// 10.0.2's <c>enum_unit_attribute</c> has no row for 127 either, but
    /// <c>ExpeditionBuffGameData.GetBonusEffects</c> hands it out for expedition buff 10
    /// ("명예로운 생활" / honorable living). Kept as a normal member because server code produces it.
    /// </summary>
    HonorPointGainBattleFieldMul = 127,

    // The nine below are missing from that table *and* unreferenced by every server code path (grep of
    // AAEmu.Game, AAEmu.World and AAEmu.UnitTests), so nothing can produce them. They are obsolete
    // rather than deleted so the ids cannot be reused for something else unnoticed.
    [Obsolete("Id 20 is not in the 10.0.2 enum_unit_attribute table and has no consumer.")]
    MeleeDodge = 20,

    [Obsolete("Id 59 is not in the 10.0.2 enum_unit_attribute table and has no consumer.")]
    RangedDodge = 59,

    [Obsolete("Id 60 is not in the 10.0.2 enum_unit_attribute table and has no consumer.")]
    RangedBlock = 60,

    [Obsolete("Id 65 is not in the 10.0.2 enum_unit_attribute table and has no consumer.")]
    MagicStability = 65,

    [Obsolete("Id 79 is not in the 10.0.2 enum_unit_attribute table and has no consumer.")]
    MeleeDodgeMul = 79,

    [Obsolete("Id 80 is not in the 10.0.2 enum_unit_attribute table and has no consumer.")]
    MeleeBlockMul = 80,

    [Obsolete("Id 84 is not in the 10.0.2 enum_unit_attribute table and has no consumer.")]
    RangedDodgeMul = 84,

    [Obsolete("Id 85 is not in the 10.0.2 enum_unit_attribute table and has no consumer.")]
    RangedBlockMul = 85,

    [Obsolete("Id 126 is not in the 10.0.2 enum_unit_attribute table and has no consumer.")]
    HonorPointGainBattleField = 126,

}

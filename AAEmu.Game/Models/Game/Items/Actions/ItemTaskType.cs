namespace AAEmu.Game.Models.Game.Items.Actions;

/// <summary>
/// What an <c>SCItemTaskSuccessPacket</c> says it was for. The client acts on this byte far beyond
/// logging: several windows watch it to learn that the operation they started has finished.
/// </summary>
/// <remarks>
/// <para>
/// Every value here is read out of 10.0.2.13's own task-name table, which the client fills one entry
/// at a time in a single function (slots are a fixed stride apart, so the entry's offset divided by
/// that stride is the value). Do not renumber by intuition - the table diverges from the numbering
/// AAEmu inherited from index 18 upward, and the drift grows to seven by the end.
/// </para>
/// <para>
/// That drift was not cosmetic. <see cref="SkillReagents"/> was 40, which this client reads as
/// "405-quest-supply-items"; the gear upgrade window waits for exactly this task to learn its cast
/// is over, so temper, socket, synthesis, awakening and the two image actions all left it locked.
/// <see cref="EnchantPhysical"/> was 50, which this client reads as "trade" - hence the "trade
/// settled" notice a tempering attempt used to produce.
/// </para>
/// <para>
/// A handful of tasks AAEmu sends have no counterpart in this client at all. Those are parked on
/// slots the table leaves empty (120, 151, 159, 168-170, 174, 175, 177, 183, 195), so an unknown
/// value simply draws no reaction instead of triggering somebody else's window.
/// </para>
/// </remarks>
public enum ItemTaskType : byte
{
    Invalid = 0,
    Destroy = 1,
    AboxDestroy = 2,
    Repair = 3,
    DurabilityLoss = 4,
    SwapItems = 5,
    Split = 6,
    SplitCofferItems = 7,
    SwapCofferItems = 8,
    Loot = 9,
    LootAll = 10,
    Gm = 11,
    GameRuleReset = 12,
    ConsumeSkillSource = 13, // 303-consume-skill-source
    DoodadCreate = 14, // 303-doodad-create
    DoodadRemove = 15,
    DoodadItemChanger = 16,
    DoodadInteraction = 17,
    DoodadOneshotPlace = 18,
    DoodadCattleFeed = 19,
    AbilityChange = 20,
    AbilityReset = 21,
    CapturePet = 22,
    RecoverDoodadItem = 23, // 508-recover-doodad-item
    MateCreate = 24,
    CraftActSaved = 25,
    CraftPaySaved = 26,
    CraftPickupProduct = 27,
    CraftCancel = 28,
    MakeCraftOrderSheet = 29,
    RestoreCraftOrderSheet = 30,
    PostCraftOrder = 31,
    HouseCreation = 32, // 303-house-creation
    HouseDeposit = 33,
    HouseBuilding = 34,
    PickupBloodstain = 35,
    AutoLootDoodadItem = 36, // 316-autoloot-doodad-item
    QuestStart = 37, // 401-quest-start
    QuestComplete = 38, // 402-quest-complete
    QuestCompleteBalance = 39, // 402-quest-complete-balance
    QuestSupplyItems = 40, // 405-quest-supply-items
    QuestRemoveSupplies = 41,

    /// <summary>
    /// A skill taking the item it was cast from.
    /// </summary>
    /// <remarks>
    /// The gear upgrade window keys its release off this one: it notes the cast it sent, and only
    /// counts the cast as over once a task of this type names the same skill. Anything else leaves
    /// its button dead until the window is closed and reopened.
    /// </remarks>
    SkillReagents = 42,

    SkillEffectConsumption = 43,
    SkillEffectGainItem = 44,
    SkillEffectGainItemWithPos = 45,
    SkillEffectSiegeTicket = 46,
    SkillEffectExpToItem = 47,
    Auction = 48,
    Mail = 49,
    Trade = 50,
    EnchantMagical = 51,
    EnchantPhysical = 52,
    GetCoinByItem = 53,
    GetItemFromDoodad = 54,
    StoreSell = 55, // 315-store-sell
    StoreBuy = 56, // 313-314-store-buy
    TodReward = 57,
    GainItemWithUcc = 58, // create-origin-ucc
    MakeUccDye = 59,
    ImprintUcc = 60,
    RepairPets = 61,
    MateDeath = 62,
    Shipyard = 63, // 303-shipyard
    SkillsReset = 64,
    DropBackpack = 65,
    UseRelic = 66,
    Conversion = 67, // 304-conversion
    Seize = 68,
    ReturnSeized = 69,
    SlaveDeath = 70,
    ExpeditionCreation = 71,
    ExpeditionBuffGrade = 72,
    DeclareExpeditionWar = 73,
    RecruitmentDecMoney = 74,
    RepairSlaves = 75,
    ExpandBag = 76,
    ExpandBank = 77,
    LifespanExpiration = 78,
    RecoverExp = 79,
    SpawnerUpdate = 80,
    UpdateSummonSlaveItem = 81,
    UpdateSummonMateItem = 82,
    DepositMoney = 83,
    WithdrawMoney = 84,
    DeliverItemToOthers = 85,
    SetSlavePosition = 86,
    ConvertFish = 87,
    Fishing = 88,
    SellHouse = 89,
    BuyHouse = 90,
    SaveMusicNotes = 91,
    ItemLock = 92,
    ItemUnlock = 93,
    ItemUnlockExcess = 94,
    GradeEnchant = 95,
    ShipGradeEnchant = 96,
    RechargeRndAttrUnitModifier = 97,
    RechargeBuff = 98,
    Socketing = 99,

    /// <summary>Synthesis - the "Synthesis" tab, <c>itemEvolving</c> in the client's own naming.</summary>
    Evolving = 100,

    Smelting = 101,
    Dyeing = 102,
    RechargeItemProcLifetime = 103,
    ItemSocketChange = 104,
    ConsumeIndunTicket = 105,
    ExpandExpert = 106,
    Exchange = 107,
    SellBackpack = 108,
    SellSpecialty = 109,
    BuySpecialty = 110,
    AskMould = 111,
    TakeMould = 112,
    FactionDeclareHostile = 113,
    EditCosmetic = 114,
    EditVisualRace = 115,
    RenewalVisualRaceTime = 116,
    ChangeAutoUseAaPoint = 117,

    /// <summary>Fusing an appearance onto a piece of equipment.</summary>
    ConvertItemLook = 118,

    /// <summary>Taking a fused appearance back off a piece of equipment.</summary>
    RevertItemLook = 119,

    /// <summary>
    /// Levelling an heir skill. This client has no task of its own for it, so it sits on an empty
    /// slot rather than borrowing a neighbour's meaning.
    /// </summary>
    UpgradeSkill = 120,

    ChangeExpertLimit = 121,
    Skinize = 122, // sknize
    ItemTaskThistimeUnpack = 123,
    BuyPremiumService = 124, // buy-premium-service-ingameshop
    BuyAaPoint = 125, // buy-aa-point-ingameshop
    TakeScheduleItem = 126,

    /// <summary>
    /// Tempering - the "Tempering" tab. The window's result half waits on this exact value: the
    /// client hard-codes 127 as the task that reports a tempering attempt as settled.
    /// </summary>
    ScaleCap = 127,

    ScaleCapReset = 128,
    HousePayTax = 129,
    BuyItemIngameshop = 130,

    /// <summary>
    /// Spending a portal scroll. Another one this client does not name; parked on an empty slot.
    /// </summary>
    Teleport = 151,

    RestoreDisableEnchant = 171,
    ItemTypeChange = 176,
    ItemElement = 185
}

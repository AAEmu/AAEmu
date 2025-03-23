namespace AAEmu.Game.Models.Game.Items.Actions;

// TODO at X2::GameClient::ApplyItemTaskToSelf
public enum ItemTaskType : byte
{
    // updated to version 5.0.7.0
    Invalid = 0,                    // invalid
    Destroy = 1,                    // destroy
    AboxDestroy = 2,                // abox-destroy
    Repair = 3,                     // repair
    DurabilityLoss = 4,             // durability-loss
    SwapItems = 5,                  // swap-items
    Split = 6,                      // split
    SplitCofferItems = 7,           // split-coffer-items
    SwapCofferItems = 8,            // swap-coffer-items
    Loot = 9,                       // loot
    LootAll = 10,                   // loot-all
    Gm = 11,                        // gm
    GameRuleReset = 12,             // gamerule-reset
    ConsumeSkillSource = 13,        // 303-consume-skill-source
    DoodadCreate = 14,              // 303-doodad-create
    DoodadRemove = 15,              // doodad-remove
    DoodadItemChanger = 16,         // doodad-item_changer
    DoodadInteraction = 17,         // doodad-interaction
    DoodadCattleFeed = 18,          // doodad-cattle-feed
    // UpgradeSkill = 19
    AbilityChange = 19,             // ability-change
    AbilityReset = 20,              // ability-reset
    // BuyPriestBuff = 22
    // Teleport = 23
    CapturePet,                     // capture-pet
    RecoverDoodadItem,              // 508-recover-doodad-item
    MateCreate,                     // mate-create
    CraftActSaved,                  // craft-act-saved
    CraftPaySaved,                  // craft-pay-saved
    CraftPickupProduct = 27,        // craft-pickup-product
    CraftCancel,                    // craft-cancel
    MakeCraftOrderSheet,            // make-craft-order-sheet
    RestoreCraftOrderSsheet,        // restore-craft-order-sheet
    PostCcraftOrder,                // post-craft-order
    HouseCreation = 32,             // 303-house-creation
    HouseDeposit,                   // house-deposit
    HouseBuilding = 34,             // house-building
    PickupBloodstain,               // pickup-bloodstain
    AutoLootDoodadItem,             // 316-autoloot-doodad-item
    QuestStart = 37,                // 401-quest-start
    QuestComplete = 38,             // 402-quest-complete
    QuestSupplyItems,               // 405-quest-supply-items
    QuestRemoveSupplies,            // 402-403-404-405-quest-remove-supplies
    SkillReagents,                  // skill-reagents
    SkillEffectConsumption,         // skill-effect-consumption
    SkillEffectGainItem,            // skill-effect-gain-item
    SkillEffectGainItemWithPos,     // skill-effect-gain-item-with-pos
    SkillEffectSiegeTicket,         // skill-effect-siege-ticket
    SkillEffectExpToItem,           // skill-effect-exp-to-item
    Auction = 48,                   // auction
    Mail = 49,                      // mail
    Trade,                          // trade
    EnchantMagical,                 // enchant-magical
    EnchantPhysical,                // enchant-physical
    GetCoinByItem,                  // get-coin-by-item
    GetItemByCoin,                  // get-item-from-doodad
    StoreSell = 55,                 // 315-store-sell
    StoreBuy = 56,                  // 313-314-store-buy
    TodReward,                      // tod-reward
    CreateOriginUcc,                // create-origin-ucc
    MakeUccDdye,                    // make-ucc-dye
    // GainItemWithUcc = 56
    ImprintUcc = 60,                // imprint-ucc
    RepairPets,                     // repair-pets
    MateDeath = 62,                 // mate-death
    Shipyard = 63,                  // 303-shipyard
    SkillsReset,                    // skills-reset
    DropBackpack = 65,              // drop-backpack
    UseRelic,                       // use-relic
    UseIndependenceRelic,           // use-independence-relic
    Conversion = 68,                // 304-conversion
    Seize,                          // seize
    ReturnSeized,                   // return-seized
    DemoDressOff,                   // demo-dress-off
    DemoDressOn,                    // demo-dress-on
    DemoClearBag,                   // demo-clear-bag
    DemoFillBag,                    // demo-fill-bag
    SlaveDeath = 75,                // slave-death
    ExpeditionCreation,             // expedition-creation
    DeclareExpeditionWar,           // declare-expedition-war
    RecruitmentDecMoney,            // recruitment-dec-money
    RepairSlaves,                   // repair-slaves
    ExpandBag,                      // expand-bag
    ExpandBank,                     // expand-bank
    // RenewEquipment = 77
    LifespanExpiration = 83,        // lifespan-expiration
    RecoverExp,                     // recover-exp
    SpawnerUpdate,                  // spawner_update
    UpdateSummonSlaveItem = 86,     // update-summon-slave-item
    UpdateSummonMateItem = 87,      // update-summon-mate-item
    DepositMoney = 88,              // deposit-money
    WithdrawMoney,                  // withdraw-money
    DeliverItemToOthers,            // deliver-item-to-others
    SetSlavePosition,               // set-slave-position
    SetBountyMoney,                 // set-bounty-money
    PayBountyMoney,                 // pay-bounty-money
    ConvertFish,                    // convert-fish
    Fishing,                        // fishing
    SellHouse,                      // sell-house
    BuyHouse,                       // buy-house
    SaveMusicNotes,                 // save-music-notes
    ItemLock,                       // item-lock
    ItemUnlock,                     // item-unlock
    ItemUnlockExcess,               // item-unlockexcess
    GradeEnchant,                   // grade-enchant
    ShipGradeEnchant,               // ship-grade-enchant
    RechargeRndAttrUnitModifier,    // recharge-rnd-attr-unit-modifier
    RechargeBuff,                   // recharge-buff
    Socketing,                      // socketing
    Evolving,                       // evolving
    Smelting,                       // smelting
    Dyeing,                         // dyeing
    RechargeItemProcLifetime,       // recharge-item-proc-lifetime
    ConsumeIndunTicket,             // consume-indun-ticket
    ExpandExpert,                   // expand-expert
    Exchange,                       // exchange
    SellBackpack,                   // sell-backpack
    SellSpecialty,                  // sell-specialty
    BuySpecialty,                   // buy-specialty
    AskMould,                       // ask-mould
    TakeMould,                      // take-mould
    FactionDeclareHostile,          // faction-declare-hostile
    EditCosmetic,                   // edit-cosmetic
    ChangeAutoUseAaPoint,           // change-auto-use-aa-point
    ConvertItemLook,                // convert_item_look
    RevertItemLook,                 // revert_item_look
    ChangeExpertLimit,              // change-expert-limit
    Sknize,                         // sknize
    ItemTaskThistimeUnpack,         // item-task-thistime-unpack
    BuyPremiumService,              // buy-premium-service-ingameshop
    BuyAaPoint,                     // buy-aa-point-ingameshop
    TakeScheduleItem,               // take-schedule-item
    ScaleCap,                       // scale-cap
    HousePayTax,                    // house-pay-tax
    BuyItemIngameshop = 134,        // buy-item-ingameshop
    ExchangeCashFromItem,           // exchange-cash-from-item
    RepairSlaveEquipment,           // repair-slave-equipment
    RechargeSkill,                  // recharge-skill
    AchievementSupplyItems,         // achievement-supply-items
    TodayAssignmentUnlock,          // today-assignment-unlock
    HouseRebuild,                   // house-rebuild
    ItemTaskResurrectionInPlace,    // item-task-resurrection-in-place
    TodayAssignmentSupplyItems,     // today-assignment-supply-items
    ItemTaskBattleCoin,             // item-task-battle-coin
    GiveRewardItem = 144,           // give-reward-item
    ExchangeAapointFromCash,        // exchange-aapoint-from-cash
    SpendItemFromChat,              // spend-item-from-chat
    ItemTaskRemoveHeroReward,       // = 143,// item-task-remove-hero-reward
    ExpeditionSummon,               // expedition-summon
    MateRevive,                     // mate-revive
    ExpandAbilitySetSlot,           // expand-ability-set-slot
    QuestCompleteBalance,           // 402-quest-complete-balance
    ExpandDecoLimit,                // expand-deco-limit
    HouseDemolish,                  // house_demolish
    SiegeAuction,                   // siege-auction
    SelectiveItem,                  // selective-item
    NationRequestFriend,            // nation_request_friend
    DoodadOneshotPlace,             // doodad-oneshot-place
    NationDelegate,                 // nation-delegate
    RenameFaction,                  // rename-faction
    BlessUthstinInitStats,          // bless-uthstin-init-stats
    BlessUthstinChangeStats,        // bless-uthstin-change-stats
    BlessUthstinExpandMaxStats,     // bless-uthstin-expand-max-stats
    BlessUthstinExpandPage,         // bless-uthstin-expand-page
    BlessUthstinSelectPage,         // bless-uthstin-select-page
    BlessUthstinCopyPage,           // bless-uthstin-copy-page
    FamilyJoin,                     // family-join
    FamilyLeave,                    // family-leave
    FamilyKick,                     // family-kick
    FamilyIncMember,                // family-inc-member
    FamilyChangeName,               // family-change-name
    HeirSkillReset,                 // heir-skill-reset
    SlaveFollow,                    // slave_follow
    RaidRecruit,                    // raid-recruit
    RestoreDisableEnchant,          // restore-disable-enchant
    ConsumeEquipSlotReinforceLevelUp,// consume-equip-slot-reinforce-level-up
    ConsumeEquipSlotReinforceAddExp, // consume-equip-slot-reinforce-add-exp
    ItemTypeChange                  // item_type_change
}

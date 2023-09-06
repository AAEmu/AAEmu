using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class AAEmuSqlliteDbContext : DbContext
{
    public AAEmuSqlliteDbContext()
    {
    }

    public AAEmuSqlliteDbContext(DbContextOptions<AAEmuSqlliteDbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<AcceptQuestEffect> AcceptQuestEffects { get; set; }

    public virtual DbSet<AccountAttributeEffect> AccountAttributeEffects { get; set; }

    public virtual DbSet<Achievement> Achievements { get; set; }

    public virtual DbSet<AchievementObjective> AchievementObjectives { get; set; }

    public virtual DbSet<ActabilityCategory> ActabilityCategories { get; set; }

    public virtual DbSet<ActabilityGroup> ActabilityGroups { get; set; }

    public virtual DbSet<ActorModel> ActorModels { get; set; }

    public virtual DbSet<AggroEffect> AggroEffects { get; set; }

    public virtual DbSet<AggroLink> AggroLinks { get; set; }

    public virtual DbSet<AiCommand> AiCommands { get; set; }

    public virtual DbSet<AiCommandSet> AiCommandSets { get; set; }

    public virtual DbSet<AiEvent> AiEvents { get; set; }

    public virtual DbSet<AiFile> AiFiles { get; set; }

    public virtual DbSet<AllowToEquipSlafe> AllowToEquipSlaves { get; set; }

    public virtual DbSet<AllowToEquipSlot> AllowToEquipSlots { get; set; }

    public virtual DbSet<AllowedNameChar> AllowedNameChars { get; set; }

    public virtual DbSet<Anim> Anims { get; set; }

    public virtual DbSet<AnimAction> AnimActions { get; set; }

    public virtual DbSet<AnimRule> AnimRules { get; set; }

    public virtual DbSet<AoeDiminishing> AoeDiminishings { get; set; }

    public virtual DbSet<AoeShape> AoeShapes { get; set; }

    public virtual DbSet<Appellation> Appellations { get; set; }

    public virtual DbSet<AreaEventCheckNpc> AreaEventCheckNpcs { get; set; }

    public virtual DbSet<ArmorAsset> ArmorAssets { get; set; }

    public virtual DbSet<ArmorGradeBuff> ArmorGradeBuffs { get; set; }

    public virtual DbSet<AttachAnim> AttachAnims { get; set; }

    public virtual DbSet<AuctionACategory> AuctionACategories { get; set; }

    public virtual DbSet<AuctionBCategory> AuctionBCategories { get; set; }

    public virtual DbSet<AuctionCCategory> AuctionCCategories { get; set; }

    public virtual DbSet<BagExpand> BagExpands { get; set; }

    public virtual DbSet<BattleField> BattleFields { get; set; }

    public virtual DbSet<BattleFieldBuff> BattleFieldBuffs { get; set; }

    public virtual DbSet<BlockedChildDoodad> BlockedChildDoodads { get; set; }

    public virtual DbSet<BlockedText> BlockedTexts { get; set; }

    public virtual DbSet<BodyDiffuseMap> BodyDiffuseMaps { get; set; }

    public virtual DbSet<Book> Books { get; set; }

    public virtual DbSet<BookElem> BookElems { get; set; }

    public virtual DbSet<BookPage> BookPages { get; set; }

    public virtual DbSet<BookPageContent> BookPageContents { get; set; }

    public virtual DbSet<Bubble> Bubbles { get; set; }

    public virtual DbSet<BubbleChat> BubbleChats { get; set; }

    public virtual DbSet<BubbleEffect> BubbleEffects { get; set; }

    public virtual DbSet<Buff> Buffs { get; set; }

    public virtual DbSet<BuffBreaker> BuffBreakers { get; set; }

    public virtual DbSet<BuffEffect> BuffEffects { get; set; }

    public virtual DbSet<BuffModifier> BuffModifiers { get; set; }

    public virtual DbSet<BuffMountSkill> BuffMountSkills { get; set; }

    public virtual DbSet<BuffSkill> BuffSkills { get; set; }

    public virtual DbSet<BuffTickEffect> BuffTickEffects { get; set; }

    public virtual DbSet<BuffTolerance> BuffTolerances { get; set; }

    public virtual DbSet<BuffToleranceStep> BuffToleranceSteps { get; set; }

    public virtual DbSet<BuffTrigger> BuffTriggers { get; set; }

    public virtual DbSet<BuffUnitModifier> BuffUnitModifiers { get; set; }

    public virtual DbSet<ChangeEquipmentBuff> ChangeEquipmentBuffs { get; set; }

    public virtual DbSet<CharRecord> CharRecords { get; set; }

    public virtual DbSet<Character> Characters { get; set; }

    public virtual DbSet<CharacterBuff> CharacterBuffs { get; set; }

    public virtual DbSet<CharacterCustomizingHairAsset> CharacterCustomizingHairAssets { get; set; }

    public virtual DbSet<CharacterEquipPack> CharacterEquipPacks { get; set; }

    public virtual DbSet<CharacterPStatLimit> CharacterPStatLimits { get; set; }

    public virtual DbSet<CharacterSupply> CharacterSupplies { get; set; }

    public virtual DbSet<ChatCommand> ChatCommands { get; set; }

    public virtual DbSet<ChatSpamRule> ChatSpamRules { get; set; }

    public virtual DbSet<ChatSpamRuleDetail> ChatSpamRuleDetails { get; set; }

    public virtual DbSet<Cinema> Cinemas { get; set; }

    public virtual DbSet<CinemaCaption> CinemaCaptions { get; set; }

    public virtual DbSet<CinemaEffect> CinemaEffects { get; set; }

    public virtual DbSet<CinemaSubtitle> CinemaSubtitles { get; set; }

    public virtual DbSet<CleanupUccEffect> CleanupUccEffects { get; set; }

    public virtual DbSet<Climate> Climates { get; set; }

    public virtual DbSet<CombatBuff> CombatBuffs { get; set; }

    public virtual DbSet<CombatSound> CombatSounds { get; set; }

    public virtual DbSet<CommonFarm> CommonFarms { get; set; }

    public virtual DbSet<ConflictZone> ConflictZones { get; set; }

    public virtual DbSet<Constant> Constants { get; set; }

    public virtual DbSet<ContentConfig> ContentConfigs { get; set; }

    public virtual DbSet<ConversionEffect> ConversionEffects { get; set; }

    public virtual DbSet<Craft> Crafts { get; set; }

    public virtual DbSet<CraftEffect> CraftEffects { get; set; }

    public virtual DbSet<CraftMaterial> CraftMaterials { get; set; }

    public virtual DbSet<CraftPack> CraftPacks { get; set; }

    public virtual DbSet<CraftPackCraft> CraftPackCrafts { get; set; }

    public virtual DbSet<CraftProduct> CraftProducts { get; set; }

    public virtual DbSet<CurrencyConfig> CurrencyConfigs { get; set; }

    public virtual DbSet<CustomDualMaterial> CustomDualMaterials { get; set; }

    public virtual DbSet<CustomFacePreset> CustomFacePresets { get; set; }

    public virtual DbSet<CustomFontColor> CustomFontColors { get; set; }

    public virtual DbSet<CustomHairTexture> CustomHairTextures { get; set; }

    public virtual DbSet<DamageEffect> DamageEffects { get; set; }

    public virtual DbSet<DdcmsMergeProtectInfo> DdcmsMergeProtectInfos { get; set; }

    public virtual DbSet<DecoActabilityGroup> DecoActabilityGroups { get; set; }

    public virtual DbSet<DefaultActionBarAction> DefaultActionBarActions { get; set; }

    public virtual DbSet<DefaultInventoryTab> DefaultInventoryTabs { get; set; }

    public virtual DbSet<DefaultInventoryTabGroup> DefaultInventoryTabGroups { get; set; }

    public virtual DbSet<DefaultSkill> DefaultSkills { get; set; }

    public virtual DbSet<Demo> Demos { get; set; }

    public virtual DbSet<DemoBag> DemoBags { get; set; }

    public virtual DbSet<DemoBagItem> DemoBagItems { get; set; }

    public virtual DbSet<DemoChar> DemoChars { get; set; }

    public virtual DbSet<DemoEquip> DemoEquips { get; set; }

    public virtual DbSet<DemoEquipItem> DemoEquipItems { get; set; }

    public virtual DbSet<DemoLoc> DemoLocs { get; set; }

    public virtual DbSet<DispelEffect> DispelEffects { get; set; }

    public virtual DbSet<District> Districts { get; set; }

    public virtual DbSet<DistrictReturnPoint> DistrictReturnPoints { get; set; }

    public virtual DbSet<DoodadAlmighty> DoodadAlmighties { get; set; }

    public virtual DbSet<DoodadBundle> DoodadBundles { get; set; }

    public virtual DbSet<DoodadBundleDoodad> DoodadBundleDoodads { get; set; }

    public virtual DbSet<DoodadFamily> DoodadFamilies { get; set; }

    public virtual DbSet<DoodadFunc> DoodadFuncs { get; set; }

    public virtual DbSet<DoodadFuncAnimate> DoodadFuncAnimates { get; set; }

    public virtual DbSet<DoodadFuncAreaTrigger> DoodadFuncAreaTriggers { get; set; }

    public virtual DbSet<DoodadFuncAttachment> DoodadFuncAttachments { get; set; }

    public virtual DbSet<DoodadFuncAuctionUi> DoodadFuncAuctionUis { get; set; }

    public virtual DbSet<DoodadFuncBankUi> DoodadFuncBankUis { get; set; }

    public virtual DbSet<DoodadFuncBinding> DoodadFuncBindings { get; set; }

    public virtual DbSet<DoodadFuncBubble> DoodadFuncBubbles { get; set; }

    public virtual DbSet<DoodadFuncBuff> DoodadFuncBuffs { get; set; }

    public virtual DbSet<DoodadFuncButcher> DoodadFuncButchers { get; set; }

    public virtual DbSet<DoodadFuncBuyFish> DoodadFuncBuyFishes { get; set; }

    public virtual DbSet<DoodadFuncBuyFishItem> DoodadFuncBuyFishItems { get; set; }

    public virtual DbSet<DoodadFuncBuyFishModel> DoodadFuncBuyFishModels { get; set; }

    public virtual DbSet<DoodadFuncBuyFishModelItem> DoodadFuncBuyFishModelItems { get; set; }

    public virtual DbSet<DoodadFuncCatch> DoodadFuncCatches { get; set; }

    public virtual DbSet<DoodadFuncCerealHarvest> DoodadFuncCerealHarvests { get; set; }

    public virtual DbSet<DoodadFuncCleanupLogicLink> DoodadFuncCleanupLogicLinks { get; set; }

    public virtual DbSet<DoodadFuncClimateReact> DoodadFuncClimateReacts { get; set; }

    public virtual DbSet<DoodadFuncClimb> DoodadFuncClimbs { get; set; }

    public virtual DbSet<DoodadFuncClout> DoodadFuncClouts { get; set; }

    public virtual DbSet<DoodadFuncCloutEffect> DoodadFuncCloutEffects { get; set; }

    public virtual DbSet<DoodadFuncCoffer> DoodadFuncCoffers { get; set; }

    public virtual DbSet<DoodadFuncCofferPerm> DoodadFuncCofferPerms { get; set; }

    public virtual DbSet<DoodadFuncConditionalUse> DoodadFuncConditionalUses { get; set; }

    public virtual DbSet<DoodadFuncConsumeChanger> DoodadFuncConsumeChangers { get; set; }

    public virtual DbSet<DoodadFuncConsumeChangerItem> DoodadFuncConsumeChangerItems { get; set; }

    public virtual DbSet<DoodadFuncConsumeChangerModel> DoodadFuncConsumeChangerModels { get; set; }

    public virtual DbSet<DoodadFuncConsumeChangerModelItem> DoodadFuncConsumeChangerModelItems { get; set; }

    public virtual DbSet<DoodadFuncConsumeItem> DoodadFuncConsumeItems { get; set; }

    public virtual DbSet<DoodadFuncConvertFish> DoodadFuncConvertFishes { get; set; }

    public virtual DbSet<DoodadFuncConvertFishItem> DoodadFuncConvertFishItems { get; set; }

    public virtual DbSet<DoodadFuncCraftAct> DoodadFuncCraftActs { get; set; }

    public virtual DbSet<DoodadFuncCraftCancel> DoodadFuncCraftCancels { get; set; }

    public virtual DbSet<DoodadFuncCraftDirect> DoodadFuncCraftDirects { get; set; }

    public virtual DbSet<DoodadFuncCraftGetItem> DoodadFuncCraftGetItems { get; set; }

    public virtual DbSet<DoodadFuncCraftGradeRatio> DoodadFuncCraftGradeRatios { get; set; }

    public virtual DbSet<DoodadFuncCraftInfo> DoodadFuncCraftInfos { get; set; }

    public virtual DbSet<DoodadFuncCraftPack> DoodadFuncCraftPacks { get; set; }

    public virtual DbSet<DoodadFuncCraftStart> DoodadFuncCraftStarts { get; set; }

    public virtual DbSet<DoodadFuncCraftStartCraft> DoodadFuncCraftStartCrafts { get; set; }

    public virtual DbSet<DoodadFuncCropHarvest> DoodadFuncCropHarvests { get; set; }

    public virtual DbSet<DoodadFuncCrystalCollect> DoodadFuncCrystalCollects { get; set; }

    public virtual DbSet<DoodadFuncCutdown> DoodadFuncCutdowns { get; set; }

    public virtual DbSet<DoodadFuncCutdowning> DoodadFuncCutdownings { get; set; }

    public virtual DbSet<DoodadFuncDairyCollect> DoodadFuncDairyCollects { get; set; }

    public virtual DbSet<DoodadFuncDeclareSiege> DoodadFuncDeclareSieges { get; set; }

    public virtual DbSet<DoodadFuncDig> DoodadFuncDigs { get; set; }

    public virtual DbSet<DoodadFuncDigTerrain> DoodadFuncDigTerrains { get; set; }

    public virtual DbSet<DoodadFuncDyeingredientCollect> DoodadFuncDyeingredientCollects { get; set; }

    public virtual DbSet<DoodadFuncEnterInstance> DoodadFuncEnterInstances { get; set; }

    public virtual DbSet<DoodadFuncEnterSysInstance> DoodadFuncEnterSysInstances { get; set; }

    public virtual DbSet<DoodadFuncEvidenceItemLoot> DoodadFuncEvidenceItemLoots { get; set; }

    public virtual DbSet<DoodadFuncExchange> DoodadFuncExchanges { get; set; }

    public virtual DbSet<DoodadFuncExchangeItem> DoodadFuncExchangeItems { get; set; }

    public virtual DbSet<DoodadFuncExitIndun> DoodadFuncExitInduns { get; set; }

    public virtual DbSet<DoodadFuncFakeUse> DoodadFuncFakeUses { get; set; }

    public virtual DbSet<DoodadFuncFeed> DoodadFuncFeeds { get; set; }

    public virtual DbSet<DoodadFuncFiberCollect> DoodadFuncFiberCollects { get; set; }

    public virtual DbSet<DoodadFuncFinal> DoodadFuncFinals { get; set; }

    public virtual DbSet<DoodadFuncFishSchool> DoodadFuncFishSchools { get; set; }

    public virtual DbSet<DoodadFuncFruitPick> DoodadFuncFruitPicks { get; set; }

    public virtual DbSet<DoodadFuncGassExtract> DoodadFuncGassExtracts { get; set; }

    public virtual DbSet<DoodadFuncGroup> DoodadFuncGroups { get; set; }

    public virtual DbSet<DoodadFuncGrowth> DoodadFuncGrowths { get; set; }

    public virtual DbSet<DoodadFuncHarvest> DoodadFuncHarvests { get; set; }

    public virtual DbSet<DoodadFuncHouseFarm> DoodadFuncHouseFarms { get; set; }

    public virtual DbSet<DoodadFuncHousingArea> DoodadFuncHousingAreas { get; set; }

    public virtual DbSet<DoodadFuncHunger> DoodadFuncHungers { get; set; }

    public virtual DbSet<DoodadFuncInsertCounter> DoodadFuncInsertCounters { get; set; }

    public virtual DbSet<DoodadFuncLivestockGrowth> DoodadFuncLivestockGrowths { get; set; }

    public virtual DbSet<DoodadFuncLogic> DoodadFuncLogics { get; set; }

    public virtual DbSet<DoodadFuncLogicDisplay> DoodadFuncLogicDisplays { get; set; }

    public virtual DbSet<DoodadFuncLogicFamilyProvider> DoodadFuncLogicFamilyProviders { get; set; }

    public virtual DbSet<DoodadFuncLogicFamilySubscriber> DoodadFuncLogicFamilySubscribers { get; set; }

    public virtual DbSet<DoodadFuncLootItem> DoodadFuncLootItems { get; set; }

    public virtual DbSet<DoodadFuncLootPack> DoodadFuncLootPacks { get; set; }

    public virtual DbSet<DoodadFuncMachinePartsCollect> DoodadFuncMachinePartsCollects { get; set; }

    public virtual DbSet<DoodadFuncMedicalingredientMine> DoodadFuncMedicalingredientMines { get; set; }

    public virtual DbSet<DoodadFuncMould> DoodadFuncMoulds { get; set; }

    public virtual DbSet<DoodadFuncMouldItem> DoodadFuncMouldItems { get; set; }

    public virtual DbSet<DoodadFuncMow> DoodadFuncMows { get; set; }

    public virtual DbSet<DoodadFuncNaviDonation> DoodadFuncNaviDonations { get; set; }

    public virtual DbSet<DoodadFuncNaviMarkPosToMap> DoodadFuncNaviMarkPosToMaps { get; set; }

    public virtual DbSet<DoodadFuncNaviNaming> DoodadFuncNaviNamings { get; set; }

    public virtual DbSet<DoodadFuncNaviOpenBounty> DoodadFuncNaviOpenBounties { get; set; }

    public virtual DbSet<DoodadFuncNaviOpenMailbox> DoodadFuncNaviOpenMailboxes { get; set; }

    public virtual DbSet<DoodadFuncNaviOpenPortal> DoodadFuncNaviOpenPortals { get; set; }

    public virtual DbSet<DoodadFuncNaviRemove> DoodadFuncNaviRemoves { get; set; }

    public virtual DbSet<DoodadFuncNaviRemoveTimer> DoodadFuncNaviRemoveTimers { get; set; }

    public virtual DbSet<DoodadFuncNaviTeleport> DoodadFuncNaviTeleports { get; set; }

    public virtual DbSet<DoodadFuncOpenFarmInfo> DoodadFuncOpenFarmInfos { get; set; }

    public virtual DbSet<DoodadFuncOpenPaper> DoodadFuncOpenPapers { get; set; }

    public virtual DbSet<DoodadFuncOreMine> DoodadFuncOreMines { get; set; }

    public virtual DbSet<DoodadFuncParentInfo> DoodadFuncParentInfos { get; set; }

    public virtual DbSet<DoodadFuncParrot> DoodadFuncParrots { get; set; }

    public virtual DbSet<DoodadFuncPlantCollect> DoodadFuncPlantCollects { get; set; }

    public virtual DbSet<DoodadFuncPlayFlowGraph> DoodadFuncPlayFlowGraphs { get; set; }

    public virtual DbSet<DoodadFuncPulse> DoodadFuncPulses { get; set; }

    public virtual DbSet<DoodadFuncPulseTrigger> DoodadFuncPulseTriggers { get; set; }

    public virtual DbSet<DoodadFuncPurchase> DoodadFuncPurchases { get; set; }

    public virtual DbSet<DoodadFuncPurchaseSiegeTicket> DoodadFuncPurchaseSiegeTickets { get; set; }

    public virtual DbSet<DoodadFuncPuzzleIn> DoodadFuncPuzzleIns { get; set; }

    public virtual DbSet<DoodadFuncPuzzleOut> DoodadFuncPuzzleOuts { get; set; }

    public virtual DbSet<DoodadFuncPuzzleRoll> DoodadFuncPuzzleRolls { get; set; }

    public virtual DbSet<DoodadFuncQuest> DoodadFuncQuests { get; set; }

    public virtual DbSet<DoodadFuncRatioChange> DoodadFuncRatioChanges { get; set; }

    public virtual DbSet<DoodadFuncRatioRespawn> DoodadFuncRatioRespawns { get; set; }

    public virtual DbSet<DoodadFuncRecoverItem> DoodadFuncRecoverItems { get; set; }

    public virtual DbSet<DoodadFuncRemoveInstance> DoodadFuncRemoveInstances { get; set; }

    public virtual DbSet<DoodadFuncRemoveItem> DoodadFuncRemoveItems { get; set; }

    public virtual DbSet<DoodadFuncRenewItem> DoodadFuncRenewItems { get; set; }

    public virtual DbSet<DoodadFuncReqBattleField> DoodadFuncReqBattleFields { get; set; }

    public virtual DbSet<DoodadFuncRequireItem> DoodadFuncRequireItems { get; set; }

    public virtual DbSet<DoodadFuncRequireQuest> DoodadFuncRequireQuests { get; set; }

    public virtual DbSet<DoodadFuncRespawn> DoodadFuncRespawns { get; set; }

    public virtual DbSet<DoodadFuncRockMine> DoodadFuncRockMines { get; set; }

    public virtual DbSet<DoodadFuncSeedCollect> DoodadFuncSeedCollects { get; set; }

    public virtual DbSet<DoodadFuncShear> DoodadFuncShears { get; set; }

    public virtual DbSet<DoodadFuncSiegePeriod> DoodadFuncSiegePeriods { get; set; }

    public virtual DbSet<DoodadFuncSign> DoodadFuncSigns { get; set; }

    public virtual DbSet<DoodadFuncSkillHit> DoodadFuncSkillHits { get; set; }

    public virtual DbSet<DoodadFuncSkinOff> DoodadFuncSkinOffs { get; set; }

    public virtual DbSet<DoodadFuncSoilCollect> DoodadFuncSoilCollects { get; set; }

    public virtual DbSet<DoodadFuncSpawnGimmick> DoodadFuncSpawnGimmicks { get; set; }

    public virtual DbSet<DoodadFuncSpawnMgmt> DoodadFuncSpawnMgmts { get; set; }

    public virtual DbSet<DoodadFuncSpiceCollect> DoodadFuncSpiceCollects { get; set; }

    public virtual DbSet<DoodadFuncStampMaker> DoodadFuncStampMakers { get; set; }

    public virtual DbSet<DoodadFuncStoreUi> DoodadFuncStoreUis { get; set; }

    public virtual DbSet<DoodadFuncTimer> DoodadFuncTimers { get; set; }

    public virtual DbSet<DoodadFuncTod> DoodadFuncTods { get; set; }

    public virtual DbSet<DoodadFuncTreeByproductsCollect> DoodadFuncTreeByproductsCollects { get; set; }

    public virtual DbSet<DoodadFuncUccImprint> DoodadFuncUccImprints { get; set; }

    public virtual DbSet<DoodadFuncUse> DoodadFuncUses { get; set; }

    public virtual DbSet<DoodadFuncVegetationGrowth> DoodadFuncVegetationGrowths { get; set; }

    public virtual DbSet<DoodadFuncWaterVolume> DoodadFuncWaterVolumes { get; set; }

    public virtual DbSet<DoodadFuncZoneReact> DoodadFuncZoneReacts { get; set; }

    public virtual DbSet<DoodadGroup> DoodadGroups { get; set; }

    public virtual DbSet<DoodadModifier> DoodadModifiers { get; set; }

    public virtual DbSet<DoodadPhaseFunc> DoodadPhaseFuncs { get; set; }

    public virtual DbSet<DoodadPlaceSkin> DoodadPlaceSkins { get; set; }

    public virtual DbSet<DyeableItem> DyeableItems { get; set; }

    public virtual DbSet<DyeingColor> DyeingColors { get; set; }

    public virtual DbSet<DynamicUnitModifier> DynamicUnitModifiers { get; set; }

    public virtual DbSet<Effect> Effects { get; set; }

    public virtual DbSet<EmblemPattern> EmblemPatterns { get; set; }

    public virtual DbSet<EquipItemAttrModifier> EquipItemAttrModifiers { get; set; }

    public virtual DbSet<EquipItemSet> EquipItemSets { get; set; }

    public virtual DbSet<EquipItemSetBonuse> EquipItemSetBonuses { get; set; }

    public virtual DbSet<EquipPackBodyPart> EquipPackBodyParts { get; set; }

    public virtual DbSet<EquipPackCloth> EquipPackCloths { get; set; }

    public virtual DbSet<EquipPackWeapon> EquipPackWeapons { get; set; }

    public virtual DbSet<EquipSlotEnchantingCost> EquipSlotEnchantingCosts { get; set; }

    public virtual DbSet<EquipSlotGroup> EquipSlotGroups { get; set; }

    public virtual DbSet<EquipSlotGroupMap> EquipSlotGroupMaps { get; set; }

    public virtual DbSet<ExpandExpertLimit> ExpandExpertLimits { get; set; }

    public virtual DbSet<ExpertLimit> ExpertLimits { get; set; }

    public virtual DbSet<ExpressText> ExpressTexts { get; set; }

    public virtual DbSet<FaceDecalAsset> FaceDecalAssets { get; set; }

    public virtual DbSet<FaceDiffuseMap> FaceDiffuseMaps { get; set; }

    public virtual DbSet<FaceEyelashMap> FaceEyelashMaps { get; set; }

    public virtual DbSet<FaceNormalMap> FaceNormalMaps { get; set; }

    public virtual DbSet<FactionChatRegion> FactionChatRegions { get; set; }

    public virtual DbSet<FarmGroup> FarmGroups { get; set; }

    public virtual DbSet<FarmGroupDoodad> FarmGroupDoodads { get; set; }

    public virtual DbSet<FishDetail> FishDetails { get; set; }

    public virtual DbSet<FlyingStateChangeEffect> FlyingStateChangeEffects { get; set; }

    public virtual DbSet<Formula> Formulas { get; set; }

    public virtual DbSet<FxCamFov> FxCamFovs { get; set; }

    public virtual DbSet<FxCga> FxCgas { get; set; }

    public virtual DbSet<FxCgf> FxCgfs { get; set; }

    public virtual DbSet<FxChr> FxChrs { get; set; }

    public virtual DbSet<FxDecal> FxDecals { get; set; }

    public virtual DbSet<FxGroup> FxGroups { get; set; }

    public virtual DbSet<FxGroupFxItem> FxGroupFxItems { get; set; }

    public virtual DbSet<FxItem> FxItems { get; set; }

    public virtual DbSet<FxMaterial> FxMaterials { get; set; }

    public virtual DbSet<FxMotionBlur> FxMotionBlurs { get; set; }

    public virtual DbSet<FxParticle> FxParticles { get; set; }

    public virtual DbSet<FxRope> FxRopes { get; set; }

    public virtual DbSet<FxShakeCamera> FxShakeCameras { get; set; }

    public virtual DbSet<FxSound> FxSounds { get; set; }

    public virtual DbSet<FxVoice> FxVoices { get; set; }

    public virtual DbSet<GainLootPackItemEffect> GainLootPackItemEffects { get; set; }

    public virtual DbSet<GameRuleEvent> GameRuleEvents { get; set; }

    public virtual DbSet<GameRuleSet> GameRuleSets { get; set; }

    public virtual DbSet<GameSchedule> GameSchedules { get; set; }

    public virtual DbSet<GameScheduleDoodad> GameScheduleDoodads { get; set; }

    public virtual DbSet<GameScheduleQuest> GameScheduleQuests { get; set; }

    public virtual DbSet<GameScheduleSpawner> GameScheduleSpawners { get; set; }

    public virtual DbSet<GameScoreRule> GameScoreRules { get; set; }

    public virtual DbSet<GameStance> GameStances { get; set; }

    public virtual DbSet<GemVisualEffect> GemVisualEffects { get; set; }

    public virtual DbSet<Gimmick> Gimmicks { get; set; }

    public virtual DbSet<GrammarTag> GrammarTags { get; set; }

    public virtual DbSet<GrammarTagNoneType> GrammarTagNoneTypes { get; set; }

    public virtual DbSet<GuardTowerSetting> GuardTowerSettings { get; set; }

    public virtual DbSet<GuardTowerStep> GuardTowerSteps { get; set; }

    public virtual DbSet<HairColor> HairColors { get; set; }

    public virtual DbSet<HealEffect> HealEffects { get; set; }

    public virtual DbSet<Holdable> Holdables { get; set; }

    public virtual DbSet<Hotkey> Hotkeys { get; set; }

    public virtual DbSet<Housing> Housings { get; set; }

    public virtual DbSet<HousingArea> HousingAreas { get; set; }

    public virtual DbSet<HousingBindingDoodad> HousingBindingDoodads { get; set; }

    public virtual DbSet<HousingBuildStep> HousingBuildSteps { get; set; }

    public virtual DbSet<HousingDecoLimit> HousingDecoLimits { get; set; }

    public virtual DbSet<HousingDecoLimitElem> HousingDecoLimitElems { get; set; }

    public virtual DbSet<HousingDecoration> HousingDecorations { get; set; }

    public virtual DbSet<HousingGroup> HousingGroups { get; set; }

    public virtual DbSet<HousingGroupCategory> HousingGroupCategories { get; set; }

    public virtual DbSet<Icon> Icons { get; set; }

    public virtual DbSet<IgnoreText> IgnoreTexts { get; set; }

    public virtual DbSet<ImprintUccEffect> ImprintUccEffects { get; set; }

    public virtual DbSet<ImpulseEffect> ImpulseEffects { get; set; }

    public virtual DbSet<IndunAction> IndunActions { get; set; }

    public virtual DbSet<IndunActionChangeDoodadPhase> IndunActionChangeDoodadPhases { get; set; }

    public virtual DbSet<IndunActionRemoveTaggedNpc> IndunActionRemoveTaggedNpcs { get; set; }

    public virtual DbSet<IndunActionSetRoomCleared> IndunActionSetRoomCleareds { get; set; }

    public virtual DbSet<IndunEvent> IndunEvents { get; set; }

    public virtual DbSet<IndunEventDoodadSpawned> IndunEventDoodadSpawneds { get; set; }

    public virtual DbSet<IndunEventNoAliveChInRoom> IndunEventNoAliveChInRooms { get; set; }

    public virtual DbSet<IndunEventNpcCombatEnded> IndunEventNpcCombatEndeds { get; set; }

    public virtual DbSet<IndunEventNpcCombatStarted> IndunEventNpcCombatStarteds { get; set; }

    public virtual DbSet<IndunEventNpcKilled> IndunEventNpcKilleds { get; set; }

    public virtual DbSet<IndunEventNpcSpawned> IndunEventNpcSpawneds { get; set; }

    public virtual DbSet<IndunRoom> IndunRooms { get; set; }

    public virtual DbSet<IndunRoomSphere> IndunRoomSpheres { get; set; }

    public virtual DbSet<IndunZone> IndunZones { get; set; }

    public virtual DbSet<InstrumentSound> InstrumentSounds { get; set; }

    public virtual DbSet<InteractionEffect> InteractionEffects { get; set; }

    public virtual DbSet<Item> Items { get; set; }

    public virtual DbSet<ItemAcceptQuest> ItemAcceptQuests { get; set; }

    public virtual DbSet<ItemAccessory> ItemAccessories { get; set; }

    public virtual DbSet<ItemArmor> ItemArmors { get; set; }

    public virtual DbSet<ItemArmorAsset> ItemArmorAssets { get; set; }

    public virtual DbSet<ItemAsset> ItemAssets { get; set; }

    public virtual DbSet<ItemBackpack> ItemBackpacks { get; set; }

    public virtual DbSet<ItemBag> ItemBags { get; set; }

    public virtual DbSet<ItemBodyPart> ItemBodyParts { get; set; }

    public virtual DbSet<ItemCapScale> ItemCapScales { get; set; }

    public virtual DbSet<ItemCapScaleForbid> ItemCapScaleForbids { get; set; }

    public virtual DbSet<ItemCategory> ItemCategories { get; set; }

    public virtual DbSet<ItemConfig> ItemConfigs { get; set; }

    public virtual DbSet<ItemConv> ItemConvs { get; set; }

    public virtual DbSet<ItemConvPpack> ItemConvPpacks { get; set; }

    public virtual DbSet<ItemConvPpackMember> ItemConvPpackMembers { get; set; }

    public virtual DbSet<ItemConvProduct> ItemConvProducts { get; set; }

    public virtual DbSet<ItemConvReagent> ItemConvReagents { get; set; }

    public virtual DbSet<ItemConvReagentFilter> ItemConvReagentFilters { get; set; }

    public virtual DbSet<ItemConvRpack> ItemConvRpacks { get; set; }

    public virtual DbSet<ItemConvRpackMember> ItemConvRpackMembers { get; set; }

    public virtual DbSet<ItemConvSet> ItemConvSets { get; set; }

    public virtual DbSet<ItemDyeing> ItemDyeings { get; set; }

    public virtual DbSet<ItemEnchantingGem> ItemEnchantingGems { get; set; }

    public virtual DbSet<ItemGrade> ItemGrades { get; set; }

    public virtual DbSet<ItemGradeBuff> ItemGradeBuffs { get; set; }

    public virtual DbSet<ItemGradeDistribution> ItemGradeDistributions { get; set; }

    public virtual DbSet<ItemGradeEnchantingSupport> ItemGradeEnchantingSupports { get; set; }

    public virtual DbSet<ItemGradeSkill> ItemGradeSkills { get; set; }

    public virtual DbSet<ItemGroup> ItemGroups { get; set; }

    public virtual DbSet<ItemHousing> ItemHousings { get; set; }

    public virtual DbSet<ItemHousingDecoration> ItemHousingDecorations { get; set; }

    public virtual DbSet<ItemLookConvert> ItemLookConverts { get; set; }

    public virtual DbSet<ItemLookConvertHoldable> ItemLookConvertHoldables { get; set; }

    public virtual DbSet<ItemLookConvertRequiredItem> ItemLookConvertRequiredItems { get; set; }

    public virtual DbSet<ItemLookConvertWearable> ItemLookConvertWearables { get; set; }

    public virtual DbSet<ItemOpenPaper> ItemOpenPapers { get; set; }

    public virtual DbSet<ItemProc> ItemProcs { get; set; }

    public virtual DbSet<ItemProcBinding> ItemProcBindings { get; set; }

    public virtual DbSet<ItemRecipe> ItemRecipes { get; set; }

    public virtual DbSet<ItemSecureException> ItemSecureExceptions { get; set; }

    public virtual DbSet<ItemSet> ItemSets { get; set; }

    public virtual DbSet<ItemSetItem> ItemSetItems { get; set; }

    public virtual DbSet<ItemShipyard> ItemShipyards { get; set; }

    public virtual DbSet<ItemSlaveEquipment> ItemSlaveEquipments { get; set; }

    public virtual DbSet<ItemSocket> ItemSockets { get; set; }

    public virtual DbSet<ItemSocketChance> ItemSocketChances { get; set; }

    public virtual DbSet<ItemSocketLevelLimit> ItemSocketLevelLimits { get; set; }

    public virtual DbSet<ItemSocketNumLimit> ItemSocketNumLimits { get; set; }

    public virtual DbSet<ItemSpawnDoodad> ItemSpawnDoodads { get; set; }

    public virtual DbSet<ItemSummonMate> ItemSummonMates { get; set; }

    public virtual DbSet<ItemSummonSlafe> ItemSummonSlaves { get; set; }

    public virtual DbSet<ItemTool> ItemTools { get; set; }

    public virtual DbSet<ItemWeapon> ItemWeapons { get; set; }

    public virtual DbSet<KillNpcWithoutCorpseEffect> KillNpcWithoutCorpseEffects { get; set; }

    public virtual DbSet<Level> Levels { get; set; }

    public virtual DbSet<LinearFunc> LinearFuncs { get; set; }

    public virtual DbSet<LocalizedText> LocalizedTexts { get; set; }

    public virtual DbSet<Loot> Loots { get; set; }

    public virtual DbSet<LootActabilityGroup> LootActabilityGroups { get; set; }

    public virtual DbSet<LootGroup> LootGroups { get; set; }

    public virtual DbSet<LootPackDroppingNpc> LootPackDroppingNpcs { get; set; }

    public virtual DbSet<ManaBurnEffect> ManaBurnEffects { get; set; }

    public virtual DbSet<ManualFunc> ManualFuncs { get; set; }

    public virtual DbSet<MateEquipPack> MateEquipPacks { get; set; }

    public virtual DbSet<MateEquipPackGroup> MateEquipPackGroups { get; set; }

    public virtual DbSet<MateEquipPackItem> MateEquipPackItems { get; set; }

    public virtual DbSet<MateEquipSlotPack> MateEquipSlotPacks { get; set; }

    public virtual DbSet<Merchant> Merchants { get; set; }

    public virtual DbSet<MerchantGood> MerchantGoods { get; set; }

    public virtual DbSet<MerchantPack> MerchantPacks { get; set; }

    public virtual DbSet<MerchantPriceRatio> MerchantPriceRatios { get; set; }

    public virtual DbSet<MineJewelRate> MineJewelRates { get; set; }

    public virtual DbSet<Model> Models { get; set; }

    public virtual DbSet<ModelAttachPointString> ModelAttachPointStrings { get; set; }

    public virtual DbSet<ModelBinding> ModelBindings { get; set; }

    public virtual DbSet<ModelQuestCamera> ModelQuestCameras { get; set; }

    public virtual DbSet<Mould> Moulds { get; set; }

    public virtual DbSet<MouldPack> MouldPacks { get; set; }

    public virtual DbSet<MouldPackItem> MouldPackItems { get; set; }

    public virtual DbSet<MountAttachedSkill> MountAttachedSkills { get; set; }

    public virtual DbSet<MountSkill> MountSkills { get; set; }

    public virtual DbSet<MoveToRezPointEffect> MoveToRezPointEffects { get; set; }

    public virtual DbSet<MusicNoteLimit> MusicNoteLimits { get; set; }

    public virtual DbSet<NpPassiveBuff> NpPassiveBuffs { get; set; }

    public virtual DbSet<NpSkill> NpSkills { get; set; }

    public virtual DbSet<Npc> Npcs { get; set; }

    public virtual DbSet<NpcAggroLink> NpcAggroLinks { get; set; }

    public virtual DbSet<NpcAiParam> NpcAiParams { get; set; }

    public virtual DbSet<NpcChatBubble> NpcChatBubbles { get; set; }

    public virtual DbSet<NpcControlEffect> NpcControlEffects { get; set; }

    public virtual DbSet<NpcDoodadBinding> NpcDoodadBindings { get; set; }

    public virtual DbSet<NpcInitialBuff> NpcInitialBuffs { get; set; }

    public virtual DbSet<NpcInteraction> NpcInteractions { get; set; }

    public virtual DbSet<NpcInteractionSet> NpcInteractionSets { get; set; }

    public virtual DbSet<NpcMountSkill> NpcMountSkills { get; set; }

    public virtual DbSet<NpcNickname> NpcNicknames { get; set; }

    public virtual DbSet<NpcPosture> NpcPostures { get; set; }

    public virtual DbSet<NpcPostureSet> NpcPostureSets { get; set; }

    public virtual DbSet<NpcSpawner> NpcSpawners { get; set; }

    public virtual DbSet<NpcSpawnerDespawnEffect> NpcSpawnerDespawnEffects { get; set; }

    public virtual DbSet<NpcSpawnerNpc> NpcSpawnerNpcs { get; set; }

    public virtual DbSet<NpcSpawnerSpawnEffect> NpcSpawnerSpawnEffects { get; set; }

    public virtual DbSet<OpenPortalEffect> OpenPortalEffects { get; set; }

    public virtual DbSet<OpenPortalInlandReagent> OpenPortalInlandReagents { get; set; }

    public virtual DbSet<OpenPortalOutlandReagent> OpenPortalOutlandReagents { get; set; }

    public virtual DbSet<PassiveBuff> PassiveBuffs { get; set; }

    public virtual DbSet<PcbangBuff> PcbangBuffs { get; set; }

    public virtual DbSet<PhysicalEnchantAbility> PhysicalEnchantAbilities { get; set; }

    public virtual DbSet<PhysicalExplosionEffect> PhysicalExplosionEffects { get; set; }

    public virtual DbSet<PlayLogEffect> PlayLogEffects { get; set; }

    public virtual DbSet<Plot> Plots { get; set; }

    public virtual DbSet<PlotAoeCondition> PlotAoeConditions { get; set; }

    public virtual DbSet<PlotCondition> PlotConditions { get; set; }

    public virtual DbSet<PlotEffect> PlotEffects { get; set; }

    public virtual DbSet<PlotEvent> PlotEvents { get; set; }

    public virtual DbSet<PlotEventCondition> PlotEventConditions { get; set; }

    public virtual DbSet<PlotNextEvent> PlotNextEvents { get; set; }

    public virtual DbSet<PreCompletedAchievement> PreCompletedAchievements { get; set; }

    public virtual DbSet<PrefabElement> PrefabElements { get; set; }

    public virtual DbSet<PrefabModel> PrefabModels { get; set; }

    public virtual DbSet<PremiumBenefit> PremiumBenefits { get; set; }

    public virtual DbSet<PremiumConfig> PremiumConfigs { get; set; }

    public virtual DbSet<PremiumGrade> PremiumGrades { get; set; }

    public virtual DbSet<PremiumPoint> PremiumPoints { get; set; }

    public virtual DbSet<PriestBuff> PriestBuffs { get; set; }

    public virtual DbSet<Projectile> Projectiles { get; set; }

    public virtual DbSet<PutDownBackpackEffect> PutDownBackpackEffects { get; set; }

    public virtual DbSet<QuestAct> QuestActs { get; set; }

    public virtual DbSet<QuestActCheckCompleteComponent> QuestActCheckCompleteComponents { get; set; }

    public virtual DbSet<QuestActCheckDistance> QuestActCheckDistances { get; set; }

    public virtual DbSet<QuestActCheckGuard> QuestActCheckGuards { get; set; }

    public virtual DbSet<QuestActCheckSphere> QuestActCheckSpheres { get; set; }

    public virtual DbSet<QuestActCheckTimer> QuestActCheckTimers { get; set; }

    public virtual DbSet<QuestActConAcceptBuff> QuestActConAcceptBuffs { get; set; }

    public virtual DbSet<QuestActConAcceptComponent> QuestActConAcceptComponents { get; set; }

    public virtual DbSet<QuestActConAcceptDoodad> QuestActConAcceptDoodads { get; set; }

    public virtual DbSet<QuestActConAcceptItem> QuestActConAcceptItems { get; set; }

    public virtual DbSet<QuestActConAcceptItemEquip> QuestActConAcceptItemEquips { get; set; }

    public virtual DbSet<QuestActConAcceptItemGain> QuestActConAcceptItemGains { get; set; }

    public virtual DbSet<QuestActConAcceptLevelUp> QuestActConAcceptLevelUps { get; set; }

    public virtual DbSet<QuestActConAcceptNpc> QuestActConAcceptNpcs { get; set; }

    public virtual DbSet<QuestActConAcceptNpcEmotion> QuestActConAcceptNpcEmotions { get; set; }

    public virtual DbSet<QuestActConAcceptNpcKill> QuestActConAcceptNpcKills { get; set; }

    public virtual DbSet<QuestActConAcceptSkill> QuestActConAcceptSkills { get; set; }

    public virtual DbSet<QuestActConAcceptSphere> QuestActConAcceptSpheres { get; set; }

    public virtual DbSet<QuestActConAutoComplete> QuestActConAutoCompletes { get; set; }

    public virtual DbSet<QuestActConFail> QuestActConFails { get; set; }

    public virtual DbSet<QuestActConReportDoodad> QuestActConReportDoodads { get; set; }

    public virtual DbSet<QuestActConReportJournal> QuestActConReportJournals { get; set; }

    public virtual DbSet<QuestActConReportNpc> QuestActConReportNpcs { get; set; }

    public virtual DbSet<QuestActEtcItemObtain> QuestActEtcItemObtains { get; set; }

    public virtual DbSet<QuestActObjAbilityLevel> QuestActObjAbilityLevels { get; set; }

    public virtual DbSet<QuestActObjAggro> QuestActObjAggros { get; set; }

    public virtual DbSet<QuestActObjAlias> QuestActObjAliases { get; set; }

    public virtual DbSet<QuestActObjCinema> QuestActObjCinemas { get; set; }

    public virtual DbSet<QuestActObjCompleteQuest> QuestActObjCompleteQuests { get; set; }

    public virtual DbSet<QuestActObjCondition> QuestActObjConditions { get; set; }

    public virtual DbSet<QuestActObjCraft> QuestActObjCrafts { get; set; }

    public virtual DbSet<QuestActObjDistance> QuestActObjDistances { get; set; }

    public virtual DbSet<QuestActObjDoodadPhaseCheck> QuestActObjDoodadPhaseChecks { get; set; }

    public virtual DbSet<QuestActObjEffectFire> QuestActObjEffectFires { get; set; }

    public virtual DbSet<QuestActObjExpressFire> QuestActObjExpressFires { get; set; }

    public virtual DbSet<QuestActObjInteraction> QuestActObjInteractions { get; set; }

    public virtual DbSet<QuestActObjItemGather> QuestActObjItemGathers { get; set; }

    public virtual DbSet<QuestActObjItemGroupGather> QuestActObjItemGroupGathers { get; set; }

    public virtual DbSet<QuestActObjItemGroupUse> QuestActObjItemGroupUses { get; set; }

    public virtual DbSet<QuestActObjItemUse> QuestActObjItemUses { get; set; }

    public virtual DbSet<QuestActObjLevel> QuestActObjLevels { get; set; }

    public virtual DbSet<QuestActObjMateLevel> QuestActObjMateLevels { get; set; }

    public virtual DbSet<QuestActObjMonsterGroupHunt> QuestActObjMonsterGroupHunts { get; set; }

    public virtual DbSet<QuestActObjMonsterHunt> QuestActObjMonsterHunts { get; set; }

    public virtual DbSet<QuestActObjSendMail> QuestActObjSendMails { get; set; }

    public virtual DbSet<QuestActObjSphere> QuestActObjSpheres { get; set; }

    public virtual DbSet<QuestActObjTalk> QuestActObjTalks { get; set; }

    public virtual DbSet<QuestActObjTalkNpcGroup> QuestActObjTalkNpcGroups { get; set; }

    public virtual DbSet<QuestActObjZoneKill> QuestActObjZoneKills { get; set; }

    public virtual DbSet<QuestActObjZoneMonsterHunt> QuestActObjZoneMonsterHunts { get; set; }

    public virtual DbSet<QuestActObjZoneNpcTalk> QuestActObjZoneNpcTalks { get; set; }

    public virtual DbSet<QuestActObjZoneQuestComplete> QuestActObjZoneQuestCompletes { get; set; }

    public virtual DbSet<QuestActSupplyAaPoint> QuestActSupplyAaPoints { get; set; }

    public virtual DbSet<QuestActSupplyAppellation> QuestActSupplyAppellations { get; set; }

    public virtual DbSet<QuestActSupplyCopper> QuestActSupplyCoppers { get; set; }

    public virtual DbSet<QuestActSupplyCrimePoint> QuestActSupplyCrimePoints { get; set; }

    public virtual DbSet<QuestActSupplyExp> QuestActSupplyExps { get; set; }

    public virtual DbSet<QuestActSupplyHonorPoint> QuestActSupplyHonorPoints { get; set; }

    public virtual DbSet<QuestActSupplyInteraction> QuestActSupplyInteractions { get; set; }

    public virtual DbSet<QuestActSupplyItem> QuestActSupplyItems { get; set; }

    public virtual DbSet<QuestActSupplyJuryPoint> QuestActSupplyJuryPoints { get; set; }

    public virtual DbSet<QuestActSupplyLivingPoint> QuestActSupplyLivingPoints { get; set; }

    public virtual DbSet<QuestActSupplyLp> QuestActSupplyLps { get; set; }

    public virtual DbSet<QuestActSupplyRemoveItem> QuestActSupplyRemoveItems { get; set; }

    public virtual DbSet<QuestActSupplySelectiveItem> QuestActSupplySelectiveItems { get; set; }

    public virtual DbSet<QuestActSupplySkill> QuestActSupplySkills { get; set; }

    public virtual DbSet<QuestCamera> QuestCameras { get; set; }

    public virtual DbSet<QuestCategory> QuestCategories { get; set; }

    public virtual DbSet<QuestChatBubble> QuestChatBubbles { get; set; }

    public virtual DbSet<QuestComponent> QuestComponents { get; set; }

    public virtual DbSet<QuestComponentText> QuestComponentTexts { get; set; }

    public virtual DbSet<QuestContext> QuestContexts { get; set; }

    public virtual DbSet<QuestContextText> QuestContextTexts { get; set; }

    public virtual DbSet<QuestItemGroup> QuestItemGroups { get; set; }

    public virtual DbSet<QuestItemGroupItem> QuestItemGroupItems { get; set; }

    public virtual DbSet<QuestMail> QuestMails { get; set; }

    public virtual DbSet<QuestMailAttachment> QuestMailAttachments { get; set; }

    public virtual DbSet<QuestMailAttachmentItem> QuestMailAttachmentItems { get; set; }

    public virtual DbSet<QuestMailSend> QuestMailSends { get; set; }

    public virtual DbSet<QuestMonsterGroup> QuestMonsterGroups { get; set; }

    public virtual DbSet<QuestMonsterNpc> QuestMonsterNpcs { get; set; }

    public virtual DbSet<QuestName> QuestNames { get; set; }

    public virtual DbSet<QuestSupply> QuestSupplies { get; set; }

    public virtual DbSet<QuestTask> QuestTasks { get; set; }

    public virtual DbSet<QuestTaskQuest> QuestTaskQuests { get; set; }

    public virtual DbSet<RaceTrack> RaceTracks { get; set; }

    public virtual DbSet<RaceTrackShape> RaceTrackShapes { get; set; }

    public virtual DbSet<Rank> Ranks { get; set; }

    public virtual DbSet<RankReward> RankRewards { get; set; }

    public virtual DbSet<RankRewardLink> RankRewardLinks { get; set; }

    public virtual DbSet<RankScope> RankScopes { get; set; }

    public virtual DbSet<RankScopeLink> RankScopeLinks { get; set; }

    public virtual DbSet<RecoverExpEffect> RecoverExpEffects { get; set; }

    public virtual DbSet<RepairSlaveEffect> RepairSlaveEffects { get; set; }

    public virtual DbSet<RepairableSlafe> RepairableSlaves { get; set; }

    public virtual DbSet<ReplaceChat> ReplaceChats { get; set; }

    public virtual DbSet<ReplaceChatKey> ReplaceChatKeys { get; set; }

    public virtual DbSet<ReplaceChatText> ReplaceChatTexts { get; set; }

    public virtual DbSet<ReportCrimeEffect> ReportCrimeEffects { get; set; }

    public virtual DbSet<ResetAoeDiminishingEffect> ResetAoeDiminishingEffects { get; set; }

    public virtual DbSet<RestoreManaEffect> RestoreManaEffects { get; set; }

    public virtual DbSet<ResurrectionWaitingTime> ResurrectionWaitingTimes { get; set; }

    public virtual DbSet<ReturnPoint> ReturnPoints { get; set; }

    public virtual DbSet<ScheduleItem> ScheduleItems { get; set; }

    public virtual DbSet<SchemaMigration> SchemaMigrations { get; set; }

    public virtual DbSet<ScopedFEffect> ScopedFEffects { get; set; }

    public virtual DbSet<ShipModel> ShipModels { get; set; }

    public virtual DbSet<Shipyard> Shipyards { get; set; }

    public virtual DbSet<ShipyardReward> ShipyardRewards { get; set; }

    public virtual DbSet<ShipyardStep> ShipyardSteps { get; set; }

    public virtual DbSet<SiegeItem> SiegeItems { get; set; }

    public virtual DbSet<SiegePlan> SiegePlans { get; set; }

    public virtual DbSet<SiegeSetting> SiegeSettings { get; set; }

    public virtual DbSet<SiegeTicketOffensePrice> SiegeTicketOffensePrices { get; set; }

    public virtual DbSet<SiegeZone> SiegeZones { get; set; }

    public virtual DbSet<Skill> Skills { get; set; }

    public virtual DbSet<SkillController> SkillControllers { get; set; }

    public virtual DbSet<SkillEffect> SkillEffects { get; set; }

    public virtual DbSet<SkillModifier> SkillModifiers { get; set; }

    public virtual DbSet<SkillProduct> SkillProducts { get; set; }

    public virtual DbSet<SkillReagent> SkillReagents { get; set; }

    public virtual DbSet<SkillReq> SkillReqs { get; set; }

    public virtual DbSet<SkillReqSkill> SkillReqSkills { get; set; }

    public virtual DbSet<SkillReqSkillTag> SkillReqSkillTags { get; set; }

    public virtual DbSet<SkillSynergyIcon> SkillSynergyIcons { get; set; }

    public virtual DbSet<SkillVisualGroup> SkillVisualGroups { get; set; }

    public virtual DbSet<SkinColor> SkinColors { get; set; }

    public virtual DbSet<Slafe> Slaves { get; set; }

    public virtual DbSet<SlashCommand> SlashCommands { get; set; }

    public virtual DbSet<SlashFunction> SlashFunctions { get; set; }

    public virtual DbSet<SlaveBinding> SlaveBindings { get; set; }

    public virtual DbSet<SlaveCustomizing> SlaveCustomizings { get; set; }

    public virtual DbSet<SlaveCustomizingEquipSlot> SlaveCustomizingEquipSlots { get; set; }

    public virtual DbSet<SlaveDoodadBinding> SlaveDoodadBindings { get; set; }

    public virtual DbSet<SlaveDropDoodad> SlaveDropDoodads { get; set; }

    public virtual DbSet<SlaveEquipPack> SlaveEquipPacks { get; set; }

    public virtual DbSet<SlaveEquipSlot> SlaveEquipSlots { get; set; }

    public virtual DbSet<SlaveEquipmentEquipSlotPack> SlaveEquipmentEquipSlotPacks { get; set; }

    public virtual DbSet<SlaveHealingPointDoodad> SlaveHealingPointDoodads { get; set; }

    public virtual DbSet<SlaveInitialBuff> SlaveInitialBuffs { get; set; }

    public virtual DbSet<SlaveInitialItem> SlaveInitialItems { get; set; }

    public virtual DbSet<SlaveInitialItemPack> SlaveInitialItemPacks { get; set; }

    public virtual DbSet<SlaveMountSkill> SlaveMountSkills { get; set; }

    public virtual DbSet<SlavePassiveBuff> SlavePassiveBuffs { get; set; }

    public virtual DbSet<Sound> Sounds { get; set; }

    public virtual DbSet<SoundPack> SoundPacks { get; set; }

    public virtual DbSet<SoundPackItem> SoundPackItems { get; set; }

    public virtual DbSet<SpawnEffect> SpawnEffects { get; set; }

    public virtual DbSet<SpawnFishEffect> SpawnFishEffects { get; set; }

    public virtual DbSet<SpawnGimmickEffect> SpawnGimmickEffects { get; set; }

    public virtual DbSet<SpecialEffect> SpecialEffects { get; set; }

    public virtual DbSet<Specialty> Specialties { get; set; }

    public virtual DbSet<SpecialtyBundle> SpecialtyBundles { get; set; }

    public virtual DbSet<SpecialtyBundleItem> SpecialtyBundleItems { get; set; }

    public virtual DbSet<SpecialtyNpc> SpecialtyNpcs { get; set; }

    public virtual DbSet<Sphere> Spheres { get; set; }

    public virtual DbSet<SphereAcceptQuest> SphereAcceptQuests { get; set; }

    public virtual DbSet<SphereAcceptQuestQuest> SphereAcceptQuestQuests { get; set; }

    public virtual DbSet<SphereBubble> SphereBubbles { get; set; }

    public virtual DbSet<SphereBuff> SphereBuffs { get; set; }

    public virtual DbSet<SphereChatBubble> SphereChatBubbles { get; set; }

    public virtual DbSet<SphereDoodadInteract> SphereDoodadInteracts { get; set; }

    public virtual DbSet<SphereQuest> SphereQuests { get; set; }

    public virtual DbSet<SphereQuestMail> SphereQuestMails { get; set; }

    public virtual DbSet<SphereSkill> SphereSkills { get; set; }

    public virtual DbSet<SphereSound> SphereSounds { get; set; }

    public virtual DbSet<SubZone> SubZones { get; set; }

    public virtual DbSet<SystemFaction> SystemFactions { get; set; }

    public virtual DbSet<SystemFactionRelation> SystemFactionRelations { get; set; }

    public virtual DbSet<Tag> Tags { get; set; }

    public virtual DbSet<TaggedBuff> TaggedBuffs { get; set; }

    public virtual DbSet<TaggedItem> TaggedItems { get; set; }

    public virtual DbSet<TaggedNpc> TaggedNpcs { get; set; }

    public virtual DbSet<TaggedSkill> TaggedSkills { get; set; }

    public virtual DbSet<Taxation> Taxations { get; set; }

    public virtual DbSet<TooltipSkillEffect> TooltipSkillEffects { get; set; }

    public virtual DbSet<TotalCharacterCustom> TotalCharacterCustoms { get; set; }

    public virtual DbSet<TowerDef> TowerDefs { get; set; }

    public virtual DbSet<TowerDefProg> TowerDefProgs { get; set; }

    public virtual DbSet<TowerDefProgKillTarget> TowerDefProgKillTargets { get; set; }

    public virtual DbSet<TowerDefProgSpawnTarget> TowerDefProgSpawnTargets { get; set; }

    public virtual DbSet<TrainCraftEffect> TrainCraftEffects { get; set; }

    public virtual DbSet<TrainCraftRankEffect> TrainCraftRankEffects { get; set; }

    public virtual DbSet<Transfer> Transfers { get; set; }

    public virtual DbSet<TransferBinding> TransferBindings { get; set; }

    public virtual DbSet<TransferBindingDoodad> TransferBindingDoodads { get; set; }

    public virtual DbSet<TransferPath> TransferPaths { get; set; }

    public virtual DbSet<UccApplicable> UccApplicables { get; set; }

    public virtual DbSet<UiText> UiTexts { get; set; }

    public virtual DbSet<UnitAttributeLimit> UnitAttributeLimits { get; set; }

    public virtual DbSet<UnitFormula> UnitFormulas { get; set; }

    public virtual DbSet<UnitFormulaVariable> UnitFormulaVariables { get; set; }

    public virtual DbSet<UnitModifier> UnitModifiers { get; set; }

    public virtual DbSet<UnitReq> UnitReqs { get; set; }

    public virtual DbSet<VehicleModel> VehicleModels { get; set; }

    public virtual DbSet<Wearable> Wearables { get; set; }

    public virtual DbSet<WearableFormula> WearableFormulas { get; set; }

    public virtual DbSet<WearableKind> WearableKinds { get; set; }

    public virtual DbSet<WearableSlot> WearableSlots { get; set; }

    public virtual DbSet<WiDetail> WiDetails { get; set; }

    public virtual DbSet<WiGroup> WiGroups { get; set; }

    public virtual DbSet<WiGroupWi> WiGroupWis { get; set; }

    public virtual DbSet<WorldGroup> WorldGroups { get; set; }

    public virtual DbSet<WorldSpecConfig> WorldSpecConfigs { get; set; }

    public virtual DbSet<WorldVarDefault> WorldVarDefaults { get; set; }

    public virtual DbSet<Zone> Zones { get; set; }

    public virtual DbSet<ZoneClimate> ZoneClimates { get; set; }

    public virtual DbSet<ZoneClimateElem> ZoneClimateElems { get; set; }

    public virtual DbSet<ZoneGroup> ZoneGroups { get; set; }

    public virtual DbSet<ZoneGroupBannedTag> ZoneGroupBannedTags { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
#warning To protect potentially sensitive information in your connection string, you should move it out of source code. You can avoid scaffolding the connection string by using the Name= syntax to read it from configuration - see https://go.microsoft.com/fwlink/?linkid=2131148. For more guidance on storing connection strings, see http://go.microsoft.com/fwlink/?LinkId=723263.
        => optionsBuilder.UseSqlite("DataSource=C:\\Users\\Lars\\Desktop\\AAEmu\\AAEmu.Game\\Data\\compact.sqlite3");

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AcceptQuestEffect>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("accept_quest_effects");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.QuestId)
                .HasColumnType("INT")
                .HasColumnName("quest_id");
        });

        modelBuilder.Entity<AccountAttributeEffect>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("account_attribute_effects");

            entity.Property(e => e.BindWorld)
                .HasColumnType("NUM")
                .HasColumnName("bind_world");
            entity.Property(e => e.Count)
                .HasColumnType("INT")
                .HasColumnName("count");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.IsAdd)
                .HasColumnType("NUM")
                .HasColumnName("is_add");
            entity.Property(e => e.KindId)
                .HasColumnType("INT")
                .HasColumnName("kind_id");
            entity.Property(e => e.Time)
                .HasColumnType("INT")
                .HasColumnName("time");
        });

        modelBuilder.Entity<Achievement>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("achievements");

            entity.Property(e => e.CategoryId)
                .HasColumnType("INT")
                .HasColumnName("category_id");
            entity.Property(e => e.CompleteNum)
                .HasColumnType("INT")
                .HasColumnName("complete_num");
            entity.Property(e => e.CompleteOr)
                .HasColumnType("NUM")
                .HasColumnName("complete_or");
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.IconId)
                .HasColumnType("INT")
                .HasColumnName("icon_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.IsActive)
                .HasColumnType("NUM")
                .HasColumnName("is_active");
            entity.Property(e => e.IsHidden)
                .HasColumnType("NUM")
                .HasColumnName("is_hidden");
            entity.Property(e => e.ItemId)
                .HasColumnType("INT")
                .HasColumnName("item_id");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.OrUnitReqs)
                .HasColumnType("NUM")
                .HasColumnName("or_unit_reqs");
            entity.Property(e => e.ParentAchievementId)
                .HasColumnType("INT")
                .HasColumnName("parent_achievement_id");
            entity.Property(e => e.Priority)
                .HasColumnType("INT")
                .HasColumnName("priority");
            entity.Property(e => e.SubCategoryId)
                .HasColumnType("INT")
                .HasColumnName("sub_category_id");
            entity.Property(e => e.Summary).HasColumnName("summary");
        });

        modelBuilder.Entity<AchievementObjective>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("achievement_objectives");

            entity.Property(e => e.AchievementId)
                .HasColumnType("INT")
                .HasColumnName("achievement_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.OrUnitReqs)
                .HasColumnType("NUM")
                .HasColumnName("or_unit_reqs");
            entity.Property(e => e.RecordId)
                .HasColumnType("INT")
                .HasColumnName("record_id");
        });

        modelBuilder.Entity<ActabilityCategory>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("actability_categories");

            entity.Property(e => e.GroupId)
                .HasColumnType("INT")
                .HasColumnName("group_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.VisibleOrder)
                .HasColumnType("INT")
                .HasColumnName("visible_order");
            entity.Property(e => e.VisibleUi)
                .HasColumnType("NUM")
                .HasColumnName("visible_ui");
        });

        modelBuilder.Entity<ActabilityGroup>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("actability_groups");

            entity.Property(e => e.IconPath).HasColumnName("icon_path");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.SkillPageVisible)
                .HasColumnType("NUM")
                .HasColumnName("skill_page_visible");
            entity.Property(e => e.UnitAttrId)
                .HasColumnType("INT")
                .HasColumnName("unit_attr_id");
            entity.Property(e => e.Visible)
                .HasColumnType("NUM")
                .HasColumnName("visible");
        });

        modelBuilder.Entity<ActorModel>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("actor_models");

            entity.Property(e => e.ActorHeight).HasColumnName("actor_height");
            entity.Property(e => e.AnimationGraph).HasColumnName("animation_graph");
            entity.Property(e => e.AttackStartRange).HasColumnName("attack_start_range");
            entity.Property(e => e.BeanstalkBack).HasColumnName("beanstalk_back");
            entity.Property(e => e.FaceTargetInstantly)
                .HasColumnType("NUM")
                .HasColumnName("face_target_instantly");
            entity.Property(e => e.FlyMode)
                .HasColumnType("NUM")
                .HasColumnName("fly_mode");
            entity.Property(e => e.GameBackwardDiagonalMultiplier).HasColumnName("game_backward_diagonal_multiplier");
            entity.Property(e => e.GameBackwardMultiplier).HasColumnName("game_backward_multiplier");
            entity.Property(e => e.GameBowLookIkBlendHead).HasColumnName("game_bow_look_ik_blend_head");
            entity.Property(e => e.GameBowLookIkBlendNeck).HasColumnName("game_bow_look_ik_blend_neck");
            entity.Property(e => e.GameBowLookIkBlendSpine1).HasColumnName("game_bow_look_ik_blend_spine1");
            entity.Property(e => e.GameBowLookIkBlendSpine2).HasColumnName("game_bow_look_ik_blend_spine2");
            entity.Property(e => e.GameBowLookIkBlendSpine3).HasColumnName("game_bow_look_ik_blend_spine3");
            entity.Property(e => e.GameForwardDiagonalMultiplier).HasColumnName("game_forward_diagonal_multiplier");
            entity.Property(e => e.GameForwardMultiplier).HasColumnName("game_forward_multiplier");
            entity.Property(e => e.GameGrabMultiplier).HasColumnName("game_grab_multiplier");
            entity.Property(e => e.GameInertia).HasColumnName("game_inertia");
            entity.Property(e => e.GameInertiaAccel).HasColumnName("game_inertia_accel");
            entity.Property(e => e.GameJumpHeight).HasColumnName("game_jump_height");
            entity.Property(e => e.GameLeanAngle)
                .HasColumnType("INT")
                .HasColumnName("game_lean_angle");
            entity.Property(e => e.GameLeanShift).HasColumnName("game_lean_shift");
            entity.Property(e => e.GameLookIkBlendHead).HasColumnName("game_look_ik_blend_head");
            entity.Property(e => e.GameLookIkBlendNeck).HasColumnName("game_look_ik_blend_neck");
            entity.Property(e => e.GameLookIkBlendSpine1).HasColumnName("game_look_ik_blend_spine1");
            entity.Property(e => e.GameLookIkBlendSpine2).HasColumnName("game_look_ik_blend_spine2");
            entity.Property(e => e.GameLookIkBlendSpine3).HasColumnName("game_look_ik_blend_spine3");
            entity.Property(e => e.GameMaxGrabMass)
                .HasColumnType("INT")
                .HasColumnName("game_max_grab_mass");
            entity.Property(e => e.GameMaxGrabVolume).HasColumnName("game_max_grab_volume");
            entity.Property(e => e.GameSprintMultiplier).HasColumnName("game_sprint_multiplier");
            entity.Property(e => e.GameStrafeMultiplier).HasColumnName("game_strafe_multiplier");
            entity.Property(e => e.GameWalkBackwardDiagonalMultiplier).HasColumnName("game_walk_backward_diagonal_multiplier");
            entity.Property(e => e.GameWalkBackwardMultiplier).HasColumnName("game_walk_backward_multiplier");
            entity.Property(e => e.GameWalkForwardDiagonalMultiplier).HasColumnName("game_walk_forward_diagonal_multiplier");
            entity.Property(e => e.GameWalkMultiplier).HasColumnName("game_walk_multiplier");
            entity.Property(e => e.GameWalkStrafeMultiplier).HasColumnName("game_walk_strafe_multiplier");
            entity.Property(e => e.GroundTargetable)
                .HasColumnType("NUM")
                .HasColumnName("ground_targetable");
            entity.Property(e => e.HandRate).HasColumnName("hand_rate");
            entity.Property(e => e.Height).HasColumnName("height");
            entity.Property(e => e.HitPower)
                .HasColumnType("INT")
                .HasColumnName("hit_power");
            entity.Property(e => e.HropeDown).HasColumnName("hrope_down");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.ModelFile).HasColumnName("model_file");
            entity.Property(e => e.MovementId)
                .HasColumnType("INT")
                .HasColumnName("movement_id");
            entity.Property(e => e.PhysicsFlags)
                .HasColumnType("INT")
                .HasColumnName("physics_flags");
            entity.Property(e => e.PhysicsLivingAirResistance).HasColumnName("physics_living_air_resistance");
            entity.Property(e => e.PhysicsLivingColliderMat).HasColumnName("physics_living_collider_mat");
            entity.Property(e => e.PhysicsLivingGravity).HasColumnName("physics_living_gravity");
            entity.Property(e => e.PhysicsLivingKAirControl).HasColumnName("physics_living_k_air_control");
            entity.Property(e => e.PhysicsLivingMass)
                .HasColumnType("INT")
                .HasColumnName("physics_living_mass");
            entity.Property(e => e.PhysicsLivingMaxClimbAngle).HasColumnName("physics_living_max_climb_angle");
            entity.Property(e => e.PhysicsLivingMaxVelGround)
                .HasColumnType("INT")
                .HasColumnName("physics_living_max_vel_ground");
            entity.Property(e => e.PhysicsLivingMinFallAngle).HasColumnName("physics_living_min_fall_angle");
            entity.Property(e => e.PhysicsLivingMinSlideAngle).HasColumnName("physics_living_min_slide_angle");
            entity.Property(e => e.PhysicsLivingTimeImpulseRecover).HasColumnName("physics_living_time_impulse_recover");
            entity.Property(e => e.PhysicsMass)
                .HasColumnType("INT")
                .HasColumnName("physics_mass");
            entity.Property(e => e.PhysicsStiffnessScale)
                .HasColumnType("INT")
                .HasColumnName("physics_stiffness_scale");
            entity.Property(e => e.Portrait).HasColumnName("portrait");
            entity.Property(e => e.PushRagdoll)
                .HasColumnType("NUM")
                .HasColumnName("push_ragdoll");
            entity.Property(e => e.Radius).HasColumnName("radius");
            entity.Property(e => e.RopeBack).HasColumnName("rope_back");
            entity.Property(e => e.RopeHangingHandOffsetX).HasColumnName("rope_hanging_hand_offset_x");
            entity.Property(e => e.RopeHangingHandOffsetY).HasColumnName("rope_hanging_hand_offset_y");
            entity.Property(e => e.RopeHangingHandOffsetZ).HasColumnName("rope_hanging_hand_offset_z");
            entity.Property(e => e.SharedDummyModel)
                .HasColumnType("NUM")
                .HasColumnName("shared_dummy_model");
            entity.Property(e => e.SightFov).HasColumnName("sight_fov");
            entity.Property(e => e.SightRange).HasColumnName("sight_range");
            entity.Property(e => e.SlopeAlignment)
                .HasColumnType("NUM")
                .HasColumnName("slope_alignment");
            entity.Property(e => e.SwimHeight).HasColumnName("swim_height");
            entity.Property(e => e.TurnSpeed).HasColumnName("turn_speed");
            entity.Property(e => e.UnderwaterCreature)
                .HasColumnType("NUM")
                .HasColumnName("underwater_creature");
            entity.Property(e => e.UpperbodyGraph).HasColumnName("upperbody_graph");
            entity.Property(e => e.UseRagdoll)
                .HasColumnType("NUM")
                .HasColumnName("use_ragdoll");
            entity.Property(e => e.UseRagdollHit)
                .HasColumnType("NUM")
                .HasColumnName("use_ragdoll_hit");
            entity.Property(e => e.UseRagdollKnockDown)
                .HasColumnType("NUM")
                .HasColumnName("use_ragdoll_knock_down");
            entity.Property(e => e.UseRandomIdleControl)
                .HasColumnType("NUM")
                .HasColumnName("use_random_idle_control");
        });

        modelBuilder.Entity<AggroEffect>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("aggro_effects");

            entity.Property(e => e.ChargedBuffId)
                .HasColumnType("INT")
                .HasColumnName("charged_buff_id");
            entity.Property(e => e.ChargedMul).HasColumnName("charged_mul");
            entity.Property(e => e.FixedMax)
                .HasColumnType("INT")
                .HasColumnName("fixed_max");
            entity.Property(e => e.FixedMin)
                .HasColumnType("INT")
                .HasColumnName("fixed_min");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.LevelMd).HasColumnName("level_md");
            entity.Property(e => e.LevelVaEnd)
                .HasColumnType("INT")
                .HasColumnName("level_va_end");
            entity.Property(e => e.LevelVaStart)
                .HasColumnType("INT")
                .HasColumnName("level_va_start");
            entity.Property(e => e.UseChargedBuff)
                .HasColumnType("NUM")
                .HasColumnName("use_charged_buff");
            entity.Property(e => e.UseFixedAggro)
                .HasColumnType("NUM")
                .HasColumnName("use_fixed_aggro");
            entity.Property(e => e.UseLevelAggro)
                .HasColumnType("NUM")
                .HasColumnName("use_level_aggro");
        });

        modelBuilder.Entity<AggroLink>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("aggro_links");

            entity.Property(e => e.Comment).HasColumnName("comment");
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name");
        });

        modelBuilder.Entity<AiCommand>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("ai_commands");

            entity.Property(e => e.CmdId).HasColumnName("cmd_id");
            entity.Property(e => e.CmdSetId).HasColumnName("cmd_set_id");
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Param1).HasColumnName("param1");
            entity.Property(e => e.Param2).HasColumnName("param2");
        });

        modelBuilder.Entity<AiCommandSet>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("ai_command_sets");

            entity.Property(e => e.CanInteract).HasColumnName("can_interact");
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name");
        });

        modelBuilder.Entity<AiEvent>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("ai_events");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.IgnoreCategoryId)
                .HasColumnType("INT")
                .HasColumnName("ignore_category_id");
            entity.Property(e => e.IgnoreTime).HasColumnName("ignore_time");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.NpcId)
                .HasColumnType("INT")
                .HasColumnName("npc_id");
            entity.Property(e => e.OrUnitReqs)
                .HasColumnType("NUM")
                .HasColumnName("or_unit_reqs");
            entity.Property(e => e.SkillId)
                .HasColumnType("INT")
                .HasColumnName("skill_id");
        });

        modelBuilder.Entity<AiFile>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("ai_files");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.ParamTemplate).HasColumnName("param_template");
        });

        modelBuilder.Entity<AllowToEquipSlafe>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("allow_to_equip_slaves");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.SlaveEquipPackId)
                .HasColumnType("INT")
                .HasColumnName("slave_equip_pack_id");
            entity.Property(e => e.SlaveId)
                .HasColumnType("INT")
                .HasColumnName("slave_id");
        });

        modelBuilder.Entity<AllowToEquipSlot>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("allow_to_equip_slots");

            entity.Property(e => e.EquipSlotId)
                .HasColumnType("INT")
                .HasColumnName("equip_slot_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.SlaveEquipmentEquipSlotPackId)
                .HasColumnType("INT")
                .HasColumnName("slave_equipment_equip_slot_pack_id");
        });

        modelBuilder.Entity<AllowedNameChar>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("allowed_name_chars");

            entity.Property(e => e.Bytes)
                .HasColumnType("INT")
                .HasColumnName("bytes");
            entity.Property(e => e.Char).HasColumnName("char");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
        });

        modelBuilder.Entity<Anim>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("anims");

            entity.Property(e => e.CategoryId)
                .HasColumnType("INT")
                .HasColumnName("category_id");
            entity.Property(e => e.HangUb).HasColumnName("hang_ub");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Loop)
                .HasColumnType("NUM")
                .HasColumnName("loop");
            entity.Property(e => e.MoveUb).HasColumnName("move_ub");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.RelaxedUb).HasColumnName("relaxed_ub");
            entity.Property(e => e.RideUb).HasColumnName("ride_ub");
            entity.Property(e => e.SwimMoveUb).HasColumnName("swim_move_ub");
            entity.Property(e => e.SwimUb).HasColumnName("swim_ub");
        });

        modelBuilder.Entity<AnimAction>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("anim_actions");

            entity.Property(e => e.ActionStateId)
                .HasColumnType("INT")
                .HasColumnName("action_state_id");
            entity.Property(e => e.AnimName).HasColumnName("anim_name");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.MainhandToolId)
                .HasColumnType("INT")
                .HasColumnName("mainhand_tool_id");
            entity.Property(e => e.ModelAngle).HasColumnName("model_angle");
            entity.Property(e => e.ModelPath).HasColumnName("model_path");
            entity.Property(e => e.ModelPhysic)
                .HasColumnType("NUM")
                .HasColumnName("model_physic");
            entity.Property(e => e.ModelPosX).HasColumnName("model_pos_x");
            entity.Property(e => e.ModelPosY).HasColumnName("model_pos_y");
            entity.Property(e => e.ModelPosZ).HasColumnName("model_pos_z");
            entity.Property(e => e.MountPoseId)
                .HasColumnType("INT")
                .HasColumnName("mount_pose_id");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.NoRotate)
                .HasColumnType("NUM")
                .HasColumnName("no_rotate");
            entity.Property(e => e.OffhandToolId)
                .HasColumnType("INT")
                .HasColumnName("offhand_tool_id");
        });

        modelBuilder.Entity<AnimRule>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("anim_rules");

            entity.Property(e => e.Before)
                .HasColumnType("INT")
                .HasColumnName("before");
            entity.Property(e => e.BeforeOperatorId)
                .HasColumnType("INT")
                .HasColumnName("before_operator_id");
            entity.Property(e => e.DefaultOperatorId)
                .HasColumnType("INT")
                .HasColumnName("default_operator_id");
            entity.Property(e => e.FirstCategoryId)
                .HasColumnType("INT")
                .HasColumnName("first_category_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.SecondCategoryId)
                .HasColumnType("INT")
                .HasColumnName("second_category_id");
        });

        modelBuilder.Entity<AoeDiminishing>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("aoe_diminishings");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Rate).HasColumnName("rate");
        });

        modelBuilder.Entity<AoeShape>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("aoe_shapes");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.KindId)
                .HasColumnType("INT")
                .HasColumnName("kind_id");
            entity.Property(e => e.Value1).HasColumnName("value1");
            entity.Property(e => e.Value2).HasColumnName("value2");
            entity.Property(e => e.Value3).HasColumnName("value3");
        });

        modelBuilder.Entity<Appellation>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("appellations");

            entity.Property(e => e.BuffId)
                .HasColumnType("INT")
                .HasColumnName("buff_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name");
        });

        modelBuilder.Entity<AreaEventCheckNpc>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("area_event_check_npcs");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.NpcId)
                .HasColumnType("INT")
                .HasColumnName("npc_id");
        });

        modelBuilder.Entity<ArmorAsset>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("armor_assets");

            entity.Property(e => e.DefaultAssetId)
                .HasColumnType("INT")
                .HasColumnName("default_asset_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.SlotTypeId)
                .HasColumnType("INT")
                .HasColumnName("slot_type_id");
        });

        modelBuilder.Entity<ArmorGradeBuff>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("armor_grade_buffs");

            entity.Property(e => e.ArmorTypeId)
                .HasColumnType("INT")
                .HasColumnName("armor_type_id");
            entity.Property(e => e.BuffId)
                .HasColumnType("INT")
                .HasColumnName("buff_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.ItemGradeId)
                .HasColumnType("INT")
                .HasColumnName("item_grade_id");
        });

        modelBuilder.Entity<AttachAnim>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("attach_anims");

            entity.Property(e => e.AnimActionId)
                .HasColumnType("INT")
                .HasColumnName("anim_action_id");
            entity.Property(e => e.AttachPointId)
                .HasColumnType("INT")
                .HasColumnName("attach_point_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.OwnerId)
                .HasColumnType("INT")
                .HasColumnName("owner_id");
            entity.Property(e => e.OwnerType).HasColumnName("owner_type");
        });

        modelBuilder.Entity<AuctionACategory>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("auction_a_categories");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name");
        });

        modelBuilder.Entity<AuctionBCategory>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("auction_b_categories");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.ParentCategoryId)
                .HasColumnType("INT")
                .HasColumnName("parent_category_id");
        });

        modelBuilder.Entity<AuctionCCategory>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("auction_c_categories");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.ParentCategoryId)
                .HasColumnType("INT")
                .HasColumnName("parent_category_id");
        });

        modelBuilder.Entity<BagExpand>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("bag_expands");

            entity.Property(e => e.CurrencyId)
                .HasColumnType("INT")
                .HasColumnName("currency_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.IsBank)
                .HasColumnType("NUM")
                .HasColumnName("is_bank");
            entity.Property(e => e.ItemCount)
                .HasColumnType("INT")
                .HasColumnName("item_count");
            entity.Property(e => e.ItemId)
                .HasColumnType("INT")
                .HasColumnName("item_id");
            entity.Property(e => e.Price)
                .HasColumnType("INT")
                .HasColumnName("price");
            entity.Property(e => e.Step)
                .HasColumnType("INT")
                .HasColumnName("step");
        });

        modelBuilder.Entity<BattleField>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("battle_fields");

            entity.Property(e => e.Comments).HasColumnName("comments");
            entity.Property(e => e.Desc).HasColumnName("desc");
            entity.Property(e => e.FieldKindId)
                .HasColumnType("INT")
                .HasColumnName("field_kind_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.ZoneKey)
                .HasColumnType("INT")
                .HasColumnName("zone_key");
        });

        modelBuilder.Entity<BattleFieldBuff>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("battle_field_buffs");

            entity.Property(e => e.BattleFieldId)
                .HasColumnType("INT")
                .HasColumnName("battle_field_id");
            entity.Property(e => e.BuffId)
                .HasColumnType("INT")
                .HasColumnName("buff_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Value)
                .HasColumnType("INT")
                .HasColumnName("value");
        });

        modelBuilder.Entity<BlockedChildDoodad>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("blocked_child_doodads");

            entity.Property(e => e.DoodadId)
                .HasColumnType("INT")
                .HasColumnName("doodad_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.OwnerId)
                .HasColumnType("INT")
                .HasColumnName("owner_id");
            entity.Property(e => e.OwnerType).HasColumnName("owner_type");
        });

        modelBuilder.Entity<BlockedText>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("blocked_texts");

            entity.Property(e => e.Bytes)
                .HasColumnType("INT")
                .HasColumnName("bytes");
            entity.Property(e => e.CheckChat)
                .HasColumnType("NUM")
                .HasColumnName("check_chat");
            entity.Property(e => e.CheckName)
                .HasColumnType("NUM")
                .HasColumnName("check_name");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.PartialMatch)
                .HasColumnType("NUM")
                .HasColumnName("partial_match");
            entity.Property(e => e.Utf8str).HasColumnName("utf8str");
        });

        modelBuilder.Entity<BodyDiffuseMap>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("body_diffuse_maps");

            entity.Property(e => e.Diffuse).HasColumnName("diffuse");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.ModelId)
                .HasColumnType("INT")
                .HasColumnName("model_id");
            entity.Property(e => e.Name).HasColumnName("name");
        });

        modelBuilder.Entity<Book>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("books");

            entity.Property(e => e.CategoryId)
                .HasColumnType("INT")
                .HasColumnName("category_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name");
        });

        modelBuilder.Entity<BookElem>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("book_elems");

            entity.Property(e => e.BookId)
                .HasColumnType("INT")
                .HasColumnName("book_id");
            entity.Property(e => e.BookPageId)
                .HasColumnType("INT")
                .HasColumnName("book_page_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
        });

        modelBuilder.Entity<BookPage>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("book_pages");

            entity.Property(e => e.CategoryId)
                .HasColumnType("INT")
                .HasColumnName("category_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name");
        });

        modelBuilder.Entity<BookPageContent>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("book_page_contents");

            entity.Property(e => e.BookPageId)
                .HasColumnType("INT")
                .HasColumnName("book_page_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Illust).HasColumnName("illust");
            entity.Property(e => e.Text).HasColumnName("text");
        });

        modelBuilder.Entity<Bubble>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("bubbles");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name");
        });

        modelBuilder.Entity<BubbleChat>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("bubble_chats");

            entity.Property(e => e.Angle)
                .HasColumnType("INT")
                .HasColumnName("angle");
            entity.Property(e => e.BubbleId)
                .HasColumnType("INT")
                .HasColumnName("bubble_id");
            entity.Property(e => e.CameraId)
                .HasColumnType("INT")
                .HasColumnName("camera_id");
            entity.Property(e => e.ChangeSpeakerName).HasColumnName("change_speaker_name");
            entity.Property(e => e.DoodadId)
                .HasColumnType("INT")
                .HasColumnName("doodad_id");
            entity.Property(e => e.Facial).HasColumnName("facial");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.KindId)
                .HasColumnType("INT")
                .HasColumnName("kind_id");
            entity.Property(e => e.Next)
                .HasColumnType("INT")
                .HasColumnName("next");
            entity.Property(e => e.NpcId)
                .HasColumnType("INT")
                .HasColumnName("npc_id");
            entity.Property(e => e.NpcSpawnerId)
                .HasColumnType("INT")
                .HasColumnName("npc_spawner_id");
            entity.Property(e => e.SoundId)
                .HasColumnType("INT")
                .HasColumnName("sound_id");
            entity.Property(e => e.Speech).HasColumnName("speech");
            entity.Property(e => e.Start)
                .HasColumnType("NUM")
                .HasColumnName("start");
            entity.Property(e => e.VoiceId)
                .HasColumnType("INT")
                .HasColumnName("voice_id");
        });

        modelBuilder.Entity<BubbleEffect>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("bubble_effects");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.KindId)
                .HasColumnType("INT")
                .HasColumnName("kind_id");
            entity.Property(e => e.Speech).HasColumnName("speech");
        });

        modelBuilder.Entity<Buff>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("buffs");

            entity.Property(e => e.ActiveWeaponId)
                .HasColumnType("INT")
                .HasColumnName("active_weapon_id");
            entity.Property(e => e.AgStance).HasColumnName("ag_stance");
            entity.Property(e => e.AnimActionId)
                .HasColumnType("INT")
                .HasColumnName("anim_action_id");
            entity.Property(e => e.AnimEndId)
                .HasColumnType("INT")
                .HasColumnName("anim_end_id");
            entity.Property(e => e.AnimStartId)
                .HasColumnType("INT")
                .HasColumnName("anim_start_id");
            entity.Property(e => e.AntiStealth)
                .HasColumnType("NUM")
                .HasColumnName("anti_stealth");
            entity.Property(e => e.AuraChildOnly)
                .HasColumnType("NUM")
                .HasColumnName("aura_child_only");
            entity.Property(e => e.AuraRadius)
                .HasColumnType("INT")
                .HasColumnName("aura_radius");
            entity.Property(e => e.AuraRelationId)
                .HasColumnType("INT")
                .HasColumnName("aura_relation_id");
            entity.Property(e => e.AuraSlaveBuffId)
                .HasColumnType("INT")
                .HasColumnName("aura_slave_buff_id");
            entity.Property(e => e.BlankMinded)
                .HasColumnType("NUM")
                .HasColumnName("blank_minded");
            entity.Property(e => e.CannotJump)
                .HasColumnType("NUM")
                .HasColumnName("cannot_jump");
            entity.Property(e => e.CombatTextEnd)
                .HasColumnType("NUM")
                .HasColumnName("combat_text_end");
            entity.Property(e => e.CombatTextStart)
                .HasColumnType("NUM")
                .HasColumnName("combat_text_start");
            entity.Property(e => e.ConditionalTick)
                .HasColumnType("NUM")
                .HasColumnName("conditional_tick");
            entity.Property(e => e.CooldownSkillId)
                .HasColumnType("INT")
                .HasColumnName("cooldown_skill_id");
            entity.Property(e => e.CooldownSkillTime)
                .HasColumnType("INT")
                .HasColumnName("cooldown_skill_time");
            entity.Property(e => e.Crime)
                .HasColumnType("NUM")
                .HasColumnName("crime");
            entity.Property(e => e.Crippled)
                .HasColumnType("NUM")
                .HasColumnName("crippled");
            entity.Property(e => e.CrowdBuffId)
                .HasColumnType("INT")
                .HasColumnName("crowd_buff_id");
            entity.Property(e => e.CrowdFriendly)
                .HasColumnType("NUM")
                .HasColumnName("crowd_friendly");
            entity.Property(e => e.CrowdHostile)
                .HasColumnType("NUM")
                .HasColumnName("crowd_hostile");
            entity.Property(e => e.CrowdNumber)
                .HasColumnType("INT")
                .HasColumnName("crowd_number");
            entity.Property(e => e.CrowdRadius).HasColumnName("crowd_radius");
            entity.Property(e => e.CustomDualMaterialFadeTime).HasColumnName("custom_dual_material_fade_time");
            entity.Property(e => e.CustomDualMaterialId)
                .HasColumnType("INT")
                .HasColumnName("custom_dual_material_id");
            entity.Property(e => e.DamageAbsorptionPerHit)
                .HasColumnType("INT")
                .HasColumnName("damage_absorption_per_hit");
            entity.Property(e => e.DamageAbsorptionTypeId)
                .HasColumnType("INT")
                .HasColumnName("damage_absorption_type_id");
            entity.Property(e => e.DeadApplicable)
                .HasColumnType("NUM")
                .HasColumnName("dead_applicable");
            entity.Property(e => e.Desc).HasColumnName("desc");
            entity.Property(e => e.DescTr)
                .HasColumnType("NUM")
                .HasColumnName("desc_tr");
            entity.Property(e => e.DetectStealth)
                .HasColumnType("NUM")
                .HasColumnName("detect_stealth");
            entity.Property(e => e.DoNotRemoveByOtherSkillController)
                .HasColumnType("NUM")
                .HasColumnName("do_not_remove_by_other_skill_controller");
            entity.Property(e => e.Duration)
                .HasColumnType("INT")
                .HasColumnName("duration");
            entity.Property(e => e.EvadeTelescope)
                .HasColumnType("NUM")
                .HasColumnName("evade_telescope");
            entity.Property(e => e.Exempt)
                .HasColumnType("NUM")
                .HasColumnName("exempt");
            entity.Property(e => e.FactionId)
                .HasColumnType("INT")
                .HasColumnName("faction_id");
            entity.Property(e => e.FallDamageImmune)
                .HasColumnType("NUM")
                .HasColumnName("fall_damage_immune");
            entity.Property(e => e.Fastened)
                .HasColumnType("NUM")
                .HasColumnName("fastened");
            entity.Property(e => e.FindSchoolOfFishRange).HasColumnName("find_school_of_fish_range");
            entity.Property(e => e.Framehold)
                .HasColumnType("NUM")
                .HasColumnName("framehold");
            entity.Property(e => e.FreezeShip)
                .HasColumnType("NUM")
                .HasColumnName("freeze_ship");
            entity.Property(e => e.FxGroupId)
                .HasColumnType("INT")
                .HasColumnName("fx_group_id");
            entity.Property(e => e.Gliding)
                .HasColumnType("NUM")
                .HasColumnName("gliding");
            entity.Property(e => e.GlidingFallSpeedFast).HasColumnName("gliding_fall_speed_fast");
            entity.Property(e => e.GlidingFallSpeedNormal).HasColumnName("gliding_fall_speed_normal");
            entity.Property(e => e.GlidingFallSpeedSlow).HasColumnName("gliding_fall_speed_slow");
            entity.Property(e => e.GlidingLandHeight).HasColumnName("gliding_land_height");
            entity.Property(e => e.GlidingLiftCount)
                .HasColumnType("INT")
                .HasColumnName("gliding_lift_count");
            entity.Property(e => e.GlidingLiftDuration).HasColumnName("gliding_lift_duration");
            entity.Property(e => e.GlidingLiftHeight).HasColumnName("gliding_lift_height");
            entity.Property(e => e.GlidingLiftSpeed).HasColumnName("gliding_lift_speed");
            entity.Property(e => e.GlidingLiftValidTime).HasColumnName("gliding_lift_valid_time");
            entity.Property(e => e.GlidingMoveSpeedFast).HasColumnName("gliding_move_speed_fast");
            entity.Property(e => e.GlidingMoveSpeedNormal).HasColumnName("gliding_move_speed_normal");
            entity.Property(e => e.GlidingMoveSpeedSlow).HasColumnName("gliding_move_speed_slow");
            entity.Property(e => e.GlidingRotateSpeed)
                .HasColumnType("INT")
                .HasColumnName("gliding_rotate_speed");
            entity.Property(e => e.GlidingSlidingTime).HasColumnName("gliding_sliding_time");
            entity.Property(e => e.GlidingSmoothTime).HasColumnName("gliding_smooth_time");
            entity.Property(e => e.GlidingStartupSpeed).HasColumnName("gliding_startup_speed");
            entity.Property(e => e.GlidingStartupTime).HasColumnName("gliding_startup_time");
            entity.Property(e => e.GroupId)
                .HasColumnType("INT")
                .HasColumnName("group_id");
            entity.Property(e => e.GroupRank)
                .HasColumnType("INT")
                .HasColumnName("group_rank");
            entity.Property(e => e.IconId)
                .HasColumnType("INT")
                .HasColumnName("icon_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.IdleAnim).HasColumnName("idle_anim");
            entity.Property(e => e.ImmuneBuffTagId)
                .HasColumnType("INT")
                .HasColumnName("immune_buff_tag_id");
            entity.Property(e => e.ImmuneDamage)
                .HasColumnType("INT")
                .HasColumnName("immune_damage");
            entity.Property(e => e.ImmuneExceptCreator)
                .HasColumnType("NUM")
                .HasColumnName("immune_except_creator");
            entity.Property(e => e.ImmuneExceptSkillTagId)
                .HasColumnType("INT")
                .HasColumnName("immune_except_skill_tag_id");
            entity.Property(e => e.InitMaxCharge)
                .HasColumnType("INT")
                .HasColumnName("init_max_charge");
            entity.Property(e => e.InitMinCharge)
                .HasColumnType("INT")
                .HasColumnName("init_min_charge");
            entity.Property(e => e.KindId)
                .HasColumnType("INT")
                .HasColumnName("kind_id");
            entity.Property(e => e.KnockDown)
                .HasColumnType("NUM")
                .HasColumnName("knock_down");
            entity.Property(e => e.KnockbackImmune)
                .HasColumnType("NUM")
                .HasColumnName("knockback_immune");
            entity.Property(e => e.LevelDuration)
                .HasColumnType("INT")
                .HasColumnName("level_duration");
            entity.Property(e => e.LinkBuffId)
                .HasColumnType("INT")
                .HasColumnName("link_buff_id");
            entity.Property(e => e.MainhandToolId)
                .HasColumnType("INT")
                .HasColumnName("mainhand_tool_id");
            entity.Property(e => e.ManaBurnImmune)
                .HasColumnType("NUM")
                .HasColumnName("mana_burn_immune");
            entity.Property(e => e.ManaShieldRatio)
                .HasColumnType("INT")
                .HasColumnName("mana_shield_ratio");
            entity.Property(e => e.MaxCharge)
                .HasColumnType("INT")
                .HasColumnName("max_charge");
            entity.Property(e => e.MaxStack)
                .HasColumnType("INT")
                .HasColumnName("max_stack");
            entity.Property(e => e.MeleeImmune)
                .HasColumnType("NUM")
                .HasColumnName("melee_immune");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.NameTr)
                .HasColumnType("NUM")
                .HasColumnName("name_tr");
            entity.Property(e => e.NoCollide)
                .HasColumnType("NUM")
                .HasColumnName("no_collide");
            entity.Property(e => e.NoCollideRigid)
                .HasColumnType("NUM")
                .HasColumnName("no_collide_rigid");
            entity.Property(e => e.NoExpPenalty)
                .HasColumnType("NUM")
                .HasColumnName("no_exp_penalty");
            entity.Property(e => e.NonPushable)
                .HasColumnType("NUM")
                .HasColumnName("non_pushable");
            entity.Property(e => e.OffhandToolId)
                .HasColumnType("INT")
                .HasColumnName("offhand_tool_id");
            entity.Property(e => e.OneTime)
                .HasColumnType("NUM")
                .HasColumnName("one_time");
            entity.Property(e => e.OwnerOnly)
                .HasColumnType("NUM")
                .HasColumnName("owner_only");
            entity.Property(e => e.Pacifist)
                .HasColumnType("NUM")
                .HasColumnName("pacifist");
            entity.Property(e => e.PerUnitCreation)
                .HasColumnType("NUM")
                .HasColumnName("per_unit_creation");
            entity.Property(e => e.PercussionInstrumentStartAnimId)
                .HasColumnType("INT")
                .HasColumnName("percussion_instrument_start_anim_id");
            entity.Property(e => e.PercussionInstrumentTickAnimId)
                .HasColumnType("INT")
                .HasColumnName("percussion_instrument_tick_anim_id");
            entity.Property(e => e.Psychokinesis)
                .HasColumnType("NUM")
                .HasColumnName("psychokinesis");
            entity.Property(e => e.PsychokinesisSpeed).HasColumnName("psychokinesis_speed");
            entity.Property(e => e.Ragdoll)
                .HasColumnType("NUM")
                .HasColumnName("ragdoll");
            entity.Property(e => e.RangedImmune)
                .HasColumnType("NUM")
                .HasColumnName("ranged_immune");
            entity.Property(e => e.RealTime)
                .HasColumnType("NUM")
                .HasColumnName("real_time");
            entity.Property(e => e.ReflectionChance)
                .HasColumnType("INT")
                .HasColumnName("reflection_chance");
            entity.Property(e => e.ReflectionRatio)
                .HasColumnType("INT")
                .HasColumnName("reflection_ratio");
            entity.Property(e => e.ReflectionTargetRatio)
                .HasColumnType("INT")
                .HasColumnName("reflection_target_ratio");
            entity.Property(e => e.ReflectionTypeId)
                .HasColumnType("INT")
                .HasColumnName("reflection_type_id");
            entity.Property(e => e.RemoveOnAttackBuffTrigger)
                .HasColumnType("NUM")
                .HasColumnName("remove_on_attack_buff_trigger");
            entity.Property(e => e.RemoveOnAttackEtc)
                .HasColumnType("NUM")
                .HasColumnName("remove_on_attack_etc");
            entity.Property(e => e.RemoveOnAttackEtcDot)
                .HasColumnType("NUM")
                .HasColumnName("remove_on_attack_etc_dot");
            entity.Property(e => e.RemoveOnAttackSpellDot)
                .HasColumnType("NUM")
                .HasColumnName("remove_on_attack_spell_dot");
            entity.Property(e => e.RemoveOnAttackedBuffTrigger)
                .HasColumnType("NUM")
                .HasColumnName("remove_on_attacked_buff_trigger");
            entity.Property(e => e.RemoveOnAttackedEtc)
                .HasColumnType("NUM")
                .HasColumnName("remove_on_attacked_etc");
            entity.Property(e => e.RemoveOnAttackedEtcDot)
                .HasColumnType("NUM")
                .HasColumnName("remove_on_attacked_etc_dot");
            entity.Property(e => e.RemoveOnAttackedSpellDot)
                .HasColumnType("NUM")
                .HasColumnName("remove_on_attacked_spell_dot");
            entity.Property(e => e.RemoveOnAutoattack)
                .HasColumnType("NUM")
                .HasColumnName("remove_on_autoattack");
            entity.Property(e => e.RemoveOnDamageBuffTrigger)
                .HasColumnType("NUM")
                .HasColumnName("remove_on_damage_buff_trigger");
            entity.Property(e => e.RemoveOnDamageEtc)
                .HasColumnType("NUM")
                .HasColumnName("remove_on_damage_etc");
            entity.Property(e => e.RemoveOnDamageEtcDot)
                .HasColumnType("NUM")
                .HasColumnName("remove_on_damage_etc_dot");
            entity.Property(e => e.RemoveOnDamageSpellDot)
                .HasColumnType("NUM")
                .HasColumnName("remove_on_damage_spell_dot");
            entity.Property(e => e.RemoveOnDamagedBuffTrigger)
                .HasColumnType("NUM")
                .HasColumnName("remove_on_damaged_buff_trigger");
            entity.Property(e => e.RemoveOnDamagedEtc)
                .HasColumnType("NUM")
                .HasColumnName("remove_on_damaged_etc");
            entity.Property(e => e.RemoveOnDamagedEtcDot)
                .HasColumnType("NUM")
                .HasColumnName("remove_on_damaged_etc_dot");
            entity.Property(e => e.RemoveOnDamagedSpellDot)
                .HasColumnType("NUM")
                .HasColumnName("remove_on_damaged_spell_dot");
            entity.Property(e => e.RemoveOnDeath)
                .HasColumnType("NUM")
                .HasColumnName("remove_on_death");
            entity.Property(e => e.RemoveOnExempt)
                .HasColumnType("NUM")
                .HasColumnName("remove_on_exempt");
            entity.Property(e => e.RemoveOnInteraction)
                .HasColumnType("NUM")
                .HasColumnName("remove_on_interaction");
            entity.Property(e => e.RemoveOnLand)
                .HasColumnType("NUM")
                .HasColumnName("remove_on_land");
            entity.Property(e => e.RemoveOnMount)
                .HasColumnType("NUM")
                .HasColumnName("remove_on_mount");
            entity.Property(e => e.RemoveOnMove)
                .HasColumnType("NUM")
                .HasColumnName("remove_on_move");
            entity.Property(e => e.RemoveOnSourceDead)
                .HasColumnType("NUM")
                .HasColumnName("remove_on_source_dead");
            entity.Property(e => e.RemoveOnStartSkill)
                .HasColumnType("NUM")
                .HasColumnName("remove_on_start_skill");
            entity.Property(e => e.RemoveOnUnmount)
                .HasColumnType("NUM")
                .HasColumnName("remove_on_unmount");
            entity.Property(e => e.RemoveOnUseSkill)
                .HasColumnType("NUM")
                .HasColumnName("remove_on_use_skill");
            entity.Property(e => e.RequireBuffId)
                .HasColumnType("INT")
                .HasColumnName("require_buff_id");
            entity.Property(e => e.ResurrectionHealth)
                .HasColumnType("INT")
                .HasColumnName("resurrection_health");
            entity.Property(e => e.ResurrectionMana)
                .HasColumnType("INT")
                .HasColumnName("resurrection_mana");
            entity.Property(e => e.ResurrectionPercent)
                .HasColumnType("NUM")
                .HasColumnName("resurrection_percent");
            entity.Property(e => e.Root)
                .HasColumnType("NUM")
                .HasColumnName("root");
            entity.Property(e => e.SaveRuleId)
                .HasColumnType("INT")
                .HasColumnName("save_rule_id");
            entity.Property(e => e.Scale).HasColumnName("scale");
            entity.Property(e => e.ScaleDuration).HasColumnName("scaleDuration");
            entity.Property(e => e.SiegeImmune)
                .HasColumnType("NUM")
                .HasColumnName("siege_immune");
            entity.Property(e => e.Silence)
                .HasColumnType("NUM")
                .HasColumnName("silence");
            entity.Property(e => e.SkillControllerId)
                .HasColumnType("INT")
                .HasColumnName("skill_controller_id");
            entity.Property(e => e.SlaveApplicable)
                .HasColumnType("NUM")
                .HasColumnName("slave_applicable");
            entity.Property(e => e.Sleep)
                .HasColumnType("NUM")
                .HasColumnName("sleep");
            entity.Property(e => e.SpellImmune)
                .HasColumnType("NUM")
                .HasColumnName("spell_immune");
            entity.Property(e => e.SprintMotion)
                .HasColumnType("NUM")
                .HasColumnName("sprint_motion");
            entity.Property(e => e.StackRuleId)
                .HasColumnType("INT")
                .HasColumnName("stack_rule_id");
            entity.Property(e => e.Stealth)
                .HasColumnType("NUM")
                .HasColumnName("stealth");
            entity.Property(e => e.StringInstrumentStartAnimId)
                .HasColumnType("INT")
                .HasColumnName("string_instrument_start_anim_id");
            entity.Property(e => e.StringInstrumentTickAnimId)
                .HasColumnType("INT")
                .HasColumnName("string_instrument_tick_anim_id");
            entity.Property(e => e.Stun)
                .HasColumnType("NUM")
                .HasColumnName("stun");
            entity.Property(e => e.System)
                .HasColumnType("NUM")
                .HasColumnName("system");
            entity.Property(e => e.Taunt)
                .HasColumnType("NUM")
                .HasColumnName("taunt");
            entity.Property(e => e.TauntWithTopAggro)
                .HasColumnType("NUM")
                .HasColumnName("taunt_with_top_aggro");
            entity.Property(e => e.TelescopeRange).HasColumnName("telescope_range");
            entity.Property(e => e.Tick)
                .HasColumnType("INT")
                .HasColumnName("tick");
            entity.Property(e => e.TickActiveWeaponId)
                .HasColumnType("INT")
                .HasColumnName("tick_active_weapon_id");
            entity.Property(e => e.TickAnimId)
                .HasColumnType("INT")
                .HasColumnName("tick_anim_id");
            entity.Property(e => e.TickAreaAngle)
                .HasColumnType("INT")
                .HasColumnName("tick_area_angle");
            entity.Property(e => e.TickAreaExcludeSource)
                .HasColumnType("NUM")
                .HasColumnName("tick_area_exclude_source");
            entity.Property(e => e.TickAreaFrontAngle)
                .HasColumnType("INT")
                .HasColumnName("tick_area_front_angle");
            entity.Property(e => e.TickAreaRadius).HasColumnName("tick_area_radius");
            entity.Property(e => e.TickAreaRelationId)
                .HasColumnType("INT")
                .HasColumnName("tick_area_relation_id");
            entity.Property(e => e.TickAreaUseOriginSource)
                .HasColumnType("NUM")
                .HasColumnName("tick_area_use_origin_source");
            entity.Property(e => e.TickLevelManaCost).HasColumnName("tick_level_mana_cost");
            entity.Property(e => e.TickMainhandToolId)
                .HasColumnType("INT")
                .HasColumnName("tick_mainhand_tool_id");
            entity.Property(e => e.TickManaCost)
                .HasColumnType("INT")
                .HasColumnName("tick_mana_cost");
            entity.Property(e => e.TickOffhandToolId)
                .HasColumnType("INT")
                .HasColumnName("tick_offhand_tool_id");
            entity.Property(e => e.TransferTelescopeRange).HasColumnName("transfer_telescope_range");
            entity.Property(e => e.TransformBuffId)
                .HasColumnType("INT")
                .HasColumnName("transform_buff_id");
            entity.Property(e => e.TubeInstrumentStartAnimId)
                .HasColumnType("INT")
                .HasColumnName("tube_instrument_start_anim_id");
            entity.Property(e => e.TubeInstrumentTickAnimId)
                .HasColumnType("INT")
                .HasColumnName("tube_instrument_tick_anim_id");
            entity.Property(e => e.UseSourceFaction)
                .HasColumnType("NUM")
                .HasColumnName("use_source_faction");
            entity.Property(e => e.WalkOnly)
                .HasColumnType("NUM")
                .HasColumnName("walk_only");
        });

        modelBuilder.Entity<BuffBreaker>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("buff_breakers");

            entity.Property(e => e.BuffId).HasColumnName("buff_id");
            entity.Property(e => e.BuffTagId).HasColumnName("buff_tag_id");
            entity.Property(e => e.Id).HasColumnName("id");
        });

        modelBuilder.Entity<BuffEffect>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("buff_effects");

            entity.Property(e => e.AbLevel)
                .HasColumnType("INT")
                .HasColumnName("ab_level");
            entity.Property(e => e.BuffId)
                .HasColumnType("INT")
                .HasColumnName("buff_id");
            entity.Property(e => e.Chance)
                .HasColumnType("INT")
                .HasColumnName("chance");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Stack)
                .HasColumnType("INT")
                .HasColumnName("stack");
        });

        modelBuilder.Entity<BuffModifier>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("buff_modifiers");

            entity.Property(e => e.BuffAttributeId)
                .HasColumnType("INT")
                .HasColumnName("buff_attribute_id");
            entity.Property(e => e.BuffId)
                .HasColumnType("INT")
                .HasColumnName("buff_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.OwnerId)
                .HasColumnType("INT")
                .HasColumnName("owner_id");
            entity.Property(e => e.OwnerType).HasColumnName("owner_type");
            entity.Property(e => e.Synergy)
                .HasColumnType("NUM")
                .HasColumnName("synergy");
            entity.Property(e => e.TagId)
                .HasColumnType("INT")
                .HasColumnName("tag_id");
            entity.Property(e => e.UnitModifierTypeId)
                .HasColumnType("INT")
                .HasColumnName("unit_modifier_type_id");
            entity.Property(e => e.Value)
                .HasColumnType("INT")
                .HasColumnName("value");
        });

        modelBuilder.Entity<BuffMountSkill>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("buff_mount_skills");

            entity.Property(e => e.BuffId)
                .HasColumnType("INT")
                .HasColumnName("buff_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.MountSkillId)
                .HasColumnType("INT")
                .HasColumnName("mount_skill_id");
        });

        modelBuilder.Entity<BuffSkill>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("buff_skills");

            entity.Property(e => e.BuffId)
                .HasColumnType("INT")
                .HasColumnName("buff_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.SkillId)
                .HasColumnType("INT")
                .HasColumnName("skill_id");
        });

        modelBuilder.Entity<BuffTickEffect>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("buff_tick_effects");

            entity.Property(e => e.BuffId)
                .HasColumnType("INT")
                .HasColumnName("buff_id");
            entity.Property(e => e.EffectId)
                .HasColumnType("INT")
                .HasColumnName("effect_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.TargetBuffTagId)
                .HasColumnType("INT")
                .HasColumnName("target_buff_tag_id");
            entity.Property(e => e.TargetNobuffTagId)
                .HasColumnType("INT")
                .HasColumnName("target_nobuff_tag_id");
        });

        modelBuilder.Entity<BuffTolerance>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("buff_tolerances");

            entity.Property(e => e.BuffTagId).HasColumnName("buff_tag_id");
            entity.Property(e => e.CharacterTimeReduction).HasColumnName("character_time_reduction");
            entity.Property(e => e.FinalStepBuffId).HasColumnName("final_step_buff_id");
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.StepDuration).HasColumnName("step_duration");
        });

        modelBuilder.Entity<BuffToleranceStep>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("buff_tolerance_steps");

            entity.Property(e => e.BuffToleranceId).HasColumnName("buff_tolerance_id");
            entity.Property(e => e.HitChance).HasColumnName("hit_chance");
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.TimeReduction).HasColumnName("time_reduction");
        });

        modelBuilder.Entity<BuffTrigger>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("buff_triggers");

            entity.Property(e => e.BuffId)
                .HasColumnType("INT")
                .HasColumnName("buff_id");
            entity.Property(e => e.EffectId)
                .HasColumnType("INT")
                .HasColumnName("effect_id");
            entity.Property(e => e.EffectOnSource)
                .HasColumnType("NUM")
                .HasColumnName("effect_on_source");
            entity.Property(e => e.EventId)
                .HasColumnType("INT")
                .HasColumnName("event_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Synergy)
                .HasColumnType("NUM")
                .HasColumnName("synergy");
            entity.Property(e => e.TargetBuffTagId)
                .HasColumnType("INT")
                .HasColumnName("target_buff_tag_id");
            entity.Property(e => e.TargetNoBuffTagId)
                .HasColumnType("INT")
                .HasColumnName("target_no_buff_tag_id");
            entity.Property(e => e.UseDamageAmount)
                .HasColumnType("NUM")
                .HasColumnName("use_damage_amount");
            entity.Property(e => e.UseOriginalSource)
                .HasColumnType("NUM")
                .HasColumnName("use_original_source");
        });

        modelBuilder.Entity<BuffUnitModifier>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("buff_unit_modifiers");

            entity.Property(e => e.BuffId)
                .HasColumnType("INT")
                .HasColumnName("buff_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.OwnerId)
                .HasColumnType("INT")
                .HasColumnName("owner_id");
            entity.Property(e => e.OwnerType).HasColumnName("owner_type");
            entity.Property(e => e.TagId)
                .HasColumnType("INT")
                .HasColumnName("tag_id");
        });

        modelBuilder.Entity<ChangeEquipmentBuff>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("change_equipment_buffs");

            entity.Property(e => e.BuffId)
                .HasColumnType("INT")
                .HasColumnName("buff_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.OwnerId)
                .HasColumnType("INT")
                .HasColumnName("owner_id");
            entity.Property(e => e.OwnerType).HasColumnName("owner_type");
        });

        modelBuilder.Entity<CharRecord>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("char_records");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.KindId)
                .HasColumnType("INT")
                .HasColumnName("kind_id");
            entity.Property(e => e.Value1)
                .HasColumnType("INT")
                .HasColumnName("value1");
            entity.Property(e => e.Value2)
                .HasColumnType("INT")
                .HasColumnName("value2");
        });

        modelBuilder.Entity<Character>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("characters");

            entity.Property(e => e.CharGenderId)
                .HasColumnType("INT")
                .HasColumnName("char_gender_id");
            entity.Property(e => e.CharRaceId)
                .HasColumnType("INT")
                .HasColumnName("char_race_id");
            entity.Property(e => e.Creatable)
                .HasColumnType("NUM")
                .HasColumnName("creatable");
            entity.Property(e => e.DefaultFxVoiceSoundPackId)
                .HasColumnType("INT")
                .HasColumnName("default_fx_voice_sound_pack_id");
            entity.Property(e => e.DefaultResurrectionDistrictId)
                .HasColumnType("INT")
                .HasColumnName("default_resurrection_district_id");
            entity.Property(e => e.DefaultReturnDistrictId)
                .HasColumnType("INT")
                .HasColumnName("default_return_district_id");
            entity.Property(e => e.DefaultSystemVoiceSoundPackId)
                .HasColumnType("INT")
                .HasColumnName("default_system_voice_sound_pack_id");
            entity.Property(e => e.FactionId)
                .HasColumnType("INT")
                .HasColumnName("faction_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.ModelId)
                .HasColumnType("INT")
                .HasColumnName("model_id");
            entity.Property(e => e.PreviewBodyPackId)
                .HasColumnType("INT")
                .HasColumnName("preview_body_pack_id");
            entity.Property(e => e.PreviewClothPackId)
                .HasColumnType("INT")
                .HasColumnName("preview_cloth_pack_id");
            entity.Property(e => e.StartingZoneId)
                .HasColumnType("INT")
                .HasColumnName("starting_zone_id");
        });

        modelBuilder.Entity<CharacterBuff>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("character_buffs");

            entity.Property(e => e.BuffId)
                .HasColumnType("INT")
                .HasColumnName("buff_id");
            entity.Property(e => e.CharacterId)
                .HasColumnType("INT")
                .HasColumnName("character_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
        });

        modelBuilder.Entity<CharacterCustomizingHairAsset>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("character_customizing_hair_assets");

            entity.Property(e => e.DisplayOrder)
                .HasColumnType("INT")
                .HasColumnName("display_order");
            entity.Property(e => e.HairId)
                .HasColumnType("INT")
                .HasColumnName("hair_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.IsHot)
                .HasColumnType("NUM")
                .HasColumnName("is_hot");
            entity.Property(e => e.IsNew)
                .HasColumnType("NUM")
                .HasColumnName("is_new");
            entity.Property(e => e.ModelId)
                .HasColumnType("INT")
                .HasColumnName("model_id");
        });

        modelBuilder.Entity<CharacterEquipPack>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("character_equip_packs");

            entity.Property(e => e.AbilityId)
                .HasColumnType("INT")
                .HasColumnName("ability_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.NewbieClothPackId)
                .HasColumnType("INT")
                .HasColumnName("newbie_cloth_pack_id");
            entity.Property(e => e.NewbieWeaponPackId)
                .HasColumnType("INT")
                .HasColumnName("newbie_weapon_pack_id");
        });

        modelBuilder.Entity<CharacterPStatLimit>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("character_p_stat_limits");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Max)
                .HasColumnType("INT")
                .HasColumnName("max");
            entity.Property(e => e.Min)
                .HasColumnType("INT")
                .HasColumnName("min");
            entity.Property(e => e.PStatId)
                .HasColumnType("INT")
                .HasColumnName("p_stat_id");
        });

        modelBuilder.Entity<CharacterSupply>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("character_supplies");

            entity.Property(e => e.AbilityId)
                .HasColumnType("INT")
                .HasColumnName("ability_id");
            entity.Property(e => e.Amount)
                .HasColumnType("INT")
                .HasColumnName("amount");
            entity.Property(e => e.GradeId)
                .HasColumnType("INT")
                .HasColumnName("grade_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.ItemId)
                .HasColumnType("INT")
                .HasColumnName("item_id");
        });

        modelBuilder.Entity<ChatCommand>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("chat_commands");

            entity.Property(e => e.ChatTypeId)
                .HasColumnType("INT")
                .HasColumnName("chat_type_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.MenuOrder)
                .HasColumnType("INT")
                .HasColumnName("menu_order");
            entity.Property(e => e.Name).HasColumnName("name");
        });

        modelBuilder.Entity<ChatSpamRule>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("chat_spam_rules");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name");
        });

        modelBuilder.Entity<ChatSpamRuleDetail>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("chat_spam_rule_details");

            entity.Property(e => e.ChatSpamRuleId)
                .HasColumnType("INT")
                .HasColumnName("chat_spam_rule_id");
            entity.Property(e => e.DetectedCaseNextDetailId)
                .HasColumnType("INT")
                .HasColumnName("detected_case_next_detail_id");
            entity.Property(e => e.EndNode)
                .HasColumnType("NUM")
                .HasColumnName("end_node");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.NotDetectedCaseNextDetailId)
                .HasColumnType("INT")
                .HasColumnName("not_detected_case_next_detail_id");
            entity.Property(e => e.StartNode)
                .HasColumnType("NUM")
                .HasColumnName("start_node");
            entity.Property(e => e.Text).HasColumnName("text");
        });

        modelBuilder.Entity<Cinema>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("cinemas");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.Replay)
                .HasColumnType("NUM")
                .HasColumnName("replay");
        });

        modelBuilder.Entity<CinemaCaption>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("cinema_captions");

            entity.Property(e => e.Caption).HasColumnName("caption");
            entity.Property(e => e.CinemaId)
                .HasColumnType("INT")
                .HasColumnName("cinema_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
        });

        modelBuilder.Entity<CinemaEffect>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("cinema_effects");

            entity.Property(e => e.CinemaId).HasColumnName("cinema_id");
            entity.Property(e => e.Id).HasColumnName("id");
        });

        modelBuilder.Entity<CinemaSubtitle>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("cinema_subtitles");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.Subtitle).HasColumnName("subtitle");
        });

        modelBuilder.Entity<CleanupUccEffect>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("cleanup_ucc_effects");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
        });

        modelBuilder.Entity<Climate>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("climates");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name");
        });

        modelBuilder.Entity<CombatBuff>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("combat_buffs");

            entity.Property(e => e.BuffFromSource).HasColumnName("buff_from_source");
            entity.Property(e => e.BuffId).HasColumnName("buff_id");
            entity.Property(e => e.BuffToSource).HasColumnName("buff_to_source");
            entity.Property(e => e.HitSkillId).HasColumnName("hit_skill_id");
            entity.Property(e => e.HitTypeId).HasColumnName("hit_type_id");
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.IsHealSpell).HasColumnName("is_heal_spell");
            entity.Property(e => e.ReqBuffId).HasColumnName("req_buff_id");
            entity.Property(e => e.ReqSkillId).HasColumnName("req_skill_id");
        });

        modelBuilder.Entity<CombatSound>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("combat_sounds");

            entity.Property(e => e.FxGroupId)
                .HasColumnType("INT")
                .HasColumnName("fx_group_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.SoundId)
                .HasColumnType("INT")
                .HasColumnName("sound_id");
            entity.Property(e => e.SourceSoundMaterialId)
                .HasColumnType("INT")
                .HasColumnName("source_sound_material_id");
            entity.Property(e => e.TargetSoundMaterialId)
                .HasColumnType("INT")
                .HasColumnName("target_sound_material_id");
        });

        modelBuilder.Entity<CommonFarm>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("common_farms");

            entity.Property(e => e.Comments).HasColumnName("comments");
            entity.Property(e => e.FarmGroupId)
                .HasColumnType("INT")
                .HasColumnName("farm_group_id");
            entity.Property(e => e.GuardTime)
                .HasColumnType("INT")
                .HasColumnName("guard_time");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name");
        });

        modelBuilder.Entity<ConflictZone>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("conflict_zones");

            entity.Property(e => e.Closed)
                .HasColumnType("NUM")
                .HasColumnName("closed");
            entity.Property(e => e.ConflictMin)
                .HasColumnType("INT")
                .HasColumnName("conflict_min");
            entity.Property(e => e.HariharaReturnPointId)
                .HasColumnType("INT")
                .HasColumnName("harihara_return_point_id");
            entity.Property(e => e.NoKillMin0)
                .HasColumnType("INT")
                .HasColumnName("no_kill_min_0");
            entity.Property(e => e.NoKillMin1)
                .HasColumnType("INT")
                .HasColumnName("no_kill_min_1");
            entity.Property(e => e.NoKillMin2)
                .HasColumnType("INT")
                .HasColumnName("no_kill_min_2");
            entity.Property(e => e.NoKillMin3)
                .HasColumnType("INT")
                .HasColumnName("no_kill_min_3");
            entity.Property(e => e.NoKillMin4)
                .HasColumnType("INT")
                .HasColumnName("no_kill_min_4");
            entity.Property(e => e.NuiaReturnPointId)
                .HasColumnType("INT")
                .HasColumnName("nuia_return_point_id");
            entity.Property(e => e.NumKills0)
                .HasColumnType("INT")
                .HasColumnName("num_kills_0");
            entity.Property(e => e.NumKills1)
                .HasColumnType("INT")
                .HasColumnName("num_kills_1");
            entity.Property(e => e.NumKills2)
                .HasColumnType("INT")
                .HasColumnName("num_kills_2");
            entity.Property(e => e.NumKills3)
                .HasColumnType("INT")
                .HasColumnName("num_kills_3");
            entity.Property(e => e.NumKills4)
                .HasColumnType("INT")
                .HasColumnName("num_kills_4");
            entity.Property(e => e.PeaceMin)
                .HasColumnType("INT")
                .HasColumnName("peace_min");
            entity.Property(e => e.PeaceProtectedFactionId)
                .HasColumnType("INT")
                .HasColumnName("peace_protected_faction_id");
            entity.Property(e => e.PeaceTowerDefId)
                .HasColumnType("INT")
                .HasColumnName("peace_tower_def_id");
            entity.Property(e => e.WarMin)
                .HasColumnType("INT")
                .HasColumnName("war_min");
            entity.Property(e => e.WarTowerDefId)
                .HasColumnType("INT")
                .HasColumnName("war_tower_def_id");
            entity.Property(e => e.ZoneGroupId)
                .HasColumnType("INT")
                .HasColumnName("zone_group_id");
        });

        modelBuilder.Entity<Constant>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("constants");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Value).HasColumnName("value");
        });

        modelBuilder.Entity<ContentConfig>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("content_configs");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.KindId)
                .HasColumnType("INT")
                .HasColumnName("kind_id");
            entity.Property(e => e.Value)
                .HasColumnType("INT")
                .HasColumnName("value");
        });

        modelBuilder.Entity<ConversionEffect>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("conversion_effects");

            entity.Property(e => e.CategoryId)
                .HasColumnType("INT")
                .HasColumnName("category_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.SourceCategoryId)
                .HasColumnType("INT")
                .HasColumnName("source_category_id");
            entity.Property(e => e.SourceValue)
                .HasColumnType("INT")
                .HasColumnName("source_value");
            entity.Property(e => e.TargetCategoryId)
                .HasColumnType("INT")
                .HasColumnName("target_category_id");
            entity.Property(e => e.TargetValue)
                .HasColumnType("INT")
                .HasColumnName("target_value");
        });

        modelBuilder.Entity<Craft>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("crafts");

            entity.Property(e => e.AcId)
                .HasColumnType("INT")
                .HasColumnName("ac_id");
            entity.Property(e => e.ActabilityLimit)
                .HasColumnType("INT")
                .HasColumnName("actability_limit");
            entity.Property(e => e.CastDelay)
                .HasColumnType("INT")
                .HasColumnName("cast_delay");
            entity.Property(e => e.Desc).HasColumnName("desc");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.MilestoneId)
                .HasColumnType("INT")
                .HasColumnName("milestone_id");
            entity.Property(e => e.NeedBind)
                .HasColumnType("NUM")
                .HasColumnName("need_bind");
            entity.Property(e => e.RecommendLevel)
                .HasColumnType("INT")
                .HasColumnName("recommend_level");
            entity.Property(e => e.ReqDoodadId)
                .HasColumnType("INT")
                .HasColumnName("req_doodad_id");
            entity.Property(e => e.ShowUpperCrafts)
                .HasColumnType("NUM")
                .HasColumnName("show_upper_crafts");
            entity.Property(e => e.SkillId)
                .HasColumnType("INT")
                .HasColumnName("skill_id");
            entity.Property(e => e.Title).HasColumnName("title");
            entity.Property(e => e.ToolId)
                .HasColumnType("INT")
                .HasColumnName("tool_id");
            entity.Property(e => e.Translate)
                .HasColumnType("NUM")
                .HasColumnName("translate");
            entity.Property(e => e.VisibleOrder)
                .HasColumnType("INT")
                .HasColumnName("visible_order");
            entity.Property(e => e.WiId)
                .HasColumnType("INT")
                .HasColumnName("wi_id");
        });

        modelBuilder.Entity<CraftEffect>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("craft_effects");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.WiId)
                .HasColumnType("INT")
                .HasColumnName("wi_id");
        });

        modelBuilder.Entity<CraftMaterial>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("craft_materials");

            entity.Property(e => e.Amount)
                .HasColumnType("INT")
                .HasColumnName("amount");
            entity.Property(e => e.CraftId)
                .HasColumnType("INT")
                .HasColumnName("craft_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.ItemId)
                .HasColumnType("INT")
                .HasColumnName("item_id");
            entity.Property(e => e.MainGrade)
                .HasColumnType("NUM")
                .HasColumnName("main_grade");
            entity.Property(e => e.RequireGrade)
                .HasColumnType("INT")
                .HasColumnName("require_grade");
        });

        modelBuilder.Entity<CraftPack>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("craft_packs");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name");
        });

        modelBuilder.Entity<CraftPackCraft>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("craft_pack_crafts");

            entity.Property(e => e.CraftId)
                .HasColumnType("INT")
                .HasColumnName("craft_id");
            entity.Property(e => e.CraftPackId)
                .HasColumnType("INT")
                .HasColumnName("craft_pack_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
        });

        modelBuilder.Entity<CraftProduct>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("craft_products");

            entity.Property(e => e.Amount)
                .HasColumnType("INT")
                .HasColumnName("amount");
            entity.Property(e => e.CraftId)
                .HasColumnType("INT")
                .HasColumnName("craft_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.ItemGradeId)
                .HasColumnType("INT")
                .HasColumnName("item_grade_id");
            entity.Property(e => e.ItemId)
                .HasColumnType("INT")
                .HasColumnName("item_id");
            entity.Property(e => e.Rate)
                .HasColumnType("INT")
                .HasColumnName("rate");
            entity.Property(e => e.ShowLowerCrafts)
                .HasColumnType("NUM")
                .HasColumnName("show_lower_crafts");
            entity.Property(e => e.UseGrade)
                .HasColumnType("NUM")
                .HasColumnName("use_grade");
        });

        modelBuilder.Entity<CurrencyConfig>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("currency_configs");

            entity.Property(e => e.CurrencyId)
                .HasColumnType("INT")
                .HasColumnName("currency_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.KindId)
                .HasColumnType("INT")
                .HasColumnName("kind_id");
        });

        modelBuilder.Entity<CustomDualMaterial>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("custom_dual_materials");

            entity.Property(e => e.Filename).HasColumnName("filename");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name");
        });

        modelBuilder.Entity<CustomFacePreset>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("custom_face_presets");

            entity.Property(e => e.FaceMorphTypeId)
                .HasColumnType("INT")
                .HasColumnName("face_morph_type_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.ModelId)
                .HasColumnType("INT")
                .HasColumnName("model_id");
            entity.Property(e => e.Modifier).HasColumnName("modifier");
            entity.Property(e => e.Name).HasColumnName("name");
        });

        modelBuilder.Entity<CustomFontColor>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("custom_font_colors");

            entity.Property(e => e.Color)
                .HasColumnType("INT")
                .HasColumnName("color");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name");
        });

        modelBuilder.Entity<CustomHairTexture>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("custom_hair_textures");

            entity.Property(e => e.DiffuseTexture).HasColumnName("diffuse_texture");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.NormalTexture).HasColumnName("normal_texture");
            entity.Property(e => e.SpecularTexture).HasColumnName("specular_texture");
        });

        modelBuilder.Entity<DamageEffect>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("damage_effects");

            entity.Property(e => e.ActabilityAdd).HasColumnName("actability_add");
            entity.Property(e => e.ActabilityGroupId)
                .HasColumnType("INT")
                .HasColumnName("actability_group_id");
            entity.Property(e => e.ActabilityMul).HasColumnName("actability_mul");
            entity.Property(e => e.ActabilityStep)
                .HasColumnType("INT")
                .HasColumnName("actability_step");
            entity.Property(e => e.AdjustDamageByHeight)
                .HasColumnType("NUM")
                .HasColumnName("adjust_damage_by_height");
            entity.Property(e => e.AggroMultiplier).HasColumnName("aggro_multiplier");
            entity.Property(e => e.ChargedBuffId)
                .HasColumnType("INT")
                .HasColumnName("charged_buff_id");
            entity.Property(e => e.ChargedLevelMul).HasColumnName("charged_level_mul");
            entity.Property(e => e.ChargedMul).HasColumnName("charged_mul");
            entity.Property(e => e.CheckCrime)
                .HasColumnType("NUM")
                .HasColumnName("check_crime");
            entity.Property(e => e.CriticalBonus)
                .HasColumnType("INT")
                .HasColumnName("critical_bonus");
            entity.Property(e => e.DamageTypeId)
                .HasColumnType("INT")
                .HasColumnName("damage_type_id");
            entity.Property(e => e.DpsIncMultiplier).HasColumnName("dps_inc_multiplier");
            entity.Property(e => e.DpsMultiplier).HasColumnName("dps_multiplier");
            entity.Property(e => e.EngageCombat)
                .HasColumnType("NUM")
                .HasColumnName("engage_combat");
            entity.Property(e => e.FireProc)
                .HasColumnType("NUM")
                .HasColumnName("fire_proc");
            entity.Property(e => e.FixedMax)
                .HasColumnType("INT")
                .HasColumnName("fixed_max");
            entity.Property(e => e.FixedMin)
                .HasColumnType("INT")
                .HasColumnName("fixed_min");
            entity.Property(e => e.HealthStealRatio)
                .HasColumnType("INT")
                .HasColumnName("health_steal_ratio");
            entity.Property(e => e.HitAnimTimingId)
                .HasColumnType("INT")
                .HasColumnName("hit_anim_timing_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.LevelMd).HasColumnName("level_md");
            entity.Property(e => e.LevelVaEnd)
                .HasColumnType("INT")
                .HasColumnName("level_va_end");
            entity.Property(e => e.LevelVaStart)
                .HasColumnType("INT")
                .HasColumnName("level_va_start");
            entity.Property(e => e.ManaStealRatio)
                .HasColumnType("INT")
                .HasColumnName("mana_steal_ratio");
            entity.Property(e => e.Multiplier).HasColumnName("multiplier");
            entity.Property(e => e.PercentMax)
                .HasColumnType("INT")
                .HasColumnName("percent_max");
            entity.Property(e => e.PercentMin)
                .HasColumnType("INT")
                .HasColumnName("percent_min");
            entity.Property(e => e.Synergy)
                .HasColumnType("NUM")
                .HasColumnName("synergy");
            entity.Property(e => e.TargetBuffBonus)
                .HasColumnType("INT")
                .HasColumnName("target_buff_bonus");
            entity.Property(e => e.TargetBuffBonusMul).HasColumnName("target_buff_bonus_mul");
            entity.Property(e => e.TargetBuffTagId)
                .HasColumnType("INT")
                .HasColumnName("target_buff_tag_id");
            entity.Property(e => e.TargetChargedBuffId)
                .HasColumnType("INT")
                .HasColumnName("target_charged_buff_id");
            entity.Property(e => e.TargetChargedMul).HasColumnName("target_charged_mul");
            entity.Property(e => e.TargetHealthAdd)
                .HasColumnType("INT")
                .HasColumnName("target_health_add");
            entity.Property(e => e.TargetHealthMax)
                .HasColumnType("INT")
                .HasColumnName("target_health_max");
            entity.Property(e => e.TargetHealthMin)
                .HasColumnType("INT")
                .HasColumnName("target_health_min");
            entity.Property(e => e.TargetHealthMul).HasColumnName("target_health_mul");
            entity.Property(e => e.UseChargedBuff)
                .HasColumnType("NUM")
                .HasColumnName("use_charged_buff");
            entity.Property(e => e.UseCurrentHealth)
                .HasColumnType("NUM")
                .HasColumnName("use_current_health");
            entity.Property(e => e.UseFixedDamage)
                .HasColumnType("NUM")
                .HasColumnName("use_fixed_damage");
            entity.Property(e => e.UseLevelDamage)
                .HasColumnType("NUM")
                .HasColumnName("use_level_damage");
            entity.Property(e => e.UseMainhandWeapon)
                .HasColumnType("NUM")
                .HasColumnName("use_mainhand_weapon");
            entity.Property(e => e.UseOffhandWeapon)
                .HasColumnType("NUM")
                .HasColumnName("use_offhand_weapon");
            entity.Property(e => e.UsePercentDamage)
                .HasColumnType("NUM")
                .HasColumnName("use_percent_damage");
            entity.Property(e => e.UseRangedWeapon)
                .HasColumnType("NUM")
                .HasColumnName("use_ranged_weapon");
            entity.Property(e => e.UseTargetChargedBuff)
                .HasColumnType("NUM")
                .HasColumnName("use_target_charged_buff");
            entity.Property(e => e.WeaponSlotId)
                .HasColumnType("INT")
                .HasColumnName("weapon_slot_id");
        });

        modelBuilder.Entity<DdcmsMergeProtectInfo>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("ddcms_merge_protect_infos");

            entity.Property(e => e.Action).HasColumnName("action");
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Idx).HasColumnName("idx");
            entity.Property(e => e.TblColumnName).HasColumnName("tbl_column_name");
            entity.Property(e => e.TblName).HasColumnName("tbl_name");
        });

        modelBuilder.Entity<DecoActabilityGroup>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("deco_actability_groups");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name");
        });

        modelBuilder.Entity<DefaultActionBarAction>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("default_action_bar_actions");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.ItemId).HasColumnName("item_id");
            entity.Property(e => e.SlotIndex).HasColumnName("slot_index");
        });

        modelBuilder.Entity<DefaultInventoryTab>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("default_inventory_tabs");

            entity.Property(e => e.IconIdx)
                .HasColumnType("INT")
                .HasColumnName("icon_idx");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.TabOrder)
                .HasColumnType("INT")
                .HasColumnName("tab_order");
        });

        modelBuilder.Entity<DefaultInventoryTabGroup>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("default_inventory_tab_groups");

            entity.Property(e => e.DefaultInventoryTabId)
                .HasColumnType("INT")
                .HasColumnName("default_inventory_tab_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.ItemGroupId)
                .HasColumnType("INT")
                .HasColumnName("item_group_id");
        });

        modelBuilder.Entity<DefaultSkill>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("default_skills");

            entity.Property(e => e.AddToSlot)
                .HasColumnType("NUM")
                .HasColumnName("add_to_slot");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.SkillBookCategoryId)
                .HasColumnType("INT")
                .HasColumnName("skill_book_category_id");
            entity.Property(e => e.SkillId)
                .HasColumnType("INT")
                .HasColumnName("skill_id");
            entity.Property(e => e.SlotIndex)
                .HasColumnType("INT")
                .HasColumnName("slot_index");
        });

        modelBuilder.Entity<Demo>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("demos");

            entity.Property(e => e.AddExp)
                .HasColumnType("NUM")
                .HasColumnName("add_exp");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name");
        });

        modelBuilder.Entity<DemoBag>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("demo_bags");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name");
        });

        modelBuilder.Entity<DemoBagItem>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("demo_bag_items");

            entity.Property(e => e.Count)
                .HasColumnType("INT")
                .HasColumnName("count");
            entity.Property(e => e.DemoBagId)
                .HasColumnType("INT")
                .HasColumnName("demo_bag_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.ItemId)
                .HasColumnType("INT")
                .HasColumnName("item_id");
        });

        modelBuilder.Entity<DemoChar>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("demo_chars");

            entity.Property(e => e.DemoBagId)
                .HasColumnType("INT")
                .HasColumnName("demo_bag_id");
            entity.Property(e => e.DemoEquipId)
                .HasColumnType("INT")
                .HasColumnName("demo_equip_id");
            entity.Property(e => e.DemoId)
                .HasColumnType("INT")
                .HasColumnName("demo_id");
            entity.Property(e => e.DemoStartLocId)
                .HasColumnType("INT")
                .HasColumnName("demo_start_loc_id");
            entity.Property(e => e.FactionId)
                .HasColumnType("INT")
                .HasColumnName("faction_id");
            entity.Property(e => e.GenderId)
                .HasColumnType("INT")
                .HasColumnName("gender_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.RaceId)
                .HasColumnType("INT")
                .HasColumnName("race_id");
        });

        modelBuilder.Entity<DemoEquip>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("demo_equips");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name");
        });

        modelBuilder.Entity<DemoEquipItem>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("demo_equip_items");

            entity.Property(e => e.DemoEquipId)
                .HasColumnType("INT")
                .HasColumnName("demo_equip_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.ItemId)
                .HasColumnType("INT")
                .HasColumnName("item_id");
        });

        modelBuilder.Entity<DemoLoc>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("demo_locs");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.X)
                .HasColumnType("INT")
                .HasColumnName("x");
            entity.Property(e => e.Y)
                .HasColumnType("INT")
                .HasColumnName("y");
            entity.Property(e => e.Z)
                .HasColumnType("INT")
                .HasColumnName("z");
            entity.Property(e => e.ZoneId)
                .HasColumnType("INT")
                .HasColumnName("zone_id");
        });

        modelBuilder.Entity<DispelEffect>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("dispel_effects");

            entity.Property(e => e.BuffTagId)
                .HasColumnType("INT")
                .HasColumnName("buff_tag_id");
            entity.Property(e => e.CureCount)
                .HasColumnType("INT")
                .HasColumnName("cure_count");
            entity.Property(e => e.DispelCount)
                .HasColumnType("INT")
                .HasColumnName("dispel_count");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
        });

        modelBuilder.Entity<District>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("districts");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.KindId)
                .HasColumnType("INT")
                .HasColumnName("kind_id");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.QuestCategoryId)
                .HasColumnType("INT")
                .HasColumnName("quest_category_id");
        });

        modelBuilder.Entity<DistrictReturnPoint>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("district_return_points");

            entity.Property(e => e.DistrictId)
                .HasColumnType("INT")
                .HasColumnName("district_id");
            entity.Property(e => e.FactionId)
                .HasColumnType("INT")
                .HasColumnName("faction_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.ReturnPointId)
                .HasColumnType("INT")
                .HasColumnName("return_point_id");
        });

        modelBuilder.Entity<DoodadAlmighty>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("doodad_almighties");

            entity.Property(e => e.Childable)
                .HasColumnType("NUM")
                .HasColumnName("childable");
            entity.Property(e => e.ClimateId)
                .HasColumnType("INT")
                .HasColumnName("climate_id");
            entity.Property(e => e.CollideShip)
                .HasColumnType("NUM")
                .HasColumnName("collide_ship");
            entity.Property(e => e.CollideVehicle)
                .HasColumnType("NUM")
                .HasColumnName("collide_vehicle");
            entity.Property(e => e.DespawnOnCollision)
                .HasColumnType("NUM")
                .HasColumnName("despawn_on_collision");
            entity.Property(e => e.FactionId)
                .HasColumnType("INT")
                .HasColumnName("faction_id");
            entity.Property(e => e.ForceTodTopPriority)
                .HasColumnType("NUM")
                .HasColumnName("force_tod_top_priority");
            entity.Property(e => e.ForceUpAction)
                .HasColumnType("NUM")
                .HasColumnName("force_up_action");
            entity.Property(e => e.GroupId)
                .HasColumnType("INT")
                .HasColumnName("group_id");
            entity.Property(e => e.GrowthTime)
                .HasColumnType("INT")
                .HasColumnName("growth_time");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.LoadModelFromWorld)
                .HasColumnType("NUM")
                .HasColumnName("load_model_from_world");
            entity.Property(e => e.MarkModel).HasColumnName("mark_model");
            entity.Property(e => e.MaxTime)
                .HasColumnType("INT")
                .HasColumnName("max_time");
            entity.Property(e => e.MgmtSpawn)
                .HasColumnType("NUM")
                .HasColumnName("mgmt_spawn");
            entity.Property(e => e.MilestoneId)
                .HasColumnType("INT")
                .HasColumnName("milestone_id");
            entity.Property(e => e.MinTime)
                .HasColumnType("INT")
                .HasColumnName("min_time");
            entity.Property(e => e.Model).HasColumnName("model");
            entity.Property(e => e.ModelKindId)
                .HasColumnType("INT")
                .HasColumnName("model_kind_id");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.NoCollision)
                .HasColumnType("NUM")
                .HasColumnName("no_collision");
            entity.Property(e => e.OnceOneInteraction)
                .HasColumnType("NUM")
                .HasColumnName("once_one_interaction");
            entity.Property(e => e.OnceOneMan)
                .HasColumnType("NUM")
                .HasColumnName("once_one_man");
            entity.Property(e => e.Parentable)
                .HasColumnType("NUM")
                .HasColumnName("parentable");
            entity.Property(e => e.Percent)
                .HasColumnType("INT")
                .HasColumnName("percent");
            entity.Property(e => e.RestrictZoneId)
                .HasColumnType("INT")
                .HasColumnName("restrict_zone_id");
            entity.Property(e => e.SaveIndun)
                .HasColumnType("NUM")
                .HasColumnName("save_indun");
            entity.Property(e => e.ShowMinimap)
                .HasColumnType("NUM")
                .HasColumnName("show_minimap");
            entity.Property(e => e.ShowName)
                .HasColumnType("NUM")
                .HasColumnName("show_name");
            entity.Property(e => e.SimRadius)
                .HasColumnType("INT")
                .HasColumnName("sim_radius");
            entity.Property(e => e.TargetDecalSize).HasColumnName("target_decal_size");
            entity.Property(e => e.Translate)
                .HasColumnType("NUM")
                .HasColumnName("translate");
            entity.Property(e => e.UseCreatorFaction)
                .HasColumnType("NUM")
                .HasColumnName("use_creator_faction");
            entity.Property(e => e.UseTargetDecal)
                .HasColumnType("NUM")
                .HasColumnName("use_target_decal");
            entity.Property(e => e.UseTargetHighlight)
                .HasColumnType("NUM")
                .HasColumnName("use_target_highlight");
            entity.Property(e => e.UseTargetSilhouette)
                .HasColumnType("NUM")
                .HasColumnName("use_target_silhouette");
        });

        modelBuilder.Entity<DoodadBundle>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("doodad_bundles");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name");
        });

        modelBuilder.Entity<DoodadBundleDoodad>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("doodad_bundle_doodads");

            entity.Property(e => e.DoodadBundleId)
                .HasColumnType("INT")
                .HasColumnName("doodad_bundle_id");
            entity.Property(e => e.DoodadId)
                .HasColumnType("INT")
                .HasColumnName("doodad_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
        });

        modelBuilder.Entity<DoodadFamily>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("doodad_families");

            entity.Property(e => e.Comment).HasColumnName("comment");
            entity.Property(e => e.FamilyId)
                .HasColumnType("INT")
                .HasColumnName("family_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
        });

        modelBuilder.Entity<DoodadFunc>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("doodad_funcs");

            entity.Property(e => e.ActCount)
                .HasColumnType("INT")
                .HasColumnName("act_count");
            entity.Property(e => e.ActualFuncId)
                .HasColumnType("INT")
                .HasColumnName("actual_func_id");
            entity.Property(e => e.ActualFuncType).HasColumnName("actual_func_type");
            entity.Property(e => e.DoodadFuncGroupId)
                .HasColumnType("INT")
                .HasColumnName("doodad_func_group_id");
            entity.Property(e => e.ForbidOnClimb)
                .HasColumnType("NUM")
                .HasColumnName("forbid_on_climb");
            entity.Property(e => e.FuncSkillId)
                .HasColumnType("INT")
                .HasColumnName("func_skill_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.NextPhase)
                .HasColumnType("INT")
                .HasColumnName("next_phase");
            entity.Property(e => e.PermId)
                .HasColumnType("INT")
                .HasColumnName("perm_id");
            entity.Property(e => e.PopupWarn)
                .HasColumnType("NUM")
                .HasColumnName("popup_warn");
            entity.Property(e => e.SoundId)
                .HasColumnType("INT")
                .HasColumnName("sound_id");
        });

        modelBuilder.Entity<DoodadFuncAnimate>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("doodad_func_animates");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.PlayOnce)
                .HasColumnType("NUM")
                .HasColumnName("play_once");
        });

        modelBuilder.Entity<DoodadFuncAreaTrigger>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("doodad_func_area_triggers");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.IsEnter)
                .HasColumnType("NUM")
                .HasColumnName("is_enter");
            entity.Property(e => e.NpcId)
                .HasColumnType("INT")
                .HasColumnName("npc_id");
        });

        modelBuilder.Entity<DoodadFuncAttachment>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("doodad_func_attachments");

            entity.Property(e => e.AttachPointId)
                .HasColumnType("INT")
                .HasColumnName("attach_point_id");
            entity.Property(e => e.BondKindId)
                .HasColumnType("INT")
                .HasColumnName("bond_kind_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Space)
                .HasColumnType("INT")
                .HasColumnName("space");
        });

        modelBuilder.Entity<DoodadFuncAuctionUi>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("doodad_func_auction_uis");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
        });

        modelBuilder.Entity<DoodadFuncBankUi>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("doodad_func_bank_uis");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
        });

        modelBuilder.Entity<DoodadFuncBinding>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("doodad_func_bindings");

            entity.Property(e => e.DistrictId)
                .HasColumnType("INT")
                .HasColumnName("district_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
        });

        modelBuilder.Entity<DoodadFuncBubble>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("doodad_func_bubbles");

            entity.Property(e => e.BubbleId)
                .HasColumnType("INT")
                .HasColumnName("bubble_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
        });

        modelBuilder.Entity<DoodadFuncBuff>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("doodad_func_buffs");

            entity.Property(e => e.BuffId)
                .HasColumnType("INT")
                .HasColumnName("buff_id");
            entity.Property(e => e.Count)
                .HasColumnType("INT")
                .HasColumnName("count");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.PermId)
                .HasColumnType("INT")
                .HasColumnName("perm_id");
            entity.Property(e => e.Radius).HasColumnName("radius");
            entity.Property(e => e.RelationshipId)
                .HasColumnType("INT")
                .HasColumnName("relationship_id");
        });

        modelBuilder.Entity<DoodadFuncButcher>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("doodad_func_butchers");

            entity.Property(e => e.CorpseModel).HasColumnName("corpse_model");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
        });

        modelBuilder.Entity<DoodadFuncBuyFish>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("doodad_func_buy_fishes");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.ItemId)
                .HasColumnType("INT")
                .HasColumnName("item_id");
        });

        modelBuilder.Entity<DoodadFuncBuyFishItem>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("doodad_func_buy_fish_items");

            entity.Property(e => e.DoodadFuncBuyFishId)
                .HasColumnType("INT")
                .HasColumnName("doodad_func_buy_fish_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.ItemId)
                .HasColumnType("INT")
                .HasColumnName("item_id");
        });

        modelBuilder.Entity<DoodadFuncBuyFishModel>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("doodad_func_buy_fish_models");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name");
        });

        modelBuilder.Entity<DoodadFuncBuyFishModelItem>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("doodad_func_buy_fish_model_items");

            entity.Property(e => e.DoodadFuncBuyFishModelId)
                .HasColumnType("INT")
                .HasColumnName("doodad_func_buy_fish_model_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.ItemId)
                .HasColumnType("INT")
                .HasColumnName("item_id");
            entity.Property(e => e.Model).HasColumnName("model");
        });

        modelBuilder.Entity<DoodadFuncCatch>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("doodad_func_catches");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
        });

        modelBuilder.Entity<DoodadFuncCerealHarvest>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("doodad_func_cereal_harvests");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
        });

        modelBuilder.Entity<DoodadFuncCleanupLogicLink>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("doodad_func_cleanup_logic_links");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
        });

        modelBuilder.Entity<DoodadFuncClimateReact>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("doodad_func_climate_reacts");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.NextPhase)
                .HasColumnType("INT")
                .HasColumnName("next_phase");
        });

        modelBuilder.Entity<DoodadFuncClimb>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("doodad_func_climbs");

            entity.Property(e => e.AllowHorizontalMultiHanger)
                .HasColumnType("NUM")
                .HasColumnName("allow_horizontal_multi_hanger");
            entity.Property(e => e.ClimbTypeId)
                .HasColumnType("INT")
                .HasColumnName("climb_type_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
        });

        modelBuilder.Entity<DoodadFuncClout>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("doodad_func_clouts");

            entity.Property(e => e.AoeShapeId)
                .HasColumnType("INT")
                .HasColumnName("aoe_shape_id");
            entity.Property(e => e.BuffId)
                .HasColumnType("INT")
                .HasColumnName("buff_id");
            entity.Property(e => e.Duration)
                .HasColumnType("INT")
                .HasColumnName("duration");
            entity.Property(e => e.FxGroupId)
                .HasColumnType("INT")
                .HasColumnName("fx_group_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.NextPhase)
                .HasColumnType("INT")
                .HasColumnName("next_phase");
            entity.Property(e => e.ProjectileId)
                .HasColumnType("INT")
                .HasColumnName("projectile_id");
            entity.Property(e => e.ShowToFriendlyOnly)
                .HasColumnType("NUM")
                .HasColumnName("show_to_friendly_only");
            entity.Property(e => e.TargetBuffTagId)
                .HasColumnType("INT")
                .HasColumnName("target_buff_tag_id");
            entity.Property(e => e.TargetNoBuffTagId)
                .HasColumnType("INT")
                .HasColumnName("target_no_buff_tag_id");
            entity.Property(e => e.TargetRelationId)
                .HasColumnType("INT")
                .HasColumnName("target_relation_id");
            entity.Property(e => e.Tick)
                .HasColumnType("INT")
                .HasColumnName("tick");
            entity.Property(e => e.UseOriginSource)
                .HasColumnType("NUM")
                .HasColumnName("use_origin_source");
        });

        modelBuilder.Entity<DoodadFuncCloutEffect>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("doodad_func_clout_effects");

            entity.Property(e => e.DoodadFuncCloutId)
                .HasColumnType("INT")
                .HasColumnName("doodad_func_clout_id");
            entity.Property(e => e.EffectId)
                .HasColumnType("INT")
                .HasColumnName("effect_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
        });

        modelBuilder.Entity<DoodadFuncCoffer>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("doodad_func_coffers");

            entity.Property(e => e.Capacity)
                .HasColumnType("INT")
                .HasColumnName("capacity");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
        });

        modelBuilder.Entity<DoodadFuncCofferPerm>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("doodad_func_coffer_perms");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
        });

        modelBuilder.Entity<DoodadFuncConditionalUse>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("doodad_func_conditional_uses");

            entity.Property(e => e.FakeSkillId)
                .HasColumnType("INT")
                .HasColumnName("fake_skill_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.ItemId)
                .HasColumnType("INT")
                .HasColumnName("item_id");
            entity.Property(e => e.ItemTriggerPhase)
                .HasColumnType("INT")
                .HasColumnName("item_trigger_phase");
            entity.Property(e => e.QuestId)
                .HasColumnType("INT")
                .HasColumnName("quest_id");
            entity.Property(e => e.QuestTriggerPhase)
                .HasColumnType("INT")
                .HasColumnName("quest_trigger_phase");
            entity.Property(e => e.SkillId)
                .HasColumnType("INT")
                .HasColumnName("skill_id");
        });

        modelBuilder.Entity<DoodadFuncConsumeChanger>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("doodad_func_consume_changers");

            entity.Property(e => e.Count)
                .HasColumnType("INT")
                .HasColumnName("count");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.SlotId)
                .HasColumnType("INT")
                .HasColumnName("slot_id");
        });

        modelBuilder.Entity<DoodadFuncConsumeChangerItem>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("doodad_func_consume_changer_items");

            entity.Property(e => e.DoodadFuncConsumeChangerId)
                .HasColumnType("INT")
                .HasColumnName("doodad_func_consume_changer_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.ItemId)
                .HasColumnType("INT")
                .HasColumnName("item_id");
        });

        modelBuilder.Entity<DoodadFuncConsumeChangerModel>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("doodad_func_consume_changer_models");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name");
        });

        modelBuilder.Entity<DoodadFuncConsumeChangerModelItem>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("doodad_func_consume_changer_model_items");

            entity.Property(e => e.DoodadFuncConsumeChangerModelId)
                .HasColumnType("INT")
                .HasColumnName("doodad_func_consume_changer_model_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.ItemId)
                .HasColumnType("INT")
                .HasColumnName("item_id");
            entity.Property(e => e.Model).HasColumnName("model");
        });

        modelBuilder.Entity<DoodadFuncConsumeItem>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("doodad_func_consume_items");

            entity.Property(e => e.Count)
                .HasColumnType("INT")
                .HasColumnName("count");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.ItemId)
                .HasColumnType("INT")
                .HasColumnName("item_id");
        });

        modelBuilder.Entity<DoodadFuncConvertFish>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("doodad_func_convert_fishes");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
        });

        modelBuilder.Entity<DoodadFuncConvertFishItem>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("doodad_func_convert_fish_items");

            entity.Property(e => e.DoodadFuncConvertFishId)
                .HasColumnType("INT")
                .HasColumnName("doodad_func_convert_fish_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.ItemId)
                .HasColumnType("INT")
                .HasColumnName("item_id");
            entity.Property(e => e.LootPackId)
                .HasColumnType("INT")
                .HasColumnName("loot_pack_id");
        });

        modelBuilder.Entity<DoodadFuncCraftAct>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("doodad_func_craft_acts");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Model20).HasColumnName("model20");
            entity.Property(e => e.Model40).HasColumnName("model40");
            entity.Property(e => e.Model60).HasColumnName("model60");
            entity.Property(e => e.Model80).HasColumnName("model80");
        });

        modelBuilder.Entity<DoodadFuncCraftCancel>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("doodad_func_craft_cancels");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
        });

        modelBuilder.Entity<DoodadFuncCraftDirect>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("doodad_func_craft_directs");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.NextPhase)
                .HasColumnType("INT")
                .HasColumnName("next_phase");
        });

        modelBuilder.Entity<DoodadFuncCraftGetItem>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("doodad_func_craft_get_items");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
        });

        modelBuilder.Entity<DoodadFuncCraftGradeRatio>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("doodad_func_craft_grade_ratios");

            entity.Property(e => e.GradeId)
                .HasColumnType("INT")
                .HasColumnName("grade_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Ratio)
                .HasColumnType("INT")
                .HasColumnName("ratio");
        });

        modelBuilder.Entity<DoodadFuncCraftInfo>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("doodad_func_craft_infos");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
        });

        modelBuilder.Entity<DoodadFuncCraftPack>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("doodad_func_craft_packs");

            entity.Property(e => e.CraftPackId)
                .HasColumnType("INT")
                .HasColumnName("craft_pack_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
        });

        modelBuilder.Entity<DoodadFuncCraftStart>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("doodad_func_craft_starts");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name");
        });

        modelBuilder.Entity<DoodadFuncCraftStartCraft>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("doodad_func_craft_start_crafts");

            entity.Property(e => e.CraftId)
                .HasColumnType("INT")
                .HasColumnName("craft_id");
            entity.Property(e => e.DoodadFuncCraftStartId)
                .HasColumnType("INT")
                .HasColumnName("doodad_func_craft_start_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
        });

        modelBuilder.Entity<DoodadFuncCropHarvest>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("doodad_func_crop_harvests");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
        });

        modelBuilder.Entity<DoodadFuncCrystalCollect>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("doodad_func_crystal_collects");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
        });

        modelBuilder.Entity<DoodadFuncCutdown>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("doodad_func_cutdowns");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
        });

        modelBuilder.Entity<DoodadFuncCutdowning>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("doodad_func_cutdownings");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
        });

        modelBuilder.Entity<DoodadFuncDairyCollect>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("doodad_func_dairy_collects");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
        });

        modelBuilder.Entity<DoodadFuncDeclareSiege>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("doodad_func_declare_sieges");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
        });

        modelBuilder.Entity<DoodadFuncDig>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("doodad_func_digs");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
        });

        modelBuilder.Entity<DoodadFuncDigTerrain>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("doodad_func_dig_terrains");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Life)
                .HasColumnType("INT")
                .HasColumnName("life");
            entity.Property(e => e.Radius)
                .HasColumnType("INT")
                .HasColumnName("radius");
        });

        modelBuilder.Entity<DoodadFuncDyeingredientCollect>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("doodad_func_dyeingredient_collects");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
        });

        modelBuilder.Entity<DoodadFuncEnterInstance>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("doodad_func_enter_instances");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.ItemId)
                .HasColumnType("INT")
                .HasColumnName("item_id");
            entity.Property(e => e.ZoneId)
                .HasColumnType("INT")
                .HasColumnName("zone_id");
        });

        modelBuilder.Entity<DoodadFuncEnterSysInstance>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("doodad_func_enter_sys_instances");

            entity.Property(e => e.FactionId)
                .HasColumnType("INT")
                .HasColumnName("faction_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Selective)
                .HasColumnType("NUM")
                .HasColumnName("selective");
            entity.Property(e => e.ZoneId)
                .HasColumnType("INT")
                .HasColumnName("zone_id");
        });

        modelBuilder.Entity<DoodadFuncEvidenceItemLoot>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("doodad_func_evidence_item_loots");

            entity.Property(e => e.CrimeKindId)
                .HasColumnType("INT")
                .HasColumnName("crime_kind_id");
            entity.Property(e => e.CrimeValue)
                .HasColumnType("INT")
                .HasColumnName("crime_value");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.SkillId)
                .HasColumnType("INT")
                .HasColumnName("skill_id");
        });

        modelBuilder.Entity<DoodadFuncExchange>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("doodad_func_exchanges");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
        });

        modelBuilder.Entity<DoodadFuncExchangeItem>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("doodad_func_exchange_items");

            entity.Property(e => e.DoodadFuncExchangeId)
                .HasColumnType("INT")
                .HasColumnName("doodad_func_exchange_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.ItemId)
                .HasColumnType("INT")
                .HasColumnName("item_id");
            entity.Property(e => e.LootPackId)
                .HasColumnType("INT")
                .HasColumnName("loot_pack_id");
        });

        modelBuilder.Entity<DoodadFuncExitIndun>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("doodad_func_exit_induns");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.ReturnPointId)
                .HasColumnType("INT")
                .HasColumnName("return_point_id");
        });

        modelBuilder.Entity<DoodadFuncFakeUse>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("doodad_func_fake_uses");

            entity.Property(e => e.FakeSkillId)
                .HasColumnType("INT")
                .HasColumnName("fake_skill_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.SkillId)
                .HasColumnType("INT")
                .HasColumnName("skill_id");
            entity.Property(e => e.TargetParent)
                .HasColumnType("NUM")
                .HasColumnName("target_parent");
        });

        modelBuilder.Entity<DoodadFuncFeed>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("doodad_func_feeds");

            entity.Property(e => e.Count)
                .HasColumnType("INT")
                .HasColumnName("count");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.ItemId)
                .HasColumnType("INT")
                .HasColumnName("item_id");
        });

        modelBuilder.Entity<DoodadFuncFiberCollect>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("doodad_func_fiber_collects");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
        });

        modelBuilder.Entity<DoodadFuncFinal>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("doodad_func_finals");

            entity.Property(e => e.After)
                .HasColumnType("INT")
                .HasColumnName("after");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.MaxTime)
                .HasColumnType("INT")
                .HasColumnName("max_time");
            entity.Property(e => e.MinTime)
                .HasColumnType("INT")
                .HasColumnName("min_time");
            entity.Property(e => e.Respawn)
                .HasColumnType("NUM")
                .HasColumnName("respawn");
            entity.Property(e => e.ShowEndTime)
                .HasColumnType("NUM")
                .HasColumnName("show_end_time");
            entity.Property(e => e.ShowTip)
                .HasColumnType("NUM")
                .HasColumnName("show_tip");
            entity.Property(e => e.Tip).HasColumnName("tip");
        });

        modelBuilder.Entity<DoodadFuncFishSchool>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("doodad_func_fish_schools");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.NpcSpawnerId)
                .HasColumnType("INT")
                .HasColumnName("npc_spawner_id");
        });

        modelBuilder.Entity<DoodadFuncFruitPick>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("doodad_func_fruit_picks");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
        });

        modelBuilder.Entity<DoodadFuncGassExtract>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("doodad_func_gass_extracts");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
        });

        modelBuilder.Entity<DoodadFuncGroup>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("doodad_func_groups");

            entity.Property(e => e.Comment).HasColumnName("comment");
            entity.Property(e => e.DoodadAlmightyId)
                .HasColumnType("INT")
                .HasColumnName("doodad_almighty_id");
            entity.Property(e => e.DoodadFuncGroupKindId)
                .HasColumnType("INT")
                .HasColumnName("doodad_func_group_kind_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.IsMsgToZone)
                .HasColumnType("NUM")
                .HasColumnName("is_msg_to_zone");
            entity.Property(e => e.Model).HasColumnName("model");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.PhaseMsg).HasColumnName("phase_msg");
            entity.Property(e => e.SoundId)
                .HasColumnType("INT")
                .HasColumnName("sound_id");
            entity.Property(e => e.SoundTime)
                .HasColumnType("INT")
                .HasColumnName("sound_time");
        });

        modelBuilder.Entity<DoodadFuncGrowth>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("doodad_func_growths");

            entity.Property(e => e.Delay)
                .HasColumnType("INT")
                .HasColumnName("delay");
            entity.Property(e => e.EndScale)
                .HasColumnType("INT")
                .HasColumnName("end_scale");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.NextPhase)
                .HasColumnType("INT")
                .HasColumnName("next_phase");
            entity.Property(e => e.StartScale)
                .HasColumnType("INT")
                .HasColumnName("start_scale");
        });

        modelBuilder.Entity<DoodadFuncHarvest>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("doodad_func_harvests");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
        });

        modelBuilder.Entity<DoodadFuncHouseFarm>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("doodad_func_house_farms");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.ItemCategoryId)
                .HasColumnType("INT")
                .HasColumnName("item_category_id");
        });

        modelBuilder.Entity<DoodadFuncHousingArea>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("doodad_func_housing_areas");

            entity.Property(e => e.FactionId)
                .HasColumnType("INT")
                .HasColumnName("faction_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Radius)
                .HasColumnType("INT")
                .HasColumnName("radius");
        });

        modelBuilder.Entity<DoodadFuncHunger>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("doodad_func_hungers");

            entity.Property(e => e.FullStep)
                .HasColumnType("INT")
                .HasColumnName("full_step");
            entity.Property(e => e.HungryTerm)
                .HasColumnType("INT")
                .HasColumnName("hungry_term");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.NextPhase)
                .HasColumnType("INT")
                .HasColumnName("next_phase");
            entity.Property(e => e.PhaseChangeLimit)
                .HasColumnType("INT")
                .HasColumnName("phase_change_limit");
        });

        modelBuilder.Entity<DoodadFuncInsertCounter>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("doodad_func_insert_counters");

            entity.Property(e => e.Count)
                .HasColumnType("INT")
                .HasColumnName("count");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.ItemCount)
                .HasColumnType("INT")
                .HasColumnName("item_count");
            entity.Property(e => e.ItemId)
                .HasColumnType("INT")
                .HasColumnName("item_id");
        });

        modelBuilder.Entity<DoodadFuncLivestockGrowth>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("doodad_func_livestock_growths");

            entity.Property(e => e.FullStep)
                .HasColumnType("INT")
                .HasColumnName("full_step");
            entity.Property(e => e.Hungry)
                .HasColumnType("INT")
                .HasColumnName("hungry");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.NeedFeed)
                .HasColumnType("NUM")
                .HasColumnName("need_feed");
            entity.Property(e => e.StepOneModel).HasColumnName("step_one_model");
            entity.Property(e => e.StepOneTime)
                .HasColumnType("INT")
                .HasColumnName("step_one_time");
            entity.Property(e => e.StepThreeModel).HasColumnName("step_three_model");
            entity.Property(e => e.StepTwoModel).HasColumnName("step_two_model");
            entity.Property(e => e.StepTwoTime)
                .HasColumnType("INT")
                .HasColumnName("step_two_time");
        });

        modelBuilder.Entity<DoodadFuncLogic>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("doodad_func_logics");

            entity.Property(e => e.DelayId)
                .HasColumnType("INT")
                .HasColumnName("delay_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.OperationId)
                .HasColumnType("INT")
                .HasColumnName("operation_id");
        });

        modelBuilder.Entity<DoodadFuncLogicDisplay>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("doodad_func_logic_displays");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
        });

        modelBuilder.Entity<DoodadFuncLogicFamilyProvider>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("doodad_func_logic_family_providers");

            entity.Property(e => e.FamilyId)
                .HasColumnType("INT")
                .HasColumnName("family_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
        });

        modelBuilder.Entity<DoodadFuncLogicFamilySubscriber>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("doodad_func_logic_family_subscribers");

            entity.Property(e => e.FamilyId)
                .HasColumnType("INT")
                .HasColumnName("family_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
        });

        modelBuilder.Entity<DoodadFuncLootItem>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("doodad_func_loot_items");

            entity.Property(e => e.CountMax)
                .HasColumnType("INT")
                .HasColumnName("count_max");
            entity.Property(e => e.CountMin)
                .HasColumnType("INT")
                .HasColumnName("count_min");
            entity.Property(e => e.GroupId)
                .HasColumnType("INT")
                .HasColumnName("group_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.ItemId)
                .HasColumnType("INT")
                .HasColumnName("item_id");
            entity.Property(e => e.Percent)
                .HasColumnType("INT")
                .HasColumnName("percent");
            entity.Property(e => e.RemainTime)
                .HasColumnType("INT")
                .HasColumnName("remain_time");
            entity.Property(e => e.WiId)
                .HasColumnType("INT")
                .HasColumnName("wi_id");
        });

        modelBuilder.Entity<DoodadFuncLootPack>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("doodad_func_loot_packs");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.LootPackId)
                .HasColumnType("INT")
                .HasColumnName("loot_pack_id");
        });

        modelBuilder.Entity<DoodadFuncMachinePartsCollect>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("doodad_func_machine_parts_collects");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
        });

        modelBuilder.Entity<DoodadFuncMedicalingredientMine>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("doodad_func_medicalingredient_mines");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
        });

        modelBuilder.Entity<DoodadFuncMould>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("doodad_func_moulds");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
        });

        modelBuilder.Entity<DoodadFuncMouldItem>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("doodad_func_mould_items");

            entity.Property(e => e.DoodadFuncMouldId)
                .HasColumnType("INT")
                .HasColumnName("doodad_func_mould_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.MouldPackId)
                .HasColumnType("INT")
                .HasColumnName("mould_pack_id");
        });

        modelBuilder.Entity<DoodadFuncMow>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("doodad_func_mows");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
        });

        modelBuilder.Entity<DoodadFuncNaviDonation>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("doodad_func_navi_donations");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
        });

        modelBuilder.Entity<DoodadFuncNaviMarkPosToMap>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("doodad_func_navi_mark_pos_to_maps");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.X)
                .HasColumnType("INT")
                .HasColumnName("x");
            entity.Property(e => e.Y)
                .HasColumnType("INT")
                .HasColumnName("y");
            entity.Property(e => e.Z)
                .HasColumnType("INT")
                .HasColumnName("z");
        });

        modelBuilder.Entity<DoodadFuncNaviNaming>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("doodad_func_navi_namings");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
        });

        modelBuilder.Entity<DoodadFuncNaviOpenBounty>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("doodad_func_navi_open_bounties");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
        });

        modelBuilder.Entity<DoodadFuncNaviOpenMailbox>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("doodad_func_navi_open_mailboxes");

            entity.Property(e => e.Duration)
                .HasColumnType("INT")
                .HasColumnName("duration");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
        });

        modelBuilder.Entity<DoodadFuncNaviOpenPortal>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("doodad_func_navi_open_portals");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
        });

        modelBuilder.Entity<DoodadFuncNaviRemove>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("doodad_func_navi_removes");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.ReqLp)
                .HasColumnType("INT")
                .HasColumnName("req_lp");
        });

        modelBuilder.Entity<DoodadFuncNaviRemoveTimer>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("doodad_func_navi_remove_timers");

            entity.Property(e => e.After)
                .HasColumnType("INT")
                .HasColumnName("after");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
        });

        modelBuilder.Entity<DoodadFuncNaviTeleport>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("doodad_func_navi_teleports");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
        });

        modelBuilder.Entity<DoodadFuncOpenFarmInfo>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("doodad_func_open_farm_infos");

            entity.Property(e => e.FarmId)
                .HasColumnType("INT")
                .HasColumnName("farm_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
        });

        modelBuilder.Entity<DoodadFuncOpenPaper>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("doodad_func_open_papers");

            entity.Property(e => e.BookId)
                .HasColumnType("INT")
                .HasColumnName("book_id");
            entity.Property(e => e.BookPageId)
                .HasColumnType("INT")
                .HasColumnName("book_page_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
        });

        modelBuilder.Entity<DoodadFuncOreMine>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("doodad_func_ore_mines");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
        });

        modelBuilder.Entity<DoodadFuncParentInfo>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("doodad_func_parent_infos");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
        });

        modelBuilder.Entity<DoodadFuncParrot>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("doodad_func_parrots");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
        });

        modelBuilder.Entity<DoodadFuncPlantCollect>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("doodad_func_plant_collects");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
        });

        modelBuilder.Entity<DoodadFuncPlayFlowGraph>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("doodad_func_play_flow_graphs");

            entity.Property(e => e.EventOnPhaseChangeId)
                .HasColumnType("INT")
                .HasColumnName("event_on_phase_change_id");
            entity.Property(e => e.EventOnVisibleId)
                .HasColumnType("INT")
                .HasColumnName("event_on_visible_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
        });

        modelBuilder.Entity<DoodadFuncPulse>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("doodad_func_pulses");

            entity.Property(e => e.Flag)
                .HasColumnType("NUM")
                .HasColumnName("flag");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
        });

        modelBuilder.Entity<DoodadFuncPulseTrigger>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("doodad_func_pulse_triggers");

            entity.Property(e => e.Flag)
                .HasColumnType("NUM")
                .HasColumnName("flag");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.NextPhase)
                .HasColumnType("INT")
                .HasColumnName("next_phase");
        });

        modelBuilder.Entity<DoodadFuncPurchase>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("doodad_func_purchases");

            entity.Property(e => e.CoinCount)
                .HasColumnType("INT")
                .HasColumnName("coin_count");
            entity.Property(e => e.CoinItemId)
                .HasColumnType("INT")
                .HasColumnName("coin_item_id");
            entity.Property(e => e.Count)
                .HasColumnType("INT")
                .HasColumnName("count");
            entity.Property(e => e.CurrencyId)
                .HasColumnType("INT")
                .HasColumnName("currency_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.ItemId)
                .HasColumnType("INT")
                .HasColumnName("item_id");
        });

        modelBuilder.Entity<DoodadFuncPurchaseSiegeTicket>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("doodad_func_purchase_siege_tickets");

            entity.Property(e => e.AdditionalCost0)
                .HasColumnType("INT")
                .HasColumnName("additional_cost0");
            entity.Property(e => e.AdditionalCost1)
                .HasColumnType("INT")
                .HasColumnName("additional_cost1");
            entity.Property(e => e.AdditionalCost10)
                .HasColumnType("INT")
                .HasColumnName("additional_cost10");
            entity.Property(e => e.AdditionalCost11)
                .HasColumnType("INT")
                .HasColumnName("additional_cost11");
            entity.Property(e => e.AdditionalCost12)
                .HasColumnType("INT")
                .HasColumnName("additional_cost12");
            entity.Property(e => e.AdditionalCost13)
                .HasColumnType("INT")
                .HasColumnName("additional_cost13");
            entity.Property(e => e.AdditionalCost14)
                .HasColumnType("INT")
                .HasColumnName("additional_cost14");
            entity.Property(e => e.AdditionalCost15)
                .HasColumnType("INT")
                .HasColumnName("additional_cost15");
            entity.Property(e => e.AdditionalCost16)
                .HasColumnType("INT")
                .HasColumnName("additional_cost16");
            entity.Property(e => e.AdditionalCost17)
                .HasColumnType("INT")
                .HasColumnName("additional_cost17");
            entity.Property(e => e.AdditionalCost18)
                .HasColumnType("INT")
                .HasColumnName("additional_cost18");
            entity.Property(e => e.AdditionalCost19)
                .HasColumnType("INT")
                .HasColumnName("additional_cost19");
            entity.Property(e => e.AdditionalCost2)
                .HasColumnType("INT")
                .HasColumnName("additional_cost2");
            entity.Property(e => e.AdditionalCost3)
                .HasColumnType("INT")
                .HasColumnName("additional_cost3");
            entity.Property(e => e.AdditionalCost4)
                .HasColumnType("INT")
                .HasColumnName("additional_cost4");
            entity.Property(e => e.AdditionalCost5)
                .HasColumnType("INT")
                .HasColumnName("additional_cost5");
            entity.Property(e => e.AdditionalCost6)
                .HasColumnType("INT")
                .HasColumnName("additional_cost6");
            entity.Property(e => e.AdditionalCost7)
                .HasColumnType("INT")
                .HasColumnName("additional_cost7");
            entity.Property(e => e.AdditionalCost8)
                .HasColumnType("INT")
                .HasColumnName("additional_cost8");
            entity.Property(e => e.AdditionalCost9)
                .HasColumnType("INT")
                .HasColumnName("additional_cost9");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Stack)
                .HasColumnType("INT")
                .HasColumnName("stack");
        });

        modelBuilder.Entity<DoodadFuncPuzzleIn>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("doodad_func_puzzle_ins");

            entity.Property(e => e.GroupId)
                .HasColumnType("INT")
                .HasColumnName("group_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Model).HasColumnName("model");
            entity.Property(e => e.Ratio).HasColumnName("ratio");
        });

        modelBuilder.Entity<DoodadFuncPuzzleOut>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("doodad_func_puzzle_outs");

            entity.Property(e => e.Anim).HasColumnName("anim");
            entity.Property(e => e.Delay)
                .HasColumnType("INT")
                .HasColumnName("delay");
            entity.Property(e => e.GroupId)
                .HasColumnType("INT")
                .HasColumnName("group_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.LootPackId)
                .HasColumnType("INT")
                .HasColumnName("loot_pack_id");
            entity.Property(e => e.NextPhase)
                .HasColumnType("INT")
                .HasColumnName("next_phase");
            entity.Property(e => e.ProjectileDelay)
                .HasColumnType("INT")
                .HasColumnName("projectile_delay");
            entity.Property(e => e.ProjectileId)
                .HasColumnType("INT")
                .HasColumnName("projectile_id");
            entity.Property(e => e.Ratio).HasColumnName("ratio");
            entity.Property(e => e.SoundId)
                .HasColumnType("INT")
                .HasColumnName("sound_id");
        });

        modelBuilder.Entity<DoodadFuncPuzzleRoll>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("doodad_func_puzzle_rolls");

            entity.Property(e => e.Count)
                .HasColumnType("INT")
                .HasColumnName("count");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.ItemId)
                .HasColumnType("INT")
                .HasColumnName("item_id");
        });

        modelBuilder.Entity<DoodadFuncQuest>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("doodad_func_quests");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.QuestId)
                .HasColumnType("INT")
                .HasColumnName("quest_id");
            entity.Property(e => e.QuestKindId)
                .HasColumnType("INT")
                .HasColumnName("quest_kind_id");
        });

        modelBuilder.Entity<DoodadFuncRatioChange>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("doodad_func_ratio_changes");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.NextPhase)
                .HasColumnType("INT")
                .HasColumnName("next_phase");
            entity.Property(e => e.Ratio)
                .HasColumnType("INT")
                .HasColumnName("ratio");
        });

        modelBuilder.Entity<DoodadFuncRatioRespawn>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("doodad_func_ratio_respawns");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Ratio)
                .HasColumnType("INT")
                .HasColumnName("ratio");
            entity.Property(e => e.SpawnDoodadId)
                .HasColumnType("INT")
                .HasColumnName("spawn_doodad_id");
        });

        modelBuilder.Entity<DoodadFuncRecoverItem>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("doodad_func_recover_items");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
        });

        modelBuilder.Entity<DoodadFuncRemoveInstance>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("doodad_func_remove_instances");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.ZoneId)
                .HasColumnType("INT")
                .HasColumnName("zone_id");
        });

        modelBuilder.Entity<DoodadFuncRemoveItem>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("doodad_func_remove_items");

            entity.Property(e => e.Count)
                .HasColumnType("INT")
                .HasColumnName("count");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.ItemId)
                .HasColumnType("INT")
                .HasColumnName("item_id");
        });

        modelBuilder.Entity<DoodadFuncRenewItem>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("doodad_func_renew_items");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.SkillId)
                .HasColumnType("INT")
                .HasColumnName("skill_id");
        });

        modelBuilder.Entity<DoodadFuncReqBattleField>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("doodad_func_req_battle_fields");

            entity.Property(e => e.Corp)
                .HasColumnType("INT")
                .HasColumnName("corp");
            entity.Property(e => e.FieldId)
                .HasColumnType("INT")
                .HasColumnName("field_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
        });

        modelBuilder.Entity<DoodadFuncRequireItem>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("doodad_func_require_items");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.ItemId)
                .HasColumnType("INT")
                .HasColumnName("item_id");
            entity.Property(e => e.WiId)
                .HasColumnType("INT")
                .HasColumnName("wi_id");
        });

        modelBuilder.Entity<DoodadFuncRequireQuest>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("doodad_func_require_quests");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.QuestId)
                .HasColumnType("INT")
                .HasColumnName("quest_id");
            entity.Property(e => e.WiId)
                .HasColumnType("INT")
                .HasColumnName("wi_id");
        });

        modelBuilder.Entity<DoodadFuncRespawn>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("doodad_func_respawns");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.MaxTime)
                .HasColumnType("INT")
                .HasColumnName("max_time");
            entity.Property(e => e.MinTime)
                .HasColumnType("INT")
                .HasColumnName("min_time");
        });

        modelBuilder.Entity<DoodadFuncRockMine>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("doodad_func_rock_mines");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
        });

        modelBuilder.Entity<DoodadFuncSeedCollect>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("doodad_func_seed_collects");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
        });

        modelBuilder.Entity<DoodadFuncShear>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("doodad_func_shears");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.ShearTerm)
                .HasColumnType("INT")
                .HasColumnName("shear_term");
            entity.Property(e => e.ShearTypeId)
                .HasColumnType("INT")
                .HasColumnName("shear_type_id");
        });

        modelBuilder.Entity<DoodadFuncSiegePeriod>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("doodad_func_siege_periods");

            entity.Property(e => e.Defense)
                .HasColumnType("NUM")
                .HasColumnName("defense");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.NextPhase)
                .HasColumnType("INT")
                .HasColumnName("next_phase");
            entity.Property(e => e.SiegePeriodId)
                .HasColumnType("INT")
                .HasColumnName("siege_period_id");
        });

        modelBuilder.Entity<DoodadFuncSign>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("doodad_func_signs");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.PickNum)
                .HasColumnType("INT")
                .HasColumnName("pick_num");
        });

        modelBuilder.Entity<DoodadFuncSkillHit>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("doodad_func_skill_hits");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.SkillId)
                .HasColumnType("INT")
                .HasColumnName("skill_id");
        });

        modelBuilder.Entity<DoodadFuncSkinOff>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("doodad_func_skin_offs");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
        });

        modelBuilder.Entity<DoodadFuncSoilCollect>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("doodad_func_soil_collects");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
        });

        modelBuilder.Entity<DoodadFuncSpawnGimmick>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("doodad_func_spawn_gimmicks");

            entity.Property(e => e.AngleX).HasColumnName("angle_x");
            entity.Property(e => e.AngleY).HasColumnName("angle_y");
            entity.Property(e => e.AngleZ).HasColumnName("angle_z");
            entity.Property(e => e.FactionId)
                .HasColumnType("INT")
                .HasColumnName("faction_id");
            entity.Property(e => e.GimmickId)
                .HasColumnType("INT")
                .HasColumnName("gimmick_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.NextPhase)
                .HasColumnType("INT")
                .HasColumnName("next_phase");
            entity.Property(e => e.OffsetX).HasColumnName("offset_x");
            entity.Property(e => e.OffsetY).HasColumnName("offset_y");
            entity.Property(e => e.OffsetZ).HasColumnName("offset_z");
            entity.Property(e => e.Scale).HasColumnName("scale");
            entity.Property(e => e.VelocityX).HasColumnName("velocity_x");
            entity.Property(e => e.VelocityY).HasColumnName("velocity_y");
            entity.Property(e => e.VelocityZ).HasColumnName("velocity_z");
        });

        modelBuilder.Entity<DoodadFuncSpawnMgmt>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("doodad_func_spawn_mgmts");

            entity.Property(e => e.GroupId)
                .HasColumnType("INT")
                .HasColumnName("group_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Spawn)
                .HasColumnType("NUM")
                .HasColumnName("spawn");
            entity.Property(e => e.ZoneId)
                .HasColumnType("INT")
                .HasColumnName("zone_id");
        });

        modelBuilder.Entity<DoodadFuncSpiceCollect>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("doodad_func_spice_collects");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
        });

        modelBuilder.Entity<DoodadFuncStampMaker>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("doodad_func_stamp_makers");

            entity.Property(e => e.ConsumeCount)
                .HasColumnType("INT")
                .HasColumnName("consume_count");
            entity.Property(e => e.ConsumeItemId)
                .HasColumnType("INT")
                .HasColumnName("consume_item_id");
            entity.Property(e => e.ConsumeMoney)
                .HasColumnType("INT")
                .HasColumnName("consume_money");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.ItemId)
                .HasColumnType("INT")
                .HasColumnName("item_id");
        });

        modelBuilder.Entity<DoodadFuncStoreUi>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("doodad_func_store_uis");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.MerchantPackId)
                .HasColumnType("INT")
                .HasColumnName("merchant_pack_id");
        });

        modelBuilder.Entity<DoodadFuncTimer>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("doodad_func_timers");

            entity.Property(e => e.Delay)
                .HasColumnType("INT")
                .HasColumnName("delay");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.KeepRequester)
                .HasColumnType("NUM")
                .HasColumnName("keep_requester");
            entity.Property(e => e.NextPhase)
                .HasColumnType("INT")
                .HasColumnName("next_phase");
            entity.Property(e => e.ShowEndTime)
                .HasColumnType("NUM")
                .HasColumnName("show_end_time");
            entity.Property(e => e.ShowTip)
                .HasColumnType("NUM")
                .HasColumnName("show_tip");
            entity.Property(e => e.Tip).HasColumnName("tip");
        });

        modelBuilder.Entity<DoodadFuncTod>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("doodad_func_tods");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.NextPhase)
                .HasColumnType("INT")
                .HasColumnName("next_phase");
            entity.Property(e => e.Tod)
                .HasColumnType("INT")
                .HasColumnName("tod");
        });

        modelBuilder.Entity<DoodadFuncTreeByproductsCollect>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("doodad_func_tree_byproducts_collects");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
        });

        modelBuilder.Entity<DoodadFuncUccImprint>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("doodad_func_ucc_imprints");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
        });

        modelBuilder.Entity<DoodadFuncUse>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("doodad_func_uses");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.SkillId)
                .HasColumnType("INT")
                .HasColumnName("skill_id");
        });

        modelBuilder.Entity<DoodadFuncVegetationGrowth>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("doodad_func_vegetation_growths");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.StepOneEndScale)
                .HasColumnType("INT")
                .HasColumnName("step_one_end_scale");
            entity.Property(e => e.StepOneModel).HasColumnName("step_one_model");
            entity.Property(e => e.StepOneStartScale)
                .HasColumnType("INT")
                .HasColumnName("step_one_start_scale");
            entity.Property(e => e.StepOneTime)
                .HasColumnType("INT")
                .HasColumnName("step_one_time");
            entity.Property(e => e.StepThreeEndScale)
                .HasColumnType("INT")
                .HasColumnName("step_three_end_scale");
            entity.Property(e => e.StepThreeModel).HasColumnName("step_three_model");
            entity.Property(e => e.StepThreeStartScale)
                .HasColumnType("INT")
                .HasColumnName("step_three_start_scale");
            entity.Property(e => e.StepThreeTime)
                .HasColumnType("INT")
                .HasColumnName("step_three_time");
            entity.Property(e => e.StepTwoEndScale)
                .HasColumnType("INT")
                .HasColumnName("step_two_end_scale");
            entity.Property(e => e.StepTwoModel).HasColumnName("step_two_model");
            entity.Property(e => e.StepTwoStartScale)
                .HasColumnType("INT")
                .HasColumnName("step_two_start_scale");
            entity.Property(e => e.StepTwoTime)
                .HasColumnType("INT")
                .HasColumnName("step_two_time");
        });

        modelBuilder.Entity<DoodadFuncWaterVolume>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("doodad_func_water_volumes");

            entity.Property(e => e.Duration).HasColumnName("duration");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.LevelChange).HasColumnName("levelChange");
        });

        modelBuilder.Entity<DoodadFuncZoneReact>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("doodad_func_zone_reacts");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.NextPhase)
                .HasColumnType("INT")
                .HasColumnName("next_phase");
            entity.Property(e => e.ZoneGroupId)
                .HasColumnType("INT")
                .HasColumnName("zone_group_id");
        });

        modelBuilder.Entity<DoodadGroup>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("doodad_groups");

            entity.Property(e => e.GuardOnFieldTime)
                .HasColumnType("INT")
                .HasColumnName("guard_on_field_time");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.IsExport)
                .HasColumnType("NUM")
                .HasColumnName("is_export");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.RemovedByHouse)
                .HasColumnType("NUM")
                .HasColumnName("removed_by_house");
        });

        modelBuilder.Entity<DoodadModifier>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("doodad_modifiers");

            entity.Property(e => e.DoodadAttributeId)
                .HasColumnType("INT")
                .HasColumnName("doodad_attribute_id");
            entity.Property(e => e.DoodadId)
                .HasColumnType("INT")
                .HasColumnName("doodad_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.OwnerId)
                .HasColumnType("INT")
                .HasColumnName("owner_id");
            entity.Property(e => e.OwnerType).HasColumnName("owner_type");
            entity.Property(e => e.TagId)
                .HasColumnType("INT")
                .HasColumnName("tag_id");
            entity.Property(e => e.UnitModifierTypeId)
                .HasColumnType("INT")
                .HasColumnName("unit_modifier_type_id");
            entity.Property(e => e.Value)
                .HasColumnType("INT")
                .HasColumnName("value");
        });

        modelBuilder.Entity<DoodadPhaseFunc>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("doodad_phase_funcs");

            entity.Property(e => e.ActualFuncId)
                .HasColumnType("INT")
                .HasColumnName("actual_func_id");
            entity.Property(e => e.ActualFuncType).HasColumnName("actual_func_type");
            entity.Property(e => e.DoodadFuncGroupId)
                .HasColumnType("INT")
                .HasColumnName("doodad_func_group_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
        });

        modelBuilder.Entity<DoodadPlaceSkin>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("doodad_place_skins");

            entity.Property(e => e.DoodadAlmightyId)
                .HasColumnType("INT")
                .HasColumnName("doodad_almighty_id");
            entity.Property(e => e.DoodadPlaceSkinKindId)
                .HasColumnType("INT")
                .HasColumnName("doodad_place_skin_kind_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
        });

        modelBuilder.Entity<DyeableItem>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("dyeable_items");

            entity.Property(e => e.DefaultDyeingItemId)
                .HasColumnType("INT")
                .HasColumnName("default_dyeing_item_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.ItemId)
                .HasColumnType("INT")
                .HasColumnName("item_id");
        });

        modelBuilder.Entity<DyeingColor>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("dyeing_colors");

            entity.Property(e => e.ColorRgb)
                .HasColumnType("INT")
                .HasColumnName("color_rgb");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name");
        });

        modelBuilder.Entity<DynamicUnitModifier>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("dynamic_unit_modifiers");

            entity.Property(e => e.BuffId)
                .HasColumnType("INT")
                .HasColumnName("buff_id");
            entity.Property(e => e.FuncId)
                .HasColumnType("INT")
                .HasColumnName("func_id");
            entity.Property(e => e.FuncType).HasColumnName("func_type");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.UnitAttributeId)
                .HasColumnType("INT")
                .HasColumnName("unit_attribute_id");
            entity.Property(e => e.UnitModifierTypeId)
                .HasColumnType("INT")
                .HasColumnName("unit_modifier_type_id");
        });

        modelBuilder.Entity<Effect>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("effects");

            entity.Property(e => e.ActualId)
                .HasColumnType("INT")
                .HasColumnName("actual_id");
            entity.Property(e => e.ActualType).HasColumnName("actual_type");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
        });

        modelBuilder.Entity<EmblemPattern>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("emblem_patterns");

            entity.Property(e => e.IconPath).HasColumnName("icon_path");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.KindId)
                .HasColumnType("INT")
                .HasColumnName("kind_id");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.Path).HasColumnName("path");
        });

        modelBuilder.Entity<EquipItemAttrModifier>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("equip_item_attr_modifiers");

            entity.Property(e => e.Alias).HasColumnName("alias");
            entity.Property(e => e.DexWeight)
                .HasColumnType("INT")
                .HasColumnName("dex_weight");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.IntWeight)
                .HasColumnType("INT")
                .HasColumnName("int_weight");
            entity.Property(e => e.SpiWeight)
                .HasColumnType("INT")
                .HasColumnName("spi_weight");
            entity.Property(e => e.StaWeight)
                .HasColumnType("INT")
                .HasColumnName("sta_weight");
            entity.Property(e => e.StrWeight)
                .HasColumnType("INT")
                .HasColumnName("str_weight");
        });

        modelBuilder.Entity<EquipItemSet>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("equip_item_sets");

            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name");
        });

        modelBuilder.Entity<EquipItemSetBonuse>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("equip_item_set_bonuses");

            entity.Property(e => e.BuffId)
                .HasColumnType("INT")
                .HasColumnName("buff_id");
            entity.Property(e => e.EquipItemSetId)
                .HasColumnType("INT")
                .HasColumnName("equip_item_set_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.NumPieces)
                .HasColumnType("INT")
                .HasColumnName("num_pieces");
            entity.Property(e => e.ProcId)
                .HasColumnType("INT")
                .HasColumnName("proc_id");
        });

        modelBuilder.Entity<EquipPackBodyPart>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("equip_pack_body_parts");

            entity.Property(e => e.BeardId)
                .HasColumnType("INT")
                .HasColumnName("beard_id");
            entity.Property(e => e.BodyDiffuseMapId)
                .HasColumnType("INT")
                .HasColumnName("body_diffuse_map_id");
            entity.Property(e => e.FaceId)
                .HasColumnType("INT")
                .HasColumnName("face_id");
            entity.Property(e => e.HairColorId)
                .HasColumnType("INT")
                .HasColumnName("hair_color_id");
            entity.Property(e => e.HairId)
                .HasColumnType("INT")
                .HasColumnName("hair_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.ModelId)
                .HasColumnType("INT")
                .HasColumnName("model_id");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.SkinColorId)
                .HasColumnType("INT")
                .HasColumnName("skin_color_id");
        });

        modelBuilder.Entity<EquipPackCloth>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("equip_pack_cloths");

            entity.Property(e => e.BackGradeId)
                .HasColumnType("INT")
                .HasColumnName("back_grade_id");
            entity.Property(e => e.BackId)
                .HasColumnType("INT")
                .HasColumnName("back_id");
            entity.Property(e => e.BeltGradeId)
                .HasColumnType("INT")
                .HasColumnName("belt_grade_id");
            entity.Property(e => e.BeltId)
                .HasColumnType("INT")
                .HasColumnName("belt_id");
            entity.Property(e => e.BraceletGradeId)
                .HasColumnType("INT")
                .HasColumnName("bracelet_grade_id");
            entity.Property(e => e.BraceletId)
                .HasColumnType("INT")
                .HasColumnName("bracelet_id");
            entity.Property(e => e.CosplayGradeId)
                .HasColumnType("INT")
                .HasColumnName("cosplay_grade_id");
            entity.Property(e => e.CosplayId)
                .HasColumnType("INT")
                .HasColumnName("cosplay_id");
            entity.Property(e => e.GloveGradeId)
                .HasColumnType("INT")
                .HasColumnName("glove_grade_id");
            entity.Property(e => e.GloveId)
                .HasColumnType("INT")
                .HasColumnName("glove_id");
            entity.Property(e => e.HeadgearGradeId)
                .HasColumnType("INT")
                .HasColumnName("headgear_grade_id");
            entity.Property(e => e.HeadgearId)
                .HasColumnType("INT")
                .HasColumnName("headgear_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.NecklaceGradeId)
                .HasColumnType("INT")
                .HasColumnName("necklace_grade_id");
            entity.Property(e => e.NecklaceId)
                .HasColumnType("INT")
                .HasColumnName("necklace_id");
            entity.Property(e => e.PantsGradeId)
                .HasColumnType("INT")
                .HasColumnName("pants_grade_id");
            entity.Property(e => e.PantsId)
                .HasColumnType("INT")
                .HasColumnName("pants_id");
            entity.Property(e => e.ShirtGradeId)
                .HasColumnType("INT")
                .HasColumnName("shirt_grade_id");
            entity.Property(e => e.ShirtId)
                .HasColumnType("INT")
                .HasColumnName("shirt_id");
            entity.Property(e => e.ShoesGradeId)
                .HasColumnType("INT")
                .HasColumnName("shoes_grade_id");
            entity.Property(e => e.ShoesId)
                .HasColumnType("INT")
                .HasColumnName("shoes_id");
            entity.Property(e => e.UnderpantsGradeId)
                .HasColumnType("INT")
                .HasColumnName("underpants_grade_id");
            entity.Property(e => e.UnderpantsId)
                .HasColumnType("INT")
                .HasColumnName("underpants_id");
            entity.Property(e => e.UndershirtGradeId)
                .HasColumnType("INT")
                .HasColumnName("undershirt_grade_id");
            entity.Property(e => e.UndershirtId)
                .HasColumnType("INT")
                .HasColumnName("undershirt_id");
        });

        modelBuilder.Entity<EquipPackWeapon>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("equip_pack_weapons");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.MainhandGradeId)
                .HasColumnType("INT")
                .HasColumnName("mainhand_grade_id");
            entity.Property(e => e.MainhandId)
                .HasColumnType("INT")
                .HasColumnName("mainhand_id");
            entity.Property(e => e.MusicalGradeId)
                .HasColumnType("INT")
                .HasColumnName("musical_grade_id");
            entity.Property(e => e.MusicalId)
                .HasColumnType("INT")
                .HasColumnName("musical_id");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.OffhandGradeId)
                .HasColumnType("INT")
                .HasColumnName("offhand_grade_id");
            entity.Property(e => e.OffhandId)
                .HasColumnType("INT")
                .HasColumnName("offhand_id");
            entity.Property(e => e.RangedGradeId)
                .HasColumnType("INT")
                .HasColumnName("ranged_grade_id");
            entity.Property(e => e.RangedId)
                .HasColumnType("INT")
                .HasColumnName("ranged_id");
        });

        modelBuilder.Entity<EquipSlotEnchantingCost>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("equip_slot_enchanting_costs");

            entity.Property(e => e.Cost)
                .HasColumnType("INT")
                .HasColumnName("cost");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.SlotTypeId)
                .HasColumnType("INT")
                .HasColumnName("slot_type_id");
        });

        modelBuilder.Entity<EquipSlotGroup>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("equip_slot_groups");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name");
        });

        modelBuilder.Entity<EquipSlotGroupMap>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("equip_slot_group_maps");

            entity.Property(e => e.EquipSlotGroupId)
                .HasColumnType("INT")
                .HasColumnName("equip_slot_group_id");
            entity.Property(e => e.EquipSlotTypeId)
                .HasColumnType("INT")
                .HasColumnName("equip_slot_type_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
        });

        modelBuilder.Entity<ExpandExpertLimit>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("expand_expert_limits");

            entity.Property(e => e.ExpandCount)
                .HasColumnType("INT")
                .HasColumnName("expand_count");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.ItemCount)
                .HasColumnType("INT")
                .HasColumnName("item_count");
            entity.Property(e => e.ItemId)
                .HasColumnType("INT")
                .HasColumnName("item_id");
            entity.Property(e => e.LifePoint)
                .HasColumnType("INT")
                .HasColumnName("life_point");
        });

        modelBuilder.Entity<ExpertLimit>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("expert_limits");

            entity.Property(e => e.Advantage)
                .HasColumnType("INT")
                .HasColumnName("advantage");
            entity.Property(e => e.CastAdv)
                .HasColumnType("INT")
                .HasColumnName("cast_adv");
            entity.Property(e => e.ColorArgb)
                .HasColumnType("INT")
                .HasColumnName("color_argb");
            entity.Property(e => e.DownCurrencyId)
                .HasColumnType("INT")
                .HasColumnName("down_currency_id");
            entity.Property(e => e.DownPrice)
                .HasColumnType("INT")
                .HasColumnName("down_price");
            entity.Property(e => e.ExpMul)
                .HasColumnType("INT")
                .HasColumnName("exp_mul");
            entity.Property(e => e.ExpertLimit1)
                .HasColumnType("INT")
                .HasColumnName("expert_limit");
            entity.Property(e => e.GaugeColor)
                .HasColumnType("INT")
                .HasColumnName("gauge_color");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.Show)
                .HasColumnType("NUM")
                .HasColumnName("show");
            entity.Property(e => e.UpCurrencyId)
                .HasColumnType("INT")
                .HasColumnName("up_currency_id");
            entity.Property(e => e.UpLimit)
                .HasColumnType("INT")
                .HasColumnName("up_limit");
            entity.Property(e => e.UpPrice)
                .HasColumnType("INT")
                .HasColumnName("up_price");
        });

        modelBuilder.Entity<ExpressText>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("express_texts");

            entity.Property(e => e.AnimId)
                .HasColumnType("INT")
                .HasColumnName("anim_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Me).HasColumnName("me");
            entity.Property(e => e.MeTarget).HasColumnName("me_target");
            entity.Property(e => e.NpcAnimId)
                .HasColumnType("INT")
                .HasColumnName("npc_anim_id");
            entity.Property(e => e.Other).HasColumnName("other");
            entity.Property(e => e.OtherMe).HasColumnName("other_me");
            entity.Property(e => e.OtherTarget).HasColumnName("other_target");
        });

        modelBuilder.Entity<FaceDecalAsset>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("face_decal_assets");

            entity.Property(e => e.AssetPath).HasColumnName("asset_path");
            entity.Property(e => e.CategoryId)
                .HasColumnType("INT")
                .HasColumnName("category_id");
            entity.Property(e => e.Comment).HasColumnName("comment");
            entity.Property(e => e.DefaultX)
                .HasColumnType("INT")
                .HasColumnName("defaultX");
            entity.Property(e => e.DefaultY)
                .HasColumnType("INT")
                .HasColumnName("defaultY");
            entity.Property(e => e.DisplayOrder)
                .HasColumnType("INT")
                .HasColumnName("display_order");
            entity.Property(e => e.IconPath).HasColumnName("icon_path");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.IsHot)
                .HasColumnType("NUM")
                .HasColumnName("is_hot");
            entity.Property(e => e.IsNew)
                .HasColumnType("NUM")
                .HasColumnName("is_new");
            entity.Property(e => e.ModelId)
                .HasColumnType("INT")
                .HasColumnName("model_id");
            entity.Property(e => e.Movable)
                .HasColumnType("NUM")
                .HasColumnName("movable");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.NpcOnly)
                .HasColumnType("NUM")
                .HasColumnName("npc_only");
        });

        modelBuilder.Entity<FaceDiffuseMap>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("face_diffuse_maps");

            entity.Property(e => e.Diffuse).HasColumnName("diffuse");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.ModelId)
                .HasColumnType("INT")
                .HasColumnName("model_id");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.NpcOnly)
                .HasColumnType("NUM")
                .HasColumnName("npc_only");
        });

        modelBuilder.Entity<FaceEyelashMap>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("face_eyelash_maps");

            entity.Property(e => e.Eyelash).HasColumnName("eyelash");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.ModelId)
                .HasColumnType("INT")
                .HasColumnName("model_id");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.NpcOnly)
                .HasColumnType("NUM")
                .HasColumnName("npc_only");
        });

        modelBuilder.Entity<FaceNormalMap>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("face_normal_maps");

            entity.Property(e => e.DisplayOrder)
                .HasColumnType("INT")
                .HasColumnName("display_order");
            entity.Property(e => e.IconPath).HasColumnName("icon_path");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.IsHot)
                .HasColumnType("NUM")
                .HasColumnName("is_hot");
            entity.Property(e => e.IsNew)
                .HasColumnType("NUM")
                .HasColumnName("is_new");
            entity.Property(e => e.ModelId)
                .HasColumnType("INT")
                .HasColumnName("model_id");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.Normal).HasColumnName("normal");
            entity.Property(e => e.NpcOnly)
                .HasColumnType("NUM")
                .HasColumnName("npc_only");
            entity.Property(e => e.Specular).HasColumnName("specular");
        });

        modelBuilder.Entity<FactionChatRegion>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("faction_chat_regions");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name");
        });

        modelBuilder.Entity<FarmGroup>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("farm_groups");

            entity.Property(e => e.Count)
                .HasColumnType("INT")
                .HasColumnName("count");
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name");
        });

        modelBuilder.Entity<FarmGroupDoodad>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("farm_group_doodads");

            entity.Property(e => e.DoodadId)
                .HasColumnType("INT")
                .HasColumnName("doodad_id");
            entity.Property(e => e.FarmGroupId)
                .HasColumnType("INT")
                .HasColumnName("farm_group_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.ItemId)
                .HasColumnType("INT")
                .HasColumnName("item_id");
        });

        modelBuilder.Entity<FishDetail>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("fish_details");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.ItemId)
                .HasColumnType("INT")
                .HasColumnName("item_id");
            entity.Property(e => e.MaxLength)
                .HasColumnType("INT")
                .HasColumnName("max_length");
            entity.Property(e => e.MaxWeight)
                .HasColumnType("INT")
                .HasColumnName("max_weight");
            entity.Property(e => e.MinLength)
                .HasColumnType("INT")
                .HasColumnName("min_length");
            entity.Property(e => e.MinWeight)
                .HasColumnType("INT")
                .HasColumnName("min_weight");
            entity.Property(e => e.Name).HasColumnName("name");
        });

        modelBuilder.Entity<FlyingStateChangeEffect>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("flying_state_change_effects");

            entity.Property(e => e.FlyingState)
                .HasColumnType("NUM")
                .HasColumnName("flying_state");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
        });

        modelBuilder.Entity<Formula>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("formulas");

            entity.Property(e => e.Formula1).HasColumnName("formula");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
        });

        modelBuilder.Entity<FxCamFov>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("fx_cam_fovs");

            entity.Property(e => e.CamFov).HasColumnName("camFov");
            entity.Property(e => e.Duration).HasColumnName("duration");
            entity.Property(e => e.FadeIn).HasColumnName("fadeIn");
            entity.Property(e => e.FadeOut).HasColumnName("fadeOut");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
        });

        modelBuilder.Entity<FxCga>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("fx_cgas");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
        });

        modelBuilder.Entity<FxCgf>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("fx_cgfs");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
        });

        modelBuilder.Entity<FxChr>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("fx_chrs");

            entity.Property(e => e.BindToBoneAfterEnd)
                .HasColumnType("NUM")
                .HasColumnName("bind_to_bone_after_end");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
        });

        modelBuilder.Entity<FxDecal>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("fx_decals");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Radius)
                .HasColumnType("INT")
                .HasColumnName("radius");
        });

        modelBuilder.Entity<FxGroup>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("fx_groups");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name");
        });

        modelBuilder.Entity<FxGroupFxItem>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("fx_group_fx_items");

            entity.Property(e => e.FxGroupId)
                .HasColumnType("INT")
                .HasColumnName("fx_group_id");
            entity.Property(e => e.FxItemId)
                .HasColumnType("INT")
                .HasColumnName("fx_item_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
        });

        modelBuilder.Entity<FxItem>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("fx_items");

            entity.Property(e => e.AssetName).HasColumnName("asset_name");
            entity.Property(e => e.BoneId)
                .HasColumnType("INT")
                .HasColumnName("bone_id");
            entity.Property(e => e.FxDetailId)
                .HasColumnType("INT")
                .HasColumnName("fx_detail_id");
            entity.Property(e => e.FxDetailType).HasColumnName("fx_detail_type");
            entity.Property(e => e.FxEventEndId)
                .HasColumnType("INT")
                .HasColumnName("fx_event_end_id");
            entity.Property(e => e.FxEventStartId)
                .HasColumnType("INT")
                .HasColumnName("fx_event_start_id");
            entity.Property(e => e.FxLocationId)
                .HasColumnType("INT")
                .HasColumnName("fx_location_id");
            entity.Property(e => e.FxScaleId)
                .HasColumnType("INT")
                .HasColumnName("fx_scale_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.OffsetAxisId)
                .HasColumnType("INT")
                .HasColumnName("offset_axis_id");
            entity.Property(e => e.OffsetX).HasColumnName("offset_x");
            entity.Property(e => e.OffsetY).HasColumnName("offset_y");
            entity.Property(e => e.OffsetZ).HasColumnName("offset_z");
        });

        modelBuilder.Entity<FxMaterial>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("fx_materials");

            entity.Property(e => e.CustomDualMaterialFadeTime).HasColumnName("custom_dual_material_fade_time");
            entity.Property(e => e.CustomDualMaterialId)
                .HasColumnType("INT")
                .HasColumnName("custom_dual_material_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
        });

        modelBuilder.Entity<FxMotionBlur>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("fx_motion_blurs");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
        });

        modelBuilder.Entity<FxParticle>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("fx_particles");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.InWater)
                .HasColumnType("NUM")
                .HasColumnName("in_water");
            entity.Property(e => e.Scale).HasColumnName("scale");
            entity.Property(e => e.SoundId)
                .HasColumnType("INT")
                .HasColumnName("sound_id");
            entity.Property(e => e.SoundPackId)
                .HasColumnType("INT")
                .HasColumnName("sound_pack_id");
        });

        modelBuilder.Entity<FxRope>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("fx_ropes");

            entity.Property(e => e.AnchorModelPath).HasColumnName("anchor_model_path");
            entity.Property(e => e.AttachmentCollision)
                .HasColumnType("NUM")
                .HasColumnName("attachment_collision");
            entity.Property(e => e.Collision)
                .HasColumnType("NUM")
                .HasColumnName("collision");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.LauncherModelPath).HasColumnName("launcher_model_path");
            entity.Property(e => e.Length).HasColumnName("length");
            entity.Property(e => e.Material).HasColumnName("material");
            entity.Property(e => e.PhyscisSegment)
                .HasColumnType("INT")
                .HasColumnName("physcis_segment");
            entity.Property(e => e.Segment)
                .HasColumnType("INT")
                .HasColumnName("segment");
            entity.Property(e => e.SideCount)
                .HasColumnType("INT")
                .HasColumnName("side_count");
            entity.Property(e => e.Smooth)
                .HasColumnType("NUM")
                .HasColumnName("smooth");
            entity.Property(e => e.Subdivide)
                .HasColumnType("NUM")
                .HasColumnName("subdivide");
            entity.Property(e => e.Thickness).HasColumnName("thickness");
        });

        modelBuilder.Entity<FxShakeCamera>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("fx_shake_cameras");

            entity.Property(e => e.AngX).HasColumnName("ang_x");
            entity.Property(e => e.AngY).HasColumnName("ang_y");
            entity.Property(e => e.AngZ).HasColumnName("ang_z");
            entity.Property(e => e.Duration).HasColumnName("duration");
            entity.Property(e => e.Frequency).HasColumnName("frequency");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Randomness).HasColumnName("randomness");
            entity.Property(e => e.Range).HasColumnName("range");
            entity.Property(e => e.ShiftX).HasColumnName("shift_x");
            entity.Property(e => e.ShiftY).HasColumnName("shift_y");
            entity.Property(e => e.ShiftZ).HasColumnName("shift_z");
        });

        modelBuilder.Entity<FxSound>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("fx_sounds");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.SoundId)
                .HasColumnType("INT")
                .HasColumnName("sound_id");
        });

        modelBuilder.Entity<FxVoice>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("fx_voices");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.SoundPackItemName).HasColumnName("sound_pack_item_name");
        });

        modelBuilder.Entity<GainLootPackItemEffect>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("gain_loot_pack_item_effects");

            entity.Property(e => e.ConsumeCount)
                .HasColumnType("INT")
                .HasColumnName("consume_count");
            entity.Property(e => e.ConsumeItemId)
                .HasColumnType("INT")
                .HasColumnName("consume_item_id");
            entity.Property(e => e.ConsumeSourceItem)
                .HasColumnType("NUM")
                .HasColumnName("consume_source_item");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.InheritGrade)
                .HasColumnType("NUM")
                .HasColumnName("inherit_grade");
            entity.Property(e => e.LootPackId)
                .HasColumnType("INT")
                .HasColumnName("loot_pack_id");
        });

        modelBuilder.Entity<GameRuleEvent>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("game_rule_events");

            entity.Property(e => e.ConditionId)
                .HasColumnType("INT")
                .HasColumnName("condition_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.KindId)
                .HasColumnType("INT")
                .HasColumnName("kind_id");
            entity.Property(e => e.Param1)
                .HasColumnType("INT")
                .HasColumnName("param1");
            entity.Property(e => e.Param2)
                .HasColumnType("INT")
                .HasColumnName("param2");
            entity.Property(e => e.RuleSetId)
                .HasColumnType("INT")
                .HasColumnName("rule_set_id");
        });

        modelBuilder.Entity<GameRuleSet>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("game_rule_sets");

            entity.Property(e => e.BattleFieldId)
                .HasColumnType("INT")
                .HasColumnName("battle_field_id");
            entity.Property(e => e.BonusLoser)
                .HasColumnType("INT")
                .HasColumnName("bonus_loser");
            entity.Property(e => e.BonusNoDeath)
                .HasColumnType("INT")
                .HasColumnName("bonus_no_death");
            entity.Property(e => e.BonusTopEnemyNonPcKill)
                .HasColumnType("INT")
                .HasColumnName("bonus_top_enemy_non_pc_kill");
            entity.Property(e => e.BonusTopEnemyPcKill)
                .HasColumnType("INT")
                .HasColumnName("bonus_top_enemy_pc_kill");
            entity.Property(e => e.BonusWinner)
                .HasColumnType("INT")
                .HasColumnName("bonus_winner");
            entity.Property(e => e.Corps1Id)
                .HasColumnType("INT")
                .HasColumnName("corps1_id");
            entity.Property(e => e.Corps2Id)
                .HasColumnType("INT")
                .HasColumnName("corps2_id");
            entity.Property(e => e.CorpsSize)
                .HasColumnType("INT")
                .HasColumnName("corps_size");
            entity.Property(e => e.DeathstreakId)
                .HasColumnType("INT")
                .HasColumnName("deathstreak_id");
            entity.Property(e => e.GameTypeId)
                .HasColumnType("INT")
                .HasColumnName("game_type_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Killstreak0Id)
                .HasColumnType("INT")
                .HasColumnName("killstreak0_id");
            entity.Property(e => e.Killstreak10Id)
                .HasColumnType("INT")
                .HasColumnName("killstreak10_id");
            entity.Property(e => e.Killstreak11Id)
                .HasColumnType("INT")
                .HasColumnName("killstreak11_id");
            entity.Property(e => e.Killstreak12Id)
                .HasColumnType("INT")
                .HasColumnName("killstreak12_id");
            entity.Property(e => e.Killstreak13Id)
                .HasColumnType("INT")
                .HasColumnName("killstreak13_id");
            entity.Property(e => e.Killstreak14Id)
                .HasColumnType("INT")
                .HasColumnName("killstreak14_id");
            entity.Property(e => e.Killstreak15Id)
                .HasColumnType("INT")
                .HasColumnName("killstreak15_id");
            entity.Property(e => e.Killstreak16Id)
                .HasColumnType("INT")
                .HasColumnName("killstreak16_id");
            entity.Property(e => e.Killstreak17Id)
                .HasColumnType("INT")
                .HasColumnName("killstreak17_id");
            entity.Property(e => e.Killstreak18Id)
                .HasColumnType("INT")
                .HasColumnName("killstreak18_id");
            entity.Property(e => e.Killstreak19Id)
                .HasColumnType("INT")
                .HasColumnName("killstreak19_id");
            entity.Property(e => e.Killstreak1Id)
                .HasColumnType("INT")
                .HasColumnName("killstreak1_id");
            entity.Property(e => e.Killstreak20Id)
                .HasColumnType("INT")
                .HasColumnName("killstreak20_id");
            entity.Property(e => e.Killstreak21Id)
                .HasColumnType("INT")
                .HasColumnName("killstreak21_id");
            entity.Property(e => e.Killstreak22Id)
                .HasColumnType("INT")
                .HasColumnName("killstreak22_id");
            entity.Property(e => e.Killstreak23Id)
                .HasColumnType("INT")
                .HasColumnName("killstreak23_id");
            entity.Property(e => e.Killstreak2Id)
                .HasColumnType("INT")
                .HasColumnName("killstreak2_id");
            entity.Property(e => e.Killstreak3Id)
                .HasColumnType("INT")
                .HasColumnName("killstreak3_id");
            entity.Property(e => e.Killstreak4Id)
                .HasColumnType("INT")
                .HasColumnName("killstreak4_id");
            entity.Property(e => e.Killstreak5Id)
                .HasColumnType("INT")
                .HasColumnName("killstreak5_id");
            entity.Property(e => e.Killstreak6Id)
                .HasColumnType("INT")
                .HasColumnName("killstreak6_id");
            entity.Property(e => e.Killstreak7Id)
                .HasColumnType("INT")
                .HasColumnName("killstreak7_id");
            entity.Property(e => e.Killstreak8Id)
                .HasColumnType("INT")
                .HasColumnName("killstreak8_id");
            entity.Property(e => e.Killstreak9Id)
                .HasColumnType("INT")
                .HasColumnName("killstreak9_id");
            entity.Property(e => e.LevelMin)
                .HasColumnType("INT")
                .HasColumnName("level_min");
            entity.Property(e => e.RankDrawPoint)
                .HasColumnType("INT")
                .HasColumnName("rank_draw_point");
            entity.Property(e => e.RankInvalidPoint)
                .HasColumnType("INT")
                .HasColumnName("rank_invalid_point");
            entity.Property(e => e.RankLosePoint)
                .HasColumnType("INT")
                .HasColumnName("rank_lose_point");
            entity.Property(e => e.RankWinPoint)
                .HasColumnType("INT")
                .HasColumnName("rank_win_point");
            entity.Property(e => e.TimeEnding)
                .HasColumnType("INT")
                .HasColumnName("time_ending");
            entity.Property(e => e.TimeOpening)
                .HasColumnType("INT")
                .HasColumnName("time_opening");
            entity.Property(e => e.TimePlaying)
                .HasColumnType("INT")
                .HasColumnName("time_playing");
            entity.Property(e => e.TimeRespawnDeadBuilding)
                .HasColumnType("INT")
                .HasColumnName("time_respawn_dead_building");
            entity.Property(e => e.TimeResurrectionDelay)
                .HasColumnType("INT")
                .HasColumnName("time_resurrection_delay");
            entity.Property(e => e.TimeUnearnedWin)
                .HasColumnType("INT")
                .HasColumnName("time_unearned_win");
            entity.Property(e => e.VictoryKillCorps1Head)
                .HasColumnType("INT")
                .HasColumnName("victory_kill_corps1_head");
            entity.Property(e => e.VictoryKillCorps2Head)
                .HasColumnType("INT")
                .HasColumnName("victory_kill_corps2_head");
            entity.Property(e => e.VictoryKillCount)
                .HasColumnType("INT")
                .HasColumnName("victory_kill_count");
            entity.Property(e => e.VictoryScore)
                .HasColumnType("INT")
                .HasColumnName("victory_score");
        });

        modelBuilder.Entity<GameSchedule>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("game_schedules");

            entity.Property(e => e.DayOfWeekId)
                .HasColumnType("INT")
                .HasColumnName("day_of_week_id");
            entity.Property(e => e.EdDay)
                .HasColumnType("INT")
                .HasColumnName("ed_day");
            entity.Property(e => e.EdHour)
                .HasColumnType("INT")
                .HasColumnName("ed_hour");
            entity.Property(e => e.EdMin)
                .HasColumnType("INT")
                .HasColumnName("ed_min");
            entity.Property(e => e.EdMonth)
                .HasColumnType("INT")
                .HasColumnName("ed_month");
            entity.Property(e => e.EdYear)
                .HasColumnType("INT")
                .HasColumnName("ed_year");
            entity.Property(e => e.EndTime)
                .HasColumnType("INT")
                .HasColumnName("end_time");
            entity.Property(e => e.EndTimeMin)
                .HasColumnType("INT")
                .HasColumnName("end_time_min");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.StDay)
                .HasColumnType("INT")
                .HasColumnName("st_day");
            entity.Property(e => e.StHour)
                .HasColumnType("INT")
                .HasColumnName("st_hour");
            entity.Property(e => e.StMin)
                .HasColumnType("INT")
                .HasColumnName("st_min");
            entity.Property(e => e.StMonth)
                .HasColumnType("INT")
                .HasColumnName("st_month");
            entity.Property(e => e.StYear)
                .HasColumnType("INT")
                .HasColumnName("st_year");
            entity.Property(e => e.StartTime)
                .HasColumnType("INT")
                .HasColumnName("start_time");
            entity.Property(e => e.StartTimeMin)
                .HasColumnType("INT")
                .HasColumnName("start_time_min");
        });

        modelBuilder.Entity<GameScheduleDoodad>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("game_schedule_doodads");

            entity.Property(e => e.DoodadId)
                .HasColumnType("INT")
                .HasColumnName("doodad_id");
            entity.Property(e => e.GameScheduleId)
                .HasColumnType("INT")
                .HasColumnName("game_schedule_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
        });

        modelBuilder.Entity<GameScheduleQuest>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("game_schedule_quests");

            entity.Property(e => e.GameScheduleId)
                .HasColumnType("INT")
                .HasColumnName("game_schedule_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.QuestId)
                .HasColumnType("INT")
                .HasColumnName("quest_id");
        });

        modelBuilder.Entity<GameScheduleSpawner>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("game_schedule_spawners");

            entity.Property(e => e.GameScheduleId)
                .HasColumnType("INT")
                .HasColumnName("game_schedule_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.SpawnerId)
                .HasColumnType("INT")
                .HasColumnName("spawner_id");
        });

        modelBuilder.Entity<GameScoreRule>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("game_score_rules");

            entity.Property(e => e.EventId)
                .HasColumnType("INT")
                .HasColumnName("event_id");
            entity.Property(e => e.EventScore)
                .HasColumnType("INT")
                .HasColumnName("event_score");
            entity.Property(e => e.EventValue)
                .HasColumnType("INT")
                .HasColumnName("event_value");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.RuleSetCorps)
                .HasColumnType("INT")
                .HasColumnName("rule_set_corps");
            entity.Property(e => e.RuleSetId)
                .HasColumnType("INT")
                .HasColumnName("rule_set_id");
        });

        modelBuilder.Entity<GameStance>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("game_stances");

            entity.Property(e => e.ActorModelId)
                .HasColumnType("INT")
                .HasColumnName("actor_model_id");
            entity.Property(e => e.AiMoveSpeedRun).HasColumnName("ai_move_speed_run");
            entity.Property(e => e.AiMoveSpeedSlow).HasColumnName("ai_move_speed_slow");
            entity.Property(e => e.AiMoveSpeedSprint).HasColumnName("ai_move_speed_sprint");
            entity.Property(e => e.AiMoveSpeedWalk).HasColumnName("ai_move_speed_walk");
            entity.Property(e => e.HeightCollider).HasColumnName("height_collider");
            entity.Property(e => e.HeightPivot).HasColumnName("height_pivot");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.MaxSpeed).HasColumnName("max_speed");
            entity.Property(e => e.ModelOffsetX).HasColumnName("model_offset_x");
            entity.Property(e => e.ModelOffsetY).HasColumnName("model_offset_y");
            entity.Property(e => e.ModelOffsetZ).HasColumnName("model_offset_z");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.NormalSpeed).HasColumnName("normal_speed");
            entity.Property(e => e.SizeX).HasColumnName("size_x");
            entity.Property(e => e.SizeY).HasColumnName("size_y");
            entity.Property(e => e.SizeZ).HasColumnName("size_z");
            entity.Property(e => e.StanceId)
                .HasColumnType("INT")
                .HasColumnName("stance_id");
            entity.Property(e => e.UseCapsule)
                .HasColumnType("NUM")
                .HasColumnName("use_capsule");
            entity.Property(e => e.ViewOffsetX).HasColumnName("view_offset_x");
            entity.Property(e => e.ViewOffsetY).HasColumnName("view_offset_y");
            entity.Property(e => e.ViewOffsetZ).HasColumnName("view_offset_z");
        });

        modelBuilder.Entity<GemVisualEffect>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("gem_visual_effects");

            entity.Property(e => e.Filename).HasColumnName("filename");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name");
        });

        modelBuilder.Entity<Gimmick>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("gimmicks");

            entity.Property(e => e.AirResistance).HasColumnName("air_resistance");
            entity.Property(e => e.CollisionMinSpeed).HasColumnName("collision_min_speed");
            entity.Property(e => e.CollisionSkillId)
                .HasColumnType("INT")
                .HasColumnName("collision_skill_id");
            entity.Property(e => e.CollisionUnitOnly)
                .HasColumnType("NUM")
                .HasColumnName("collision_unit_only");
            entity.Property(e => e.Damping).HasColumnName("damping");
            entity.Property(e => e.Density).HasColumnName("density");
            entity.Property(e => e.DisappearByCollision)
                .HasColumnType("NUM")
                .HasColumnName("disappear_by_collision");
            entity.Property(e => e.FadeInDuration)
                .HasColumnType("INT")
                .HasColumnName("fade_in_duration");
            entity.Property(e => e.FadeOutDuration)
                .HasColumnType("INT")
                .HasColumnName("fade_out_duration");
            entity.Property(e => e.FreeFallDamping).HasColumnName("free_fall_damping");
            entity.Property(e => e.Graspable)
                .HasColumnType("NUM")
                .HasColumnName("graspable");
            entity.Property(e => e.Gravity).HasColumnName("gravity");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.LifeTime)
                .HasColumnType("INT")
                .HasColumnName("life_time");
            entity.Property(e => e.Mass).HasColumnName("mass");
            entity.Property(e => e.ModelPath).HasColumnName("model_path");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.NoGroundCollider)
                .HasColumnType("NUM")
                .HasColumnName("no_ground_collider");
            entity.Property(e => e.PushableByPlayer)
                .HasColumnType("NUM")
                .HasColumnName("pushable_by_player");
            entity.Property(e => e.SkillDelay)
                .HasColumnType("INT")
                .HasColumnName("skill_delay");
            entity.Property(e => e.SkillId)
                .HasColumnType("INT")
                .HasColumnName("skill_id");
            entity.Property(e => e.SpawnDelay)
                .HasColumnType("INT")
                .HasColumnName("spawn_delay");
            entity.Property(e => e.WaterDamping).HasColumnName("water_damping");
            entity.Property(e => e.WaterDensity).HasColumnName("water_density");
            entity.Property(e => e.WaterResistance).HasColumnName("water_resistance");
        });

        modelBuilder.Entity<GrammarTag>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("grammar_tags");

            entity.Property(e => e.GrammartagA).HasColumnName("grammartag_a");
            entity.Property(e => e.GrammartagG).HasColumnName("grammartag_g");
            entity.Property(e => e.GrammartagI).HasColumnName("grammartag_i");
            entity.Property(e => e.GrammartagPl).HasColumnName("grammartag_pl");
            entity.Property(e => e.GrammartagPla).HasColumnName("grammartag_pla");
            entity.Property(e => e.GrammartagPld).HasColumnName("grammartag_pld");
            entity.Property(e => e.GrammartagPlg).HasColumnName("grammartag_plg");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Idx)
                .HasColumnType("INT")
                .HasColumnName("idx");
            entity.Property(e => e.Tagid)
                .HasColumnType("INT")
                .HasColumnName("tagid");
        });

        modelBuilder.Entity<GrammarTagNoneType>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("grammar_tag_none_types");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Locale).HasColumnName("locale");
            entity.Property(e => e.Macrotag).HasColumnName("macrotag");
        });

        modelBuilder.Entity<GuardTowerSetting>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("guard_tower_settings");

            entity.Property(e => e.Comments).HasColumnName("comments");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.InitialBuffId)
                .HasColumnType("INT")
                .HasColumnName("initial_buff_id");
            entity.Property(e => e.MaxGates)
                .HasColumnType("INT")
                .HasColumnName("max_gates");
            entity.Property(e => e.MaxWalls)
                .HasColumnType("INT")
                .HasColumnName("max_walls");
            entity.Property(e => e.RadiusDeclare)
                .HasColumnType("INT")
                .HasColumnName("radius_declare");
            entity.Property(e => e.RadiusDominion)
                .HasColumnType("INT")
                .HasColumnName("radius_dominion");
            entity.Property(e => e.RadiusOffenseHq)
                .HasColumnType("INT")
                .HasColumnName("radius_offense_hq");
            entity.Property(e => e.RadiusSiege)
                .HasColumnType("INT")
                .HasColumnName("radius_siege");
        });

        modelBuilder.Entity<GuardTowerStep>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("guard_tower_steps");

            entity.Property(e => e.BuffId)
                .HasColumnType("INT")
                .HasColumnName("buff_id");
            entity.Property(e => e.GuardTowerSettingId)
                .HasColumnType("INT")
                .HasColumnName("guard_tower_setting_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.NumGates)
                .HasColumnType("INT")
                .HasColumnName("num_gates");
            entity.Property(e => e.NumWalls)
                .HasColumnType("INT")
                .HasColumnName("num_walls");
            entity.Property(e => e.Step)
                .HasColumnType("INT")
                .HasColumnName("step");
        });

        modelBuilder.Entity<HairColor>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("hair_colors");

            entity.Property(e => e.AssetId)
                .HasColumnType("INT")
                .HasColumnName("asset_id");
            entity.Property(e => e.Comment).HasColumnName("comment");
            entity.Property(e => e.HairBaseColorB)
                .HasColumnType("INT")
                .HasColumnName("hair_base_color_b");
            entity.Property(e => e.HairBaseColorG)
                .HasColumnType("INT")
                .HasColumnName("hair_base_color_g");
            entity.Property(e => e.HairBaseColorR)
                .HasColumnType("INT")
                .HasColumnName("hair_base_color_r");
            entity.Property(e => e.HairDiffuseColorB)
                .HasColumnType("INT")
                .HasColumnName("hair_diffuse_color_b");
            entity.Property(e => e.HairDiffuseColorG)
                .HasColumnType("INT")
                .HasColumnName("hair_diffuse_color_g");
            entity.Property(e => e.HairDiffuseColorR)
                .HasColumnType("INT")
                .HasColumnName("hair_diffuse_color_r");
            entity.Property(e => e.HairMaterial).HasColumnName("hair_material");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.ModelId)
                .HasColumnType("INT")
                .HasColumnName("model_id");
            entity.Property(e => e.NpcOnly)
                .HasColumnType("NUM")
                .HasColumnName("npc_only");
        });

        modelBuilder.Entity<HealEffect>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("heal_effects");

            entity.Property(e => e.ActabilityAdd).HasColumnName("actability_add");
            entity.Property(e => e.ActabilityGroupId)
                .HasColumnType("INT")
                .HasColumnName("actability_group_id");
            entity.Property(e => e.ActabilityMul).HasColumnName("actability_mul");
            entity.Property(e => e.ActabilityStep)
                .HasColumnType("INT")
                .HasColumnName("actability_step");
            entity.Property(e => e.ChargedBuffId)
                .HasColumnType("INT")
                .HasColumnName("charged_buff_id");
            entity.Property(e => e.ChargedMul).HasColumnName("charged_mul");
            entity.Property(e => e.DpsMultiplier).HasColumnName("dps_multiplier");
            entity.Property(e => e.FixedMax)
                .HasColumnType("INT")
                .HasColumnName("fixed_max");
            entity.Property(e => e.FixedMin)
                .HasColumnType("INT")
                .HasColumnName("fixed_min");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.IgnoreHealAggro)
                .HasColumnType("NUM")
                .HasColumnName("ignore_heal_aggro");
            entity.Property(e => e.LevelMd).HasColumnName("level_md");
            entity.Property(e => e.LevelVaEnd)
                .HasColumnType("INT")
                .HasColumnName("level_va_end");
            entity.Property(e => e.LevelVaStart)
                .HasColumnType("INT")
                .HasColumnName("level_va_start");
            entity.Property(e => e.Percent)
                .HasColumnType("NUM")
                .HasColumnName("percent");
            entity.Property(e => e.SlaveApplicable)
                .HasColumnType("NUM")
                .HasColumnName("slave_applicable");
            entity.Property(e => e.UseChargedBuff)
                .HasColumnType("NUM")
                .HasColumnName("use_charged_buff");
            entity.Property(e => e.UseFixedHeal)
                .HasColumnType("NUM")
                .HasColumnName("use_fixed_heal");
            entity.Property(e => e.UseLevelHeal)
                .HasColumnType("NUM")
                .HasColumnName("use_level_heal");
        });

        modelBuilder.Entity<Holdable>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("holdables");

            entity.Property(e => e.Angle)
                .HasColumnType("INT")
                .HasColumnName("angle");
            entity.Property(e => e.AnimL1Id)
                .HasColumnType("INT")
                .HasColumnName("anim_l1_id");
            entity.Property(e => e.AnimL1Ratio)
                .HasColumnType("INT")
                .HasColumnName("anim_l1_ratio");
            entity.Property(e => e.AnimL2Id)
                .HasColumnType("INT")
                .HasColumnName("anim_l2_id");
            entity.Property(e => e.AnimL2Ratio)
                .HasColumnType("INT")
                .HasColumnName("anim_l2_ratio");
            entity.Property(e => e.AnimL3Id)
                .HasColumnType("INT")
                .HasColumnName("anim_l3_id");
            entity.Property(e => e.AnimR1Id)
                .HasColumnType("INT")
                .HasColumnName("anim_r1_id");
            entity.Property(e => e.AnimR1Ratio)
                .HasColumnType("INT")
                .HasColumnName("anim_r1_ratio");
            entity.Property(e => e.AnimR2Id)
                .HasColumnType("INT")
                .HasColumnName("anim_r2_id");
            entity.Property(e => e.AnimR2Ratio)
                .HasColumnType("INT")
                .HasColumnName("anim_r2_ratio");
            entity.Property(e => e.AnimR3Id)
                .HasColumnType("INT")
                .HasColumnName("anim_r3_id");
            entity.Property(e => e.Code).HasColumnName("code");
            entity.Property(e => e.Comments).HasColumnName("comments");
            entity.Property(e => e.DamageScale)
                .HasColumnType("INT")
                .HasColumnName("damage_scale");
            entity.Property(e => e.DurabilityRatio).HasColumnName("durability_ratio");
            entity.Property(e => e.EnchantedDps1000)
                .HasColumnType("INT")
                .HasColumnName("enchanted_dps1000");
            entity.Property(e => e.ExtraDamageBluntFactor)
                .HasColumnType("INT")
                .HasColumnName("extra_damage_blunt_factor");
            entity.Property(e => e.ExtraDamagePierceFactor)
                .HasColumnType("INT")
                .HasColumnName("extra_damage_pierce_factor");
            entity.Property(e => e.ExtraDamageSlashFactor)
                .HasColumnType("INT")
                .HasColumnName("extra_damage_slash_factor");
            entity.Property(e => e.FormulaArmor).HasColumnName("formula_armor");
            entity.Property(e => e.FormulaDps).HasColumnName("formula_dps");
            entity.Property(e => e.FormulaHdps).HasColumnName("formula_hdps");
            entity.Property(e => e.FormulaMdps).HasColumnName("formula_mdps");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.ItemProcId)
                .HasColumnType("INT")
                .HasColumnName("item_proc_id");
            entity.Property(e => e.KindId)
                .HasColumnType("INT")
                .HasColumnName("kind_id");
            entity.Property(e => e.MaxRange)
                .HasColumnType("INT")
                .HasColumnName("max_range");
            entity.Property(e => e.MinRange)
                .HasColumnType("INT")
                .HasColumnName("min_range");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.PoseId)
                .HasColumnType("INT")
                .HasColumnName("pose_id");
            entity.Property(e => e.RenewCategory)
                .HasColumnType("INT")
                .HasColumnName("renew_category");
            entity.Property(e => e.SheathePriority)
                .HasColumnType("INT")
                .HasColumnName("sheathe_priority");
            entity.Property(e => e.SlotTypeId)
                .HasColumnType("INT")
                .HasColumnName("slot_type_id");
            entity.Property(e => e.SoundMaterialId)
                .HasColumnType("INT")
                .HasColumnName("sound_material_id");
            entity.Property(e => e.Speed)
                .HasColumnType("INT")
                .HasColumnName("speed");
            entity.Property(e => e.StatMultiplier)
                .HasColumnType("INT")
                .HasColumnName("stat_multiplier");
        });

        modelBuilder.Entity<Hotkey>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("hotkeys");

            entity.Property(e => e.ActionId)
                .HasColumnType("INT")
                .HasColumnName("action_id");
            entity.Property(e => e.ActionType1Id)
                .HasColumnType("INT")
                .HasColumnName("action_type1_id");
            entity.Property(e => e.ActionType2Id)
                .HasColumnType("INT")
                .HasColumnName("action_type2_id");
            entity.Property(e => e.Activation).HasColumnName("activation");
            entity.Property(e => e.CategoryId)
                .HasColumnType("INT")
                .HasColumnName("category_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.KeyPrimary).HasColumnName("key_primary");
            entity.Property(e => e.KeySecond).HasColumnName("key_second");
            entity.Property(e => e.ModeId)
                .HasColumnType("INT")
                .HasColumnName("mode_id");
        });

        modelBuilder.Entity<Housing>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("housings");

            entity.Property(e => e.AbsoluteDecoLimit)
                .HasColumnType("INT")
                .HasColumnName("absolute_deco_limit");
            entity.Property(e => e.Alley).HasColumnName("alley");
            entity.Property(e => e.AlwaysPublic)
                .HasColumnType("NUM")
                .HasColumnName("always_public");
            entity.Property(e => e.AutoZ)
                .HasColumnType("NUM")
                .HasColumnName("auto_z");
            entity.Property(e => e.AutoZOffsetX).HasColumnName("auto_z_offset_x");
            entity.Property(e => e.AutoZOffsetY).HasColumnName("auto_z_offset_y");
            entity.Property(e => e.AutoZOffsetZ).HasColumnName("auto_z_offset_z");
            entity.Property(e => e.CategoryId)
                .HasColumnType("INT")
                .HasColumnName("category_id");
            entity.Property(e => e.CinemaId)
                .HasColumnType("INT")
                .HasColumnName("cinema_id");
            entity.Property(e => e.CinemaRadius).HasColumnName("cinema_radius");
            entity.Property(e => e.Comments).HasColumnName("comments");
            entity.Property(e => e.DecoLimit)
                .HasColumnType("INT")
                .HasColumnName("deco_limit");
            entity.Property(e => e.DemolishRefundItemId)
                .HasColumnType("INT")
                .HasColumnName("demolish_refund_item_id");
            entity.Property(e => e.DoorModelId)
                .HasColumnType("INT")
                .HasColumnName("door_model_id");
            entity.Property(e => e.ExtraHeightAbove).HasColumnName("extra_height_above");
            entity.Property(e => e.ExtraHeightBelow).HasColumnName("extra_height_below");
            entity.Property(e => e.Family).HasColumnName("family");
            entity.Property(e => e.GardenRadius).HasColumnName("garden_radius");
            entity.Property(e => e.GateExists)
                .HasColumnType("NUM")
                .HasColumnName("gate_exists");
            entity.Property(e => e.GuardTowerSettingId)
                .HasColumnType("INT")
                .HasColumnName("guard_tower_setting_id");
            entity.Property(e => e.HeavyTax)
                .HasColumnType("NUM")
                .HasColumnName("heavy_tax");
            entity.Property(e => e.HousingDecoLimitId)
                .HasColumnType("INT")
                .HasColumnName("housing_deco_limit_id");
            entity.Property(e => e.Hp)
                .HasColumnType("INT")
                .HasColumnName("hp");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.IsSellable)
                .HasColumnType("NUM")
                .HasColumnName("is_sellable");
            entity.Property(e => e.MainModelId)
                .HasColumnType("INT")
                .HasColumnName("main_model_id");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.RepairCost)
                .HasColumnType("INT")
                .HasColumnName("repair_cost");
            entity.Property(e => e.StairModelId)
                .HasColumnType("INT")
                .HasColumnName("stair_model_id");
            entity.Property(e => e.TaxationId)
                .HasColumnType("INT")
                .HasColumnName("taxation_id");
        });

        modelBuilder.Entity<HousingArea>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("housing_areas");

            entity.Property(e => e.Comments).HasColumnName("comments");
            entity.Property(e => e.HousingGroupId)
                .HasColumnType("INT")
                .HasColumnName("housing_group_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name");
        });

        modelBuilder.Entity<HousingBindingDoodad>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("housing_binding_doodads");

            entity.Property(e => e.AttachPointId)
                .HasColumnType("INT")
                .HasColumnName("attach_point_id");
            entity.Property(e => e.DoodadId)
                .HasColumnType("INT")
                .HasColumnName("doodad_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.OwnerId)
                .HasColumnType("INT")
                .HasColumnName("owner_id");
            entity.Property(e => e.OwnerType).HasColumnName("owner_type");
        });

        modelBuilder.Entity<HousingBuildStep>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("housing_build_steps");

            entity.Property(e => e.HousingId)
                .HasColumnType("INT")
                .HasColumnName("housing_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.ModelId)
                .HasColumnType("INT")
                .HasColumnName("model_id");
            entity.Property(e => e.NumActions)
                .HasColumnType("INT")
                .HasColumnName("num_actions");
            entity.Property(e => e.SkillId)
                .HasColumnType("INT")
                .HasColumnName("skill_id");
            entity.Property(e => e.Step)
                .HasColumnType("INT")
                .HasColumnName("step");
        });

        modelBuilder.Entity<HousingDecoLimit>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("housing_deco_limits");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name");
        });

        modelBuilder.Entity<HousingDecoLimitElem>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("housing_deco_limit_elems");

            entity.Property(e => e.Count)
                .HasColumnType("INT")
                .HasColumnName("count");
            entity.Property(e => e.DecoActabilityGroupId)
                .HasColumnType("INT")
                .HasColumnName("deco_actability_group_id");
            entity.Property(e => e.HousingDecoLimitId)
                .HasColumnType("INT")
                .HasColumnName("housing_deco_limit_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
        });

        modelBuilder.Entity<HousingDecoration>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("housing_decorations");

            entity.Property(e => e.ActabilityGroupId)
                .HasColumnType("INT")
                .HasColumnName("actability_group_id");
            entity.Property(e => e.ActabilityUp)
                .HasColumnType("INT")
                .HasColumnName("actability_up");
            entity.Property(e => e.AllowMeshOnGarden)
                .HasColumnType("NUM")
                .HasColumnName("allow_mesh_on_garden");
            entity.Property(e => e.AllowOnCeiling)
                .HasColumnType("NUM")
                .HasColumnName("allow_on_ceiling");
            entity.Property(e => e.AllowOnFloor)
                .HasColumnType("NUM")
                .HasColumnName("allow_on_floor");
            entity.Property(e => e.AllowOnWall)
                .HasColumnType("NUM")
                .HasColumnName("allow_on_wall");
            entity.Property(e => e.AllowPivotOnGarden)
                .HasColumnType("NUM")
                .HasColumnName("allow_pivot_on_garden");
            entity.Property(e => e.DecoActabilityGroupId)
                .HasColumnType("INT")
                .HasColumnName("deco_actability_group_id");
            entity.Property(e => e.DoodadId)
                .HasColumnType("INT")
                .HasColumnName("doodad_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name");
        });

        modelBuilder.Entity<HousingGroup>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("housing_groups");

            entity.Property(e => e.AllowedTaxDelayWeek)
                .HasColumnType("INT")
                .HasColumnName("allowed_tax_delay_week");
            entity.Property(e => e.CanExtend)
                .HasColumnType("NUM")
                .HasColumnName("can_extend");
            entity.Property(e => e.Desc).HasColumnName("desc");
            entity.Property(e => e.DoodadId)
                .HasColumnType("INT")
                .HasColumnName("doodad_id");
            entity.Property(e => e.ExistingCategoryId)
                .HasColumnType("INT")
                .HasColumnName("existing_category_id");
            entity.Property(e => e.Houseless)
                .HasColumnType("NUM")
                .HasColumnName("houseless");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name");
        });

        modelBuilder.Entity<HousingGroupCategory>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("housing_group_categories");

            entity.Property(e => e.CategoryId)
                .HasColumnType("INT")
                .HasColumnName("category_id");
            entity.Property(e => e.HousingGroupId)
                .HasColumnType("INT")
                .HasColumnName("housing_group_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.MaxConstructCount)
                .HasColumnType("INT")
                .HasColumnName("max_construct_count");
        });

        modelBuilder.Entity<Icon>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("icons");

            entity.Property(e => e.Filename).HasColumnName("filename");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name");
        });

        modelBuilder.Entity<IgnoreText>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("ignore_texts");

            entity.Property(e => e.Bytes)
                .HasColumnType("INT")
                .HasColumnName("bytes");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Utf8str).HasColumnName("utf8str");
        });

        modelBuilder.Entity<ImprintUccEffect>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("imprint_ucc_effects");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.ItemId)
                .HasColumnType("INT")
                .HasColumnName("item_id");
        });

        modelBuilder.Entity<ImpulseEffect>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("impulse_effects");

            entity.Property(e => e.AngImpulseX).HasColumnName("ang_impulse_x");
            entity.Property(e => e.AngImpulseY).HasColumnName("ang_impulse_y");
            entity.Property(e => e.AngImpulseZ).HasColumnName("ang_impulse_z");
            entity.Property(e => e.AngvelImpulseX).HasColumnName("angvel_impulse_x");
            entity.Property(e => e.AngvelImpulseY).HasColumnName("angvel_impulse_y");
            entity.Property(e => e.AngvelImpulseZ).HasColumnName("angvel_impulse_z");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.ImpulseX).HasColumnName("impulse_x");
            entity.Property(e => e.ImpulseY).HasColumnName("impulse_y");
            entity.Property(e => e.ImpulseZ).HasColumnName("impulse_z");
            entity.Property(e => e.VelImpulseX).HasColumnName("vel_impulse_x");
            entity.Property(e => e.VelImpulseY).HasColumnName("vel_impulse_y");
            entity.Property(e => e.VelImpulseZ).HasColumnName("vel_impulse_z");
        });

        modelBuilder.Entity<IndunAction>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("indun_actions");

            entity.Property(e => e.DetailId).HasColumnName("detail_id");
            entity.Property(e => e.DetailType).HasColumnName("detail_type");
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.NextActionId).HasColumnName("next_action_id");
            entity.Property(e => e.ZoneGroupId).HasColumnName("zone_group_id");
        });

        modelBuilder.Entity<IndunActionChangeDoodadPhase>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("indun_action_change_doodad_phases");

            entity.Property(e => e.DoodadAlmightyId).HasColumnName("doodad_almighty_id");
            entity.Property(e => e.DoodadFuncGroupId).HasColumnName("doodad_func_group_id");
            entity.Property(e => e.Id).HasColumnName("id");
        });

        modelBuilder.Entity<IndunActionRemoveTaggedNpc>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("indun_action_remove_tagged_npcs");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.TagId).HasColumnName("tag_id");
        });

        modelBuilder.Entity<IndunActionSetRoomCleared>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("indun_action_set_room_cleareds");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.IndunRoomId).HasColumnName("indun_room_id");
        });

        modelBuilder.Entity<IndunEvent>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("indun_events");

            entity.Property(e => e.ConditionId).HasColumnName("condition_id");
            entity.Property(e => e.ConditionType).HasColumnName("condition_type");
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.StartActionId).HasColumnName("start_action_id");
            entity.Property(e => e.ZoneGroupId).HasColumnName("zone_group_id");
        });

        modelBuilder.Entity<IndunEventDoodadSpawned>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("indun_event_doodad_spawneds");

            entity.Property(e => e.DoodadAlmightyId).HasColumnName("doodad_almighty_id");
            entity.Property(e => e.DoodadFuncGroupId).HasColumnName("doodad_func_group_id");
            entity.Property(e => e.Id).HasColumnName("id");
        });

        modelBuilder.Entity<IndunEventNoAliveChInRoom>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("indun_event_no_alive_ch_in_rooms");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.RoomId).HasColumnName("room_id");
        });

        modelBuilder.Entity<IndunEventNpcCombatEnded>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("indun_event_npc_combat_endeds");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.NpcId).HasColumnName("npc_id");
        });

        modelBuilder.Entity<IndunEventNpcCombatStarted>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("indun_event_npc_combat_starteds");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.NpcId).HasColumnName("npc_id");
        });

        modelBuilder.Entity<IndunEventNpcKilled>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("indun_event_npc_killeds");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.NpcId).HasColumnName("npc_id");
        });

        modelBuilder.Entity<IndunEventNpcSpawned>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("indun_event_npc_spawneds");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.NpcId).HasColumnName("npc_id");
        });

        modelBuilder.Entity<IndunRoom>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("indun_rooms");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.ShapeId).HasColumnName("shape_id");
            entity.Property(e => e.ShapeType).HasColumnName("shape_type");
            entity.Property(e => e.ZoneGroupId).HasColumnName("zone_group_id");
        });

        modelBuilder.Entity<IndunRoomSphere>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("indun_room_spheres");

            entity.Property(e => e.CenterDoodadId).HasColumnName("center_doodad_id");
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Radius).HasColumnName("radius");
        });

        modelBuilder.Entity<IndunZone>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("indun_zones");

            entity.Property(e => e.ClientDriven)
                .HasColumnType("NUM")
                .HasColumnName("client_driven");
            entity.Property(e => e.Comment).HasColumnName("comment");
            entity.Property(e => e.HasGraveyard)
                .HasColumnType("NUM")
                .HasColumnName("has_graveyard");
            entity.Property(e => e.ItemId)
                .HasColumnType("INT")
                .HasColumnName("item_id");
            entity.Property(e => e.LevelMax)
                .HasColumnType("INT")
                .HasColumnName("level_max");
            entity.Property(e => e.LevelMin)
                .HasColumnType("INT")
                .HasColumnName("level_min");
            entity.Property(e => e.MaxPlayers)
                .HasColumnType("INT")
                .HasColumnName("max_players");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.PartyOnly)
                .HasColumnType("NUM")
                .HasColumnName("party_only");
            entity.Property(e => e.Pvp)
                .HasColumnType("NUM")
                .HasColumnName("pvp");
            entity.Property(e => e.RestoreItemTime)
                .HasColumnType("INT")
                .HasColumnName("restore_item_time");
            entity.Property(e => e.SelectChannel)
                .HasColumnType("NUM")
                .HasColumnName("select_channel");
            entity.Property(e => e.ZoneGroupId)
                .HasColumnType("INT")
                .HasColumnName("zone_group_id");
        });

        modelBuilder.Entity<InstrumentSound>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("instrument_sounds");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.ItemId)
                .HasColumnType("INT")
                .HasColumnName("item_id");
            entity.Property(e => e.Midi)
                .HasColumnType("INT")
                .HasColumnName("midi");
        });

        modelBuilder.Entity<InteractionEffect>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("interaction_effects");

            entity.Property(e => e.DoodadId)
                .HasColumnType("INT")
                .HasColumnName("doodad_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.WiId)
                .HasColumnType("INT")
                .HasColumnName("wi_id");
        });

        modelBuilder.Entity<Item>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("items");

            entity.Property(e => e.ActabilityGroupId)
                .HasColumnType("INT")
                .HasColumnName("actability_group_id");
            entity.Property(e => e.ActabilityRequirement)
                .HasColumnType("INT")
                .HasColumnName("actability_requirement");
            entity.Property(e => e.AuctionACategoryId)
                .HasColumnType("INT")
                .HasColumnName("auction_a_category_id");
            entity.Property(e => e.AuctionBCategoryId)
                .HasColumnType("INT")
                .HasColumnName("auction_b_category_id");
            entity.Property(e => e.AuctionCCategoryId)
                .HasColumnType("INT")
                .HasColumnName("auction_c_category_id");
            entity.Property(e => e.AutoRegisterToActionbar)
                .HasColumnType("NUM")
                .HasColumnName("auto_register_to_actionbar");
            entity.Property(e => e.BindId)
                .HasColumnType("INT")
                .HasColumnName("bind_id");
            entity.Property(e => e.BuffId)
                .HasColumnType("INT")
                .HasColumnName("buff_id");
            entity.Property(e => e.CategoryId)
                .HasColumnType("INT")
                .HasColumnName("category_id");
            entity.Property(e => e.CharGenderId)
                .HasColumnType("INT")
                .HasColumnName("char_gender_id");
            entity.Property(e => e.Comment).HasColumnName("comment");
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.Disenchantable)
                .HasColumnType("NUM")
                .HasColumnName("disenchantable");
            entity.Property(e => e.ExpAbsLifetime)
                .HasColumnType("INT")
                .HasColumnName("exp_abs_lifetime");
            entity.Property(e => e.ExpDate)
                .HasColumnType("NUM")
                .HasColumnName("exp_date");
            entity.Property(e => e.ExpOnlineLifetime)
                .HasColumnType("INT")
                .HasColumnName("exp_online_lifetime");
            entity.Property(e => e.FixedGrade)
                .HasColumnType("INT")
                .HasColumnName("fixed_grade");
            entity.Property(e => e.Gradable)
                .HasColumnType("NUM")
                .HasColumnName("gradable");
            entity.Property(e => e.GradeEnchantable)
                .HasColumnType("NUM")
                .HasColumnName("grade_enchantable");
            entity.Property(e => e.HonorPrice)
                .HasColumnType("INT")
                .HasColumnName("honor_price");
            entity.Property(e => e.IconId)
                .HasColumnType("INT")
                .HasColumnName("icon_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.ImplId)
                .HasColumnType("INT")
                .HasColumnName("impl_id");
            entity.Property(e => e.Level)
                .HasColumnType("INT")
                .HasColumnName("level");
            entity.Property(e => e.LevelLimit)
                .HasColumnType("INT")
                .HasColumnName("level_limit");
            entity.Property(e => e.LevelRequirement)
                .HasColumnType("INT")
                .HasColumnName("level_requirement");
            entity.Property(e => e.LimitedSaleCount)
                .HasColumnType("INT")
                .HasColumnName("limited_sale_count");
            entity.Property(e => e.LivingPointPrice)
                .HasColumnType("INT")
                .HasColumnName("living_point_price");
            entity.Property(e => e.LootMulti)
                .HasColumnType("NUM")
                .HasColumnName("loot_multi");
            entity.Property(e => e.LootQuestId)
                .HasColumnType("INT")
                .HasColumnName("loot_quest_id");
            entity.Property(e => e.MaleIconId)
                .HasColumnType("INT")
                .HasColumnName("male_icon_id");
            entity.Property(e => e.MaxStackSize)
                .HasColumnType("INT")
                .HasColumnName("max_stack_size");
            entity.Property(e => e.MilestoneId)
                .HasColumnType("INT")
                .HasColumnName("milestone_id");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.NotifyUi)
                .HasColumnType("NUM")
                .HasColumnName("notify_ui");
            entity.Property(e => e.OneTimeSale)
                .HasColumnType("NUM")
                .HasColumnName("one_time_sale");
            entity.Property(e => e.OverIconId)
                .HasColumnType("INT")
                .HasColumnName("over_icon_id");
            entity.Property(e => e.PickupLimit)
                .HasColumnType("INT")
                .HasColumnName("pickup_limit");
            entity.Property(e => e.PickupSoundId)
                .HasColumnType("INT")
                .HasColumnName("pickup_sound_id");
            entity.Property(e => e.Price)
                .HasColumnType("INT")
                .HasColumnName("price");
            entity.Property(e => e.Refund)
                .HasColumnType("INT")
                .HasColumnName("refund");
            entity.Property(e => e.Sellable)
                .HasColumnType("NUM")
                .HasColumnName("sellable");
            entity.Property(e => e.SpecialtyZoneId)
                .HasColumnType("INT")
                .HasColumnName("specialty_zone_id");
            entity.Property(e => e.Translate)
                .HasColumnType("NUM")
                .HasColumnName("translate");
            entity.Property(e => e.UseOrEquipmentSoundId)
                .HasColumnType("INT")
                .HasColumnName("use_or_equipment_sound_id");
            entity.Property(e => e.UseSkillAsReagent)
                .HasColumnType("NUM")
                .HasColumnName("use_skill_as_reagent");
            entity.Property(e => e.UseSkillId)
                .HasColumnType("INT")
                .HasColumnName("use_skill_id");
        });

        modelBuilder.Entity<ItemAcceptQuest>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("item_accept_quests");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.ItemId)
                .HasColumnType("INT")
                .HasColumnName("item_id");
            entity.Property(e => e.QuestId)
                .HasColumnType("INT")
                .HasColumnName("quest_id");
        });

        modelBuilder.Entity<ItemAccessory>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("item_accessories");

            entity.Property(e => e.ChargeCount)
                .HasColumnType("INT")
                .HasColumnName("charge_count");
            entity.Property(e => e.ChargeLifetime)
                .HasColumnType("INT")
                .HasColumnName("charge_lifetime");
            entity.Property(e => e.DurabilityMultiplier)
                .HasColumnType("INT")
                .HasColumnName("durability_multiplier");
            entity.Property(e => e.EisetId)
                .HasColumnType("INT")
                .HasColumnName("eiset_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.ItemId)
                .HasColumnType("INT")
                .HasColumnName("item_id");
            entity.Property(e => e.ModSetId)
                .HasColumnType("INT")
                .HasColumnName("mod_set_id");
            entity.Property(e => e.OrUnitReqs)
                .HasColumnType("NUM")
                .HasColumnName("or_unit_reqs");
            entity.Property(e => e.RechargeBuffId)
                .HasColumnType("INT")
                .HasColumnName("recharge_buff_id");
            entity.Property(e => e.Repairable)
                .HasColumnType("NUM")
                .HasColumnName("repairable");
            entity.Property(e => e.SlotTypeId)
                .HasColumnType("INT")
                .HasColumnName("slot_type_id");
            entity.Property(e => e.TypeId)
                .HasColumnType("INT")
                .HasColumnName("type_id");
        });

        modelBuilder.Entity<ItemArmor>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("item_armors");

            entity.Property(e => e.AssetId)
                .HasColumnType("INT")
                .HasColumnName("asset_id");
            entity.Property(e => e.BaseEnchantable)
                .HasColumnType("NUM")
                .HasColumnName("base_enchantable");
            entity.Property(e => e.BaseEquipment)
                .HasColumnType("NUM")
                .HasColumnName("base_equipment");
            entity.Property(e => e.ChargeCount)
                .HasColumnType("INT")
                .HasColumnName("charge_count");
            entity.Property(e => e.ChargeLifetime)
                .HasColumnType("INT")
                .HasColumnName("charge_lifetime");
            entity.Property(e => e.DurabilityMultiplier)
                .HasColumnType("INT")
                .HasColumnName("durability_multiplier");
            entity.Property(e => e.EisetId)
                .HasColumnType("INT")
                .HasColumnName("eiset_id");
            entity.Property(e => e.EquipOnlyHasArmorVisual)
                .HasColumnType("NUM")
                .HasColumnName("equip_only_has_armor_visual");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.ItemId)
                .HasColumnType("INT")
                .HasColumnName("item_id");
            entity.Property(e => e.ModSetId)
                .HasColumnType("INT")
                .HasColumnName("mod_set_id");
            entity.Property(e => e.NoVisualErrorMessage).HasColumnName("no_visual_error_message");
            entity.Property(e => e.OrUnitReqs)
                .HasColumnType("NUM")
                .HasColumnName("or_unit_reqs");
            entity.Property(e => e.RechargeBuffId)
                .HasColumnType("INT")
                .HasColumnName("recharge_buff_id");
            entity.Property(e => e.Repairable)
                .HasColumnType("NUM")
                .HasColumnName("repairable");
            entity.Property(e => e.SkinKindId)
                .HasColumnType("INT")
                .HasColumnName("skin_kind_id");
            entity.Property(e => e.SlotTypeId)
                .HasColumnType("INT")
                .HasColumnName("slot_type_id");
            entity.Property(e => e.TypeId)
                .HasColumnType("INT")
                .HasColumnName("type_id");
            entity.Property(e => e.UseAsStat)
                .HasColumnType("NUM")
                .HasColumnName("useAsStat");
        });

        modelBuilder.Entity<ItemArmorAsset>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("item_armor_assets");

            entity.Property(e => e.ArmorAssetId)
                .HasColumnType("INT")
                .HasColumnName("armor_asset_id");
            entity.Property(e => e.AssetId)
                .HasColumnType("INT")
                .HasColumnName("asset_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
        });

        modelBuilder.Entity<ItemAsset>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("item_assets");

            entity.Property(e => e.AllowMirror)
                .HasColumnType("NUM")
                .HasColumnName("allow_mirror");
            entity.Property(e => e.AttachmentOffsetPosX).HasColumnName("attachment_offset_pos_x");
            entity.Property(e => e.AttachmentOffsetPosY).HasColumnName("attachment_offset_pos_y");
            entity.Property(e => e.AttachmentOffsetPosZ).HasColumnName("attachment_offset_pos_z");
            entity.Property(e => e.AttachmentOffsetRotX).HasColumnName("attachment_offset_rot_x");
            entity.Property(e => e.AttachmentOffsetRotY).HasColumnName("attachment_offset_rot_y");
            entity.Property(e => e.AttachmentOffsetRotZ).HasColumnName("attachment_offset_rot_z");
            entity.Property(e => e.DefaultAnim).HasColumnName("default_anim");
            entity.Property(e => e.Detail)
                .HasColumnType("INT")
                .HasColumnName("detail");
            entity.Property(e => e.HeelOffsetHeight).HasColumnName("heel_offset_height");
            entity.Property(e => e.HingeDamping).HasColumnName("hinge_damping");
            entity.Property(e => e.HingeIdx)
                .HasColumnType("INT")
                .HasColumnName("hinge_idx");
            entity.Property(e => e.HingeLimit).HasColumnName("hinge_limit");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.ModelId)
                .HasColumnType("INT")
                .HasColumnName("model_id");
            entity.Property(e => e.MoreAssetId)
                .HasColumnType("INT")
                .HasColumnName("more_asset_id");
            entity.Property(e => e.NameTagOffsetHeight).HasColumnName("name_tag_offset_height");
            entity.Property(e => e.Path).HasColumnName("path");
        });

        modelBuilder.Entity<ItemBackpack>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("item_backpacks");

            entity.Property(e => e.Asset2Id)
                .HasColumnType("INT")
                .HasColumnName("asset2_id");
            entity.Property(e => e.AssetId)
                .HasColumnType("INT")
                .HasColumnName("asset_id");
            entity.Property(e => e.BackpackTypeId)
                .HasColumnType("INT")
                .HasColumnName("backpack_type_id");
            entity.Property(e => e.DeclareSiegeZoneGroupId)
                .HasColumnType("INT")
                .HasColumnName("declare_siege_zone_group_id");
            entity.Property(e => e.Heavy)
                .HasColumnType("NUM")
                .HasColumnName("heavy");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.ItemId)
                .HasColumnType("INT")
                .HasColumnName("item_id");
            entity.Property(e => e.NormalSpecialty)
                .HasColumnType("NUM")
                .HasColumnName("normal_specialty");
            entity.Property(e => e.SkinKindId)
                .HasColumnType("INT")
                .HasColumnName("skin_kind_id");
            entity.Property(e => e.UseAsStat)
                .HasColumnType("NUM")
                .HasColumnName("use_as_stat");
        });

        modelBuilder.Entity<ItemBag>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("item_bags");

            entity.Property(e => e.Capacity)
                .HasColumnType("INT")
                .HasColumnName("capacity");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.ItemId)
                .HasColumnType("INT")
                .HasColumnName("item_id");
        });

        modelBuilder.Entity<ItemBodyPart>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("item_body_parts");

            entity.Property(e => e.Asset1Id)
                .HasColumnType("INT")
                .HasColumnName("asset_1_id");
            entity.Property(e => e.Asset2Id)
                .HasColumnType("INT")
                .HasColumnName("asset_2_id");
            entity.Property(e => e.Asset3Id)
                .HasColumnType("INT")
                .HasColumnName("asset_3_id");
            entity.Property(e => e.Asset4Id)
                .HasColumnType("INT")
                .HasColumnName("asset_4_id");
            entity.Property(e => e.AssetId)
                .HasColumnType("INT")
                .HasColumnName("asset_id");
            entity.Property(e => e.BeautyshopOnly)
                .HasColumnType("NUM")
                .HasColumnName("beautyshop_only");
            entity.Property(e => e.CustomTexture1Id)
                .HasColumnType("INT")
                .HasColumnName("custom_texture_1_id");
            entity.Property(e => e.CustomTexture2Id)
                .HasColumnType("INT")
                .HasColumnName("custom_texture_2_id");
            entity.Property(e => e.CustomTexture3Id)
                .HasColumnType("INT")
                .HasColumnName("custom_texture_3_id");
            entity.Property(e => e.CustomTexture4Id)
                .HasColumnType("INT")
                .HasColumnName("custom_texture_4_id");
            entity.Property(e => e.CustomTextureId)
                .HasColumnType("INT")
                .HasColumnName("custom_texture_id");
            entity.Property(e => e.FaceMask).HasColumnName("face_mask");
            entity.Property(e => e.HairBase).HasColumnName("hair_base");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.ItemId)
                .HasColumnType("INT")
                .HasColumnName("item_id");
            entity.Property(e => e.ModelId)
                .HasColumnType("INT")
                .HasColumnName("model_id");
            entity.Property(e => e.NpcOnly)
                .HasColumnType("NUM")
                .HasColumnName("npc_only");
            entity.Property(e => e.SlotTypeId)
                .HasColumnType("INT")
                .HasColumnName("slot_type_id");
        });

        modelBuilder.Entity<ItemCapScale>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("item_cap_scales");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.ScaleMax)
                .HasColumnType("INT")
                .HasColumnName("scale_max");
            entity.Property(e => e.ScaleMin)
                .HasColumnType("INT")
                .HasColumnName("scale_min");
            entity.Property(e => e.SkillId)
                .HasColumnType("INT")
                .HasColumnName("skill_id");
        });

        modelBuilder.Entity<ItemCapScaleForbid>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("item_cap_scale_forbids");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.ItemId)
                .HasColumnType("INT")
                .HasColumnName("item_id");
            entity.Property(e => e.Name).HasColumnName("name");
        });

        modelBuilder.Entity<ItemCategory>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("item_categories");

            entity.Property(e => e.CategoryOrder)
                .HasColumnType("INT")
                .HasColumnName("category_order");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Impl1Id)
                .HasColumnType("INT")
                .HasColumnName("impl1_id");
            entity.Property(e => e.Impl2Id)
                .HasColumnType("INT")
                .HasColumnName("impl2_id");
            entity.Property(e => e.ItemGroupId)
                .HasColumnType("INT")
                .HasColumnName("item_group_id");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.PickupSoundId)
                .HasColumnType("INT")
                .HasColumnName("pickup_sound_id");
            entity.Property(e => e.ProcessedStateId)
                .HasColumnType("INT")
                .HasColumnName("processed_state_id");
            entity.Property(e => e.Secure)
                .HasColumnType("NUM")
                .HasColumnName("secure");
            entity.Property(e => e.UsageId)
                .HasColumnType("INT")
                .HasColumnName("usage_id");
            entity.Property(e => e.UseOrEquipmentSoundId)
                .HasColumnType("INT")
                .HasColumnName("use_or_equipment_sound_id");
        });

        modelBuilder.Entity<ItemConfig>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("item_configs");

            entity.Property(e => e.DeathDurabilityLossRatio)
                .HasColumnType("INT")
                .HasColumnName("death_durability_loss_ratio");
            entity.Property(e => e.DurabilityConst).HasColumnName("durability_const");
            entity.Property(e => e.DurabilityDecrementChance).HasColumnName("durability_decrement_chance");
            entity.Property(e => e.DurabilityRepairCostFactor).HasColumnName("durability_repair_cost_factor");
            entity.Property(e => e.HoldableDurabilityConst).HasColumnName("holdable_durability_const");
            entity.Property(e => e.HoldableStatConst)
                .HasColumnType("INT")
                .HasColumnName("holdable_stat_const");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.ItemStatConst)
                .HasColumnType("INT")
                .HasColumnName("item_stat_const");
            entity.Property(e => e.StatValueConst)
                .HasColumnType("INT")
                .HasColumnName("stat_value_const");
            entity.Property(e => e.WearableDurabilityConst).HasColumnName("wearable_durability_const");
            entity.Property(e => e.WearableStatConst)
                .HasColumnType("INT")
                .HasColumnName("wearable_stat_const");
        });

        modelBuilder.Entity<ItemConv>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("item_convs");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.ItemConvSetId)
                .HasColumnType("INT")
                .HasColumnName("item_conv_set_id");
            entity.Property(e => e.Name).HasColumnName("name");
        });

        modelBuilder.Entity<ItemConvPpack>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("item_conv_ppacks");

            entity.Property(e => e.ChanceRate)
                .HasColumnType("INT")
                .HasColumnName("chance_rate");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name");
        });

        modelBuilder.Entity<ItemConvPpackMember>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("item_conv_ppack_members");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.ItemConvId)
                .HasColumnType("INT")
                .HasColumnName("item_conv_id");
            entity.Property(e => e.ItemConvPpackId)
                .HasColumnType("INT")
                .HasColumnName("item_conv_ppack_id");
        });

        modelBuilder.Entity<ItemConvProduct>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("item_conv_products");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.ItemConvPpackId)
                .HasColumnType("INT")
                .HasColumnName("item_conv_ppack_id");
            entity.Property(e => e.ItemId)
                .HasColumnType("INT")
                .HasColumnName("item_id");
            entity.Property(e => e.Max)
                .HasColumnType("INT")
                .HasColumnName("max");
            entity.Property(e => e.Min)
                .HasColumnType("INT")
                .HasColumnName("min");
            entity.Property(e => e.Weight)
                .HasColumnType("INT")
                .HasColumnName("weight");
        });

        modelBuilder.Entity<ItemConvReagent>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("item_conv_reagents");

            entity.Property(e => e.GradeId)
                .HasColumnType("INT")
                .HasColumnName("grade_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.ItemConvRpackId)
                .HasColumnType("INT")
                .HasColumnName("item_conv_rpack_id");
            entity.Property(e => e.ItemId)
                .HasColumnType("INT")
                .HasColumnName("item_id");
            entity.Property(e => e.MaxGradeId)
                .HasColumnType("INT")
                .HasColumnName("max_grade_id");
        });

        modelBuilder.Entity<ItemConvReagentFilter>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("item_conv_reagent_filters");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.ItemConvRpackId)
                .HasColumnType("INT")
                .HasColumnName("item_conv_rpack_id");
            entity.Property(e => e.ItemGradeId)
                .HasColumnType("INT")
                .HasColumnName("item_grade_id");
            entity.Property(e => e.ItemImplId)
                .HasColumnType("INT")
                .HasColumnName("item_impl_id");
            entity.Property(e => e.MaxItemGradeId)
                .HasColumnType("INT")
                .HasColumnName("max_item_grade_id");
            entity.Property(e => e.MaxLevel)
                .HasColumnType("INT")
                .HasColumnName("max_level");
            entity.Property(e => e.MinLevel)
                .HasColumnType("INT")
                .HasColumnName("min_level");
            entity.Property(e => e.Name).HasColumnName("name");
        });

        modelBuilder.Entity<ItemConvRpack>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("item_conv_rpacks");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name");
        });

        modelBuilder.Entity<ItemConvRpackMember>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("item_conv_rpack_members");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.ItemConvId)
                .HasColumnType("INT")
                .HasColumnName("item_conv_id");
            entity.Property(e => e.ItemConvRpackId)
                .HasColumnType("INT")
                .HasColumnName("item_conv_rpack_id");
        });

        modelBuilder.Entity<ItemConvSet>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("item_conv_sets");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name");
        });

        modelBuilder.Entity<ItemDyeing>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("item_dyeings");

            entity.Property(e => e.DyeingColorId)
                .HasColumnType("INT")
                .HasColumnName("dyeing_color_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.ItemId)
                .HasColumnType("INT")
                .HasColumnName("item_id");
        });

        modelBuilder.Entity<ItemEnchantingGem>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("item_enchanting_gems");

            entity.Property(e => e.EquipLevel)
                .HasColumnType("INT")
                .HasColumnName("equip_level");
            entity.Property(e => e.EquipSlotGroupId)
                .HasColumnType("INT")
                .HasColumnName("equip_slot_group_id");
            entity.Property(e => e.GemVisualEffectId)
                .HasColumnType("INT")
                .HasColumnName("gem_visual_effect_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.ItemGradeId)
                .HasColumnType("INT")
                .HasColumnName("item_grade_id");
            entity.Property(e => e.ItemId)
                .HasColumnType("INT")
                .HasColumnName("item_id");
        });

        modelBuilder.Entity<ItemGrade>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("item_grades");

            entity.Property(e => e.ColorArgb).HasColumnName("color_argb");
            entity.Property(e => e.ColorArgbSecond).HasColumnName("color_argb_second");
            entity.Property(e => e.Comments).HasColumnName("comments");
            entity.Property(e => e.CurrencyId)
                .HasColumnType("INT")
                .HasColumnName("currency_id");
            entity.Property(e => e.DurabilityValue).HasColumnName("durability_value");
            entity.Property(e => e.GradeEnchantBreakRatio)
                .HasColumnType("INT")
                .HasColumnName("grade_enchant_break_ratio");
            entity.Property(e => e.GradeEnchantCost)
                .HasColumnType("INT")
                .HasColumnName("grade_enchant_cost");
            entity.Property(e => e.GradeEnchantDowngradeMax)
                .HasColumnType("INT")
                .HasColumnName("grade_enchant_downgrade_max");
            entity.Property(e => e.GradeEnchantDowngradeMin)
                .HasColumnType("INT")
                .HasColumnName("grade_enchant_downgrade_min");
            entity.Property(e => e.GradeEnchantDowngradeRatio)
                .HasColumnType("INT")
                .HasColumnName("grade_enchant_downgrade_ratio");
            entity.Property(e => e.GradeEnchantGreatSuccessRatio)
                .HasColumnType("INT")
                .HasColumnName("grade_enchant_great_success_ratio");
            entity.Property(e => e.GradeEnchantSuccessRatio)
                .HasColumnType("INT")
                .HasColumnName("grade_enchant_success_ratio");
            entity.Property(e => e.GradeOrder)
                .HasColumnType("INT")
                .HasColumnName("grade_order");
            entity.Property(e => e.IconId)
                .HasColumnType("INT")
                .HasColumnName("icon_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.RefundMultiplier)
                .HasColumnType("INT")
                .HasColumnName("refund_multiplier");
            entity.Property(e => e.StatMultiplier)
                .HasColumnType("INT")
                .HasColumnName("stat_multiplier");
            entity.Property(e => e.UpgradeRatio)
                .HasColumnType("INT")
                .HasColumnName("upgrade_ratio");
            entity.Property(e => e.VarHoldableArmor).HasColumnName("var_holdable_armor");
            entity.Property(e => e.VarHoldableDps).HasColumnName("var_holdable_dps");
            entity.Property(e => e.VarHoldableHealDps).HasColumnName("var_holdable_heal_dps");
            entity.Property(e => e.VarHoldableMagicDps).HasColumnName("var_holdable_magic_dps");
            entity.Property(e => e.VarWearableArmor).HasColumnName("var_wearable_armor");
            entity.Property(e => e.VarWearableMagicResistance).HasColumnName("var_wearable_magic_resistance");
        });

        modelBuilder.Entity<ItemGradeBuff>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("item_grade_buffs");

            entity.Property(e => e.BuffId)
                .HasColumnType("INT")
                .HasColumnName("buff_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.ItemGradeId)
                .HasColumnType("INT")
                .HasColumnName("item_grade_id");
            entity.Property(e => e.ItemId)
                .HasColumnType("INT")
                .HasColumnName("item_id");
        });

        modelBuilder.Entity<ItemGradeDistribution>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("item_grade_distributions");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.Weight0)
                .HasColumnType("INT")
                .HasColumnName("weight_0");
            entity.Property(e => e.Weight1)
                .HasColumnType("INT")
                .HasColumnName("weight_1");
            entity.Property(e => e.Weight10)
                .HasColumnType("INT")
                .HasColumnName("weight_10");
            entity.Property(e => e.Weight11)
                .HasColumnType("INT")
                .HasColumnName("weight_11");
            entity.Property(e => e.Weight2)
                .HasColumnType("INT")
                .HasColumnName("weight_2");
            entity.Property(e => e.Weight3)
                .HasColumnType("INT")
                .HasColumnName("weight_3");
            entity.Property(e => e.Weight4)
                .HasColumnType("INT")
                .HasColumnName("weight_4");
            entity.Property(e => e.Weight5)
                .HasColumnType("INT")
                .HasColumnName("weight_5");
            entity.Property(e => e.Weight6)
                .HasColumnType("INT")
                .HasColumnName("weight_6");
            entity.Property(e => e.Weight7)
                .HasColumnType("INT")
                .HasColumnName("weight_7");
            entity.Property(e => e.Weight8)
                .HasColumnType("INT")
                .HasColumnName("weight_8");
            entity.Property(e => e.Weight9)
                .HasColumnType("INT")
                .HasColumnName("weight_9");
        });

        modelBuilder.Entity<ItemGradeEnchantingSupport>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("item_grade_enchanting_supports");

            entity.Property(e => e.AddBreakMul)
                .HasColumnType("INT")
                .HasColumnName("add_break_mul");
            entity.Property(e => e.AddBreakRatio)
                .HasColumnType("INT")
                .HasColumnName("add_break_ratio");
            entity.Property(e => e.AddDowngradeMul)
                .HasColumnType("INT")
                .HasColumnName("add_downgrade_mul");
            entity.Property(e => e.AddDowngradeRatio)
                .HasColumnType("INT")
                .HasColumnName("add_downgrade_ratio");
            entity.Property(e => e.AddGreatSuccessGrade)
                .HasColumnType("INT")
                .HasColumnName("add_great_success_grade");
            entity.Property(e => e.AddGreatSuccessMul)
                .HasColumnType("INT")
                .HasColumnName("add_great_success_mul");
            entity.Property(e => e.AddGreatSuccessRatio)
                .HasColumnType("INT")
                .HasColumnName("add_great_success_ratio");
            entity.Property(e => e.AddSuccessMul)
                .HasColumnType("INT")
                .HasColumnName("add_success_mul");
            entity.Property(e => e.AddSuccessRatio)
                .HasColumnType("INT")
                .HasColumnName("add_success_ratio");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.ItemId)
                .HasColumnType("INT")
                .HasColumnName("item_id");
            entity.Property(e => e.RequireGradeMax)
                .HasColumnType("INT")
                .HasColumnName("require_grade_max");
            entity.Property(e => e.RequireGradeMin)
                .HasColumnType("INT")
                .HasColumnName("require_grade_min");
        });

        modelBuilder.Entity<ItemGradeSkill>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("item_grade_skills");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.ItemGradeId)
                .HasColumnType("INT")
                .HasColumnName("item_grade_id");
            entity.Property(e => e.ItemId)
                .HasColumnType("INT")
                .HasColumnName("item_id");
            entity.Property(e => e.SkillId)
                .HasColumnType("INT")
                .HasColumnName("skill_id");
        });

        modelBuilder.Entity<ItemGroup>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("item_groups");

            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.VisibleUi)
                .HasColumnType("NUM")
                .HasColumnName("visible_ui");
        });

        modelBuilder.Entity<ItemHousing>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("item_housings");

            entity.Property(e => e.DesignId)
                .HasColumnType("INT")
                .HasColumnName("design_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.ItemId)
                .HasColumnType("INT")
                .HasColumnName("item_id");
        });

        modelBuilder.Entity<ItemHousingDecoration>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("item_housing_decorations");

            entity.Property(e => e.DesignId)
                .HasColumnType("INT")
                .HasColumnName("design_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.ItemId)
                .HasColumnType("INT")
                .HasColumnName("item_id");
            entity.Property(e => e.Restore)
                .HasColumnType("NUM")
                .HasColumnName("restore");
        });

        modelBuilder.Entity<ItemLookConvert>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("item_look_converts");

            entity.Property(e => e.Gold)
                .HasColumnType("INT")
                .HasColumnName("gold");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name");
        });

        modelBuilder.Entity<ItemLookConvertHoldable>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("item_look_convert_holdables");

            entity.Property(e => e.HoldableId)
                .HasColumnType("INT")
                .HasColumnName("holdable_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.ItemLookConvertId)
                .HasColumnType("INT")
                .HasColumnName("item_look_convert_id");
        });

        modelBuilder.Entity<ItemLookConvertRequiredItem>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("item_look_convert_required_items");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.ItemCount)
                .HasColumnType("INT")
                .HasColumnName("item_count");
            entity.Property(e => e.ItemId)
                .HasColumnType("INT")
                .HasColumnName("item_id");
            entity.Property(e => e.ItemLookConvertId)
                .HasColumnType("INT")
                .HasColumnName("item_look_convert_id");
        });

        modelBuilder.Entity<ItemLookConvertWearable>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("item_look_convert_wearables");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.ItemLookConvertId)
                .HasColumnType("INT")
                .HasColumnName("item_look_convert_id");
            entity.Property(e => e.WearableSlotId)
                .HasColumnType("INT")
                .HasColumnName("wearable_slot_id");
        });

        modelBuilder.Entity<ItemOpenPaper>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("item_open_papers");

            entity.Property(e => e.BookId)
                .HasColumnType("INT")
                .HasColumnName("book_id");
            entity.Property(e => e.BookPageId)
                .HasColumnType("INT")
                .HasColumnName("book_page_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.ItemId)
                .HasColumnType("INT")
                .HasColumnName("item_id");
        });

        modelBuilder.Entity<ItemProc>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("item_procs");

            entity.Property(e => e.ChanceKindId)
                .HasColumnType("INT")
                .HasColumnName("chance_kind_id");
            entity.Property(e => e.ChanceParam)
                .HasColumnType("INT")
                .HasColumnName("chance_param");
            entity.Property(e => e.ChanceRate)
                .HasColumnType("INT")
                .HasColumnName("chance_rate");
            entity.Property(e => e.CooldownSec)
                .HasColumnType("INT")
                .HasColumnName("cooldown_sec");
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.Finisher)
                .HasColumnType("NUM")
                .HasColumnName("finisher");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.ItemLevelBasedChanceBonus)
                .HasColumnType("INT")
                .HasColumnName("item_level_based_chance_bonus");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.SkillId)
                .HasColumnType("INT")
                .HasColumnName("skill_id");
        });

        modelBuilder.Entity<ItemProcBinding>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("item_proc_bindings");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.ItemId)
                .HasColumnType("INT")
                .HasColumnName("item_id");
            entity.Property(e => e.ProcId)
                .HasColumnType("INT")
                .HasColumnName("proc_id");
        });

        modelBuilder.Entity<ItemRecipe>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("item_recipes");

            entity.Property(e => e.CraftId)
                .HasColumnType("INT")
                .HasColumnName("craft_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.ItemId)
                .HasColumnType("INT")
                .HasColumnName("item_id");
        });

        modelBuilder.Entity<ItemSecureException>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("item_secure_exceptions");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.ItemId)
                .HasColumnType("INT")
                .HasColumnName("item_id");
        });

        modelBuilder.Entity<ItemSet>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("item_sets");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.KindId)
                .HasColumnType("INT")
                .HasColumnName("kind_id");
            entity.Property(e => e.Name).HasColumnName("name");
        });

        modelBuilder.Entity<ItemSetItem>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("item_set_items");

            entity.Property(e => e.Count)
                .HasColumnType("INT")
                .HasColumnName("count");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.ItemId)
                .HasColumnType("INT")
                .HasColumnName("item_id");
            entity.Property(e => e.ItemSetId)
                .HasColumnType("INT")
                .HasColumnName("item_set_id");
        });

        modelBuilder.Entity<ItemShipyard>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("item_shipyards");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.ItemId)
                .HasColumnType("INT")
                .HasColumnName("item_id");
            entity.Property(e => e.ShipyardId)
                .HasColumnType("INT")
                .HasColumnName("shipyard_id");
        });

        modelBuilder.Entity<ItemSlaveEquipment>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("item_slave_equipments");

            entity.Property(e => e.DoodadId)
                .HasColumnType("INT")
                .HasColumnName("doodad_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.ItemId)
                .HasColumnType("INT")
                .HasColumnName("item_id");
            entity.Property(e => e.RequireItemId)
                .HasColumnType("INT")
                .HasColumnName("require_item_id");
            entity.Property(e => e.SlaveEquipPackId)
                .HasColumnType("INT")
                .HasColumnName("slave_equip_pack_id");
            entity.Property(e => e.SlaveId)
                .HasColumnType("INT")
                .HasColumnName("slave_id");
            entity.Property(e => e.SlotPackId)
                .HasColumnType("INT")
                .HasColumnName("slot_pack_id");
        });

        modelBuilder.Entity<ItemSocket>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("item_sockets");

            entity.Property(e => e.EquipSlotGroupId)
                .HasColumnType("INT")
                .HasColumnName("equip_slot_group_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.ItemId)
                .HasColumnType("INT")
                .HasColumnName("item_id");
        });

        modelBuilder.Entity<ItemSocketChance>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("item_socket_chances");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.NumSockets).HasColumnName("num_sockets");
            entity.Property(e => e.SuccessRatio).HasColumnName("success_ratio");
        });

        modelBuilder.Entity<ItemSocketLevelLimit>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("item_socket_level_limits");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.ItemId)
                .HasColumnType("INT")
                .HasColumnName("item_id");
            entity.Property(e => e.Level)
                .HasColumnType("INT")
                .HasColumnName("level");
            entity.Property(e => e.Name).HasColumnName("name");
        });

        modelBuilder.Entity<ItemSocketNumLimit>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("item_socket_num_limits");

            entity.Property(e => e.GradeId)
                .HasColumnType("INT")
                .HasColumnName("grade_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.NumSocket)
                .HasColumnType("INT")
                .HasColumnName("num_socket");
            entity.Property(e => e.SlotId)
                .HasColumnType("INT")
                .HasColumnName("slot_id");
        });

        modelBuilder.Entity<ItemSpawnDoodad>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("item_spawn_doodads");

            entity.Property(e => e.DoodadId)
                .HasColumnType("INT")
                .HasColumnName("doodad_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.ItemId)
                .HasColumnType("INT")
                .HasColumnName("item_id");
        });

        modelBuilder.Entity<ItemSummonMate>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("item_summon_mates");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.ItemId)
                .HasColumnType("INT")
                .HasColumnName("item_id");
            entity.Property(e => e.NpcId)
                .HasColumnType("INT")
                .HasColumnName("npc_id");
        });

        modelBuilder.Entity<ItemSummonSlafe>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("item_summon_slaves");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.ItemId)
                .HasColumnType("INT")
                .HasColumnName("item_id");
            entity.Property(e => e.SlaveId)
                .HasColumnType("INT")
                .HasColumnName("slave_id");
        });

        modelBuilder.Entity<ItemTool>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("item_tools");

            entity.Property(e => e.AssetId)
                .HasColumnType("INT")
                .HasColumnName("asset_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.ItemId)
                .HasColumnType("INT")
                .HasColumnName("item_id");
        });

        modelBuilder.Entity<ItemWeapon>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("item_weapons");

            entity.Property(e => e.AssetId)
                .HasColumnType("INT")
                .HasColumnName("asset_id");
            entity.Property(e => e.BaseEnchantable)
                .HasColumnType("NUM")
                .HasColumnName("base_enchantable");
            entity.Property(e => e.BaseEquipment)
                .HasColumnType("NUM")
                .HasColumnName("base_equipment");
            entity.Property(e => e.ChargeCount)
                .HasColumnType("INT")
                .HasColumnName("charge_count");
            entity.Property(e => e.ChargeLifetime)
                .HasColumnType("INT")
                .HasColumnName("charge_lifetime");
            entity.Property(e => e.DrawnScale).HasColumnName("drawn_scale");
            entity.Property(e => e.DurabilityMultiplier)
                .HasColumnType("INT")
                .HasColumnName("durability_multiplier");
            entity.Property(e => e.EisetId)
                .HasColumnType("INT")
                .HasColumnName("eiset_id");
            entity.Property(e => e.FixedVisualEffectId)
                .HasColumnType("INT")
                .HasColumnName("fixed_visual_effect_id");
            entity.Property(e => e.HoldableId)
                .HasColumnType("INT")
                .HasColumnName("holdable_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.ItemId)
                .HasColumnType("INT")
                .HasColumnName("item_id");
            entity.Property(e => e.ModSetId)
                .HasColumnType("INT")
                .HasColumnName("mod_set_id");
            entity.Property(e => e.OrUnitReqs)
                .HasColumnType("NUM")
                .HasColumnName("or_unit_reqs");
            entity.Property(e => e.RechargeBuffId)
                .HasColumnType("INT")
                .HasColumnName("recharge_buff_id");
            entity.Property(e => e.Repairable)
                .HasColumnType("NUM")
                .HasColumnName("repairable");
            entity.Property(e => e.SkinKindId)
                .HasColumnType("INT")
                .HasColumnName("skin_kind_id");
            entity.Property(e => e.UseAsStat)
                .HasColumnType("NUM")
                .HasColumnName("useAsStat");
            entity.Property(e => e.WornScale).HasColumnName("worn_scale");
        });

        modelBuilder.Entity<KillNpcWithoutCorpseEffect>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("kill_npc_without_corpse_effects");

            entity.Property(e => e.GiveExp)
                .HasColumnType("NUM")
                .HasColumnName("give_exp");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.NpcId)
                .HasColumnType("INT")
                .HasColumnName("npc_id");
            entity.Property(e => e.Radius).HasColumnName("radius");
            entity.Property(e => e.Vanish)
                .HasColumnType("NUM")
                .HasColumnName("vanish");
        });

        modelBuilder.Entity<Level>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("levels");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.SkillPoints)
                .HasColumnType("INT")
                .HasColumnName("skill_points");
            entity.Property(e => e.TotalExp)
                .HasColumnType("INT")
                .HasColumnName("total_exp");
            entity.Property(e => e.TotalMateExp)
                .HasColumnType("INT")
                .HasColumnName("total_mate_exp");
        });

        modelBuilder.Entity<LinearFunc>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("linear_funcs");

            entity.Property(e => e.EndValue)
                .HasColumnType("INT")
                .HasColumnName("end_value");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.StartValue)
                .HasColumnType("INT")
                .HasColumnName("start_value");
        });

        modelBuilder.Entity<LocalizedText>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("localized_texts");

            entity.Property(e => e.De).HasColumnName("de");
            entity.Property(e => e.DeVer)
                .HasColumnType("INT")
                .HasColumnName("de_ver");
            entity.Property(e => e.EnUs).HasColumnName("en_us");
            entity.Property(e => e.EnUsVer)
                .HasColumnType("INT")
                .HasColumnName("en_us_ver");
            entity.Property(e => e.Fr).HasColumnName("fr");
            entity.Property(e => e.FrVer)
                .HasColumnType("INT")
                .HasColumnName("fr_ver");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Idx)
                .HasColumnType("INT")
                .HasColumnName("idx");
            entity.Property(e => e.Ja).HasColumnName("ja");
            entity.Property(e => e.JaVer)
                .HasColumnType("INT")
                .HasColumnName("ja_ver");
            entity.Property(e => e.Ko).HasColumnName("ko");
            entity.Property(e => e.KoVer)
                .HasColumnType("INT")
                .HasColumnName("ko_ver");
            entity.Property(e => e.Ru).HasColumnName("ru");
            entity.Property(e => e.RuVer)
                .HasColumnType("INT")
                .HasColumnName("ru_ver");
            entity.Property(e => e.TblColumnName).HasColumnName("tbl_column_name");
            entity.Property(e => e.TblName).HasColumnName("tbl_name");
            entity.Property(e => e.ZhCn).HasColumnName("zh_cn");
            entity.Property(e => e.ZhCnVer)
                .HasColumnType("INT")
                .HasColumnName("zh_cn_ver");
            entity.Property(e => e.ZhTw).HasColumnName("zh_tw");
            entity.Property(e => e.ZhTwVer)
                .HasColumnType("INT")
                .HasColumnName("zh_tw_ver");
        });

        modelBuilder.Entity<Loot>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("loots");

            entity.Property(e => e.AlwaysDrop).HasColumnName("always_drop");
            entity.Property(e => e.DropRate).HasColumnName("drop_rate");
            entity.Property(e => e.GradeId).HasColumnName("grade_id");
            entity.Property(e => e.Group).HasColumnName("group");
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.ItemId).HasColumnName("item_id");
            entity.Property(e => e.LootPackId).HasColumnName("loot_pack_id");
            entity.Property(e => e.MaxAmount).HasColumnName("max_amount");
            entity.Property(e => e.MinAmount).HasColumnName("min_amount");
        });

        modelBuilder.Entity<LootActabilityGroup>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("loot_actability_groups");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.LootGroupId).HasColumnName("loot_group_id");
            entity.Property(e => e.LootPackId).HasColumnName("loot_pack_id");
            entity.Property(e => e.MaxDice).HasColumnName("max_dice");
            entity.Property(e => e.MinDice).HasColumnName("min_dice");
        });

        modelBuilder.Entity<LootGroup>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("loot_groups");

            entity.Property(e => e.DropRate).HasColumnName("drop_rate");
            entity.Property(e => e.GroupNo).HasColumnName("group_no");
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.ItemGradeDistributionId).HasColumnName("item_grade_distribution_id");
            entity.Property(e => e.PackId).HasColumnName("pack_id");
        });

        modelBuilder.Entity<LootPackDroppingNpc>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("loot_pack_dropping_npcs");

            entity.Property(e => e.DefaultPack).HasColumnName("default_pack");
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.LootPackId).HasColumnName("loot_pack_id");
            entity.Property(e => e.NpcId).HasColumnName("npc_id");
        });

        modelBuilder.Entity<ManaBurnEffect>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("mana_burn_effects");

            entity.Property(e => e.BaseMax)
                .HasColumnType("INT")
                .HasColumnName("base_max");
            entity.Property(e => e.BaseMin)
                .HasColumnType("INT")
                .HasColumnName("base_min");
            entity.Property(e => e.DamageRatio)
                .HasColumnType("INT")
                .HasColumnName("damage_ratio");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.LevelMd).HasColumnName("level_md");
            entity.Property(e => e.LevelVaEnd)
                .HasColumnType("INT")
                .HasColumnName("level_va_end");
            entity.Property(e => e.LevelVaStart)
                .HasColumnType("INT")
                .HasColumnName("level_va_start");
        });

        modelBuilder.Entity<ManualFunc>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("manual_funcs");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.ValueList).HasColumnName("value_list");
        });

        modelBuilder.Entity<MateEquipPack>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("mate_equip_packs");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name");
        });

        modelBuilder.Entity<MateEquipPackGroup>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("mate_equip_pack_groups");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.MateEquipPackId)
                .HasColumnType("INT")
                .HasColumnName("mate_equip_pack_id");
            entity.Property(e => e.NpcId)
                .HasColumnType("INT")
                .HasColumnName("npc_id");
        });

        modelBuilder.Entity<MateEquipPackItem>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("mate_equip_pack_items");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.ItemId)
                .HasColumnType("INT")
                .HasColumnName("item_id");
            entity.Property(e => e.MateEquipPackId)
                .HasColumnType("INT")
                .HasColumnName("mate_equip_pack_id");
        });

        modelBuilder.Entity<MateEquipSlotPack>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("mate_equip_slot_packs");

            entity.Property(e => e.Chest)
                .HasColumnType("NUM")
                .HasColumnName("chest");
            entity.Property(e => e.Feet)
                .HasColumnType("NUM")
                .HasColumnName("feet");
            entity.Property(e => e.Head)
                .HasColumnType("NUM")
                .HasColumnName("head");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.Waist)
                .HasColumnType("NUM")
                .HasColumnName("waist");
        });

        modelBuilder.Entity<Merchant>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("merchants");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.MerchantPackId)
                .HasColumnType("INT")
                .HasColumnName("merchant_pack_id");
            entity.Property(e => e.NpcId)
                .HasColumnType("INT")
                .HasColumnName("npc_id");
        });

        modelBuilder.Entity<MerchantGood>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("merchant_goods");

            entity.Property(e => e.GradeId)
                .HasColumnType("INT")
                .HasColumnName("grade_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.ItemId)
                .HasColumnType("INT")
                .HasColumnName("item_id");
            entity.Property(e => e.MerchantPackId)
                .HasColumnType("INT")
                .HasColumnName("merchant_pack_id");
        });

        modelBuilder.Entity<MerchantPack>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("merchant_packs");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.KindId)
                .HasColumnType("INT")
                .HasColumnName("kind_id");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.OwnerNpcId)
                .HasColumnType("INT")
                .HasColumnName("owner_npc_id");
        });

        modelBuilder.Entity<MerchantPriceRatio>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("merchant_price_ratios");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.NpcId)
                .HasColumnType("INT")
                .HasColumnName("npc_id");
            entity.Property(e => e.Ratio)
                .HasColumnType("INT")
                .HasColumnName("ratio");
        });

        modelBuilder.Entity<MineJewelRate>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("mine_jewel_rates");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.JewelItemId)
                .HasColumnType("INT")
                .HasColumnName("jewel_item_id");
            entity.Property(e => e.MineItemId)
                .HasColumnType("INT")
                .HasColumnName("mine_item_id");
            entity.Property(e => e.Rate)
                .HasColumnType("INT")
                .HasColumnName("rate");
        });

        modelBuilder.Entity<Model>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("models");

            entity.Property(e => e.Big)
                .HasColumnType("NUM")
                .HasColumnName("big");
            entity.Property(e => e.CameraDistance).HasColumnName("camera_distance");
            entity.Property(e => e.CameraDistanceForWideAngle).HasColumnName("camera_distance_for_wide_angle");
            entity.Property(e => e.Comment).HasColumnName("comment");
            entity.Property(e => e.DespawnDoodadOnCollision)
                .HasColumnType("NUM")
                .HasColumnName("despawn_doodad_on_collision");
            entity.Property(e => e.DyingTime).HasColumnName("dying_time");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.MountPoseId)
                .HasColumnType("INT")
                .HasColumnName("mount_pose_id");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.NameTagOffset).HasColumnName("name_tag_offset");
            entity.Property(e => e.PlayMountAnimation)
                .HasColumnType("NUM")
                .HasColumnName("play_mount_animation");
            entity.Property(e => e.Selectable)
                .HasColumnType("NUM")
                .HasColumnName("selectable");
            entity.Property(e => e.ShowNameTag)
                .HasColumnType("NUM")
                .HasColumnName("show_name_tag");
            entity.Property(e => e.SoundMaterialId)
                .HasColumnType("INT")
                .HasColumnName("sound_material_id");
            entity.Property(e => e.SoundPackId)
                .HasColumnType("INT")
                .HasColumnName("sound_pack_id");
            entity.Property(e => e.SubId)
                .HasColumnType("INT")
                .HasColumnName("sub_id");
            entity.Property(e => e.SubType).HasColumnName("sub_type");
            entity.Property(e => e.TargetDecalSize).HasColumnName("target_decal_size");
            entity.Property(e => e.UseTargetDecal)
                .HasColumnType("NUM")
                .HasColumnName("use_target_decal");
            entity.Property(e => e.UseTargetHighlight)
                .HasColumnType("NUM")
                .HasColumnName("use_target_highlight");
            entity.Property(e => e.UseTargetSilhouette)
                .HasColumnType("NUM")
                .HasColumnName("use_target_silhouette");
        });

        modelBuilder.Entity<ModelAttachPointString>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("model_attach_point_strings");

            entity.Property(e => e.Actor).HasColumnName("actor");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Prefab).HasColumnName("prefab");
        });

        modelBuilder.Entity<ModelBinding>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("model_bindings");

            entity.Property(e => e.AttachPointId)
                .HasColumnType("INT")
                .HasColumnName("attach_point_id");
            entity.Property(e => e.HorseRein)
                .HasColumnType("NUM")
                .HasColumnName("horse_rein");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.OwnerId)
                .HasColumnType("INT")
                .HasColumnName("owner_id");
            entity.Property(e => e.OwnerType).HasColumnName("owner_type");
        });

        modelBuilder.Entity<ModelQuestCamera>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("model_quest_cameras");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.ModelId)
                .HasColumnType("INT")
                .HasColumnName("model_id");
            entity.Property(e => e.QuestCameraId)
                .HasColumnType("INT")
                .HasColumnName("quest_camera_id");
        });

        modelBuilder.Entity<Mould>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("moulds");

            entity.Property(e => e.CraftId)
                .HasColumnType("INT")
                .HasColumnName("craft_id");
            entity.Property(e => e.Delay)
                .HasColumnType("INT")
                .HasColumnName("delay");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name");
        });

        modelBuilder.Entity<MouldPack>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("mould_packs");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name");
        });

        modelBuilder.Entity<MouldPackItem>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("mould_pack_items");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.MouldId)
                .HasColumnType("INT")
                .HasColumnName("mould_id");
            entity.Property(e => e.MouldPackId)
                .HasColumnType("INT")
                .HasColumnName("mould_pack_id");
        });

        modelBuilder.Entity<MountAttachedSkill>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("mount_attached_skills");

            entity.Property(e => e.AttachPointId)
                .HasColumnType("INT")
                .HasColumnName("attach_point_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.MountSkillId)
                .HasColumnType("INT")
                .HasColumnName("mount_skill_id");
            entity.Property(e => e.SkillId)
                .HasColumnType("INT")
                .HasColumnName("skill_id");
        });

        modelBuilder.Entity<MountSkill>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("mount_skills");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.SkillId)
                .HasColumnType("INT")
                .HasColumnName("skill_id");
        });

        modelBuilder.Entity<MoveToRezPointEffect>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("move_to_rez_point_effects");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
        });

        modelBuilder.Entity<MusicNoteLimit>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("music_note_limits");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.NoteLength)
                .HasColumnType("INT")
                .HasColumnName("note_length");
            entity.Property(e => e.Step)
                .HasColumnType("INT")
                .HasColumnName("step");
        });

        modelBuilder.Entity<NpPassiveBuff>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("np_passive_buffs");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.OwnerId).HasColumnName("owner_id");
            entity.Property(e => e.OwnerType).HasColumnName("owner_type");
            entity.Property(e => e.PassiveBuffId).HasColumnName("passive_buff_id");
        });

        modelBuilder.Entity<NpSkill>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("np_skills");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.OwnerId).HasColumnName("owner_id");
            entity.Property(e => e.OwnerType).HasColumnName("owner_type");
            entity.Property(e => e.SkillId).HasColumnName("skill_id");
            entity.Property(e => e.SkillUseConditionId).HasColumnName("skill_use_condition_id");
            entity.Property(e => e.SkillUseParam1).HasColumnName("skill_use_param1");
            entity.Property(e => e.SkillUseParam2).HasColumnName("skill_use_param2");
        });

        modelBuilder.Entity<Npc>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("npcs");

            entity.Property(e => e.AbilityChanger)
                .HasColumnType("NUM")
                .HasColumnName("ability_changer");
            entity.Property(e => e.AbsoluteReturnDistance).HasColumnName("absolute_return_distance");
            entity.Property(e => e.AcceptAggroLink)
                .HasColumnType("NUM")
                .HasColumnName("accept_aggro_link");
            entity.Property(e => e.ActivateAiAlways)
                .HasColumnType("NUM")
                .HasColumnName("activate_ai_always");
            entity.Property(e => e.Aggression)
                .HasColumnType("NUM")
                .HasColumnName("aggression");
            entity.Property(e => e.AggroLinkHelpDist).HasColumnName("aggro_link_help_dist");
            entity.Property(e => e.AggroLinkSightCheck)
                .HasColumnType("NUM")
                .HasColumnName("aggro_link_sight_check");
            entity.Property(e => e.AggroLinkSpecialGuard)
                .HasColumnType("NUM")
                .HasColumnName("aggro_link_special_guard");
            entity.Property(e => e.AggroLinkSpecialIgnoreNpcAttacker)
                .HasColumnType("NUM")
                .HasColumnName("aggro_link_special_ignore_npc_attacker");
            entity.Property(e => e.AggroLinkSpecialRuleId)
                .HasColumnType("INT")
                .HasColumnName("aggro_link_special_rule_id");
            entity.Property(e => e.AiFileId)
                .HasColumnType("INT")
                .HasColumnName("ai_file_id");
            entity.Property(e => e.AttackStartRangeScale).HasColumnName("attack_start_range_scale");
            entity.Property(e => e.Auctioneer)
                .HasColumnType("NUM")
                .HasColumnName("auctioneer");
            entity.Property(e => e.Banker)
                .HasColumnType("NUM")
                .HasColumnName("banker");
            entity.Property(e => e.BaseSkillDelay).HasColumnName("base_skill_delay");
            entity.Property(e => e.BaseSkillId)
                .HasColumnType("INT")
                .HasColumnName("base_skill_id");
            entity.Property(e => e.BaseSkillStrafe)
                .HasColumnType("NUM")
                .HasColumnName("base_skill_strafe");
            entity.Property(e => e.Blacksmith)
                .HasColumnType("NUM")
                .HasColumnName("blacksmith");
            entity.Property(e => e.CharRaceId)
                .HasColumnType("INT")
                .HasColumnName("char_race_id");
            entity.Property(e => e.Comment1).HasColumnName("comment1");
            entity.Property(e => e.Comment2).HasColumnName("comment2");
            entity.Property(e => e.Comment3).HasColumnName("comment3");
            entity.Property(e => e.CommentWear).HasColumnName("comment_wear");
            entity.Property(e => e.CrowdEffect)
                .HasColumnType("NUM")
                .HasColumnName("crowd_effect");
            entity.Property(e => e.EngageCombatGiveQuestId)
                .HasColumnType("INT")
                .HasColumnName("engage_combat_give_quest_id");
            entity.Property(e => e.EquipBodiesId)
                .HasColumnType("INT")
                .HasColumnName("equip_bodies_id");
            entity.Property(e => e.EquipClothsId)
                .HasColumnType("INT")
                .HasColumnName("equip_cloths_id");
            entity.Property(e => e.EquipWeaponsId)
                .HasColumnType("INT")
                .HasColumnName("equip_weapons_id");
            entity.Property(e => e.ExpAdder)
                .HasColumnType("INT")
                .HasColumnName("exp_adder");
            entity.Property(e => e.ExpMultiplier).HasColumnName("exp_multiplier");
            entity.Property(e => e.Expedition)
                .HasColumnType("NUM")
                .HasColumnName("expedition");
            entity.Property(e => e.FactionId)
                .HasColumnType("INT")
                .HasColumnName("faction_id");
            entity.Property(e => e.FxScale).HasColumnName("fx_scale");
            entity.Property(e => e.HonorPoint)
                .HasColumnType("INT")
                .HasColumnName("honor_point");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Level)
                .HasColumnType("INT")
                .HasColumnName("level");
            entity.Property(e => e.LookConverter)
                .HasColumnType("NUM")
                .HasColumnName("look_converter");
            entity.Property(e => e.MateEquipSlotPackId)
                .HasColumnType("INT")
                .HasColumnName("mate_equip_slot_pack_id");
            entity.Property(e => e.MateKindId)
                .HasColumnType("INT")
                .HasColumnName("mate_kind_id");
            entity.Property(e => e.Merchant)
                .HasColumnType("NUM")
                .HasColumnName("merchant");
            entity.Property(e => e.MilestoneId)
                .HasColumnType("INT")
                .HasColumnName("milestone_id");
            entity.Property(e => e.ModelId)
                .HasColumnType("INT")
                .HasColumnName("model_id");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.NoApplyTotalCustom)
                .HasColumnType("NUM")
                .HasColumnName("no_apply_total_custom");
            entity.Property(e => e.NoExp)
                .HasColumnType("NUM")
                .HasColumnName("no_exp");
            entity.Property(e => e.NoPenalty)
                .HasColumnType("NUM")
                .HasColumnName("no_penalty");
            entity.Property(e => e.NonPushableByActor)
                .HasColumnType("NUM")
                .HasColumnName("non_pushable_by_actor");
            entity.Property(e => e.NpcAiParamId)
                .HasColumnType("INT")
                .HasColumnName("npc_ai_param_id");
            entity.Property(e => e.NpcGradeId)
                .HasColumnType("INT")
                .HasColumnName("npc_grade_id");
            entity.Property(e => e.NpcInteractionSetId)
                .HasColumnType("INT")
                .HasColumnName("npc_interaction_set_id");
            entity.Property(e => e.NpcKindId)
                .HasColumnType("INT")
                .HasColumnName("npc_kind_id");
            entity.Property(e => e.NpcNicknameId)
                .HasColumnType("INT")
                .HasColumnName("npc_nickname_id");
            entity.Property(e => e.NpcPostureSetId)
                .HasColumnType("INT")
                .HasColumnName("npc_posture_set_id");
            entity.Property(e => e.NpcTemplateId)
                .HasColumnType("INT")
                .HasColumnName("npc_template_id");
            entity.Property(e => e.NpcTendencyId)
                .HasColumnType("INT")
                .HasColumnName("npc_tendency_id");
            entity.Property(e => e.Opacity).HasColumnName("opacity");
            entity.Property(e => e.PetItemId)
                .HasColumnType("INT")
                .HasColumnName("pet_item_id");
            entity.Property(e => e.Priest)
                .HasColumnType("NUM")
                .HasColumnName("priest");
            entity.Property(e => e.RecruitingBattleFieldId)
                .HasColumnType("INT")
                .HasColumnName("recruiting_battle_field_id");
            entity.Property(e => e.Repairman)
                .HasColumnType("NUM")
                .HasColumnName("repairman");
            entity.Property(e => e.ReturnDistance).HasColumnName("return_distance");
            entity.Property(e => e.ReturnWhenEnterHousingArea)
                .HasColumnType("NUM")
                .HasColumnName("return_when_enter_housing_area");
            entity.Property(e => e.Scale).HasColumnName("scale");
            entity.Property(e => e.ShowFactionTag)
                .HasColumnType("NUM")
                .HasColumnName("show_faction_tag");
            entity.Property(e => e.ShowNameTag)
                .HasColumnType("NUM")
                .HasColumnName("show_name_tag");
            entity.Property(e => e.SightFovScale).HasColumnName("sight_fov_scale");
            entity.Property(e => e.SightRangeScale).HasColumnName("sight_range_scale");
            entity.Property(e => e.SkillTrainer)
                .HasColumnType("NUM")
                .HasColumnName("skill_trainer");
            entity.Property(e => e.SoState).HasColumnName("so_state");
            entity.Property(e => e.SoundPackId)
                .HasColumnType("INT")
                .HasColumnName("sound_pack_id");
            entity.Property(e => e.Specialty)
                .HasColumnType("NUM")
                .HasColumnName("specialty");
            entity.Property(e => e.SpecialtyCoinId)
                .HasColumnType("INT")
                .HasColumnName("specialty_coin_id");
            entity.Property(e => e.Stabler)
                .HasColumnType("NUM")
                .HasColumnName("stabler");
            entity.Property(e => e.Teleporter)
                .HasColumnType("NUM")
                .HasColumnName("teleporter");
            entity.Property(e => e.TotalCustomId)
                .HasColumnType("INT")
                .HasColumnName("total_custom_id");
            entity.Property(e => e.TrackFriendship)
                .HasColumnType("NUM")
                .HasColumnName("track_friendship");
            entity.Property(e => e.Trader)
                .HasColumnType("NUM")
                .HasColumnName("trader");
            entity.Property(e => e.Translate)
                .HasColumnType("NUM")
                .HasColumnName("translate");
            entity.Property(e => e.UseAbuserList)
                .HasColumnType("NUM")
                .HasColumnName("use_abuser_list");
            entity.Property(e => e.UseDdcmsMountSkill)
                .HasColumnType("NUM")
                .HasColumnName("use_ddcms_mount_skill");
            entity.Property(e => e.UseRangeMod)
                .HasColumnType("NUM")
                .HasColumnName("use_range_mod");
            entity.Property(e => e.VisibleToCreatorOnly)
                .HasColumnType("NUM")
                .HasColumnName("visible_to_creator_only");
        });

        modelBuilder.Entity<NpcAggroLink>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("npc_aggro_links");

            entity.Property(e => e.AggroLinkId).HasColumnName("aggro_link_id");
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.NpcId).HasColumnName("npc_id");
        });

        modelBuilder.Entity<NpcAiParam>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("npc_ai_params");

            entity.Property(e => e.AiParam).HasColumnName("ai_param");
            entity.Property(e => e.Id).HasColumnName("id");
        });

        modelBuilder.Entity<NpcChatBubble>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("npc_chat_bubbles");

            entity.Property(e => e.AiEventId)
                .HasColumnType("INT")
                .HasColumnName("ai_event_id");
            entity.Property(e => e.Bubble).HasColumnName("bubble");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
        });

        modelBuilder.Entity<NpcControlEffect>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("npc_control_effects");

            entity.Property(e => e.CategoryId).HasColumnName("category_id");
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.ParamInt).HasColumnName("param_int");
            entity.Property(e => e.ParamString).HasColumnName("param_string");
        });

        modelBuilder.Entity<NpcDoodadBinding>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("npc_doodad_bindings");

            entity.Property(e => e.AttachPointId)
                .HasColumnType("INT")
                .HasColumnName("attach_point_id");
            entity.Property(e => e.DoodadId)
                .HasColumnType("INT")
                .HasColumnName("doodad_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.OwnerId)
                .HasColumnType("INT")
                .HasColumnName("owner_id");
            entity.Property(e => e.OwnerType).HasColumnName("owner_type");
            entity.Property(e => e.Persist)
                .HasColumnType("NUM")
                .HasColumnName("persist");
        });

        modelBuilder.Entity<NpcInitialBuff>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("npc_initial_buffs");

            entity.Property(e => e.BuffId)
                .HasColumnType("INT")
                .HasColumnName("buff_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.NpcId)
                .HasColumnType("INT")
                .HasColumnName("npc_id");
        });

        modelBuilder.Entity<NpcInteraction>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("npc_interactions");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.NpcInteractionSetId)
                .HasColumnType("INT")
                .HasColumnName("npc_interaction_set_id");
            entity.Property(e => e.SkillId)
                .HasColumnType("INT")
                .HasColumnName("skill_id");
        });

        modelBuilder.Entity<NpcInteractionSet>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("npc_interaction_sets");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name");
        });

        modelBuilder.Entity<NpcMountSkill>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("npc_mount_skills");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.MountSkillId)
                .HasColumnType("INT")
                .HasColumnName("mount_skill_id");
            entity.Property(e => e.NpcId)
                .HasColumnType("INT")
                .HasColumnName("npc_id");
        });

        modelBuilder.Entity<NpcNickname>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("npc_nicknames");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name");
        });

        modelBuilder.Entity<NpcPosture>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("npc_postures");

            entity.Property(e => e.AnimActionId)
                .HasColumnType("INT")
                .HasColumnName("anim_action_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.NpcPostureSetId)
                .HasColumnType("INT")
                .HasColumnName("npc_posture_set_id");
            entity.Property(e => e.StartTodTime)
                .HasColumnType("INT")
                .HasColumnName("start_tod_time");
            entity.Property(e => e.TalkAnim).HasColumnName("talk_anim");
        });

        modelBuilder.Entity<NpcPostureSet>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("npc_posture_sets");

            entity.Property(e => e.Comment).HasColumnName("comment");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.QuestAnimActionId)
                .HasColumnType("INT")
                .HasColumnName("quest_anim_action_id");
        });

        modelBuilder.Entity<NpcSpawner>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("npc_spawners");

            entity.Property(e => e.ActivationState).HasColumnName("activation_state");
            entity.Property(e => e.Comment).HasColumnName("comment");
            entity.Property(e => e.DestroyTime).HasColumnName("destroyTime");
            entity.Property(e => e.EndTime).HasColumnName("endTime");
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.MaxPopulation).HasColumnName("maxPopulation");
            entity.Property(e => e.MinPopulation).HasColumnName("min_population");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.NpcSpawnerCategoryId).HasColumnName("npc_spawner_category_id");
            entity.Property(e => e.SaveIndun).HasColumnName("save_indun");
            entity.Property(e => e.SpawnDelayMax).HasColumnName("spawn_delay_max");
            entity.Property(e => e.SpawnDelayMin).HasColumnName("spawn_delay_min");
            entity.Property(e => e.StartTime).HasColumnName("startTime");
            entity.Property(e => e.SuspendSpawnCount).HasColumnName("suspend_spawn_count");
            entity.Property(e => e.TestRadiusNpc).HasColumnName("test_radius_npc");
            entity.Property(e => e.TestRadiusPc).HasColumnName("test_radius_pc");
        });

        modelBuilder.Entity<NpcSpawnerDespawnEffect>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("npc_spawner_despawn_effects");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.SpawnerId).HasColumnName("spawner_id");
        });

        modelBuilder.Entity<NpcSpawnerNpc>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("npc_spawner_npcs");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.MemberId).HasColumnName("member_id");
            entity.Property(e => e.MemberType).HasColumnName("member_type");
            entity.Property(e => e.NpcSpawnerId).HasColumnName("npc_spawner_id");
            entity.Property(e => e.Weight).HasColumnName("weight");
        });

        modelBuilder.Entity<NpcSpawnerSpawnEffect>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("npc_spawner_spawn_effects");

            entity.Property(e => e.ActivationState).HasColumnName("activation_state");
            entity.Property(e => e.DespawnOnCreatorDeath).HasColumnName("despawn_on_creator_death");
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.LifeTime).HasColumnName("life_time");
            entity.Property(e => e.SpawnerId).HasColumnName("spawner_id");
            entity.Property(e => e.UseSummonerAggroTarget).HasColumnName("use_summoner_aggro_target");
        });

        modelBuilder.Entity<OpenPortalEffect>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("open_portal_effects");

            entity.Property(e => e.Distance).HasColumnName("distance");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
        });

        modelBuilder.Entity<OpenPortalInlandReagent>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("open_portal_inland_reagents");

            entity.Property(e => e.Amount)
                .HasColumnType("INT")
                .HasColumnName("amount");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.ItemId)
                .HasColumnType("INT")
                .HasColumnName("item_id");
            entity.Property(e => e.OpenPortalEffectId)
                .HasColumnType("INT")
                .HasColumnName("open_portal_effect_id");
            entity.Property(e => e.Priority)
                .HasColumnType("INT")
                .HasColumnName("priority");
        });

        modelBuilder.Entity<OpenPortalOutlandReagent>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("open_portal_outland_reagents");

            entity.Property(e => e.Amount)
                .HasColumnType("INT")
                .HasColumnName("amount");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.ItemId)
                .HasColumnType("INT")
                .HasColumnName("item_id");
            entity.Property(e => e.OpenPortalEffectId)
                .HasColumnType("INT")
                .HasColumnName("open_portal_effect_id");
            entity.Property(e => e.Priority)
                .HasColumnType("INT")
                .HasColumnName("priority");
        });

        modelBuilder.Entity<PassiveBuff>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("passive_buffs");

            entity.Property(e => e.AbilityId)
                .HasColumnType("INT")
                .HasColumnName("ability_id");
            entity.Property(e => e.Active)
                .HasColumnType("NUM")
                .HasColumnName("active");
            entity.Property(e => e.BuffId)
                .HasColumnType("INT")
                .HasColumnName("buff_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Level)
                .HasColumnType("INT")
                .HasColumnName("level");
            entity.Property(e => e.ReqPoints)
                .HasColumnType("INT")
                .HasColumnName("req_points");
        });

        modelBuilder.Entity<PcbangBuff>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("pcbang_buffs");

            entity.Property(e => e.Active)
                .HasColumnType("NUM")
                .HasColumnName("active");
            entity.Property(e => e.BuffId)
                .HasColumnType("INT")
                .HasColumnName("buff_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.KindId)
                .HasColumnType("INT")
                .HasColumnName("kind_id");
            entity.Property(e => e.PremiumGradeId)
                .HasColumnType("INT")
                .HasColumnName("premium_grade_id");
        });

        modelBuilder.Entity<PhysicalEnchantAbility>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("physical_enchant_abilities");

            entity.Property(e => e.Armor).HasColumnName("armor");
            entity.Property(e => e.EnchantLevel).HasColumnName("enchant_level");
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.MinFriendship).HasColumnName("min_friendship");
            entity.Property(e => e.NpcId).HasColumnName("npc_id");
            entity.Property(e => e.SuccessRatio).HasColumnName("success_ratio");
        });

        modelBuilder.Entity<PhysicalExplosionEffect>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("physical_explosion_effects");

            entity.Property(e => e.HoleSize).HasColumnName("hole_size");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Pressure).HasColumnName("pressure");
            entity.Property(e => e.Radius).HasColumnName("radius");
        });

        modelBuilder.Entity<PlayLogEffect>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("play_log_effects");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Message).HasColumnName("message");
        });

        modelBuilder.Entity<Plot>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("plots");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.TargetTypeId)
                .HasColumnType("INT")
                .HasColumnName("target_type_id");
        });

        modelBuilder.Entity<PlotAoeCondition>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("plot_aoe_conditions");

            entity.Property(e => e.ConditionId)
                .HasColumnType("INT")
                .HasColumnName("condition_id");
            entity.Property(e => e.EventId)
                .HasColumnType("INT")
                .HasColumnName("event_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Position)
                .HasColumnType("INT")
                .HasColumnName("position");
        });

        modelBuilder.Entity<PlotCondition>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("plot_conditions");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.KindId)
                .HasColumnType("INT")
                .HasColumnName("kind_id");
            entity.Property(e => e.NotCondition)
                .HasColumnType("NUM")
                .HasColumnName("not_condition");
            entity.Property(e => e.Param1)
                .HasColumnType("INT")
                .HasColumnName("param1");
            entity.Property(e => e.Param2)
                .HasColumnType("INT")
                .HasColumnName("param2");
            entity.Property(e => e.Param3)
                .HasColumnType("INT")
                .HasColumnName("param3");
        });

        modelBuilder.Entity<PlotEffect>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("plot_effects");

            entity.Property(e => e.ActualId)
                .HasColumnType("INT")
                .HasColumnName("actual_id");
            entity.Property(e => e.ActualType).HasColumnName("actual_type");
            entity.Property(e => e.EventId)
                .HasColumnType("INT")
                .HasColumnName("event_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Position)
                .HasColumnType("INT")
                .HasColumnName("position");
            entity.Property(e => e.SourceId)
                .HasColumnType("INT")
                .HasColumnName("source_id");
            entity.Property(e => e.TargetId)
                .HasColumnType("INT")
                .HasColumnName("target_id");
        });

        modelBuilder.Entity<PlotEvent>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("plot_events");

            entity.Property(e => e.AoeDiminishing)
                .HasColumnType("NUM")
                .HasColumnName("aoe_diminishing");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.PlotId)
                .HasColumnType("INT")
                .HasColumnName("plot_id");
            entity.Property(e => e.Position)
                .HasColumnType("INT")
                .HasColumnName("position");
            entity.Property(e => e.SourceUpdateMethodId)
                .HasColumnType("INT")
                .HasColumnName("source_update_method_id");
            entity.Property(e => e.TargetUpdateMethodId)
                .HasColumnType("INT")
                .HasColumnName("target_update_method_id");
            entity.Property(e => e.TargetUpdateMethodParam1)
                .HasColumnType("INT")
                .HasColumnName("target_update_method_param1");
            entity.Property(e => e.TargetUpdateMethodParam2)
                .HasColumnType("INT")
                .HasColumnName("target_update_method_param2");
            entity.Property(e => e.TargetUpdateMethodParam3)
                .HasColumnType("INT")
                .HasColumnName("target_update_method_param3");
            entity.Property(e => e.TargetUpdateMethodParam4)
                .HasColumnType("INT")
                .HasColumnName("target_update_method_param4");
            entity.Property(e => e.TargetUpdateMethodParam5)
                .HasColumnType("INT")
                .HasColumnName("target_update_method_param5");
            entity.Property(e => e.TargetUpdateMethodParam6)
                .HasColumnType("INT")
                .HasColumnName("target_update_method_param6");
            entity.Property(e => e.TargetUpdateMethodParam7)
                .HasColumnType("INT")
                .HasColumnName("target_update_method_param7");
            entity.Property(e => e.TargetUpdateMethodParam8)
                .HasColumnType("INT")
                .HasColumnName("target_update_method_param8");
            entity.Property(e => e.TargetUpdateMethodParam9)
                .HasColumnType("INT")
                .HasColumnName("target_update_method_param9");
            entity.Property(e => e.Tickets)
                .HasColumnType("INT")
                .HasColumnName("tickets");
        });

        modelBuilder.Entity<PlotEventCondition>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("plot_event_conditions");

            entity.Property(e => e.ConditionId)
                .HasColumnType("INT")
                .HasColumnName("condition_id");
            entity.Property(e => e.EventId)
                .HasColumnType("INT")
                .HasColumnName("event_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.NotifyFailure)
                .HasColumnType("NUM")
                .HasColumnName("notify_failure");
            entity.Property(e => e.Position)
                .HasColumnType("INT")
                .HasColumnName("position");
            entity.Property(e => e.SourceId)
                .HasColumnType("INT")
                .HasColumnName("source_id");
            entity.Property(e => e.TargetId)
                .HasColumnType("INT")
                .HasColumnName("target_id");
        });

        modelBuilder.Entity<PlotNextEvent>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("plot_next_events");

            entity.Property(e => e.AddAnimCsTime)
                .HasColumnType("NUM")
                .HasColumnName("add_anim_cs_time");
            entity.Property(e => e.CancelOnBigHit)
                .HasColumnType("NUM")
                .HasColumnName("cancel_on_big_hit");
            entity.Property(e => e.Casting)
                .HasColumnType("NUM")
                .HasColumnName("casting");
            entity.Property(e => e.CastingCancelable)
                .HasColumnType("NUM")
                .HasColumnName("casting_cancelable");
            entity.Property(e => e.CastingDelayable)
                .HasColumnType("NUM")
                .HasColumnName("casting_delayable");
            entity.Property(e => e.CastingInc)
                .HasColumnType("INT")
                .HasColumnName("casting_inc");
            entity.Property(e => e.Channeling)
                .HasColumnType("NUM")
                .HasColumnName("channeling");
            entity.Property(e => e.Delay)
                .HasColumnType("INT")
                .HasColumnName("delay");
            entity.Property(e => e.EventId)
                .HasColumnType("INT")
                .HasColumnName("event_id");
            entity.Property(e => e.Fail)
                .HasColumnType("NUM")
                .HasColumnName("fail");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.NextEventId)
                .HasColumnType("INT")
                .HasColumnName("next_event_id");
            entity.Property(e => e.PerTarget)
                .HasColumnType("NUM")
                .HasColumnName("per_target");
            entity.Property(e => e.Position)
                .HasColumnType("INT")
                .HasColumnName("position");
            entity.Property(e => e.Speed)
                .HasColumnType("INT")
                .HasColumnName("speed");
            entity.Property(e => e.UseExeTime)
                .HasColumnType("NUM")
                .HasColumnName("use_exe_time");
        });

        modelBuilder.Entity<PreCompletedAchievement>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("pre_completed_achievements");

            entity.Property(e => e.CompletedAchievementId)
                .HasColumnType("INT")
                .HasColumnName("completed_achievement_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.MyAchievementId)
                .HasColumnType("INT")
                .HasColumnName("my_achievement_id");
        });

        modelBuilder.Entity<PrefabElement>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("prefab_elements");

            entity.Property(e => e.FilePath).HasColumnName("file_path");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.PrefabModelId)
                .HasColumnType("INT")
                .HasColumnName("prefab_model_id");
            entity.Property(e => e.StateId)
                .HasColumnType("INT")
                .HasColumnName("state_id");
        });

        modelBuilder.Entity<PrefabModel>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("prefab_models");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
        });

        modelBuilder.Entity<PremiumBenefit>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("premium_benefits");

            entity.Property(e => e.GradeId)
                .HasColumnType("INT")
                .HasColumnName("grade_id");
            entity.Property(e => e.IconId)
                .HasColumnType("INT")
                .HasColumnName("icon_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.MaxLabor)
                .HasColumnType("INT")
                .HasColumnName("max_labor");
            entity.Property(e => e.OfflineLabor)
                .HasColumnType("INT")
                .HasColumnName("offline_labor");
            entity.Property(e => e.OnlineLabor)
                .HasColumnType("INT")
                .HasColumnName("online_labor");
        });

        modelBuilder.Entity<PremiumConfig>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("premium_configs");

            entity.Property(e => e.ConnectPoint)
                .HasColumnType("INT")
                .HasColumnName("connect_point");
            entity.Property(e => e.DeactivatePoint)
                .HasColumnType("INT")
                .HasColumnName("deactivate_point");
            entity.Property(e => e.DisconnectPoint)
                .HasColumnType("INT")
                .HasColumnName("disconnect_point");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.MaxGrade)
                .HasColumnType("INT")
                .HasColumnName("max_grade");
            entity.Property(e => e.MaxPoint)
                .HasColumnType("INT")
                .HasColumnName("max_point");
        });

        modelBuilder.Entity<PremiumGrade>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("premium_grades");

            entity.Property(e => e.GradeId)
                .HasColumnType("INT")
                .HasColumnName("grade_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Point)
                .HasColumnType("INT")
                .HasColumnName("point");
        });

        modelBuilder.Entity<PremiumPoint>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("premium_points");

            entity.Property(e => e.Grade)
                .HasColumnType("INT")
                .HasColumnName("grade");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Money)
                .HasColumnType("INT")
                .HasColumnName("money");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.PremiumId)
                .HasColumnType("INT")
                .HasColumnName("premium_id");
            entity.Property(e => e.SellType)
                .HasColumnType("INT")
                .HasColumnName("sell_type");
            entity.Property(e => e.Time)
                .HasColumnType("INT")
                .HasColumnName("time");
        });

        modelBuilder.Entity<PriestBuff>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("priest_buffs");

            entity.Property(e => e.BuffId)
                .HasColumnType("INT")
                .HasColumnName("buff_id");
            entity.Property(e => e.Cost)
                .HasColumnType("INT")
                .HasColumnName("cost");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Position)
                .HasColumnType("INT")
                .HasColumnName("position");
        });

        modelBuilder.Entity<Projectile>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("projectiles");

            entity.Property(e => e.DestBoneId)
                .HasColumnType("INT")
                .HasColumnName("dest_bone_id");
            entity.Property(e => e.FxGroupId)
                .HasColumnType("INT")
                .HasColumnName("fx_group_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.IgnoreZRotation)
                .HasColumnType("NUM")
                .HasColumnName("ignore_z_rotation");
            entity.Property(e => e.IsPermanent)
                .HasColumnType("NUM")
                .HasColumnName("is_permanent");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.ProjPhysicId)
                .HasColumnType("INT")
                .HasColumnName("proj_physic_id");
            entity.Property(e => e.SrcBoneId)
                .HasColumnType("INT")
                .HasColumnName("src_bone_id");
        });

        modelBuilder.Entity<PutDownBackpackEffect>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("put_down_backpack_effects");

            entity.Property(e => e.BackpackDoodadId)
                .HasColumnType("INT")
                .HasColumnName("backpack_doodad_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
        });

        modelBuilder.Entity<QuestAct>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("quest_acts");

            entity.Property(e => e.ActDetailId)
                .HasColumnType("INT")
                .HasColumnName("act_detail_id");
            entity.Property(e => e.ActDetailType).HasColumnName("act_detail_type");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.QuestComponentId)
                .HasColumnType("INT")
                .HasColumnName("quest_component_id");
        });

        modelBuilder.Entity<QuestActCheckCompleteComponent>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("quest_act_check_complete_components");

            entity.Property(e => e.CompleteComponent)
                .HasColumnType("INT")
                .HasColumnName("complete_component");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
        });

        modelBuilder.Entity<QuestActCheckDistance>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("quest_act_check_distances");

            entity.Property(e => e.Distance)
                .HasColumnType("INT")
                .HasColumnName("distance");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.NpcId)
                .HasColumnType("INT")
                .HasColumnName("npc_id");
            entity.Property(e => e.Within)
                .HasColumnType("NUM")
                .HasColumnName("within");
        });

        modelBuilder.Entity<QuestActCheckGuard>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("quest_act_check_guards");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.NpcId)
                .HasColumnType("INT")
                .HasColumnName("npc_id");
        });

        modelBuilder.Entity<QuestActCheckSphere>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("quest_act_check_spheres");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.SphereId)
                .HasColumnType("INT")
                .HasColumnName("sphere_id");
        });

        modelBuilder.Entity<QuestActCheckTimer>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("quest_act_check_timers");

            entity.Property(e => e.BuffId)
                .HasColumnType("INT")
                .HasColumnName("buff_id");
            entity.Property(e => e.CheckBuf)
                .HasColumnType("NUM")
                .HasColumnName("check_buf");
            entity.Property(e => e.ForceChangeComponent)
                .HasColumnType("NUM")
                .HasColumnName("force_change_component");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.IsSkillPlayer)
                .HasColumnType("NUM")
                .HasColumnName("is_skill_player");
            entity.Property(e => e.LimitTime)
                .HasColumnType("INT")
                .HasColumnName("limit_time");
            entity.Property(e => e.NextComponent)
                .HasColumnType("INT")
                .HasColumnName("next_component");
            entity.Property(e => e.PlaySkill)
                .HasColumnType("NUM")
                .HasColumnName("play_skill");
            entity.Property(e => e.SkillId)
                .HasColumnType("INT")
                .HasColumnName("skill_id");
            entity.Property(e => e.SustainBuf)
                .HasColumnType("NUM")
                .HasColumnName("sustain_buf");
            entity.Property(e => e.TimerNpcId)
                .HasColumnType("INT")
                .HasColumnName("timer_npc_id");
        });

        modelBuilder.Entity<QuestActConAcceptBuff>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("quest_act_con_accept_buffs");

            entity.Property(e => e.BuffId)
                .HasColumnType("INT")
                .HasColumnName("buff_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
        });

        modelBuilder.Entity<QuestActConAcceptComponent>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("quest_act_con_accept_components");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.QuestContextId)
                .HasColumnType("INT")
                .HasColumnName("quest_context_id");
        });

        modelBuilder.Entity<QuestActConAcceptDoodad>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("quest_act_con_accept_doodads");

            entity.Property(e => e.DoodadId)
                .HasColumnType("INT")
                .HasColumnName("doodad_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
        });

        modelBuilder.Entity<QuestActConAcceptItem>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("quest_act_con_accept_items");

            entity.Property(e => e.Cleanup)
                .HasColumnType("NUM")
                .HasColumnName("cleanup");
            entity.Property(e => e.DestroyWhenDrop)
                .HasColumnType("NUM")
                .HasColumnName("destroy_when_drop");
            entity.Property(e => e.DropWhenDestroy)
                .HasColumnType("NUM")
                .HasColumnName("drop_when_destroy");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.ItemId)
                .HasColumnType("INT")
                .HasColumnName("item_id");
        });

        modelBuilder.Entity<QuestActConAcceptItemEquip>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("quest_act_con_accept_item_equips");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.ItemId)
                .HasColumnType("INT")
                .HasColumnName("item_id");
        });

        modelBuilder.Entity<QuestActConAcceptItemGain>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("quest_act_con_accept_item_gains");

            entity.Property(e => e.Count)
                .HasColumnType("INT")
                .HasColumnName("count");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.ItemId)
                .HasColumnType("INT")
                .HasColumnName("item_id");
        });

        modelBuilder.Entity<QuestActConAcceptLevelUp>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("quest_act_con_accept_level_ups");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Level)
                .HasColumnType("INT")
                .HasColumnName("level");
        });

        modelBuilder.Entity<QuestActConAcceptNpc>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("quest_act_con_accept_npcs");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.NpcId)
                .HasColumnType("INT")
                .HasColumnName("npc_id");
        });

        modelBuilder.Entity<QuestActConAcceptNpcEmotion>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("quest_act_con_accept_npc_emotions");

            entity.Property(e => e.Emotion).HasColumnName("emotion");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.NpcId)
                .HasColumnType("INT")
                .HasColumnName("npc_id");
        });

        modelBuilder.Entity<QuestActConAcceptNpcKill>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("quest_act_con_accept_npc_kills");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.NpcId)
                .HasColumnType("INT")
                .HasColumnName("npc_id");
        });

        modelBuilder.Entity<QuestActConAcceptSkill>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("quest_act_con_accept_skills");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.SkillId)
                .HasColumnType("INT")
                .HasColumnName("skill_id");
        });

        modelBuilder.Entity<QuestActConAcceptSphere>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("quest_act_con_accept_spheres");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.SphereId)
                .HasColumnType("INT")
                .HasColumnName("sphere_id");
        });

        modelBuilder.Entity<QuestActConAutoComplete>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("quest_act_con_auto_completes");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
        });

        modelBuilder.Entity<QuestActConFail>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("quest_act_con_fails");

            entity.Property(e => e.ForceChangeComponent)
                .HasColumnType("NUM")
                .HasColumnName("force_change_component");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
        });

        modelBuilder.Entity<QuestActConReportDoodad>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("quest_act_con_report_doodads");

            entity.Property(e => e.DoodadId)
                .HasColumnType("INT")
                .HasColumnName("doodad_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.QuestActObjAliasId)
                .HasColumnType("INT")
                .HasColumnName("quest_act_obj_alias_id");
            entity.Property(e => e.UseAlias)
                .HasColumnType("NUM")
                .HasColumnName("use_alias");
        });

        modelBuilder.Entity<QuestActConReportJournal>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("quest_act_con_report_journals");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
        });

        modelBuilder.Entity<QuestActConReportNpc>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("quest_act_con_report_npcs");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.NpcId)
                .HasColumnType("INT")
                .HasColumnName("npc_id");
            entity.Property(e => e.QuestActObjAliasId)
                .HasColumnType("INT")
                .HasColumnName("quest_act_obj_alias_id");
            entity.Property(e => e.UseAlias)
                .HasColumnType("NUM")
                .HasColumnName("use_alias");
        });

        modelBuilder.Entity<QuestActEtcItemObtain>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("quest_act_etc_item_obtains");

            entity.Property(e => e.Cleanup)
                .HasColumnType("NUM")
                .HasColumnName("cleanup");
            entity.Property(e => e.Count)
                .HasColumnType("INT")
                .HasColumnName("count");
            entity.Property(e => e.HighlightDoodadId)
                .HasColumnType("INT")
                .HasColumnName("highlight_doodad_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.ItemId)
                .HasColumnType("INT")
                .HasColumnName("item_id");
        });

        modelBuilder.Entity<QuestActObjAbilityLevel>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("quest_act_obj_ability_levels");

            entity.Property(e => e.AbilityId)
                .HasColumnType("INT")
                .HasColumnName("ability_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Level)
                .HasColumnType("INT")
                .HasColumnName("level");
            entity.Property(e => e.QuestActObjAliasId)
                .HasColumnType("INT")
                .HasColumnName("quest_act_obj_alias_id");
            entity.Property(e => e.UseAlias)
                .HasColumnType("NUM")
                .HasColumnName("use_alias");
        });

        modelBuilder.Entity<QuestActObjAggro>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("quest_act_obj_aggros");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.QuestActObjAliasId)
                .HasColumnType("INT")
                .HasColumnName("quest_act_obj_alias_id");
            entity.Property(e => e.Range)
                .HasColumnType("INT")
                .HasColumnName("range");
            entity.Property(e => e.Rank1)
                .HasColumnType("INT")
                .HasColumnName("rank1");
            entity.Property(e => e.Rank1Item)
                .HasColumnType("NUM")
                .HasColumnName("rank1_item");
            entity.Property(e => e.Rank1Ratio)
                .HasColumnType("INT")
                .HasColumnName("rank1_ratio");
            entity.Property(e => e.Rank2)
                .HasColumnType("INT")
                .HasColumnName("rank2");
            entity.Property(e => e.Rank2Item)
                .HasColumnType("NUM")
                .HasColumnName("rank2_item");
            entity.Property(e => e.Rank2Ratio)
                .HasColumnType("INT")
                .HasColumnName("rank2_ratio");
            entity.Property(e => e.Rank3)
                .HasColumnType("INT")
                .HasColumnName("rank3");
            entity.Property(e => e.Rank3Item)
                .HasColumnType("NUM")
                .HasColumnName("rank3_item");
            entity.Property(e => e.Rank3Ratio)
                .HasColumnType("INT")
                .HasColumnName("rank3_ratio");
            entity.Property(e => e.UseAlias)
                .HasColumnType("NUM")
                .HasColumnName("use_alias");
        });

        modelBuilder.Entity<QuestActObjAlias>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("quest_act_obj_aliases");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name");
        });

        modelBuilder.Entity<QuestActObjCinema>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("quest_act_obj_cinemas");

            entity.Property(e => e.CinemaId)
                .HasColumnType("INT")
                .HasColumnName("cinema_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.QuestActObjAliasId)
                .HasColumnType("INT")
                .HasColumnName("quest_act_obj_alias_id");
            entity.Property(e => e.UseAlias)
                .HasColumnType("NUM")
                .HasColumnName("use_alias");
        });

        modelBuilder.Entity<QuestActObjCompleteQuest>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("quest_act_obj_complete_quests");

            entity.Property(e => e.AcceptWith)
                .HasColumnType("NUM")
                .HasColumnName("accept_with");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.QuestActObjAliasId)
                .HasColumnType("INT")
                .HasColumnName("quest_act_obj_alias_id");
            entity.Property(e => e.QuestId)
                .HasColumnType("INT")
                .HasColumnName("quest_id");
            entity.Property(e => e.UseAlias)
                .HasColumnType("NUM")
                .HasColumnName("use_alias");
        });

        modelBuilder.Entity<QuestActObjCondition>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("quest_act_obj_conditions");

            entity.Property(e => e.ConditionId)
                .HasColumnType("INT")
                .HasColumnName("condition_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.QuestActObjAliasId)
                .HasColumnType("INT")
                .HasColumnName("quest_act_obj_alias_id");
            entity.Property(e => e.QuestContextId)
                .HasColumnType("INT")
                .HasColumnName("quest_context_id");
            entity.Property(e => e.UseAlias)
                .HasColumnType("NUM")
                .HasColumnName("use_alias");
        });

        modelBuilder.Entity<QuestActObjCraft>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("quest_act_obj_crafts");

            entity.Property(e => e.Count)
                .HasColumnType("INT")
                .HasColumnName("count");
            entity.Property(e => e.CraftId)
                .HasColumnType("INT")
                .HasColumnName("craft_id");
            entity.Property(e => e.HighlightDoodadId)
                .HasColumnType("INT")
                .HasColumnName("highlight_doodad_id");
            entity.Property(e => e.HighlightDoodadPhase)
                .HasColumnType("INT")
                .HasColumnName("highlight_doodad_phase");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.QuestActObjAliasId)
                .HasColumnType("INT")
                .HasColumnName("quest_act_obj_alias_id");
            entity.Property(e => e.UseAlias)
                .HasColumnType("NUM")
                .HasColumnName("use_alias");
        });

        modelBuilder.Entity<QuestActObjDistance>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("quest_act_obj_distances");

            entity.Property(e => e.Distance)
                .HasColumnType("INT")
                .HasColumnName("distance");
            entity.Property(e => e.HighlightDoodadId)
                .HasColumnType("INT")
                .HasColumnName("highlight_doodad_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.NpcId)
                .HasColumnType("INT")
                .HasColumnName("npc_id");
            entity.Property(e => e.QuestActObjAliasId)
                .HasColumnType("INT")
                .HasColumnName("quest_act_obj_alias_id");
            entity.Property(e => e.UseAlias)
                .HasColumnType("NUM")
                .HasColumnName("use_alias");
            entity.Property(e => e.Within)
                .HasColumnType("NUM")
                .HasColumnName("within");
        });

        modelBuilder.Entity<QuestActObjDoodadPhaseCheck>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("quest_act_obj_doodad_phase_checks");

            entity.Property(e => e.DoodadId)
                .HasColumnType("INT")
                .HasColumnName("doodad_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Phase1)
                .HasColumnType("INT")
                .HasColumnName("phase1");
            entity.Property(e => e.Phase2)
                .HasColumnType("INT")
                .HasColumnName("phase2");
            entity.Property(e => e.QuestActObjAliasId)
                .HasColumnType("INT")
                .HasColumnName("quest_act_obj_alias_id");
            entity.Property(e => e.UseAlias)
                .HasColumnType("NUM")
                .HasColumnName("use_alias");
        });

        modelBuilder.Entity<QuestActObjEffectFire>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("quest_act_obj_effect_fires");

            entity.Property(e => e.Count)
                .HasColumnType("INT")
                .HasColumnName("count");
            entity.Property(e => e.EffectId)
                .HasColumnType("INT")
                .HasColumnName("effect_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.QuestActObjAliasId)
                .HasColumnType("INT")
                .HasColumnName("quest_act_obj_alias_id");
            entity.Property(e => e.UseAlias)
                .HasColumnType("NUM")
                .HasColumnName("use_alias");
        });

        modelBuilder.Entity<QuestActObjExpressFire>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("quest_act_obj_express_fires");

            entity.Property(e => e.Count)
                .HasColumnType("INT")
                .HasColumnName("count");
            entity.Property(e => e.ExpressKeyId)
                .HasColumnType("INT")
                .HasColumnName("express_key_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.NpcGroupId)
                .HasColumnType("INT")
                .HasColumnName("npc_group_id");
            entity.Property(e => e.QuestActObjAliasId)
                .HasColumnType("INT")
                .HasColumnName("quest_act_obj_alias_id");
            entity.Property(e => e.UseAlias)
                .HasColumnType("NUM")
                .HasColumnName("use_alias");
        });

        modelBuilder.Entity<QuestActObjInteraction>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("quest_act_obj_interactions");

            entity.Property(e => e.Count)
                .HasColumnType("INT")
                .HasColumnName("count");
            entity.Property(e => e.DoodadId)
                .HasColumnType("INT")
                .HasColumnName("doodad_id");
            entity.Property(e => e.HighlightDoodadId)
                .HasColumnType("INT")
                .HasColumnName("highlight_doodad_id");
            entity.Property(e => e.HighlightDoodadPhase)
                .HasColumnType("INT")
                .HasColumnName("highlight_doodad_phase");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Phase)
                .HasColumnType("INT")
                .HasColumnName("phase");
            entity.Property(e => e.QuestActObjAliasId)
                .HasColumnType("INT")
                .HasColumnName("quest_act_obj_alias_id");
            entity.Property(e => e.TeamShare)
                .HasColumnType("NUM")
                .HasColumnName("team_share");
            entity.Property(e => e.UseAlias)
                .HasColumnType("NUM")
                .HasColumnName("use_alias");
            entity.Property(e => e.WiId)
                .HasColumnType("INT")
                .HasColumnName("wi_id");
        });

        modelBuilder.Entity<QuestActObjItemGather>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("quest_act_obj_item_gathers");

            entity.Property(e => e.Cleanup)
                .HasColumnType("NUM")
                .HasColumnName("cleanup");
            entity.Property(e => e.Count)
                .HasColumnType("INT")
                .HasColumnName("count");
            entity.Property(e => e.DestroyWhenDrop)
                .HasColumnType("NUM")
                .HasColumnName("destroy_when_drop");
            entity.Property(e => e.DropWhenDestroy)
                .HasColumnType("NUM")
                .HasColumnName("drop_when_destroy");
            entity.Property(e => e.HighlightDoodadId)
                .HasColumnType("INT")
                .HasColumnName("highlight_doodad_id");
            entity.Property(e => e.HighlightDoodadPhase)
                .HasColumnType("INT")
                .HasColumnName("highlight_doodad_phase");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.ItemId)
                .HasColumnType("INT")
                .HasColumnName("item_id");
            entity.Property(e => e.QuestActObjAliasId)
                .HasColumnType("INT")
                .HasColumnName("quest_act_obj_alias_id");
            entity.Property(e => e.UseAlias)
                .HasColumnType("NUM")
                .HasColumnName("use_alias");
        });

        modelBuilder.Entity<QuestActObjItemGroupGather>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("quest_act_obj_item_group_gathers");

            entity.Property(e => e.Cleanup)
                .HasColumnType("NUM")
                .HasColumnName("cleanup");
            entity.Property(e => e.Count)
                .HasColumnType("INT")
                .HasColumnName("count");
            entity.Property(e => e.DestroyWhenDrop)
                .HasColumnType("NUM")
                .HasColumnName("destroy_when_drop");
            entity.Property(e => e.DropWhenDestroy)
                .HasColumnType("NUM")
                .HasColumnName("drop_when_destroy");
            entity.Property(e => e.HighlightDoodadId)
                .HasColumnType("INT")
                .HasColumnName("highlight_doodad_id");
            entity.Property(e => e.HighlightDoodadPhase)
                .HasColumnType("INT")
                .HasColumnName("highlight_doodad_phase");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.ItemGroupId)
                .HasColumnType("INT")
                .HasColumnName("item_group_id");
            entity.Property(e => e.QuestActObjAliasId)
                .HasColumnType("INT")
                .HasColumnName("quest_act_obj_alias_id");
            entity.Property(e => e.UseAlias)
                .HasColumnType("NUM")
                .HasColumnName("use_alias");
        });

        modelBuilder.Entity<QuestActObjItemGroupUse>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("quest_act_obj_item_group_uses");

            entity.Property(e => e.Count)
                .HasColumnType("INT")
                .HasColumnName("count");
            entity.Property(e => e.DropWhenDestroy)
                .HasColumnType("NUM")
                .HasColumnName("drop_when_destroy");
            entity.Property(e => e.HighlightDoodadId)
                .HasColumnType("INT")
                .HasColumnName("highlight_doodad_id");
            entity.Property(e => e.HighlightDoodadPhase)
                .HasColumnType("INT")
                .HasColumnName("highlight_doodad_phase");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.ItemGroupId)
                .HasColumnType("INT")
                .HasColumnName("item_group_id");
            entity.Property(e => e.QuestActObjAliasId)
                .HasColumnType("INT")
                .HasColumnName("quest_act_obj_alias_id");
            entity.Property(e => e.UseAlias)
                .HasColumnType("NUM")
                .HasColumnName("use_alias");
        });

        modelBuilder.Entity<QuestActObjItemUse>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("quest_act_obj_item_uses");

            entity.Property(e => e.Cinema).HasColumnName("cinema");
            entity.Property(e => e.Count)
                .HasColumnType("INT")
                .HasColumnName("count");
            entity.Property(e => e.DropWhenDestroy)
                .HasColumnType("NUM")
                .HasColumnName("drop_when_destroy");
            entity.Property(e => e.HighlightDoodadId)
                .HasColumnType("INT")
                .HasColumnName("highlight_doodad_id");
            entity.Property(e => e.HighlightDoodadPhase)
                .HasColumnType("INT")
                .HasColumnName("highlight_doodad_phase");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.ItemId)
                .HasColumnType("INT")
                .HasColumnName("item_id");
            entity.Property(e => e.QuestActObjAliasId)
                .HasColumnType("INT")
                .HasColumnName("quest_act_obj_alias_id");
            entity.Property(e => e.UseAlias)
                .HasColumnType("NUM")
                .HasColumnName("use_alias");
        });

        modelBuilder.Entity<QuestActObjLevel>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("quest_act_obj_levels");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Level)
                .HasColumnType("INT")
                .HasColumnName("level");
            entity.Property(e => e.QuestActObjAliasId)
                .HasColumnType("INT")
                .HasColumnName("quest_act_obj_alias_id");
            entity.Property(e => e.UseAlias)
                .HasColumnType("NUM")
                .HasColumnName("use_alias");
        });

        modelBuilder.Entity<QuestActObjMateLevel>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("quest_act_obj_mate_levels");

            entity.Property(e => e.Cleanup)
                .HasColumnType("NUM")
                .HasColumnName("cleanup");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.ItemId)
                .HasColumnType("INT")
                .HasColumnName("item_id");
            entity.Property(e => e.Level)
                .HasColumnType("INT")
                .HasColumnName("level");
            entity.Property(e => e.QuestActObjAliasId)
                .HasColumnType("INT")
                .HasColumnName("quest_act_obj_alias_id");
            entity.Property(e => e.UseAlias)
                .HasColumnType("NUM")
                .HasColumnName("use_alias");
        });

        modelBuilder.Entity<QuestActObjMonsterGroupHunt>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("quest_act_obj_monster_group_hunts");

            entity.Property(e => e.Count)
                .HasColumnType("INT")
                .HasColumnName("count");
            entity.Property(e => e.HighlightDoodadId)
                .HasColumnType("INT")
                .HasColumnName("highlight_doodad_id");
            entity.Property(e => e.HighlightDoodadPhase)
                .HasColumnType("INT")
                .HasColumnName("highlight_doodad_phase");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.QuestActObjAliasId)
                .HasColumnType("INT")
                .HasColumnName("quest_act_obj_alias_id");
            entity.Property(e => e.QuestMonsterGroupId)
                .HasColumnType("INT")
                .HasColumnName("quest_monster_group_id");
            entity.Property(e => e.UseAlias)
                .HasColumnType("NUM")
                .HasColumnName("use_alias");
        });

        modelBuilder.Entity<QuestActObjMonsterHunt>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("quest_act_obj_monster_hunts");

            entity.Property(e => e.Count)
                .HasColumnType("INT")
                .HasColumnName("count");
            entity.Property(e => e.HighlightDoodadId)
                .HasColumnType("INT")
                .HasColumnName("highlight_doodad_id");
            entity.Property(e => e.HighlightDoodadPhase)
                .HasColumnType("INT")
                .HasColumnName("highlight_doodad_phase");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.NpcId)
                .HasColumnType("INT")
                .HasColumnName("npc_id");
            entity.Property(e => e.QuestActObjAliasId)
                .HasColumnType("INT")
                .HasColumnName("quest_act_obj_alias_id");
            entity.Property(e => e.UseAlias)
                .HasColumnType("NUM")
                .HasColumnName("use_alias");
        });

        modelBuilder.Entity<QuestActObjSendMail>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("quest_act_obj_send_mails");

            entity.Property(e => e.Count1)
                .HasColumnType("INT")
                .HasColumnName("count1");
            entity.Property(e => e.Count2)
                .HasColumnType("INT")
                .HasColumnName("count2");
            entity.Property(e => e.Count3)
                .HasColumnType("INT")
                .HasColumnName("count3");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Item1Id)
                .HasColumnType("INT")
                .HasColumnName("item1_id");
            entity.Property(e => e.Item2Id)
                .HasColumnType("INT")
                .HasColumnName("item2_id");
            entity.Property(e => e.Item3Id)
                .HasColumnType("INT")
                .HasColumnName("item3_id");
            entity.Property(e => e.QuestActObjAliasId)
                .HasColumnType("INT")
                .HasColumnName("quest_act_obj_alias_id");
            entity.Property(e => e.UseAlias)
                .HasColumnType("NUM")
                .HasColumnName("use_alias");
        });

        modelBuilder.Entity<QuestActObjSphere>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("quest_act_obj_spheres");

            entity.Property(e => e.Cinema).HasColumnName("cinema");
            entity.Property(e => e.HighlightDoodadId)
                .HasColumnType("INT")
                .HasColumnName("highlight_doodad_id");
            entity.Property(e => e.HighlightDoodadPhase)
                .HasColumnType("INT")
                .HasColumnName("highlight_doodad_phase");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.NpcId)
                .HasColumnType("INT")
                .HasColumnName("npc_id");
            entity.Property(e => e.QuestActObjAliasId)
                .HasColumnType("INT")
                .HasColumnName("quest_act_obj_alias_id");
            entity.Property(e => e.SphereId)
                .HasColumnType("INT")
                .HasColumnName("sphere_id");
            entity.Property(e => e.UseAlias)
                .HasColumnType("NUM")
                .HasColumnName("use_alias");
        });

        modelBuilder.Entity<QuestActObjTalk>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("quest_act_obj_talks");

            entity.Property(e => e.HighlightDoodadId)
                .HasColumnType("INT")
                .HasColumnName("highlight_doodad_id");
            entity.Property(e => e.HighlightDoodadPhase)
                .HasColumnType("INT")
                .HasColumnName("highlight_doodad_phase");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.ItemId)
                .HasColumnType("INT")
                .HasColumnName("item_id");
            entity.Property(e => e.NpcId)
                .HasColumnType("INT")
                .HasColumnName("npc_id");
            entity.Property(e => e.QuestActObjAliasId)
                .HasColumnType("INT")
                .HasColumnName("quest_act_obj_alias_id");
            entity.Property(e => e.TeamShare)
                .HasColumnType("NUM")
                .HasColumnName("team_share");
            entity.Property(e => e.UseAlias)
                .HasColumnType("NUM")
                .HasColumnName("use_alias");
        });

        modelBuilder.Entity<QuestActObjTalkNpcGroup>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("quest_act_obj_talk_npc_groups");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.NpcGroupId)
                .HasColumnType("INT")
                .HasColumnName("npc_group_id");
            entity.Property(e => e.QuestActObjAliasId)
                .HasColumnType("INT")
                .HasColumnName("quest_act_obj_alias_id");
            entity.Property(e => e.UseAlias)
                .HasColumnType("NUM")
                .HasColumnName("use_alias");
        });

        modelBuilder.Entity<QuestActObjZoneKill>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("quest_act_obj_zone_kills");

            entity.Property(e => e.CountNpc)
                .HasColumnType("INT")
                .HasColumnName("count_npc");
            entity.Property(e => e.CountPk)
                .HasColumnType("INT")
                .HasColumnName("count_pk");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.IsParty)
                .HasColumnType("NUM")
                .HasColumnName("is_party");
            entity.Property(e => e.LvMax)
                .HasColumnType("INT")
                .HasColumnName("lv_max");
            entity.Property(e => e.LvMaxNpc)
                .HasColumnType("INT")
                .HasColumnName("lv_max_npc");
            entity.Property(e => e.LvMin)
                .HasColumnType("INT")
                .HasColumnName("lv_min");
            entity.Property(e => e.LvMinNpc)
                .HasColumnType("INT")
                .HasColumnName("lv_min_npc");
            entity.Property(e => e.NpcFactionExclusive)
                .HasColumnType("NUM")
                .HasColumnName("npc_faction_exclusive");
            entity.Property(e => e.NpcFactionId)
                .HasColumnType("INT")
                .HasColumnName("npc_faction_id");
            entity.Property(e => e.PcFactionExclusive)
                .HasColumnType("NUM")
                .HasColumnName("pc_faction_exclusive");
            entity.Property(e => e.PcFactionId)
                .HasColumnType("INT")
                .HasColumnName("pc_faction_id");
            entity.Property(e => e.QuestActObjAliasId)
                .HasColumnType("INT")
                .HasColumnName("quest_act_obj_alias_id");
            entity.Property(e => e.TeamShare)
                .HasColumnType("NUM")
                .HasColumnName("team_share");
            entity.Property(e => e.UseAlias)
                .HasColumnType("NUM")
                .HasColumnName("use_alias");
            entity.Property(e => e.ZoneId)
                .HasColumnType("INT")
                .HasColumnName("zone_id");
        });

        modelBuilder.Entity<QuestActObjZoneMonsterHunt>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("quest_act_obj_zone_monster_hunts");

            entity.Property(e => e.Count)
                .HasColumnType("INT")
                .HasColumnName("count");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.QuestActObjAliasId)
                .HasColumnType("INT")
                .HasColumnName("quest_act_obj_alias_id");
            entity.Property(e => e.UseAlias)
                .HasColumnType("NUM")
                .HasColumnName("use_alias");
            entity.Property(e => e.ZoneId)
                .HasColumnType("INT")
                .HasColumnName("zone_id");
        });

        modelBuilder.Entity<QuestActObjZoneNpcTalk>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("quest_act_obj_zone_npc_talks");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.NpcId)
                .HasColumnType("INT")
                .HasColumnName("npc_id");
            entity.Property(e => e.QuestActObjAliasId)
                .HasColumnType("INT")
                .HasColumnName("quest_act_obj_alias_id");
            entity.Property(e => e.UseAlias)
                .HasColumnType("NUM")
                .HasColumnName("use_alias");
        });

        modelBuilder.Entity<QuestActObjZoneQuestComplete>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("quest_act_obj_zone_quest_completes");

            entity.Property(e => e.Count)
                .HasColumnType("INT")
                .HasColumnName("count");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.QuestActObjAliasId)
                .HasColumnType("INT")
                .HasColumnName("quest_act_obj_alias_id");
            entity.Property(e => e.UseAlias)
                .HasColumnType("NUM")
                .HasColumnName("use_alias");
            entity.Property(e => e.ZoneId)
                .HasColumnType("INT")
                .HasColumnName("zone_id");
        });

        modelBuilder.Entity<QuestActSupplyAaPoint>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("quest_act_supply_aa_points");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Point)
                .HasColumnType("INT")
                .HasColumnName("point");
        });

        modelBuilder.Entity<QuestActSupplyAppellation>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("quest_act_supply_appellations");

            entity.Property(e => e.AppellationId)
                .HasColumnType("INT")
                .HasColumnName("appellation_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
        });

        modelBuilder.Entity<QuestActSupplyCopper>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("quest_act_supply_coppers");

            entity.Property(e => e.Amount)
                .HasColumnType("INT")
                .HasColumnName("amount");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
        });

        modelBuilder.Entity<QuestActSupplyCrimePoint>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("quest_act_supply_crime_points");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Point)
                .HasColumnType("INT")
                .HasColumnName("point");
        });

        modelBuilder.Entity<QuestActSupplyExp>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("quest_act_supply_exps");

            entity.Property(e => e.Exp)
                .HasColumnType("INT")
                .HasColumnName("exp");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
        });

        modelBuilder.Entity<QuestActSupplyHonorPoint>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("quest_act_supply_honor_points");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Point)
                .HasColumnType("INT")
                .HasColumnName("point");
        });

        modelBuilder.Entity<QuestActSupplyInteraction>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("quest_act_supply_interactions");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.WiId)
                .HasColumnType("INT")
                .HasColumnName("wi_id");
        });

        modelBuilder.Entity<QuestActSupplyItem>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("quest_act_supply_items");

            entity.Property(e => e.Cleanup)
                .HasColumnType("NUM")
                .HasColumnName("cleanup");
            entity.Property(e => e.Count)
                .HasColumnType("INT")
                .HasColumnName("count");
            entity.Property(e => e.DestroyWhenDrop)
                .HasColumnType("NUM")
                .HasColumnName("destroy_when_drop");
            entity.Property(e => e.DropWhenDestroy)
                .HasColumnType("NUM")
                .HasColumnName("drop_when_destroy");
            entity.Property(e => e.GradeId)
                .HasColumnType("INT")
                .HasColumnName("grade_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.ItemId)
                .HasColumnType("INT")
                .HasColumnName("item_id");
            entity.Property(e => e.ShowActionBar)
                .HasColumnType("NUM")
                .HasColumnName("show_action_bar");
        });

        modelBuilder.Entity<QuestActSupplyJuryPoint>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("quest_act_supply_jury_points");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Point)
                .HasColumnType("INT")
                .HasColumnName("point");
        });

        modelBuilder.Entity<QuestActSupplyLivingPoint>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("quest_act_supply_living_points");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Point)
                .HasColumnType("INT")
                .HasColumnName("point");
        });

        modelBuilder.Entity<QuestActSupplyLp>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("quest_act_supply_lps");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Lp)
                .HasColumnType("INT")
                .HasColumnName("lp");
        });

        modelBuilder.Entity<QuestActSupplyRemoveItem>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("quest_act_supply_remove_items");

            entity.Property(e => e.Count)
                .HasColumnType("INT")
                .HasColumnName("count");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.ItemId)
                .HasColumnType("INT")
                .HasColumnName("item_id");
        });

        modelBuilder.Entity<QuestActSupplySelectiveItem>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("quest_act_supply_selective_items");

            entity.Property(e => e.Count)
                .HasColumnType("INT")
                .HasColumnName("count");
            entity.Property(e => e.GradeId)
                .HasColumnType("INT")
                .HasColumnName("grade_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.ItemId)
                .HasColumnType("INT")
                .HasColumnName("item_id");
        });

        modelBuilder.Entity<QuestActSupplySkill>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("quest_act_supply_skills");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.SkillId)
                .HasColumnType("INT")
                .HasColumnName("skill_id");
        });

        modelBuilder.Entity<QuestCamera>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("quest_cameras");

            entity.Property(e => e.CameraOffsetX).HasColumnName("camera_offset_x");
            entity.Property(e => e.CameraOffsetY).HasColumnName("camera_offset_y");
            entity.Property(e => e.CameraOffsetZ).HasColumnName("camera_offset_z");
            entity.Property(e => e.Comment).HasColumnName("comment");
            entity.Property(e => e.Dof)
                .HasColumnType("NUM")
                .HasColumnName("dof");
            entity.Property(e => e.Fov).HasColumnName("fov");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Interpolate)
                .HasColumnType("NUM")
                .HasColumnName("interpolate");
            entity.Property(e => e.Invisible)
                .HasColumnType("NUM")
                .HasColumnName("invisible");
            entity.Property(e => e.NpcOffsetX).HasColumnName("npc_offset_x");
            entity.Property(e => e.NpcOffsetY).HasColumnName("npc_offset_y");
            entity.Property(e => e.NpcOffsetZ).HasColumnName("npc_offset_z");
            entity.Property(e => e.NvBokehSize).HasColumnName("nv_bokeh_size");
            entity.Property(e => e.NvDof)
                .HasColumnType("NUM")
                .HasColumnName("nv_dof");
            entity.Property(e => e.NvIntensity).HasColumnName("nv_intensity");
            entity.Property(e => e.NvLuminance).HasColumnName("nv_luminance");
        });

        modelBuilder.Entity<QuestCategory>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("quest_categories");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.Translate)
                .HasColumnType("NUM")
                .HasColumnName("translate");
        });

        modelBuilder.Entity<QuestChatBubble>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("quest_chat_bubbles");

            entity.Property(e => e.Angle)
                .HasColumnType("INT")
                .HasColumnName("angle");
            entity.Property(e => e.CameraId)
                .HasColumnType("INT")
                .HasColumnName("camera_id");
            entity.Property(e => e.ChangeSpeakerName).HasColumnName("change_speaker_name");
            entity.Property(e => e.ChatBubbleKindId)
                .HasColumnType("INT")
                .HasColumnName("chat_bubble_kind_id");
            entity.Property(e => e.Facial).HasColumnName("facial");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.IsStart)
                .HasColumnType("NUM")
                .HasColumnName("is_start");
            entity.Property(e => e.NextBubble)
                .HasColumnType("INT")
                .HasColumnName("next_bubble");
            entity.Property(e => e.NpcGroupId)
                .HasColumnType("INT")
                .HasColumnName("npc_group_id");
            entity.Property(e => e.NpcId)
                .HasColumnType("INT")
                .HasColumnName("npc_id");
            entity.Property(e => e.NpcSpawnerId)
                .HasColumnType("INT")
                .HasColumnName("npc_spawner_id");
            entity.Property(e => e.QuestComponentId)
                .HasColumnType("INT")
                .HasColumnName("quest_component_id");
            entity.Property(e => e.SoundId)
                .HasColumnType("INT")
                .HasColumnName("sound_id");
            entity.Property(e => e.Speech).HasColumnName("speech");
        });

        modelBuilder.Entity<QuestComponent>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("quest_components");

            entity.Property(e => e.AiCommandSetId)
                .HasColumnType("INT")
                .HasColumnName("ai_command_set_id");
            entity.Property(e => e.AiPathName).HasColumnName("ai_path_name");
            entity.Property(e => e.AiPathTypeId)
                .HasColumnType("INT")
                .HasColumnName("ai_path_type_id");
            entity.Property(e => e.BuffId)
                .HasColumnType("INT")
                .HasColumnName("buff_id");
            entity.Property(e => e.CinemaId)
                .HasColumnType("INT")
                .HasColumnName("cinema_id");
            entity.Property(e => e.ComponentKindId)
                .HasColumnType("INT")
                .HasColumnName("component_kind_id");
            entity.Property(e => e.HideQuestMarker)
                .HasColumnType("NUM")
                .HasColumnName("hide_quest_marker");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.NextComponent)
                .HasColumnType("INT")
                .HasColumnName("next_component");
            entity.Property(e => e.NpcAiId)
                .HasColumnType("INT")
                .HasColumnName("npc_ai_id");
            entity.Property(e => e.NpcId)
                .HasColumnType("INT")
                .HasColumnName("npc_id");
            entity.Property(e => e.NpcSpawnerId)
                .HasColumnType("INT")
                .HasColumnName("npc_spawner_id");
            entity.Property(e => e.OrUnitReqs)
                .HasColumnType("NUM")
                .HasColumnName("or_unit_reqs");
            entity.Property(e => e.PlayCinemaBeforeBubble)
                .HasColumnType("NUM")
                .HasColumnName("play_cinema_before_bubble");
            entity.Property(e => e.QuestContextId)
                .HasColumnType("INT")
                .HasColumnName("quest_context_id");
            entity.Property(e => e.SkillId)
                .HasColumnType("INT")
                .HasColumnName("skill_id");
            entity.Property(e => e.SkillSelf)
                .HasColumnType("NUM")
                .HasColumnName("skill_self");
            entity.Property(e => e.SoundId)
                .HasColumnType("INT")
                .HasColumnName("sound_id");
            entity.Property(e => e.SummaryVoiceId)
                .HasColumnType("INT")
                .HasColumnName("summary_voice_id");
        });

        modelBuilder.Entity<QuestComponentText>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("quest_component_texts");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.QuestComponentId)
                .HasColumnType("INT")
                .HasColumnName("quest_component_id");
            entity.Property(e => e.QuestComponentTextKindId)
                .HasColumnType("INT")
                .HasColumnName("quest_component_text_kind_id");
            entity.Property(e => e.Text).HasColumnName("text");
        });

        modelBuilder.Entity<QuestContext>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("quest_contexts");

            entity.Property(e => e.CategoryId)
                .HasColumnType("INT")
                .HasColumnName("category_id");
            entity.Property(e => e.ChapterIdx)
                .HasColumnType("INT")
                .HasColumnName("chapter_idx");
            entity.Property(e => e.Degree)
                .HasColumnType("INT")
                .HasColumnName("degree");
            entity.Property(e => e.DetailId)
                .HasColumnType("INT")
                .HasColumnName("detail_id");
            entity.Property(e => e.GradeId)
                .HasColumnType("INT")
                .HasColumnName("grade_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.LetItDone)
                .HasColumnType("NUM")
                .HasColumnName("let_it_done");
            entity.Property(e => e.Level)
                .HasColumnType("INT")
                .HasColumnName("level");
            entity.Property(e => e.MilestoneId)
                .HasColumnType("INT")
                .HasColumnName("milestone_id");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.QuestIdx)
                .HasColumnType("INT")
                .HasColumnName("quest_idx");
            entity.Property(e => e.Repeatable)
                .HasColumnType("NUM")
                .HasColumnName("repeatable");
            entity.Property(e => e.RestartOnFail)
                .HasColumnType("NUM")
                .HasColumnName("restart_on_fail");
            entity.Property(e => e.Score)
                .HasColumnType("INT")
                .HasColumnName("score");
            entity.Property(e => e.Selective)
                .HasColumnType("NUM")
                .HasColumnName("selective");
            entity.Property(e => e.Successive)
                .HasColumnType("NUM")
                .HasColumnName("successive");
            entity.Property(e => e.Translate)
                .HasColumnType("NUM")
                .HasColumnName("translate");
            entity.Property(e => e.UseAcceptMessage)
                .HasColumnType("NUM")
                .HasColumnName("use_accept_message");
            entity.Property(e => e.UseCompleteMessage)
                .HasColumnType("NUM")
                .HasColumnName("use_complete_message");
            entity.Property(e => e.UseQuestCamera)
                .HasColumnType("NUM")
                .HasColumnName("use_quest_camera");
            entity.Property(e => e.ZoneId)
                .HasColumnType("INT")
                .HasColumnName("zone_id");
        });

        modelBuilder.Entity<QuestContextText>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("quest_context_texts");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.QuestContextId)
                .HasColumnType("INT")
                .HasColumnName("quest_context_id");
            entity.Property(e => e.QuestContextTextKindId)
                .HasColumnType("INT")
                .HasColumnName("quest_context_text_kind_id");
            entity.Property(e => e.Text).HasColumnName("text");
        });

        modelBuilder.Entity<QuestItemGroup>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("quest_item_groups");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name");
        });

        modelBuilder.Entity<QuestItemGroupItem>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("quest_item_group_items");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.ItemId)
                .HasColumnType("INT")
                .HasColumnName("item_id");
            entity.Property(e => e.QuestItemGroupId)
                .HasColumnType("INT")
                .HasColumnName("quest_item_group_id");
        });

        modelBuilder.Entity<QuestMail>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("quest_mails");

            entity.Property(e => e.CategoryId)
                .HasColumnType("INT")
                .HasColumnName("category_id");
            entity.Property(e => e.Comment).HasColumnName("comment");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.NpcId)
                .HasColumnType("INT")
                .HasColumnName("npc_id");
            entity.Property(e => e.QuestMailAttachmentId)
                .HasColumnType("INT")
                .HasColumnName("quest_mail_attachment_id");
            entity.Property(e => e.SendMoney)
                .HasColumnType("INT")
                .HasColumnName("send_money");
            entity.Property(e => e.SenderName).HasColumnName("sender_name");
            entity.Property(e => e.Text).HasColumnName("text");
        });

        modelBuilder.Entity<QuestMailAttachment>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("quest_mail_attachments");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name");
        });

        modelBuilder.Entity<QuestMailAttachmentItem>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("quest_mail_attachment_items");

            entity.Property(e => e.Count)
                .HasColumnType("INT")
                .HasColumnName("count");
            entity.Property(e => e.GradeId)
                .HasColumnType("INT")
                .HasColumnName("grade_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.ItemId)
                .HasColumnType("INT")
                .HasColumnName("item_id");
            entity.Property(e => e.QuestMailAttachmentId)
                .HasColumnType("INT")
                .HasColumnName("quest_mail_attachment_id");
        });

        modelBuilder.Entity<QuestMailSend>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("quest_mail_sends");

            entity.Property(e => e.ActabilityGroupId)
                .HasColumnType("INT")
                .HasColumnName("actability_group_id");
            entity.Property(e => e.ActabilityPoint)
                .HasColumnType("INT")
                .HasColumnName("actability_point");
            entity.Property(e => e.CategoryId)
                .HasColumnType("INT")
                .HasColumnName("category_id");
            entity.Property(e => e.ComponentId)
                .HasColumnType("INT")
                .HasColumnName("component_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Level)
                .HasColumnType("INT")
                .HasColumnName("level");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.OrUnitReqs)
                .HasColumnType("NUM")
                .HasColumnName("or_unit_reqs");
            entity.Property(e => e.QuestId)
                .HasColumnType("INT")
                .HasColumnName("quest_id");
            entity.Property(e => e.QuestMailId)
                .HasColumnType("INT")
                .HasColumnName("quest_mail_id");
            entity.Property(e => e.SphereId)
                .HasColumnType("INT")
                .HasColumnName("sphere_id");
        });

        modelBuilder.Entity<QuestMonsterGroup>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("quest_monster_groups");

            entity.Property(e => e.CategoryId)
                .HasColumnType("INT")
                .HasColumnName("category_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name");
        });

        modelBuilder.Entity<QuestMonsterNpc>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("quest_monster_npcs");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.NpcId)
                .HasColumnType("INT")
                .HasColumnName("npc_id");
            entity.Property(e => e.QuestMonsterGroupId)
                .HasColumnType("INT")
                .HasColumnName("quest_monster_group_id");
        });

        modelBuilder.Entity<QuestName>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("quest_names");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.QuestContextId)
                .HasColumnType("INT")
                .HasColumnName("quest_context_id");
            entity.Property(e => e.QuestNameKindId)
                .HasColumnType("INT")
                .HasColumnName("quest_name_kind_id");
        });

        modelBuilder.Entity<QuestSupply>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("quest_supplies");

            entity.Property(e => e.Copper)
                .HasColumnType("INT")
                .HasColumnName("copper");
            entity.Property(e => e.Exp)
                .HasColumnType("INT")
                .HasColumnName("exp");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Level)
                .HasColumnType("INT")
                .HasColumnName("level");
        });

        modelBuilder.Entity<QuestTask>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("quest_tasks");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Level)
                .HasColumnType("INT")
                .HasColumnName("level");
            entity.Property(e => e.Name).HasColumnName("name");
        });

        modelBuilder.Entity<QuestTaskQuest>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("quest_task_quests");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.QuestId)
                .HasColumnType("INT")
                .HasColumnName("quest_id");
            entity.Property(e => e.QuestTaskId)
                .HasColumnType("INT")
                .HasColumnName("quest_task_id");
        });

        modelBuilder.Entity<RaceTrack>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("race_tracks");

            entity.Property(e => e.DoodadGroupId)
                .HasColumnType("INT")
                .HasColumnName("doodad_group_id");
            entity.Property(e => e.DoodadId)
                .HasColumnType("INT")
                .HasColumnName("doodad_id");
            entity.Property(e => e.EndBuffId)
                .HasColumnType("INT")
                .HasColumnName("end_buff_id");
            entity.Property(e => e.EndNpcId)
                .HasColumnType("INT")
                .HasColumnName("end_npc_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.RaceLoop)
                .HasColumnType("INT")
                .HasColumnName("race_loop");
            entity.Property(e => e.ReadyBuffId)
                .HasColumnType("INT")
                .HasColumnName("ready_buff_id");
            entity.Property(e => e.ReadyDelay)
                .HasColumnType("INT")
                .HasColumnName("ready_delay");
            entity.Property(e => e.ReadyNpcId)
                .HasColumnType("INT")
                .HasColumnName("ready_npc_id");
            entity.Property(e => e.RecordMax)
                .HasColumnType("INT")
                .HasColumnName("record_max");
            entity.Property(e => e.RecordMin)
                .HasColumnType("INT")
                .HasColumnName("record_min");
            entity.Property(e => e.StartBuffId)
                .HasColumnType("INT")
                .HasColumnName("start_buff_id");
            entity.Property(e => e.StartDelay)
                .HasColumnType("INT")
                .HasColumnName("start_delay");
            entity.Property(e => e.StartNpcId)
                .HasColumnType("INT")
                .HasColumnName("start_npc_id");
            entity.Property(e => e.WaitDelay)
                .HasColumnType("INT")
                .HasColumnName("wait_delay");
            entity.Property(e => e.ZoneId)
                .HasColumnType("INT")
                .HasColumnName("zone_id");
        });

        modelBuilder.Entity<RaceTrackShape>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("race_track_shapes");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.RaceTrackId)
                .HasColumnType("INT")
                .HasColumnName("race_track_id");
            entity.Property(e => e.ShapeOrder)
                .HasColumnType("INT")
                .HasColumnName("shape_order");
            entity.Property(e => e.V1)
                .HasColumnType("INT")
                .HasColumnName("v1");
        });

        modelBuilder.Entity<Rank>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("ranks");

            entity.Property(e => e.DayOfWeekId)
                .HasColumnType("INT")
                .HasColumnName("day_of_week_id");
            entity.Property(e => e.EdDay)
                .HasColumnType("INT")
                .HasColumnName("ed_day");
            entity.Property(e => e.EdHour)
                .HasColumnType("INT")
                .HasColumnName("ed_hour");
            entity.Property(e => e.EdMin)
                .HasColumnType("INT")
                .HasColumnName("ed_min");
            entity.Property(e => e.EdMonth)
                .HasColumnType("INT")
                .HasColumnName("ed_month");
            entity.Property(e => e.EdYear)
                .HasColumnType("INT")
                .HasColumnName("ed_year");
            entity.Property(e => e.EndTime)
                .HasColumnType("INT")
                .HasColumnName("end_time");
            entity.Property(e => e.EndTimeAlarm)
                .HasColumnType("INT")
                .HasColumnName("end_time_alarm");
            entity.Property(e => e.EndTimeAlarmMsg).HasColumnName("end_time_alarm_msg");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.RankKindId)
                .HasColumnType("INT")
                .HasColumnName("rank_kind_id");
            entity.Property(e => e.ResetWeek)
                .HasColumnType("INT")
                .HasColumnName("reset_week");
            entity.Property(e => e.StDay)
                .HasColumnType("INT")
                .HasColumnName("st_day");
            entity.Property(e => e.StHour)
                .HasColumnType("INT")
                .HasColumnName("st_hour");
            entity.Property(e => e.StMin)
                .HasColumnType("INT")
                .HasColumnName("st_min");
            entity.Property(e => e.StMonth)
                .HasColumnType("INT")
                .HasColumnName("st_month");
            entity.Property(e => e.StYear)
                .HasColumnType("INT")
                .HasColumnName("st_year");
            entity.Property(e => e.StartTime)
                .HasColumnType("INT")
                .HasColumnName("start_time");
            entity.Property(e => e.StartTimeAlarm)
                .HasColumnType("INT")
                .HasColumnName("start_time_alarm");
            entity.Property(e => e.StartTimeAlarmMsg).HasColumnName("start_time_alarm_msg");
            entity.Property(e => e.V1).HasColumnName("v1");
            entity.Property(e => e.V2).HasColumnName("v2");
            entity.Property(e => e.ZoneGroupId)
                .HasColumnType("INT")
                .HasColumnName("zone_group_id");
        });

        modelBuilder.Entity<RankReward>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("rank_rewards");

            entity.Property(e => e.AppellationId)
                .HasColumnType("INT")
                .HasColumnName("appellation_id");
            entity.Property(e => e.Comment).HasColumnName("comment");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.ItemCount)
                .HasColumnType("INT")
                .HasColumnName("item_count");
            entity.Property(e => e.ItemGradeId)
                .HasColumnType("INT")
                .HasColumnName("item_grade_id");
            entity.Property(e => e.ItemId)
                .HasColumnType("INT")
                .HasColumnName("item_id");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.Weeks)
                .HasColumnType("INT")
                .HasColumnName("weeks");
        });

        modelBuilder.Entity<RankRewardLink>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("rank_reward_links");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.RankId)
                .HasColumnType("INT")
                .HasColumnName("rank_id");
            entity.Property(e => e.RankRewardId)
                .HasColumnType("INT")
                .HasColumnName("rank_reward_id");
            entity.Property(e => e.RankScopeId)
                .HasColumnType("INT")
                .HasColumnName("rank_scope_id");
        });

        modelBuilder.Entity<RankScope>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("rank_scopes");

            entity.Property(e => e.Comment).HasColumnName("comment");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.ScopeFrom)
                .HasColumnType("INT")
                .HasColumnName("scope_from");
            entity.Property(e => e.ScopeTo)
                .HasColumnType("INT")
                .HasColumnName("scope_to");
        });

        modelBuilder.Entity<RankScopeLink>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("rank_scope_links");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.RankId)
                .HasColumnType("INT")
                .HasColumnName("rank_id");
            entity.Property(e => e.RankScopeId)
                .HasColumnType("INT")
                .HasColumnName("rank_scope_id");
        });

        modelBuilder.Entity<RecoverExpEffect>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("recover_exp_effects");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.NeedLaborPower)
                .HasColumnType("NUM")
                .HasColumnName("need_labor_power");
            entity.Property(e => e.NeedMoney)
                .HasColumnType("NUM")
                .HasColumnName("need_money");
            entity.Property(e => e.NeedPriest)
                .HasColumnType("NUM")
                .HasColumnName("need_priest");
            entity.Property(e => e.Penaltied)
                .HasColumnType("NUM")
                .HasColumnName("penaltied");
        });

        modelBuilder.Entity<RepairSlaveEffect>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("repair_slave_effects");

            entity.Property(e => e.Health)
                .HasColumnType("INT")
                .HasColumnName("health");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Mana)
                .HasColumnType("INT")
                .HasColumnName("mana");
        });

        modelBuilder.Entity<RepairableSlafe>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("repairable_slaves");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.RepairSlaveEffectId)
                .HasColumnType("INT")
                .HasColumnName("repair_slave_effect_id");
            entity.Property(e => e.SlaveId)
                .HasColumnType("INT")
                .HasColumnName("slave_id");
        });

        modelBuilder.Entity<ReplaceChat>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("replace_chats");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name");
        });

        modelBuilder.Entity<ReplaceChatKey>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("replace_chat_keys");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Key).HasColumnName("key");
            entity.Property(e => e.ReplaceChatId)
                .HasColumnType("INT")
                .HasColumnName("replace_chat_id");
        });

        modelBuilder.Entity<ReplaceChatText>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("replace_chat_texts");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.ReplaceChatId)
                .HasColumnType("INT")
                .HasColumnName("replace_chat_id");
            entity.Property(e => e.Text).HasColumnName("text");
        });

        modelBuilder.Entity<ReportCrimeEffect>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("report_crime_effects");

            entity.Property(e => e.CrimeKindId)
                .HasColumnType("INT")
                .HasColumnName("crime_kind_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Value)
                .HasColumnType("INT")
                .HasColumnName("value");
        });

        modelBuilder.Entity<ResetAoeDiminishingEffect>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("reset_aoe_diminishing_effects");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
        });

        modelBuilder.Entity<RestoreManaEffect>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("restore_mana_effects");

            entity.Property(e => e.FixedMax)
                .HasColumnType("INT")
                .HasColumnName("fixed_max");
            entity.Property(e => e.FixedMin)
                .HasColumnType("INT")
                .HasColumnName("fixed_min");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.LevelMd).HasColumnName("level_md");
            entity.Property(e => e.LevelVaEnd)
                .HasColumnType("INT")
                .HasColumnName("level_va_end");
            entity.Property(e => e.LevelVaStart)
                .HasColumnType("INT")
                .HasColumnName("level_va_start");
            entity.Property(e => e.Percent)
                .HasColumnType("NUM")
                .HasColumnName("percent");
            entity.Property(e => e.UseFixedValue)
                .HasColumnType("NUM")
                .HasColumnName("use_fixed_value");
            entity.Property(e => e.UseLevelValue)
                .HasColumnType("NUM")
                .HasColumnName("use_level_value");
        });

        modelBuilder.Entity<ResurrectionWaitingTime>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("resurrection_waiting_times");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.PenaltyDuration).HasColumnName("penalty_duration");
            entity.Property(e => e.SiegeWaitingTime).HasColumnName("siege_waiting_time");
            entity.Property(e => e.WaitingTime).HasColumnName("waiting_time");
        });

        modelBuilder.Entity<ReturnPoint>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("return_points");

            entity.Property(e => e.EditorName).HasColumnName("editor_name");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.MilestoneId)
                .HasColumnType("INT")
                .HasColumnName("milestone_id");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.QuestCategoryId)
                .HasColumnType("INT")
                .HasColumnName("quest_category_id");
        });

        modelBuilder.Entity<ScheduleItem>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("schedule_items");

            entity.Property(e => e.ActiveTake)
                .HasColumnType("NUM")
                .HasColumnName("active_take");
            entity.Property(e => e.DisableKeyString).HasColumnName("disable_key_string");
            entity.Property(e => e.EdDay)
                .HasColumnType("INT")
                .HasColumnName("ed_day");
            entity.Property(e => e.EdHour)
                .HasColumnType("INT")
                .HasColumnName("ed_hour");
            entity.Property(e => e.EdMin)
                .HasColumnType("INT")
                .HasColumnName("ed_min");
            entity.Property(e => e.EdMonth)
                .HasColumnType("INT")
                .HasColumnName("ed_month");
            entity.Property(e => e.EdYear)
                .HasColumnType("INT")
                .HasColumnName("ed_year");
            entity.Property(e => e.EnableKeyString).HasColumnName("enable_key_string");
            entity.Property(e => e.GiveMax)
                .HasColumnType("INT")
                .HasColumnName("give_max");
            entity.Property(e => e.GiveTerm)
                .HasColumnType("INT")
                .HasColumnName("give_term");
            entity.Property(e => e.IconPath).HasColumnName("icon_path");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.ItemCount)
                .HasColumnType("INT")
                .HasColumnName("item_count");
            entity.Property(e => e.ItemId)
                .HasColumnType("INT")
                .HasColumnName("item_id");
            entity.Property(e => e.KindId)
                .HasColumnType("INT")
                .HasColumnName("kind_id");
            entity.Property(e => e.LabelKeyString).HasColumnName("label_key_string");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.OnAir)
                .HasColumnType("NUM")
                .HasColumnName("on_air");
            entity.Property(e => e.PremiumGradeId)
                .HasColumnType("INT")
                .HasColumnName("premium_grade_id");
            entity.Property(e => e.ShowWhenever)
                .HasColumnType("NUM")
                .HasColumnName("show_whenever");
            entity.Property(e => e.ShowWherever)
                .HasColumnType("NUM")
                .HasColumnName("show_wherever");
            entity.Property(e => e.StDay)
                .HasColumnType("INT")
                .HasColumnName("st_day");
            entity.Property(e => e.StHour)
                .HasColumnType("INT")
                .HasColumnName("st_hour");
            entity.Property(e => e.StMin)
                .HasColumnType("INT")
                .HasColumnName("st_min");
            entity.Property(e => e.StMonth)
                .HasColumnType("INT")
                .HasColumnName("st_month");
            entity.Property(e => e.StYear)
                .HasColumnType("INT")
                .HasColumnName("st_year");
            entity.Property(e => e.ToolTip).HasColumnName("tool_tip");
        });

        modelBuilder.Entity<SchemaMigration>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("schema_migrations");

            entity.Property(e => e.Version).HasColumnName("version");
        });

        modelBuilder.Entity<ScopedFEffect>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("scoped_f_effects");

            entity.Property(e => e.DoodadId)
                .HasColumnType("INT")
                .HasColumnName("doodad_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Key).HasColumnName("key");
            entity.Property(e => e.Range)
                .HasColumnType("INT")
                .HasColumnName("range");
        });

        modelBuilder.Entity<ShipModel>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("ship_models");

            entity.Property(e => e.Accel).HasColumnName("accel");
            entity.Property(e => e.Damaged25).HasColumnName("damaged25");
            entity.Property(e => e.Damaged50).HasColumnName("damaged50");
            entity.Property(e => e.Damaged75).HasColumnName("damaged75");
            entity.Property(e => e.Dead).HasColumnName("dead");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.KeelHeight).HasColumnName("keel_height");
            entity.Property(e => e.KeelLength).HasColumnName("keel_length");
            entity.Property(e => e.KeelOffsetZ).HasColumnName("keel_offset_z");
            entity.Property(e => e.Mass).HasColumnName("mass");
            entity.Property(e => e.MassBoxSizeX).HasColumnName("mass_box_size_x");
            entity.Property(e => e.MassBoxSizeY).HasColumnName("mass_box_size_y");
            entity.Property(e => e.MassBoxSizeZ).HasColumnName("mass_box_size_z");
            entity.Property(e => e.MassCenterX).HasColumnName("mass_center_x");
            entity.Property(e => e.MassCenterY).HasColumnName("mass_center_y");
            entity.Property(e => e.MassCenterZ).HasColumnName("mass_center_z");
            entity.Property(e => e.Normal).HasColumnName("normal");
            entity.Property(e => e.ReverseAccel).HasColumnName("reverse_accel");
            entity.Property(e => e.ReverseVelocity).HasColumnName("reverse_velocity");
            entity.Property(e => e.SteerVel).HasColumnName("steer_vel");
            entity.Property(e => e.TubeLength).HasColumnName("tube_length");
            entity.Property(e => e.TubeOffsetZ).HasColumnName("tube_offset_z");
            entity.Property(e => e.TubeRadius).HasColumnName("tube_radius");
            entity.Property(e => e.TurnAccel).HasColumnName("turn_accel");
            entity.Property(e => e.Velocity).HasColumnName("velocity");
            entity.Property(e => e.WaterDamping).HasColumnName("water_damping");
            entity.Property(e => e.WaterDensity).HasColumnName("water_density");
            entity.Property(e => e.WaterResistance).HasColumnName("water_resistance");
        });

        modelBuilder.Entity<Shipyard>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("shipyards");

            entity.Property(e => e.BuildRadius)
                .HasColumnType("INT")
                .HasColumnName("build_radius");
            entity.Property(e => e.CeremonyAnimKey).HasColumnName("ceremony_anim_key");
            entity.Property(e => e.CeremonyAnimTime)
                .HasColumnType("INT")
                .HasColumnName("ceremony_anim_time");
            entity.Property(e => e.CeremonyModelId)
                .HasColumnType("INT")
                .HasColumnName("ceremony_model_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.ItemId)
                .HasColumnType("INT")
                .HasColumnName("item_id");
            entity.Property(e => e.MainModelId)
                .HasColumnType("INT")
                .HasColumnName("main_model_id");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.OriginItemId)
                .HasColumnType("INT")
                .HasColumnName("origin_item_id");
            entity.Property(e => e.SpawnOffsetFront).HasColumnName("spawn_offset_front");
            entity.Property(e => e.SpawnOffsetZ).HasColumnName("spawn_offset_z");
            entity.Property(e => e.TaxDuration)
                .HasColumnType("INT")
                .HasColumnName("tax_duration");
            entity.Property(e => e.TaxationId)
                .HasColumnType("INT")
                .HasColumnName("taxation_id");
        });

        modelBuilder.Entity<ShipyardReward>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("shipyard_rewards");

            entity.Property(e => e.Count)
                .HasColumnType("INT")
                .HasColumnName("count");
            entity.Property(e => e.DoodadId)
                .HasColumnType("INT")
                .HasColumnName("doodad_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.OnWater)
                .HasColumnType("NUM")
                .HasColumnName("on_water");
            entity.Property(e => e.Radius).HasColumnName("radius");
            entity.Property(e => e.ShipyardId)
                .HasColumnType("INT")
                .HasColumnName("shipyard_id");
        });

        modelBuilder.Entity<ShipyardStep>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("shipyard_steps");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.MaxHp)
                .HasColumnType("INT")
                .HasColumnName("max_hp");
            entity.Property(e => e.ModelId)
                .HasColumnType("INT")
                .HasColumnName("model_id");
            entity.Property(e => e.NumActions)
                .HasColumnType("INT")
                .HasColumnName("num_actions");
            entity.Property(e => e.ShipyardId)
                .HasColumnType("INT")
                .HasColumnName("shipyard_id");
            entity.Property(e => e.SkillId)
                .HasColumnType("INT")
                .HasColumnName("skill_id");
            entity.Property(e => e.Step)
                .HasColumnType("INT")
                .HasColumnName("step");
        });

        modelBuilder.Entity<SiegeItem>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("siege_items");

            entity.Property(e => e.DefenseHqDuringSiege)
                .HasColumnType("NUM")
                .HasColumnName("defense_hq_during_siege");
            entity.Property(e => e.DefenseHqDuringWarmup)
                .HasColumnType("NUM")
                .HasColumnName("defense_hq_during_warmup");
            entity.Property(e => e.DuringDeclare)
                .HasColumnType("NUM")
                .HasColumnName("during_declare");
            entity.Property(e => e.DuringNoDominion)
                .HasColumnType("NUM")
                .HasColumnName("during_no_dominion");
            entity.Property(e => e.DuringPeace)
                .HasColumnType("NUM")
                .HasColumnName("during_peace");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.OffenseHqDuringSiege)
                .HasColumnType("NUM")
                .HasColumnName("offense_hq_during_siege");
            entity.Property(e => e.OffenseHqDuringWarmup)
                .HasColumnType("NUM")
                .HasColumnName("offense_hq_during_warmup");
            entity.Property(e => e.OutsideSiegeAreaDuringSiege)
                .HasColumnType("NUM")
                .HasColumnName("outside_siege_area_during_siege");
            entity.Property(e => e.OutsideSiegeAreaDuringWarmup)
                .HasColumnType("NUM")
                .HasColumnName("outside_siege_area_during_warmup");
            entity.Property(e => e.OutsideSiegeZone)
                .HasColumnType("NUM")
                .HasColumnName("outside_siege_zone");
            entity.Property(e => e.SiegeCircleDuringSiege)
                .HasColumnType("NUM")
                .HasColumnName("siege_circle_during_siege");
            entity.Property(e => e.SiegeCircleDuringWarmup)
                .HasColumnType("NUM")
                .HasColumnName("siege_circle_during_warmup");
            entity.Property(e => e.Usage).HasColumnName("usage");
        });

        modelBuilder.Entity<SiegePlan>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("siege_plans");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.WeekStart)
                .HasColumnType("NUMERIC")
                .HasColumnName("week_start");
            entity.Property(e => e.ZoneGroupId).HasColumnName("zone_group_id");
        });

        modelBuilder.Entity<SiegeSetting>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("siege_settings");

            entity.Property(e => e.NumDefenders)
                .HasColumnType("INT")
                .HasColumnName("num_defenders");
            entity.Property(e => e.NumReinforcements)
                .HasColumnType("INT")
                .HasColumnName("num_reinforcements");
            entity.Property(e => e.TotalCastles)
                .HasColumnType("INT")
                .HasColumnName("total_castles");
        });

        modelBuilder.Entity<SiegeTicketOffensePrice>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("siege_ticket_offense_prices");

            entity.Property(e => e.Count)
                .HasColumnType("INT")
                .HasColumnName("count");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.PerPrice)
                .HasColumnType("INT")
                .HasColumnName("per_price");
        });

        modelBuilder.Entity<SiegeZone>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("siege_zones");

            entity.Property(e => e.DeclareItemId)
                .HasColumnType("INT")
                .HasColumnName("declare_item_id");
            entity.Property(e => e.DefenseMerchantId)
                .HasColumnType("INT")
                .HasColumnName("defense_merchant_id");
            entity.Property(e => e.DefenseTicketId)
                .HasColumnType("INT")
                .HasColumnName("defense_ticket_id");
            entity.Property(e => e.DominionMerchantId)
                .HasColumnType("INT")
                .HasColumnName("dominion_merchant_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.MonumentDoodadId)
                .HasColumnType("INT")
                .HasColumnName("monument_doodad_id");
            entity.Property(e => e.OffenseMerchantId)
                .HasColumnType("INT")
                .HasColumnName("offense_merchant_id");
            entity.Property(e => e.OffenseTicketId)
                .HasColumnType("INT")
                .HasColumnName("offense_ticket_id");
            entity.Property(e => e.OpenDurationHours)
                .HasColumnType("INT")
                .HasColumnName("open_duration_hours");
            entity.Property(e => e.OpenHour)
                .HasColumnType("INT")
                .HasColumnName("open_hour");
            entity.Property(e => e.OpenWeekday)
                .HasColumnType("INT")
                .HasColumnName("open_weekday");
            entity.Property(e => e.PayHour)
                .HasColumnType("INT")
                .HasColumnName("pay_hour");
            entity.Property(e => e.PayMin)
                .HasColumnType("INT")
                .HasColumnName("pay_min");
            entity.Property(e => e.PayWeekday)
                .HasColumnType("INT")
                .HasColumnName("pay_weekday");
            entity.Property(e => e.ReinforceDefenseDelayMins)
                .HasColumnType("INT")
                .HasColumnName("reinforce_defense_delay_mins");
            entity.Property(e => e.SiegeDays)
                .HasColumnType("INT")
                .HasColumnName("siege_days");
            entity.Property(e => e.SiegeHours)
                .HasColumnType("INT")
                .HasColumnName("siege_hours");
            entity.Property(e => e.SiegeMins)
                .HasColumnType("INT")
                .HasColumnName("siege_mins");
            entity.Property(e => e.StartAuctionHour)
                .HasColumnType("INT")
                .HasColumnName("start_auction_hour");
            entity.Property(e => e.StartAuctionMin)
                .HasColumnType("INT")
                .HasColumnName("start_auction_min");
            entity.Property(e => e.StartAuctionWeekday)
                .HasColumnType("INT")
                .HasColumnName("start_auction_weekday");
            entity.Property(e => e.StartDeclareHour)
                .HasColumnType("INT")
                .HasColumnName("start_declare_hour");
            entity.Property(e => e.StartDeclareMin)
                .HasColumnType("INT")
                .HasColumnName("start_declare_min");
            entity.Property(e => e.StartDeclareWeekday)
                .HasColumnType("INT")
                .HasColumnName("start_declare_weekday");
            entity.Property(e => e.StartSiegeHour)
                .HasColumnType("INT")
                .HasColumnName("start_siege_hour");
            entity.Property(e => e.StartSiegeMin)
                .HasColumnType("INT")
                .HasColumnName("start_siege_min");
            entity.Property(e => e.StartSiegeWeekday)
                .HasColumnType("INT")
                .HasColumnName("start_siege_weekday");
            entity.Property(e => e.StartWarmupHour)
                .HasColumnType("INT")
                .HasColumnName("start_warmup_hour");
            entity.Property(e => e.StartWarmupMin)
                .HasColumnType("INT")
                .HasColumnName("start_warmup_min");
            entity.Property(e => e.StartWarmupWeekday)
                .HasColumnType("INT")
                .HasColumnName("start_warmup_weekday");
            entity.Property(e => e.ZoneGroupId)
                .HasColumnType("INT")
                .HasColumnName("zone_group_id");
        });

        modelBuilder.Entity<Skill>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("skills");

            entity.Property(e => e.AbilityId)
                .HasColumnType("INT")
                .HasColumnName("ability_id");
            entity.Property(e => e.AbilityLevel)
                .HasColumnType("INT")
                .HasColumnName("ability_level");
            entity.Property(e => e.ActabilityGroupId)
                .HasColumnType("INT")
                .HasColumnName("actability_group_id");
            entity.Property(e => e.ActiveWeaponId)
                .HasColumnType("INT")
                .HasColumnName("active_weapon_id");
            entity.Property(e => e.Aggro)
                .HasColumnType("INT")
                .HasColumnName("aggro");
            entity.Property(e => e.AllowToPrisoner)
                .HasColumnType("NUM")
                .HasColumnName("allow_to_prisoner");
            entity.Property(e => e.AutoFire)
                .HasColumnType("NUM")
                .HasColumnName("auto_fire");
            entity.Property(e => e.AutoLearn)
                .HasColumnType("NUM")
                .HasColumnName("auto_learn");
            entity.Property(e => e.AutoReuse)
                .HasColumnType("NUM")
                .HasColumnName("auto_reuse");
            entity.Property(e => e.AutoReuseDelay)
                .HasColumnType("INT")
                .HasColumnName("auto_reuse_delay");
            entity.Property(e => e.CameraAcceleration).HasColumnName("camera_acceleration");
            entity.Property(e => e.CameraDuration).HasColumnName("camera_duration");
            entity.Property(e => e.CameraHoldZ)
                .HasColumnType("NUM")
                .HasColumnName("camera_hold_z");
            entity.Property(e => e.CameraMaxDistance).HasColumnName("camera_max_distance");
            entity.Property(e => e.CameraSlowDownDistance).HasColumnName("camera_slow_down_distance");
            entity.Property(e => e.CameraSpeed).HasColumnName("camera_speed");
            entity.Property(e => e.CanActiveWeaponWithoutAnim)
                .HasColumnType("NUM")
                .HasColumnName("can_active_weapon_without_anim");
            entity.Property(e => e.CancelOngoingBuffExceptionTagId)
                .HasColumnType("INT")
                .HasColumnName("cancel_ongoing_buff_exception_tag_id");
            entity.Property(e => e.CancelOngoingBuffs)
                .HasColumnType("NUM")
                .HasColumnName("cancel_ongoing_buffs");
            entity.Property(e => e.CastingCancelable)
                .HasColumnType("NUM")
                .HasColumnName("casting_cancelable");
            entity.Property(e => e.CastingDelayable)
                .HasColumnType("NUM")
                .HasColumnName("casting_delayable");
            entity.Property(e => e.CastingInc)
                .HasColumnType("INT")
                .HasColumnName("casting_inc");
            entity.Property(e => e.CastingTime)
                .HasColumnType("INT")
                .HasColumnName("casting_time");
            entity.Property(e => e.CategoryId)
                .HasColumnType("INT")
                .HasColumnName("category_id");
            entity.Property(e => e.ChannelingAnimId)
                .HasColumnType("INT")
                .HasColumnName("channeling_anim_id");
            entity.Property(e => e.ChannelingBuffId)
                .HasColumnType("INT")
                .HasColumnName("channeling_buff_id");
            entity.Property(e => e.ChannelingCancelable)
                .HasColumnType("NUM")
                .HasColumnName("channeling_cancelable");
            entity.Property(e => e.ChannelingDoodadId)
                .HasColumnType("INT")
                .HasColumnName("channeling_doodad_id");
            entity.Property(e => e.ChannelingMana)
                .HasColumnType("INT")
                .HasColumnName("channeling_mana");
            entity.Property(e => e.ChannelingTargetBuffId)
                .HasColumnType("INT")
                .HasColumnName("channeling_target_buff_id");
            entity.Property(e => e.ChannelingTick)
                .HasColumnType("INT")
                .HasColumnName("channeling_tick");
            entity.Property(e => e.ChannelingTime)
                .HasColumnType("INT")
                .HasColumnName("channeling_time");
            entity.Property(e => e.CheckObstacle)
                .HasColumnType("NUM")
                .HasColumnName("check_obstacle");
            entity.Property(e => e.CheckTerrain)
                .HasColumnType("NUM")
                .HasColumnName("check_terrain");
            entity.Property(e => e.CombatDiceId)
                .HasColumnType("INT")
                .HasColumnName("combat_dice_id");
            entity.Property(e => e.ConsumeLp)
                .HasColumnType("INT")
                .HasColumnName("consume_lp");
            entity.Property(e => e.ControllerCamera)
                .HasColumnType("NUM")
                .HasColumnName("controller_camera");
            entity.Property(e => e.ControllerCameraSpeed)
                .HasColumnType("INT")
                .HasColumnName("controller_camera_speed");
            entity.Property(e => e.CooldownTagId)
                .HasColumnType("INT")
                .HasColumnName("cooldown_tag_id");
            entity.Property(e => e.CooldownTime)
                .HasColumnType("INT")
                .HasColumnName("cooldown_time");
            entity.Property(e => e.Cost)
                .HasColumnType("INT")
                .HasColumnName("cost");
            entity.Property(e => e.CrimePoint)
                .HasColumnType("INT")
                .HasColumnName("crime_point");
            entity.Property(e => e.CustomGcd)
                .HasColumnType("INT")
                .HasColumnName("custom_gcd");
            entity.Property(e => e.DamageTypeId)
                .HasColumnType("INT")
                .HasColumnName("damage_type_id");
            entity.Property(e => e.DefaultGcd)
                .HasColumnType("NUM")
                .HasColumnName("default_gcd");
            entity.Property(e => e.Desc).HasColumnName("desc");
            entity.Property(e => e.DescTr)
                .HasColumnType("NUM")
                .HasColumnName("desc_tr");
            entity.Property(e => e.DoodadBundleId)
                .HasColumnType("INT")
                .HasColumnName("doodad_bundle_id");
            entity.Property(e => e.DoodadHitFamily)
                .HasColumnType("INT")
                .HasColumnName("doodad_hit_family");
            entity.Property(e => e.DualWieldFireAnimId)
                .HasColumnType("INT")
                .HasColumnName("dual_wield_fire_anim_id");
            entity.Property(e => e.EffectDelay)
                .HasColumnType("INT")
                .HasColumnName("effect_delay");
            entity.Property(e => e.EffectRepeatCount)
                .HasColumnType("INT")
                .HasColumnName("effect_repeat_count");
            entity.Property(e => e.EffectRepeatTick)
                .HasColumnType("INT")
                .HasColumnName("effect_repeat_tick");
            entity.Property(e => e.EffectSpeed).HasColumnName("effect_speed");
            entity.Property(e => e.EndSkillController)
                .HasColumnType("NUM")
                .HasColumnName("end_skill_controller");
            entity.Property(e => e.FireAnimId)
                .HasColumnType("INT")
                .HasColumnName("fire_anim_id");
            entity.Property(e => e.FirstReagentOnly)
                .HasColumnType("NUM")
                .HasColumnName("first_reagent_only");
            entity.Property(e => e.FrontAngle)
                .HasColumnType("INT")
                .HasColumnName("front_angle");
            entity.Property(e => e.FxGroupId)
                .HasColumnType("INT")
                .HasColumnName("fx_group_id");
            entity.Property(e => e.GainLifePoint)
                .HasColumnType("INT")
                .HasColumnName("gain_life_point");
            entity.Property(e => e.IconId)
                .HasColumnType("INT")
                .HasColumnName("icon_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.IgnoreGlobalCooldown)
                .HasColumnType("NUM")
                .HasColumnName("ignore_global_cooldown");
            entity.Property(e => e.KeepManaRegen)
                .HasColumnType("NUM")
                .HasColumnName("keep_mana_regen");
            entity.Property(e => e.KeepStealth)
                .HasColumnType("NUM")
                .HasColumnName("keep_stealth");
            entity.Property(e => e.LevelRuleNoConsideration)
                .HasColumnType("NUM")
                .HasColumnName("level_rule_no_consideration");
            entity.Property(e => e.LevelStep)
                .HasColumnType("INT")
                .HasColumnName("level_step");
            entity.Property(e => e.LinkBackpackTypeId)
                .HasColumnType("INT")
                .HasColumnName("link_backpack_type_id");
            entity.Property(e => e.LinkEquipSlotId)
                .HasColumnType("INT")
                .HasColumnName("link_equip_slot_id");
            entity.Property(e => e.MainhandToolId)
                .HasColumnType("INT")
                .HasColumnName("mainhand_tool_id");
            entity.Property(e => e.ManaCost)
                .HasColumnType("INT")
                .HasColumnName("mana_cost");
            entity.Property(e => e.ManaLevelMd).HasColumnName("mana_level_md");
            entity.Property(e => e.MatchAnimation)
                .HasColumnType("NUM")
                .HasColumnName("match_animation");
            entity.Property(e => e.MatchAnimationCount)
                .HasColumnType("NUM")
                .HasColumnName("match_animation_count");
            entity.Property(e => e.MaxRange)
                .HasColumnType("INT")
                .HasColumnName("max_range");
            entity.Property(e => e.MilestoneId)
                .HasColumnType("INT")
                .HasColumnName("milestone_id");
            entity.Property(e => e.MinRange)
                .HasColumnType("INT")
                .HasColumnName("min_range");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.NameTr)
                .HasColumnType("NUM")
                .HasColumnName("name_tr");
            entity.Property(e => e.NeedLearn)
                .HasColumnType("NUM")
                .HasColumnName("need_learn");
            entity.Property(e => e.OffhandToolId)
                .HasColumnType("INT")
                .HasColumnName("offhand_tool_id");
            entity.Property(e => e.OrUnitReqs)
                .HasColumnType("NUM")
                .HasColumnName("or_unit_reqs");
            entity.Property(e => e.PercussionInstrumentFireAnimId)
                .HasColumnType("INT")
                .HasColumnName("percussion_instrument_fire_anim_id");
            entity.Property(e => e.PercussionInstrumentStartAnimId)
                .HasColumnType("INT")
                .HasColumnName("percussion_instrument_start_anim_id");
            entity.Property(e => e.PitchAngle).HasColumnName("pitch_angle");
            entity.Property(e => e.PlotId)
                .HasColumnType("INT")
                .HasColumnName("plot_id");
            entity.Property(e => e.PlotOnly)
                .HasColumnType("NUM")
                .HasColumnName("plot_only");
            entity.Property(e => e.ProjectileId)
                .HasColumnType("INT")
                .HasColumnName("projectile_id");
            entity.Property(e => e.ReagentCorpseStatusId)
                .HasColumnType("INT")
                .HasColumnName("reagent_corpse_status_id");
            entity.Property(e => e.RepeatCount)
                .HasColumnType("INT")
                .HasColumnName("repeat_count");
            entity.Property(e => e.RepeatTick)
                .HasColumnType("INT")
                .HasColumnName("repeat_tick");
            entity.Property(e => e.SensitiveOperation)
                .HasColumnType("NUM")
                .HasColumnName("sensitive_operation");
            entity.Property(e => e.Show)
                .HasColumnType("NUM")
                .HasColumnName("show");
            entity.Property(e => e.ShowTargetCastingTime)
                .HasColumnType("NUM")
                .HasColumnName("show_target_casting_time");
            entity.Property(e => e.SkillControllerAtEnd)
                .HasColumnType("NUM")
                .HasColumnName("skill_controller_at_end");
            entity.Property(e => e.SkillControllerId)
                .HasColumnType("INT")
                .HasColumnName("skill_controller_id");
            entity.Property(e => e.SkillPoints)
                .HasColumnType("INT")
                .HasColumnName("skill_points");
            entity.Property(e => e.SourceAlive)
                .HasColumnType("NUM")
                .HasColumnName("source_alive");
            entity.Property(e => e.SourceCannotUseWhileWalk)
                .HasColumnType("NUM")
                .HasColumnName("source_cannot_use_while_walk");
            entity.Property(e => e.SourceDead)
                .HasColumnType("NUM")
                .HasColumnName("source_dead");
            entity.Property(e => e.SourceMount)
                .HasColumnType("NUM")
                .HasColumnName("source_mount");
            entity.Property(e => e.SourceMountMate)
                .HasColumnType("NUM")
                .HasColumnName("source_mount_mate");
            entity.Property(e => e.SourceNoSlave)
                .HasColumnType("NUM")
                .HasColumnName("source_no_slave");
            entity.Property(e => e.SourceNotCollided)
                .HasColumnType("NUM")
                .HasColumnName("source_not_collided");
            entity.Property(e => e.SourceNotSwim)
                .HasColumnType("NUM")
                .HasColumnName("source_not_swim");
            entity.Property(e => e.SourceStun)
                .HasColumnType("NUM")
                .HasColumnName("source_stun");
            entity.Property(e => e.StartAnimId)
                .HasColumnType("INT")
                .HasColumnName("start_anim_id");
            entity.Property(e => e.StartAutoattack)
                .HasColumnType("NUM")
                .HasColumnName("start_autoattack");
            entity.Property(e => e.StopAutoattack)
                .HasColumnType("NUM")
                .HasColumnName("stop_autoattack");
            entity.Property(e => e.StopCastingByTurn)
                .HasColumnType("NUM")
                .HasColumnName("stop_casting_by_turn");
            entity.Property(e => e.StopCastingOnBigHit)
                .HasColumnType("NUM")
                .HasColumnName("stop_casting_on_big_hit");
            entity.Property(e => e.StopChannelingOnBigHit)
                .HasColumnType("NUM")
                .HasColumnName("stop_channeling_on_big_hit");
            entity.Property(e => e.StopChannelingOnStartSkill)
                .HasColumnType("NUM")
                .HasColumnName("stop_channeling_on_start_skill");
            entity.Property(e => e.StringInstrumentFireAnimId)
                .HasColumnType("INT")
                .HasColumnName("string_instrument_fire_anim_id");
            entity.Property(e => e.StringInstrumentStartAnimId)
                .HasColumnType("INT")
                .HasColumnName("string_instrument_start_anim_id");
            entity.Property(e => e.SynergyIcon1Buffkind)
                .HasColumnType("NUM")
                .HasColumnName("synergy_icon1_buffkind");
            entity.Property(e => e.SynergyIcon1Id)
                .HasColumnType("INT")
                .HasColumnName("synergy_icon1_id");
            entity.Property(e => e.SynergyIcon2Buffkind)
                .HasColumnType("NUM")
                .HasColumnName("synergy_icon2_buffkind");
            entity.Property(e => e.SynergyIcon2Id)
                .HasColumnType("INT")
                .HasColumnName("synergy_icon2_id");
            entity.Property(e => e.TargetAlive)
                .HasColumnType("NUM")
                .HasColumnName("target_alive");
            entity.Property(e => e.TargetAngle)
                .HasColumnType("INT")
                .HasColumnName("target_angle");
            entity.Property(e => e.TargetAreaAngle)
                .HasColumnType("INT")
                .HasColumnName("target_area_angle");
            entity.Property(e => e.TargetAreaCount)
                .HasColumnType("INT")
                .HasColumnName("target_area_count");
            entity.Property(e => e.TargetAreaRadius)
                .HasColumnType("INT")
                .HasColumnName("target_area_radius");
            entity.Property(e => e.TargetDead)
                .HasColumnType("NUM")
                .HasColumnName("target_dead");
            entity.Property(e => e.TargetDecalRadius)
                .HasColumnType("INT")
                .HasColumnName("target_decal_radius");
            entity.Property(e => e.TargetFishing)
                .HasColumnType("NUM")
                .HasColumnName("target_fishing");
            entity.Property(e => e.TargetMyNpc)
                .HasColumnType("NUM")
                .HasColumnName("target_my_npc");
            entity.Property(e => e.TargetOffsetAngle).HasColumnName("target_offset_angle");
            entity.Property(e => e.TargetOffsetDistance).HasColumnName("target_offset_distance");
            entity.Property(e => e.TargetOnlyWater)
                .HasColumnType("NUM")
                .HasColumnName("target_only_water");
            entity.Property(e => e.TargetPreoccupied)
                .HasColumnType("NUM")
                .HasColumnName("target_preoccupied");
            entity.Property(e => e.TargetRelationId)
                .HasColumnType("INT")
                .HasColumnName("target_relation_id");
            entity.Property(e => e.TargetSelectionId)
                .HasColumnType("INT")
                .HasColumnName("target_selection_id");
            entity.Property(e => e.TargetSiege)
                .HasColumnType("NUM")
                .HasColumnName("target_siege");
            entity.Property(e => e.TargetTypeId)
                .HasColumnType("INT")
                .HasColumnName("target_type_id");
            entity.Property(e => e.TargetValidHeight).HasColumnName("target_valid_height");
            entity.Property(e => e.TargetWater)
                .HasColumnType("NUM")
                .HasColumnName("target_water");
            entity.Property(e => e.TimingId)
                .HasColumnType("INT")
                .HasColumnName("timing_id");
            entity.Property(e => e.ToggleBuffId)
                .HasColumnType("INT")
                .HasColumnName("toggle_buff_id");
            entity.Property(e => e.TubeInstrumentFireAnimId)
                .HasColumnType("INT")
                .HasColumnName("tube_instrument_fire_anim_id");
            entity.Property(e => e.TubeInstrumentStartAnimId)
                .HasColumnType("INT")
                .HasColumnName("tube_instrument_start_anim_id");
            entity.Property(e => e.TwohandFireAnimId)
                .HasColumnType("INT")
                .HasColumnName("twohand_fire_anim_id");
            entity.Property(e => e.Unmount)
                .HasColumnType("NUM")
                .HasColumnName("unmount");
            entity.Property(e => e.UseAnimTime)
                .HasColumnType("NUM")
                .HasColumnName("use_anim_time");
            entity.Property(e => e.UseSkillCamera)
                .HasColumnType("NUM")
                .HasColumnName("use_skill_camera");
            entity.Property(e => e.UseWeaponCooldownTime)
                .HasColumnType("NUM")
                .HasColumnName("use_weapon_cooldown_time");
            entity.Property(e => e.ValidHeight).HasColumnName("valid_height");
            entity.Property(e => e.ValidHeightEdgeToEdge)
                .HasColumnType("NUM")
                .HasColumnName("valid_height_edge_to_edge");
            entity.Property(e => e.WeaponSlotForAngleId)
                .HasColumnType("INT")
                .HasColumnName("weapon_slot_for_angle_id");
            entity.Property(e => e.WeaponSlotForAutoattackId)
                .HasColumnType("INT")
                .HasColumnName("weapon_slot_for_autoattack_id");
            entity.Property(e => e.WeaponSlotForRangeId)
                .HasColumnType("INT")
                .HasColumnName("weapon_slot_for_range_id");
            entity.Property(e => e.WebDesc).HasColumnName("web_desc");
            entity.Property(e => e.WebDescTr)
                .HasColumnType("NUM")
                .HasColumnName("web_desc_tr");
        });

        modelBuilder.Entity<SkillController>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("skill_controllers");

            entity.Property(e => e.ActiveWeaponId)
                .HasColumnType("INT")
                .HasColumnName("active_weapon_id");
            entity.Property(e => e.EndAnimId)
                .HasColumnType("INT")
                .HasColumnName("end_anim_id");
            entity.Property(e => e.EndSkillId)
                .HasColumnType("INT")
                .HasColumnName("end_skill_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.KindId)
                .HasColumnType("INT")
                .HasColumnName("kind_id");
            entity.Property(e => e.StartAnimId)
                .HasColumnType("INT")
                .HasColumnName("start_anim_id");
            entity.Property(e => e.StrValue1).HasColumnName("str_value1");
            entity.Property(e => e.TransitionAnim1Id)
                .HasColumnType("INT")
                .HasColumnName("transition_anim_1_id");
            entity.Property(e => e.TransitionAnim2Id)
                .HasColumnType("INT")
                .HasColumnName("transition_anim_2_id");
            entity.Property(e => e.Value1)
                .HasColumnType("INT")
                .HasColumnName("value1");
            entity.Property(e => e.Value10)
                .HasColumnType("INT")
                .HasColumnName("value10");
            entity.Property(e => e.Value11)
                .HasColumnType("INT")
                .HasColumnName("value11");
            entity.Property(e => e.Value12)
                .HasColumnType("INT")
                .HasColumnName("value12");
            entity.Property(e => e.Value13)
                .HasColumnType("INT")
                .HasColumnName("value13");
            entity.Property(e => e.Value14)
                .HasColumnType("INT")
                .HasColumnName("value14");
            entity.Property(e => e.Value15)
                .HasColumnType("INT")
                .HasColumnName("value15");
            entity.Property(e => e.Value2)
                .HasColumnType("INT")
                .HasColumnName("value2");
            entity.Property(e => e.Value3)
                .HasColumnType("INT")
                .HasColumnName("value3");
            entity.Property(e => e.Value4)
                .HasColumnType("INT")
                .HasColumnName("value4");
            entity.Property(e => e.Value5)
                .HasColumnType("INT")
                .HasColumnName("value5");
            entity.Property(e => e.Value6)
                .HasColumnType("INT")
                .HasColumnName("value6");
            entity.Property(e => e.Value7)
                .HasColumnType("INT")
                .HasColumnName("value7");
            entity.Property(e => e.Value8)
                .HasColumnType("INT")
                .HasColumnName("value8");
            entity.Property(e => e.Value9)
                .HasColumnType("INT")
                .HasColumnName("value9");
        });

        modelBuilder.Entity<SkillEffect>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("skill_effects");

            entity.Property(e => e.AlwaysHit)
                .HasColumnType("NUM")
                .HasColumnName("always_hit");
            entity.Property(e => e.ApplicationMethodId)
                .HasColumnType("INT")
                .HasColumnName("application_method_id");
            entity.Property(e => e.Back)
                .HasColumnType("NUM")
                .HasColumnName("back");
            entity.Property(e => e.Chance)
                .HasColumnType("INT")
                .HasColumnName("chance");
            entity.Property(e => e.ConsumeItemCount)
                .HasColumnType("INT")
                .HasColumnName("consume_item_count");
            entity.Property(e => e.ConsumeItemId)
                .HasColumnType("INT")
                .HasColumnName("consume_item_id");
            entity.Property(e => e.ConsumeSourceItem)
                .HasColumnType("NUM")
                .HasColumnName("consume_source_item");
            entity.Property(e => e.EffectId)
                .HasColumnType("INT")
                .HasColumnName("effect_id");
            entity.Property(e => e.EndLevel)
                .HasColumnType("INT")
                .HasColumnName("end_level");
            entity.Property(e => e.Friendly)
                .HasColumnType("NUM")
                .HasColumnName("friendly");
            entity.Property(e => e.Front)
                .HasColumnType("NUM")
                .HasColumnName("front");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.InteractionSuccessHit)
                .HasColumnType("NUM")
                .HasColumnName("interaction_success_hit");
            entity.Property(e => e.ItemSetId)
                .HasColumnType("INT")
                .HasColumnName("item_set_id");
            entity.Property(e => e.NonFriendly)
                .HasColumnType("NUM")
                .HasColumnName("non_friendly");
            entity.Property(e => e.SkillId)
                .HasColumnType("INT")
                .HasColumnName("skill_id");
            entity.Property(e => e.SourceBuffTagId)
                .HasColumnType("INT")
                .HasColumnName("source_buff_tag_id");
            entity.Property(e => e.SourceNobuffTagId)
                .HasColumnType("INT")
                .HasColumnName("source_nobuff_tag_id");
            entity.Property(e => e.StartLevel)
                .HasColumnType("INT")
                .HasColumnName("start_level");
            entity.Property(e => e.SynergyText)
                .HasColumnType("NUM")
                .HasColumnName("synergy_text");
            entity.Property(e => e.TargetBuffTagId)
                .HasColumnType("INT")
                .HasColumnName("target_buff_tag_id");
            entity.Property(e => e.TargetNobuffTagId)
                .HasColumnType("INT")
                .HasColumnName("target_nobuff_tag_id");
            entity.Property(e => e.TargetNpcTagId)
                .HasColumnType("INT")
                .HasColumnName("target_npc_tag_id");
            entity.Property(e => e.Weight)
                .HasColumnType("INT")
                .HasColumnName("weight");
        });

        modelBuilder.Entity<SkillModifier>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("skill_modifiers");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.OwnerId)
                .HasColumnType("INT")
                .HasColumnName("owner_id");
            entity.Property(e => e.OwnerType).HasColumnName("owner_type");
            entity.Property(e => e.SkillAttributeId)
                .HasColumnType("INT")
                .HasColumnName("skill_attribute_id");
            entity.Property(e => e.SkillId)
                .HasColumnType("INT")
                .HasColumnName("skill_id");
            entity.Property(e => e.Synergy)
                .HasColumnType("NUM")
                .HasColumnName("synergy");
            entity.Property(e => e.TagId)
                .HasColumnType("INT")
                .HasColumnName("tag_id");
            entity.Property(e => e.UnitModifierTypeId)
                .HasColumnType("INT")
                .HasColumnName("unit_modifier_type_id");
            entity.Property(e => e.Value)
                .HasColumnType("INT")
                .HasColumnName("value");
        });

        modelBuilder.Entity<SkillProduct>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("skill_products");

            entity.Property(e => e.Amount)
                .HasColumnType("INT")
                .HasColumnName("amount");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.ItemId)
                .HasColumnType("INT")
                .HasColumnName("item_id");
            entity.Property(e => e.SkillId)
                .HasColumnType("INT")
                .HasColumnName("skill_id");
        });

        modelBuilder.Entity<SkillReagent>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("skill_reagents");

            entity.Property(e => e.Amount)
                .HasColumnType("INT")
                .HasColumnName("amount");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.ItemId)
                .HasColumnType("INT")
                .HasColumnName("item_id");
            entity.Property(e => e.SkillId)
                .HasColumnType("INT")
                .HasColumnName("skill_id");
        });

        modelBuilder.Entity<SkillReq>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("skill_reqs");

            entity.Property(e => e.BuffId)
                .HasColumnType("INT")
                .HasColumnName("buff_id");
            entity.Property(e => e.BuffTagId)
                .HasColumnType("INT")
                .HasColumnName("buff_tag_id");
            entity.Property(e => e.DefaultResult)
                .HasColumnType("NUM")
                .HasColumnName("default_result");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Message).HasColumnName("message");
            entity.Property(e => e.Target)
                .HasColumnType("NUM")
                .HasColumnName("target");
        });

        modelBuilder.Entity<SkillReqSkill>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("skill_req_skills");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.SkillId)
                .HasColumnType("INT")
                .HasColumnName("skill_id");
            entity.Property(e => e.SkillReqId)
                .HasColumnType("INT")
                .HasColumnName("skill_req_id");
        });

        modelBuilder.Entity<SkillReqSkillTag>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("skill_req_skill_tags");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.SkillReqId)
                .HasColumnType("INT")
                .HasColumnName("skill_req_id");
            entity.Property(e => e.SkillTagId)
                .HasColumnType("INT")
                .HasColumnName("skill_tag_id");
        });

        modelBuilder.Entity<SkillSynergyIcon>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("skill_synergy_icons");

            entity.Property(e => e.BuffTagId)
                .HasColumnType("INT")
                .HasColumnName("buff_tag_id");
            entity.Property(e => e.ConditionBuffkind)
                .HasColumnType("NUM")
                .HasColumnName("condition_buffkind");
            entity.Property(e => e.ConditionIconId)
                .HasColumnType("INT")
                .HasColumnName("condition_icon_id");
            entity.Property(e => e.Desc).HasColumnName("desc");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.ResultBuffkind)
                .HasColumnType("NUM")
                .HasColumnName("result_buffkind");
            entity.Property(e => e.ResultIconId)
                .HasColumnType("INT")
                .HasColumnName("result_icon_id");
            entity.Property(e => e.SkillId)
                .HasColumnType("INT")
                .HasColumnName("skill_id");
            entity.Property(e => e.UnitSelectionId)
                .HasColumnType("INT")
                .HasColumnName("unit_selection_id");
            entity.Property(e => e.WebDesc).HasColumnName("web_desc");
        });

        modelBuilder.Entity<SkillVisualGroup>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("skill_visual_groups");

            entity.Property(e => e.FxGroupId)
                .HasColumnType("INT")
                .HasColumnName("fx_group_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Level)
                .HasColumnType("INT")
                .HasColumnName("level");
            entity.Property(e => e.OwnerId)
                .HasColumnType("INT")
                .HasColumnName("owner_id");
            entity.Property(e => e.OwnerType).HasColumnName("owner_type");
            entity.Property(e => e.ProjectileId)
                .HasColumnType("INT")
                .HasColumnName("projectile_id");
        });

        modelBuilder.Entity<SkinColor>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("skin_colors");

            entity.Property(e => e.BrightSkinColorB)
                .HasColumnType("INT")
                .HasColumnName("bright_skin_color_b");
            entity.Property(e => e.BrightSkinColorG)
                .HasColumnType("INT")
                .HasColumnName("bright_skin_color_g");
            entity.Property(e => e.BrightSkinColorR)
                .HasColumnType("INT")
                .HasColumnName("bright_skin_color_r");
            entity.Property(e => e.Comment).HasColumnName("comment");
            entity.Property(e => e.CustomPostfix).HasColumnName("custom_postfix");
            entity.Property(e => e.DiffuseColorB)
                .HasColumnType("INT")
                .HasColumnName("diffuse_color_b");
            entity.Property(e => e.DiffuseColorG)
                .HasColumnType("INT")
                .HasColumnName("diffuse_color_g");
            entity.Property(e => e.DiffuseColorR)
                .HasColumnType("INT")
                .HasColumnName("diffuse_color_r");
            entity.Property(e => e.Glossness)
                .HasColumnType("INT")
                .HasColumnName("glossness");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.MiddleSkinColorB)
                .HasColumnType("INT")
                .HasColumnName("middle_skin_color_b");
            entity.Property(e => e.MiddleSkinColorG)
                .HasColumnType("INT")
                .HasColumnName("middle_skin_color_g");
            entity.Property(e => e.MiddleSkinColorR)
                .HasColumnType("INT")
                .HasColumnName("middle_skin_color_r");
            entity.Property(e => e.ModelId)
                .HasColumnType("INT")
                .HasColumnName("model_id");
            entity.Property(e => e.NpcOnly)
                .HasColumnType("NUM")
                .HasColumnName("npc_only");
            entity.Property(e => e.SpecularColorB)
                .HasColumnType("INT")
                .HasColumnName("specular_color_b");
            entity.Property(e => e.SpecularColorG)
                .HasColumnType("INT")
                .HasColumnName("specular_color_g");
            entity.Property(e => e.SpecularColorR)
                .HasColumnType("INT")
                .HasColumnName("specular_color_r");
            entity.Property(e => e.SpecularLevel).HasColumnName("specular_level");
        });

        modelBuilder.Entity<Slafe>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("slaves");

            entity.Property(e => e.Cost)
                .HasColumnType("INT")
                .HasColumnName("cost");
            entity.Property(e => e.Customizable)
                .HasColumnType("NUM")
                .HasColumnName("customizable");
            entity.Property(e => e.FactionId)
                .HasColumnType("INT")
                .HasColumnName("faction_id");
            entity.Property(e => e.Hp25DoodadCount)
                .HasColumnType("INT")
                .HasColumnName("hp25_doodad_count");
            entity.Property(e => e.Hp50DoodadCount)
                .HasColumnType("INT")
                .HasColumnName("hp50_doodad_count");
            entity.Property(e => e.Hp75DoodadCount)
                .HasColumnType("INT")
                .HasColumnName("hp75_doodad_count");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Level)
                .HasColumnType("INT")
                .HasColumnName("level");
            entity.Property(e => e.ModelId)
                .HasColumnType("INT")
                .HasColumnName("model_id");
            entity.Property(e => e.Mountable)
                .HasColumnType("NUM")
                .HasColumnName("mountable");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.ObbPosX).HasColumnName("obb_pos_x");
            entity.Property(e => e.ObbPosY).HasColumnName("obb_pos_y");
            entity.Property(e => e.ObbPosZ).HasColumnName("obb_pos_z");
            entity.Property(e => e.ObbSizeX).HasColumnName("obb_size_x");
            entity.Property(e => e.ObbSizeY).HasColumnName("obb_size_y");
            entity.Property(e => e.ObbSizeZ).HasColumnName("obb_size_z");
            entity.Property(e => e.OffsetX).HasColumnName("offset_x");
            entity.Property(e => e.OffsetY).HasColumnName("offset_y");
            entity.Property(e => e.OffsetZ).HasColumnName("offset_z");
            entity.Property(e => e.PortalDespawnFxId)
                .HasColumnType("INT")
                .HasColumnName("portal_despawn_fx_id");
            entity.Property(e => e.PortalScale).HasColumnName("portal_scale");
            entity.Property(e => e.PortalSpawnFxId)
                .HasColumnType("INT")
                .HasColumnName("portal_spawn_fx_id");
            entity.Property(e => e.PortalTime).HasColumnName("portal_time");
            entity.Property(e => e.SlaveCustomizingId)
                .HasColumnType("INT")
                .HasColumnName("slave_customizing_id");
            entity.Property(e => e.SlaveInitialItemPackId)
                .HasColumnType("INT")
                .HasColumnName("slave_initial_item_pack_id");
            entity.Property(e => e.SlaveKindId)
                .HasColumnType("INT")
                .HasColumnName("slave_kind_id");
            entity.Property(e => e.SpawnValidAreaRange)
                .HasColumnType("INT")
                .HasColumnName("spawn_valid_area_range");
            entity.Property(e => e.SpawnXOffset).HasColumnName("spawn_x_offset");
            entity.Property(e => e.SpawnYOffset).HasColumnName("spawn_y_offset");
        });

        modelBuilder.Entity<SlashCommand>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("slash_commands");

            entity.Property(e => e.ActionId)
                .HasColumnType("INT")
                .HasColumnName("action_id");
            entity.Property(e => e.ActionType).HasColumnName("action_type");
            entity.Property(e => e.CommandList).HasColumnName("command_list");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.SkillId)
                .HasColumnType("INT")
                .HasColumnName("skill_id");
        });

        modelBuilder.Entity<SlashFunction>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("slash_functions");

            entity.Property(e => e.Comments).HasColumnName("comments");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.SlashFuncId)
                .HasColumnType("INT")
                .HasColumnName("slash_func_id");
        });

        modelBuilder.Entity<SlaveBinding>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("slave_bindings");

            entity.Property(e => e.AttachPointId)
                .HasColumnType("INT")
                .HasColumnName("attach_point_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.OwnerId)
                .HasColumnType("INT")
                .HasColumnName("owner_id");
            entity.Property(e => e.OwnerType).HasColumnName("owner_type");
            entity.Property(e => e.SlaveId)
                .HasColumnType("INT")
                .HasColumnName("slave_id");
        });

        modelBuilder.Entity<SlaveCustomizing>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("slave_customizings");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name");
        });

        modelBuilder.Entity<SlaveCustomizingEquipSlot>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("slave_customizing_equip_slots");

            entity.Property(e => e.EquipSlotId)
                .HasColumnType("INT")
                .HasColumnName("equip_slot_id");
            entity.Property(e => e.EquipSlotName).HasColumnName("equip_slot_name");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.SlaveCustomizingId)
                .HasColumnType("INT")
                .HasColumnName("slave_customizing_id");
        });

        modelBuilder.Entity<SlaveDoodadBinding>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("slave_doodad_bindings");

            entity.Property(e => e.AttachPointId)
                .HasColumnType("INT")
                .HasColumnName("attach_point_id");
            entity.Property(e => e.DoodadId)
                .HasColumnType("INT")
                .HasColumnName("doodad_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.OwnerId)
                .HasColumnType("INT")
                .HasColumnName("owner_id");
            entity.Property(e => e.OwnerType).HasColumnName("owner_type");
            entity.Property(e => e.Persist)
                .HasColumnType("NUM")
                .HasColumnName("persist");
            entity.Property(e => e.Scale).HasColumnName("scale");
        });

        modelBuilder.Entity<SlaveDropDoodad>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("slave_drop_doodads");

            entity.Property(e => e.Count)
                .HasColumnType("INT")
                .HasColumnName("count");
            entity.Property(e => e.DoodadId)
                .HasColumnType("INT")
                .HasColumnName("doodad_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.OnWater)
                .HasColumnType("NUM")
                .HasColumnName("on_water");
            entity.Property(e => e.OwnerId)
                .HasColumnType("INT")
                .HasColumnName("owner_id");
            entity.Property(e => e.OwnerType).HasColumnName("owner_type");
            entity.Property(e => e.Radius).HasColumnName("radius");
        });

        modelBuilder.Entity<SlaveEquipPack>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("slave_equip_packs");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name");
        });

        modelBuilder.Entity<SlaveEquipSlot>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("slave_equip_slots");

            entity.Property(e => e.AttachPointId)
                .HasColumnType("INT")
                .HasColumnName("attach_point_id");
            entity.Property(e => e.EquipSlotId)
                .HasColumnType("INT")
                .HasColumnName("equip_slot_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.RequireSlotId)
                .HasColumnType("INT")
                .HasColumnName("require_slot_id");
            entity.Property(e => e.SlaveId)
                .HasColumnType("INT")
                .HasColumnName("slave_id");
        });

        modelBuilder.Entity<SlaveEquipmentEquipSlotPack>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("slave_equipment_equip_slot_packs");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name");
        });

        modelBuilder.Entity<SlaveHealingPointDoodad>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("slave_healing_point_doodads");

            entity.Property(e => e.AttachPointId)
                .HasColumnType("INT")
                .HasColumnName("attach_point_id");
            entity.Property(e => e.DoodadId)
                .HasColumnType("INT")
                .HasColumnName("doodad_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.OwnerId)
                .HasColumnType("INT")
                .HasColumnName("owner_id");
            entity.Property(e => e.OwnerType).HasColumnName("owner_type");
        });

        modelBuilder.Entity<SlaveInitialBuff>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("slave_initial_buffs");

            entity.Property(e => e.BuffId)
                .HasColumnType("INT")
                .HasColumnName("buff_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.SlaveId)
                .HasColumnType("INT")
                .HasColumnName("slave_id");
        });

        modelBuilder.Entity<SlaveInitialItem>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("slave_initial_items");

            entity.Property(e => e.EquipSlotId)
                .HasColumnType("INT")
                .HasColumnName("equip_slot_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.ItemId)
                .HasColumnType("INT")
                .HasColumnName("item_id");
            entity.Property(e => e.SlaveInitialItemPackId)
                .HasColumnType("INT")
                .HasColumnName("slave_initial_item_pack_id");
        });

        modelBuilder.Entity<SlaveInitialItemPack>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("slave_initial_item_packs");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name");
        });

        modelBuilder.Entity<SlaveMountSkill>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("slave_mount_skills");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.MountSkillId)
                .HasColumnType("INT")
                .HasColumnName("mount_skill_id");
            entity.Property(e => e.SlaveId)
                .HasColumnType("INT")
                .HasColumnName("slave_id");
        });

        modelBuilder.Entity<SlavePassiveBuff>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("slave_passive_buffs");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.OwnerId)
                .HasColumnType("INT")
                .HasColumnName("owner_id");
            entity.Property(e => e.OwnerType).HasColumnName("owner_type");
            entity.Property(e => e.PassiveBuffId)
                .HasColumnType("INT")
                .HasColumnName("passive_buff_id");
        });

        modelBuilder.Entity<Sound>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("sounds");

            entity.Property(e => e.CategoryId)
                .HasColumnType("INT")
                .HasColumnName("category_id");
            entity.Property(e => e.Comment).HasColumnName("comment");
            entity.Property(e => e.EndMethodId)
                .HasColumnType("INT")
                .HasColumnName("end_method_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.LevelId)
                .HasColumnType("INT")
                .HasColumnName("level_id");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.Path).HasColumnName("path");
        });

        modelBuilder.Entity<SoundPack>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("sound_packs");

            entity.Property(e => e.CategoryId)
                .HasColumnType("INT")
                .HasColumnName("category_id");
            entity.Property(e => e.Comment).HasColumnName("comment");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name");
        });

        modelBuilder.Entity<SoundPackItem>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("sound_pack_items");

            entity.Property(e => e.Comment).HasColumnName("comment");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.SoundId)
                .HasColumnType("INT")
                .HasColumnName("sound_id");
            entity.Property(e => e.SoundPackId)
                .HasColumnType("INT")
                .HasColumnName("sound_pack_id");
        });

        modelBuilder.Entity<SpawnEffect>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("spawn_effects");

            entity.Property(e => e.DespawnOnCreatorDeath)
                .HasColumnType("NUM")
                .HasColumnName("despawn_on_creator_death");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.LifeTime).HasColumnName("life_time");
            entity.Property(e => e.MateStateId)
                .HasColumnType("INT")
                .HasColumnName("mate_state_id");
            entity.Property(e => e.OriAngle).HasColumnName("ori_angle");
            entity.Property(e => e.OriDirId)
                .HasColumnType("INT")
                .HasColumnName("ori_dir_id");
            entity.Property(e => e.OwnerTypeId)
                .HasColumnType("INT")
                .HasColumnName("owner_type_id");
            entity.Property(e => e.PosAngle).HasColumnName("pos_angle");
            entity.Property(e => e.PosDirId)
                .HasColumnType("INT")
                .HasColumnName("pos_dir_id");
            entity.Property(e => e.PosDistance).HasColumnName("pos_distance");
            entity.Property(e => e.SubType)
                .HasColumnType("INT")
                .HasColumnName("sub_type");
            entity.Property(e => e.UseSummonerAggroTarget)
                .HasColumnType("NUM")
                .HasColumnName("use_summoner_aggro_target");
            entity.Property(e => e.UseSummonerFaction)
                .HasColumnType("NUM")
                .HasColumnName("use_summoner_faction");
        });

        modelBuilder.Entity<SpawnFishEffect>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("spawn_fish_effects");

            entity.Property(e => e.DoodadId)
                .HasColumnType("INT")
                .HasColumnName("doodad_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Range)
                .HasColumnType("INT")
                .HasColumnName("range");
        });

        modelBuilder.Entity<SpawnGimmickEffect>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("spawn_gimmick_effects");

            entity.Property(e => e.AngVelCoordiateId)
                .HasColumnType("INT")
                .HasColumnName("ang_vel_coordiate_id");
            entity.Property(e => e.AngVelX).HasColumnName("ang_vel_x");
            entity.Property(e => e.AngVelY).HasColumnName("ang_vel_y");
            entity.Property(e => e.AngVelZ).HasColumnName("ang_vel_z");
            entity.Property(e => e.GimmickId)
                .HasColumnType("INT")
                .HasColumnName("gimmick_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.OffsetCoordiateId)
                .HasColumnType("INT")
                .HasColumnName("offset_coordiate_id");
            entity.Property(e => e.OffsetFromSource)
                .HasColumnType("NUM")
                .HasColumnName("offset_from_source");
            entity.Property(e => e.OffsetX).HasColumnName("offset_x");
            entity.Property(e => e.OffsetY).HasColumnName("offset_y");
            entity.Property(e => e.OffsetZ).HasColumnName("offset_z");
            entity.Property(e => e.Scale).HasColumnName("scale");
            entity.Property(e => e.VelocityCoordiateId)
                .HasColumnType("INT")
                .HasColumnName("velocity_coordiate_id");
            entity.Property(e => e.VelocityX).HasColumnName("velocity_x");
            entity.Property(e => e.VelocityY).HasColumnName("velocity_y");
            entity.Property(e => e.VelocityZ).HasColumnName("velocity_z");
        });

        modelBuilder.Entity<SpecialEffect>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("special_effects");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.SpecialEffectTypeId)
                .HasColumnType("INT")
                .HasColumnName("special_effect_type_id");
            entity.Property(e => e.Value1)
                .HasColumnType("INT")
                .HasColumnName("value1");
            entity.Property(e => e.Value2)
                .HasColumnType("INT")
                .HasColumnName("value2");
            entity.Property(e => e.Value3)
                .HasColumnType("INT")
                .HasColumnName("value3");
            entity.Property(e => e.Value4)
                .HasColumnType("INT")
                .HasColumnName("value4");
        });

        modelBuilder.Entity<Specialty>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("specialties");

            entity.Property(e => e.ColZoneGroupId)
                .HasColumnType("INT")
                .HasColumnName("col_zone_group_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Profit)
                .HasColumnType("INT")
                .HasColumnName("profit");
            entity.Property(e => e.Ratio)
                .HasColumnType("INT")
                .HasColumnName("ratio");
            entity.Property(e => e.RowZoneGroupId)
                .HasColumnType("INT")
                .HasColumnName("row_zone_group_id");
            entity.Property(e => e.VendorExist)
                .HasColumnType("NUM")
                .HasColumnName("vendor_exist");
        });

        modelBuilder.Entity<SpecialtyBundle>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("specialty_bundles");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name");
        });

        modelBuilder.Entity<SpecialtyBundleItem>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("specialty_bundle_items");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.ItemId)
                .HasColumnType("INT")
                .HasColumnName("item_id");
            entity.Property(e => e.Profit)
                .HasColumnType("INT")
                .HasColumnName("profit");
            entity.Property(e => e.Ratio)
                .HasColumnType("INT")
                .HasColumnName("ratio");
            entity.Property(e => e.SpecialtyBundleId)
                .HasColumnType("INT")
                .HasColumnName("specialty_bundle_id");
        });

        modelBuilder.Entity<SpecialtyNpc>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("specialty_npcs");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.NpcId)
                .HasColumnType("INT")
                .HasColumnName("npc_id");
            entity.Property(e => e.SpecialtyBundleId)
                .HasColumnType("INT")
                .HasColumnName("specialty_bundle_id");
        });

        modelBuilder.Entity<Sphere>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("spheres");

            entity.Property(e => e.CategoryId)
                .HasColumnType("INT")
                .HasColumnName("category_id");
            entity.Property(e => e.EnterOrLeave)
                .HasColumnType("NUM")
                .HasColumnName("enter_or_leave");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.IsPersonalMsg)
                .HasColumnType("NUM")
                .HasColumnName("is_personal_msg");
            entity.Property(e => e.MilestoneId)
                .HasColumnType("INT")
                .HasColumnName("milestone_id");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.NameTr)
                .HasColumnType("NUM")
                .HasColumnName("name_tr");
            entity.Property(e => e.OrUnitReqs)
                .HasColumnType("NUM")
                .HasColumnName("or_unit_reqs");
            entity.Property(e => e.SphereDetailId)
                .HasColumnType("INT")
                .HasColumnName("sphere_detail_id");
            entity.Property(e => e.SphereDetailType).HasColumnName("sphere_detail_type");
            entity.Property(e => e.TeamMsg).HasColumnName("team_msg");
            entity.Property(e => e.TeamMsgTr)
                .HasColumnType("NUM")
                .HasColumnName("team_msg_tr");
            entity.Property(e => e.TriggerConditionId)
                .HasColumnType("INT")
                .HasColumnName("trigger_condition_id");
            entity.Property(e => e.TriggerConditionTime)
                .HasColumnType("INT")
                .HasColumnName("trigger_condition_time");
        });

        modelBuilder.Entity<SphereAcceptQuest>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("sphere_accept_quests");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
        });

        modelBuilder.Entity<SphereAcceptQuestQuest>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("sphere_accept_quest_quests");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.QuestId)
                .HasColumnType("INT")
                .HasColumnName("quest_id");
            entity.Property(e => e.SphereAcceptQuestId)
                .HasColumnType("INT")
                .HasColumnName("sphere_accept_quest_id");
        });

        modelBuilder.Entity<SphereBubble>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("sphere_bubbles");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
        });

        modelBuilder.Entity<SphereBuff>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("sphere_buffs");

            entity.Property(e => e.BuffId)
                .HasColumnType("INT")
                .HasColumnName("buff_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
        });

        modelBuilder.Entity<SphereChatBubble>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("sphere_chat_bubbles");

            entity.Property(e => e.Angle)
                .HasColumnType("INT")
                .HasColumnName("angle");
            entity.Property(e => e.CameraId)
                .HasColumnType("INT")
                .HasColumnName("camera_id");
            entity.Property(e => e.ChangeSpeakerName).HasColumnName("change_speaker_name");
            entity.Property(e => e.ChatBubbleKindId)
                .HasColumnType("INT")
                .HasColumnName("chat_bubble_kind_id");
            entity.Property(e => e.Facial).HasColumnName("facial");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.IsStart)
                .HasColumnType("NUM")
                .HasColumnName("is_start");
            entity.Property(e => e.NextBubble)
                .HasColumnType("INT")
                .HasColumnName("next_bubble");
            entity.Property(e => e.NpcId)
                .HasColumnType("INT")
                .HasColumnName("npc_id");
            entity.Property(e => e.NpcSpawnerId)
                .HasColumnType("INT")
                .HasColumnName("npc_spawner_id");
            entity.Property(e => e.SoundId)
                .HasColumnType("INT")
                .HasColumnName("sound_id");
            entity.Property(e => e.Speech).HasColumnName("speech");
            entity.Property(e => e.SphereBubbleId)
                .HasColumnType("INT")
                .HasColumnName("sphere_bubble_id");
        });

        modelBuilder.Entity<SphereDoodadInteract>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("sphere_doodad_interacts");

            entity.Property(e => e.DoodadFamilyId)
                .HasColumnType("INT")
                .HasColumnName("doodad_family_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.SkillId)
                .HasColumnType("INT")
                .HasColumnName("skill_id");
        });

        modelBuilder.Entity<SphereQuest>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("sphere_quests");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.QuestId)
                .HasColumnType("INT")
                .HasColumnName("quest_id");
            entity.Property(e => e.QuestTriggerId)
                .HasColumnType("INT")
                .HasColumnName("quest_trigger_id");
        });

        modelBuilder.Entity<SphereQuestMail>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("sphere_quest_mails");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.MailId)
                .HasColumnType("INT")
                .HasColumnName("mail_id");
        });

        modelBuilder.Entity<SphereSkill>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("sphere_skills");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.MaxRate)
                .HasColumnType("INT")
                .HasColumnName("max_rate");
            entity.Property(e => e.MinRate)
                .HasColumnType("INT")
                .HasColumnName("min_rate");
            entity.Property(e => e.SkillId)
                .HasColumnType("INT")
                .HasColumnName("skill_id");
        });

        modelBuilder.Entity<SphereSound>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("sphere_sounds");

            entity.Property(e => e.Broadcast)
                .HasColumnType("NUM")
                .HasColumnName("broadcast");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.SoundId)
                .HasColumnType("INT")
                .HasColumnName("sound_id");
        });

        modelBuilder.Entity<SubZone>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("sub_zones");

            entity.Property(e => e.CategoryId)
                .HasColumnType("INT")
                .HasColumnName("category_id");
            entity.Property(e => e.H).HasColumnName("h");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Idx)
                .HasColumnType("INT")
                .HasColumnName("idx");
            entity.Property(e => e.ImageMap)
                .HasColumnType("INT")
                .HasColumnName("image_map");
            entity.Property(e => e.LinkedZoneGroupId)
                .HasColumnType("INT")
                .HasColumnName("linked_zone_group_id");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.ParentSubZoneId)
                .HasColumnType("INT")
                .HasColumnName("parent_sub_zone_id");
            entity.Property(e => e.SoundId)
                .HasColumnType("INT")
                .HasColumnName("sound_id");
            entity.Property(e => e.SoundPackId)
                .HasColumnType("INT")
                .HasColumnName("sound_pack_id");
            entity.Property(e => e.W).HasColumnName("w");
            entity.Property(e => e.X).HasColumnName("x");
            entity.Property(e => e.Y).HasColumnName("y");
        });

        modelBuilder.Entity<SystemFaction>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("system_factions");

            entity.Property(e => e.AggroLink)
                .HasColumnType("NUM")
                .HasColumnName("aggro_link");
            entity.Property(e => e.DiplomacyLinkId)
                .HasColumnType("INT")
                .HasColumnName("diplomacy_link_id");
            entity.Property(e => e.GuardHelp)
                .HasColumnType("NUM")
                .HasColumnName("guard_help");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.IsDiplomacyTgt)
                .HasColumnType("NUM")
                .HasColumnName("is_diplomacy_tgt");
            entity.Property(e => e.MotherId)
                .HasColumnType("INT")
                .HasColumnName("mother_id");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.OwnerId)
                .HasColumnType("INT")
                .HasColumnName("owner_id");
            entity.Property(e => e.OwnerName).HasColumnName("owner_name");
            entity.Property(e => e.OwnerTypeId)
                .HasColumnType("INT")
                .HasColumnName("owner_type_id");
            entity.Property(e => e.PoliticalSystemId)
                .HasColumnType("INT")
                .HasColumnName("political_system_id");
        });

        modelBuilder.Entity<SystemFactionRelation>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("system_faction_relations");

            entity.Property(e => e.Faction1Id)
                .HasColumnType("INT")
                .HasColumnName("faction1_id");
            entity.Property(e => e.Faction2Id)
                .HasColumnType("INT")
                .HasColumnName("faction2_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.StateId)
                .HasColumnType("INT")
                .HasColumnName("state_id");
        });

        modelBuilder.Entity<Tag>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("tags");

            entity.Property(e => e.Desc).HasColumnName("desc");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.Translate)
                .HasColumnType("NUM")
                .HasColumnName("translate");
        });

        modelBuilder.Entity<TaggedBuff>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("tagged_buffs");

            entity.Property(e => e.BuffId)
                .HasColumnType("INT")
                .HasColumnName("buff_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.TagId)
                .HasColumnType("INT")
                .HasColumnName("tag_id");
        });

        modelBuilder.Entity<TaggedItem>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("tagged_items");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.ItemId)
                .HasColumnType("INT")
                .HasColumnName("item_id");
            entity.Property(e => e.TagId)
                .HasColumnType("INT")
                .HasColumnName("tag_id");
        });

        modelBuilder.Entity<TaggedNpc>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("tagged_npcs");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.NpcId)
                .HasColumnType("INT")
                .HasColumnName("npc_id");
            entity.Property(e => e.TagId)
                .HasColumnType("INT")
                .HasColumnName("tag_id");
        });

        modelBuilder.Entity<TaggedSkill>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("tagged_skills");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.SkillId)
                .HasColumnType("INT")
                .HasColumnName("skill_id");
            entity.Property(e => e.TagId)
                .HasColumnType("INT")
                .HasColumnName("tag_id");
        });

        modelBuilder.Entity<Taxation>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("taxations");

            entity.Property(e => e.Desc).HasColumnName("desc");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Show)
                .HasColumnType("NUM")
                .HasColumnName("show");
            entity.Property(e => e.Tax)
                .HasColumnType("INT")
                .HasColumnName("tax");
        });

        modelBuilder.Entity<TooltipSkillEffect>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("tooltip_skill_effects");

            entity.Property(e => e.EffectId)
                .HasColumnType("INT")
                .HasColumnName("effect_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.SkillId)
                .HasColumnType("INT")
                .HasColumnName("skill_id");
        });

        modelBuilder.Entity<TotalCharacterCustom>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("total_character_customs");

            entity.Property(e => e.DecoColor)
                .HasColumnType("INT")
                .HasColumnName("deco_color");
            entity.Property(e => e.EyebrowColor)
                .HasColumnType("INT")
                .HasColumnName("eyebrow_color");
            entity.Property(e => e.FaceDiffuseMapId)
                .HasColumnType("INT")
                .HasColumnName("face_diffuse_map_id");
            entity.Property(e => e.FaceEyelashMapId)
                .HasColumnType("INT")
                .HasColumnName("face_eyelash_map_id");
            entity.Property(e => e.FaceFixedDecalAsset0Id)
                .HasColumnType("INT")
                .HasColumnName("face_fixed_decal_asset_0_id");
            entity.Property(e => e.FaceFixedDecalAsset0Weight).HasColumnName("face_fixed_decal_asset_0_weight");
            entity.Property(e => e.FaceFixedDecalAsset1Id)
                .HasColumnType("INT")
                .HasColumnName("face_fixed_decal_asset_1_id");
            entity.Property(e => e.FaceFixedDecalAsset1Weight).HasColumnName("face_fixed_decal_asset_1_weight");
            entity.Property(e => e.FaceFixedDecalAsset2Id)
                .HasColumnType("INT")
                .HasColumnName("face_fixed_decal_asset_2_id");
            entity.Property(e => e.FaceFixedDecalAsset2Weight).HasColumnName("face_fixed_decal_asset_2_weight");
            entity.Property(e => e.FaceFixedDecalAsset3Id)
                .HasColumnType("INT")
                .HasColumnName("face_fixed_decal_asset_3_id");
            entity.Property(e => e.FaceFixedDecalAsset3Weight).HasColumnName("face_fixed_decal_asset_3_weight");
            entity.Property(e => e.FaceMovableDecalAssetId)
                .HasColumnType("INT")
                .HasColumnName("face_movable_decal_asset_id");
            entity.Property(e => e.FaceMovableDecalMoveX)
                .HasColumnType("INT")
                .HasColumnName("face_movable_decal_move_x");
            entity.Property(e => e.FaceMovableDecalMoveY)
                .HasColumnType("INT")
                .HasColumnName("face_movable_decal_move_y");
            entity.Property(e => e.FaceMovableDecalRotate).HasColumnName("face_movable_decal_rotate");
            entity.Property(e => e.FaceMovableDecalScale).HasColumnName("face_movable_decal_scale");
            entity.Property(e => e.FaceMovableDecalWeight).HasColumnName("face_movable_decal_weight");
            entity.Property(e => e.FaceNormalMapId)
                .HasColumnType("INT")
                .HasColumnName("face_normal_map_id");
            entity.Property(e => e.FaceNormalMapWeight).HasColumnName("face_normal_map_weight");
            entity.Property(e => e.HairColorId)
                .HasColumnType("INT")
                .HasColumnName("hair_color_id");
            entity.Property(e => e.HairId)
                .HasColumnType("INT")
                .HasColumnName("hair_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.LeftPupilColor)
                .HasColumnType("INT")
                .HasColumnName("left_pupil_color");
            entity.Property(e => e.LipColor)
                .HasColumnType("INT")
                .HasColumnName("lip_color");
            entity.Property(e => e.ModelId)
                .HasColumnType("INT")
                .HasColumnName("model_id");
            entity.Property(e => e.Modifier).HasColumnName("modifier");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.NpcOnly)
                .HasColumnType("NUM")
                .HasColumnName("npcOnly");
            entity.Property(e => e.OwnerTypeId)
                .HasColumnType("INT")
                .HasColumnName("owner_type_id");
            entity.Property(e => e.RightPupilColor)
                .HasColumnType("INT")
                .HasColumnName("right_pupil_color");
            entity.Property(e => e.SkinColorId)
                .HasColumnType("INT")
                .HasColumnName("skin_color_id");
        });

        modelBuilder.Entity<TowerDef>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("tower_defs");

            entity.Property(e => e.EndMsg).HasColumnName("end_msg");
            entity.Property(e => e.FirstWaveAfter).HasColumnName("first_wave_after");
            entity.Property(e => e.ForceEndTime).HasColumnName("force_end_time");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.KillNpcCount)
                .HasColumnType("INT")
                .HasColumnName("kill_npc_count");
            entity.Property(e => e.KillNpcId)
                .HasColumnType("INT")
                .HasColumnName("kill_npc_id");
            entity.Property(e => e.MilestoneId)
                .HasColumnType("INT")
                .HasColumnName("milestone_id");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.StartMsg).HasColumnName("start_msg");
            entity.Property(e => e.TargetNpcSpawnerId)
                .HasColumnType("INT")
                .HasColumnName("target_npc_spawner_id");
            entity.Property(e => e.TitleMsg).HasColumnName("title_msg");
            entity.Property(e => e.Tod).HasColumnName("tod");
            entity.Property(e => e.TodDayInterval)
                .HasColumnType("INT")
                .HasColumnName("tod_day_interval");
        });

        modelBuilder.Entity<TowerDefProg>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("tower_def_progs");

            entity.Property(e => e.CondCompByAnd)
                .HasColumnType("NUM")
                .HasColumnName("cond_comp_by_and");
            entity.Property(e => e.CondToNextTime).HasColumnName("cond_to_next_time");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Msg).HasColumnName("msg");
            entity.Property(e => e.TowerDefId)
                .HasColumnType("INT")
                .HasColumnName("tower_def_id");
        });

        modelBuilder.Entity<TowerDefProgKillTarget>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("tower_def_prog_kill_targets");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.KillCount).HasColumnName("kill_count");
            entity.Property(e => e.KillTargetId).HasColumnName("kill_target_id");
            entity.Property(e => e.KillTargetType).HasColumnName("kill_target_type");
            entity.Property(e => e.TowerDefProgId).HasColumnName("tower_def_prog_id");
        });

        modelBuilder.Entity<TowerDefProgSpawnTarget>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("tower_def_prog_spawn_targets");

            entity.Property(e => e.DespawnOnNextStep)
                .HasColumnType("NUM")
                .HasColumnName("despawn_on_next_step");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.SpawnTargetId)
                .HasColumnType("INT")
                .HasColumnName("spawn_target_id");
            entity.Property(e => e.SpawnTargetType).HasColumnName("spawn_target_type");
            entity.Property(e => e.TowerDefProgId)
                .HasColumnType("INT")
                .HasColumnName("tower_def_prog_id");
        });

        modelBuilder.Entity<TrainCraftEffect>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("train_craft_effects");

            entity.Property(e => e.CraftId)
                .HasColumnType("INT")
                .HasColumnName("craft_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
        });

        modelBuilder.Entity<TrainCraftRankEffect>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("train_craft_rank_effects");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.KindId)
                .HasColumnType("INT")
                .HasColumnName("kind_id");
            entity.Property(e => e.RankId)
                .HasColumnType("INT")
                .HasColumnName("rank_id");
        });

        modelBuilder.Entity<Transfer>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("transfers");

            entity.Property(e => e.Comment).HasColumnName("comment");
            entity.Property(e => e.Cyclic)
                .HasColumnType("NUM")
                .HasColumnName("cyclic");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.ModelId)
                .HasColumnType("INT")
                .HasColumnName("model_id");
            entity.Property(e => e.PathSmoothing).HasColumnName("path_smoothing");
            entity.Property(e => e.WaitTime).HasColumnName("wait_time");
        });

        modelBuilder.Entity<TransferBinding>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("transfer_bindings");

            entity.Property(e => e.AttachPointId)
                .HasColumnType("INT")
                .HasColumnName("attach_point_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.OwnerId)
                .HasColumnType("INT")
                .HasColumnName("owner_id");
            entity.Property(e => e.OwnerType).HasColumnName("owner_type");
            entity.Property(e => e.TransferId)
                .HasColumnType("INT")
                .HasColumnName("transfer_id");
        });

        modelBuilder.Entity<TransferBindingDoodad>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("transfer_binding_doodads");

            entity.Property(e => e.AttachPointId)
                .HasColumnType("INT")
                .HasColumnName("attach_point_id");
            entity.Property(e => e.DoodadId)
                .HasColumnType("INT")
                .HasColumnName("doodad_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.OwnerId)
                .HasColumnType("INT")
                .HasColumnName("owner_id");
            entity.Property(e => e.OwnerType).HasColumnName("owner_type");
        });

        modelBuilder.Entity<TransferPath>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("transfer_paths");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.OwnerId)
                .HasColumnType("INT")
                .HasColumnName("owner_id");
            entity.Property(e => e.OwnerType).HasColumnName("owner_type");
            entity.Property(e => e.PathName).HasColumnName("path_name");
            entity.Property(e => e.WaitTimeEnd).HasColumnName("wait_time_end");
            entity.Property(e => e.WaitTimeStart).HasColumnName("wait_time_start");
        });

        modelBuilder.Entity<UccApplicable>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("ucc_applicables");

            entity.Property(e => e.ActualId)
                .HasColumnType("INT")
                .HasColumnName("actual_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.KindId)
                .HasColumnType("INT")
                .HasColumnName("kind_id");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.TooltipMsg).HasColumnName("tooltip_msg");
        });

        modelBuilder.Entity<UiText>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("ui_texts");

            entity.Property(e => e.CategoryId)
                .HasColumnType("INT")
                .HasColumnName("category_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Key).HasColumnName("key");
            entity.Property(e => e.Text).HasColumnName("text");
        });

        modelBuilder.Entity<UnitAttributeLimit>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("unit_attribute_limits");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Maximum)
                .HasColumnType("INT")
                .HasColumnName("maximum");
            entity.Property(e => e.Minimum)
                .HasColumnType("INT")
                .HasColumnName("minimum");
            entity.Property(e => e.UnitAttributeId)
                .HasColumnType("INT")
                .HasColumnName("unit_attribute_id");
        });

        modelBuilder.Entity<UnitFormula>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("unit_formulas");

            entity.Property(e => e.Formula).HasColumnName("formula");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.KindId)
                .HasColumnType("INT")
                .HasColumnName("kind_id");
            entity.Property(e => e.OwnerTypeId)
                .HasColumnType("INT")
                .HasColumnName("owner_type_id");
        });

        modelBuilder.Entity<UnitFormulaVariable>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("unit_formula_variables");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Key)
                .HasColumnType("INT")
                .HasColumnName("key");
            entity.Property(e => e.UnitFormulaId)
                .HasColumnType("INT")
                .HasColumnName("unit_formula_id");
            entity.Property(e => e.Value).HasColumnName("value");
            entity.Property(e => e.VariableKindId)
                .HasColumnType("INT")
                .HasColumnName("variable_kind_id");
        });

        modelBuilder.Entity<UnitModifier>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("unit_modifiers");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.LinearLevelBonus)
                .HasColumnType("INT")
                .HasColumnName("linear_level_bonus");
            entity.Property(e => e.OwnerId)
                .HasColumnType("INT")
                .HasColumnName("owner_id");
            entity.Property(e => e.OwnerType).HasColumnName("owner_type");
            entity.Property(e => e.UnitAttributeId)
                .HasColumnType("INT")
                .HasColumnName("unit_attribute_id");
            entity.Property(e => e.UnitModifierTypeId)
                .HasColumnType("INT")
                .HasColumnName("unit_modifier_type_id");
            entity.Property(e => e.Value)
                .HasColumnType("INT")
                .HasColumnName("value");
        });

        modelBuilder.Entity<UnitReq>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("unit_reqs");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.KindId)
                .HasColumnType("INT")
                .HasColumnName("kind_id");
            entity.Property(e => e.OwnerId)
                .HasColumnType("INT")
                .HasColumnName("owner_id");
            entity.Property(e => e.OwnerType).HasColumnName("owner_type");
            entity.Property(e => e.Value1)
                .HasColumnType("INT")
                .HasColumnName("value1");
            entity.Property(e => e.Value2)
                .HasColumnType("INT")
                .HasColumnName("value2");
        });

        modelBuilder.Entity<VehicleModel>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("vehicle_models");

            entity.Property(e => e.AngVel).HasColumnName("angVel");
            entity.Property(e => e.AutoLevel)
                .HasColumnType("NUM")
                .HasColumnName("auto_level");
            entity.Property(e => e.CanFly)
                .HasColumnType("NUM")
                .HasColumnName("can_fly");
            entity.Property(e => e.Damaged25).HasColumnName("damaged25");
            entity.Property(e => e.Damaged50).HasColumnName("damaged50");
            entity.Property(e => e.Damaged75).HasColumnName("damaged75");
            entity.Property(e => e.Dead).HasColumnName("dead");
            entity.Property(e => e.DriverWalk)
                .HasColumnType("NUM")
                .HasColumnName("driver_walk");
            entity.Property(e => e.Dying).HasColumnName("dying");
            entity.Property(e => e.FloatingHeight).HasColumnName("floating_height");
            entity.Property(e => e.FloatingWaveHeight).HasColumnName("floating_wave_height");
            entity.Property(e => e.FloatingWavePeriodRatio).HasColumnName("floating_wave_period_ratio");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.InstalledTurret)
                .HasColumnType("NUM")
                .HasColumnName("installed_turret");
            entity.Property(e => e.LinDeaccelInertia).HasColumnName("lin_deaccel_inertia");
            entity.Property(e => e.LinInertia).HasColumnName("lin_inertia");
            entity.Property(e => e.MaxClimbAng).HasColumnName("max_climb_ang");
            entity.Property(e => e.Normal).HasColumnName("normal");
            entity.Property(e => e.RotDeaccelInertia).HasColumnName("rot_deaccel_inertia");
            entity.Property(e => e.RotInertia).HasColumnName("rot_inertia");
            entity.Property(e => e.SoundId)
                .HasColumnType("INT")
                .HasColumnName("sound_id");
            entity.Property(e => e.SuspAxle)
                .HasColumnType("NUM")
                .HasColumnName("susp_axle");
            entity.Property(e => e.SuspStroke).HasColumnName("susp_stroke");
            entity.Property(e => e.TrailAlignRatio).HasColumnName("trail_align_ratio");
            entity.Property(e => e.TurretPitchAngleMax).HasColumnName("turret_pitch_angle_max");
            entity.Property(e => e.TurretPitchAngleMin).HasColumnName("turret_pitch_angle_min");
            entity.Property(e => e.TurretPitchAngvel).HasColumnName("turret_pitch_angvel");
            entity.Property(e => e.TurretYawAngleMax).HasColumnName("turret_yaw_angle_max");
            entity.Property(e => e.TurretYawAngleMin).HasColumnName("turret_yaw_angle_min");
            entity.Property(e => e.TurretYawAngvel).HasColumnName("turret_yaw_angvel");
            entity.Property(e => e.UseCenterSpindle)
                .HasColumnType("NUM")
                .HasColumnName("use_center_spindle");
            entity.Property(e => e.UseProxyCollision)
                .HasColumnType("NUM")
                .HasColumnName("use_proxy_collision");
            entity.Property(e => e.UseWheeledVehicleSimulation)
                .HasColumnType("NUM")
                .HasColumnName("use_wheeled_vehicle_simulation");
            entity.Property(e => e.Velocity).HasColumnName("velocity");
            entity.Property(e => e.Wheel).HasColumnName("wheel");
            entity.Property(e => e.Wheel2).HasColumnName("wheel2");
            entity.Property(e => e.WheeledVehicleBallastMass).HasColumnName("wheeled_vehicle_ballast_mass");
            entity.Property(e => e.WheeledVehicleBallastPosY).HasColumnName("wheeled_vehicle_ballast_pos_y");
            entity.Property(e => e.WheeledVehicleBrakeTorque).HasColumnName("wheeled_vehicle_brake_torque");
            entity.Property(e => e.WheeledVehicleDrive)
                .HasColumnType("INT")
                .HasColumnName("wheeled_vehicle_drive");
            entity.Property(e => e.WheeledVehicleFrontOptimalSa).HasColumnName("wheeled_vehicle_front_optimal_sa");
            entity.Property(e => e.WheeledVehicleGearSpeedRatio1).HasColumnName("wheeled_vehicle_gear_speed_ratio_1");
            entity.Property(e => e.WheeledVehicleGearSpeedRatio2).HasColumnName("wheeled_vehicle_gear_speed_ratio_2");
            entity.Property(e => e.WheeledVehicleGearSpeedRatio3).HasColumnName("wheeled_vehicle_gear_speed_ratio_3");
            entity.Property(e => e.WheeledVehicleGearSpeedRatioReverse).HasColumnName("wheeled_vehicle_gear_speed_ratio_reverse");
            entity.Property(e => e.WheeledVehicleMass).HasColumnName("wheeled_vehicle_mass");
            entity.Property(e => e.WheeledVehicleMaxGear)
                .HasColumnType("INT")
                .HasColumnName("wheeled_vehicle_max_gear");
            entity.Property(e => e.WheeledVehiclePower).HasColumnName("wheeled_vehicle_power");
            entity.Property(e => e.WheeledVehicleRearOptimalSa).HasColumnName("wheeled_vehicle_rear_optimal_sa");
            entity.Property(e => e.WheeledVehicleSuspDamping).HasColumnName("wheeled_vehicle_susp_damping");
            entity.Property(e => e.WheeledVehicleSuspStroke).HasColumnName("wheeled_vehicle_susp_stroke");
        });

        modelBuilder.Entity<Wearable>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("wearables");

            entity.Property(e => e.ArmorBp)
                .HasColumnType("INT")
                .HasColumnName("armor_bp");
            entity.Property(e => e.ArmorTypeId)
                .HasColumnType("INT")
                .HasColumnName("armor_type_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.MagicResistanceBp)
                .HasColumnType("INT")
                .HasColumnName("magic_resistance_bp");
            entity.Property(e => e.SlotTypeId)
                .HasColumnType("INT")
                .HasColumnName("slot_type_id");
        });

        modelBuilder.Entity<WearableFormula>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("wearable_formulas");

            entity.Property(e => e.Formula).HasColumnName("formula");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.KindId)
                .HasColumnType("INT")
                .HasColumnName("kind_id");
        });

        modelBuilder.Entity<WearableKind>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("wearable_kinds");

            entity.Property(e => e.ArmorRatio)
                .HasColumnType("INT")
                .HasColumnName("armor_ratio");
            entity.Property(e => e.ArmorTypeId)
                .HasColumnType("INT")
                .HasColumnName("armor_type_id");
            entity.Property(e => e.DurabilityRatio).HasColumnName("durability_ratio");
            entity.Property(e => e.ExtraDamageBlunt)
                .HasColumnType("INT")
                .HasColumnName("extra_damage_blunt");
            entity.Property(e => e.ExtraDamagePierce)
                .HasColumnType("INT")
                .HasColumnName("extra_damage_pierce");
            entity.Property(e => e.ExtraDamageSlash)
                .HasColumnType("INT")
                .HasColumnName("extra_damage_slash");
            entity.Property(e => e.FullBuffId)
                .HasColumnType("INT")
                .HasColumnName("full_buff_id");
            entity.Property(e => e.HalfBuffId)
                .HasColumnType("INT")
                .HasColumnName("half_buff_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.MagicResistanceRatio)
                .HasColumnType("INT")
                .HasColumnName("magic_resistance_ratio");
            entity.Property(e => e.SoundMaterialId)
                .HasColumnType("INT")
                .HasColumnName("sound_material_id");
        });

        modelBuilder.Entity<WearableSlot>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("wearable_slots");

            entity.Property(e => e.Coverage)
                .HasColumnType("INT")
                .HasColumnName("coverage");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.SlotTypeId)
                .HasColumnType("INT")
                .HasColumnName("slot_type_id");
        });

        modelBuilder.Entity<WiDetail>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("wi_details");

            entity.Property(e => e.ApplyExpert)
                .HasColumnType("NUM")
                .HasColumnName("apply_expert");
            entity.Property(e => e.DistanceSqrt)
                .HasColumnType("INT")
                .HasColumnName("distance_sqrt");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Lp)
                .HasColumnType("INT")
                .HasColumnName("lp");
            entity.Property(e => e.WiId)
                .HasColumnType("INT")
                .HasColumnName("wi_id");
        });

        modelBuilder.Entity<WiGroup>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("wi_groups");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name");
        });

        modelBuilder.Entity<WiGroupWi>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("wi_group_wis");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.WiGroupId)
                .HasColumnType("INT")
                .HasColumnName("wi_group_id");
            entity.Property(e => e.WiId)
                .HasColumnType("INT")
                .HasColumnName("wi_id");
        });

        modelBuilder.Entity<WorldGroup>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("world_groups");

            entity.Property(e => e.H)
                .HasColumnType("INT")
                .HasColumnName("h");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.ImageH)
                .HasColumnType("INT")
                .HasColumnName("image_h");
            entity.Property(e => e.ImageMap)
                .HasColumnType("INT")
                .HasColumnName("image_map");
            entity.Property(e => e.ImageW)
                .HasColumnType("INT")
                .HasColumnName("image_w");
            entity.Property(e => e.ImageX)
                .HasColumnType("INT")
                .HasColumnName("image_x");
            entity.Property(e => e.ImageY)
                .HasColumnType("INT")
                .HasColumnName("image_y");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.TargetId)
                .HasColumnType("INT")
                .HasColumnName("target_id");
            entity.Property(e => e.W)
                .HasColumnType("INT")
                .HasColumnName("w");
            entity.Property(e => e.X)
                .HasColumnType("INT")
                .HasColumnName("x");
            entity.Property(e => e.Y)
                .HasColumnType("INT")
                .HasColumnName("y");
        });

        modelBuilder.Entity<WorldSpecConfig>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("world_spec_configs");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.SpecialtyAdjustRatio)
                .HasColumnType("INT")
                .HasColumnName("specialty_adjust_ratio");
            entity.Property(e => e.SpecialtyMod)
                .HasColumnType("INT")
                .HasColumnName("specialty_mod");
            entity.Property(e => e.WorldId)
                .HasColumnType("INT")
                .HasColumnName("world_id");
        });

        modelBuilder.Entity<WorldVarDefault>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("world_var_defaults");

            entity.Property(e => e.DefaultValue).HasColumnName("default_value");
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.KindId).HasColumnName("kind_id");
            entity.Property(e => e.VariableName).HasColumnName("variable_name");
        });

        modelBuilder.Entity<Zone>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("zones");

            entity.Property(e => e.AboxShow)
                .HasColumnType("NUM")
                .HasColumnName("abox_show");
            entity.Property(e => e.Closed)
                .HasColumnType("NUM")
                .HasColumnName("closed");
            entity.Property(e => e.DisplayText).HasColumnName("display_text");
            entity.Property(e => e.FactionId)
                .HasColumnType("INT")
                .HasColumnName("faction_id");
            entity.Property(e => e.GroupId)
                .HasColumnType("INT")
                .HasColumnName("group_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.ZoneClimateId)
                .HasColumnType("INT")
                .HasColumnName("zone_climate_id");
            entity.Property(e => e.ZoneKey)
                .HasColumnType("INT")
                .HasColumnName("zone_key");
        });

        modelBuilder.Entity<ZoneClimate>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("zone_climates");

            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name");
        });

        modelBuilder.Entity<ZoneClimateElem>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("zone_climate_elems");

            entity.Property(e => e.ClimateId)
                .HasColumnType("INT")
                .HasColumnName("climate_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.ZoneClimateId)
                .HasColumnType("INT")
                .HasColumnName("zone_climate_id");
        });

        modelBuilder.Entity<ZoneGroup>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("zone_groups");

            entity.Property(e => e.BuffId)
                .HasColumnType("INT")
                .HasColumnName("buff_id");
            entity.Property(e => e.DisplayText).HasColumnName("display_text");
            entity.Property(e => e.FactionChatRegionId)
                .HasColumnType("INT")
                .HasColumnName("faction_chat_region_id");
            entity.Property(e => e.FishingLandLootPackId)
                .HasColumnType("INT")
                .HasColumnName("fishing_land_loot_pack_id");
            entity.Property(e => e.FishingSeaLootPackId)
                .HasColumnType("INT")
                .HasColumnName("fishing_sea_loot_pack_id");
            entity.Property(e => e.H).HasColumnName("h");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.ImageMap)
                .HasColumnType("INT")
                .HasColumnName("image_map");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.PirateDesperado)
                .HasColumnType("NUM")
                .HasColumnName("pirate_desperado");
            entity.Property(e => e.SoundId)
                .HasColumnType("INT")
                .HasColumnName("sound_id");
            entity.Property(e => e.SoundPackId)
                .HasColumnType("INT")
                .HasColumnName("sound_pack_id");
            entity.Property(e => e.TargetId)
                .HasColumnType("INT")
                .HasColumnName("target_id");
            entity.Property(e => e.W).HasColumnName("w");
            entity.Property(e => e.X).HasColumnName("x");
            entity.Property(e => e.Y).HasColumnName("y");
        });

        modelBuilder.Entity<ZoneGroupBannedTag>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("zone_group_banned_tags");

            entity.Property(e => e.BannedPeriodsId)
                .HasColumnType("INT")
                .HasColumnName("banned_periods_id");
            entity.Property(e => e.Id)
                .HasColumnType("INT")
                .HasColumnName("id");
            entity.Property(e => e.TagId)
                .HasColumnType("INT")
                .HasColumnName("tag_id");
            entity.Property(e => e.Usage).HasColumnName("usage");
            entity.Property(e => e.ZoneGroupId)
                .HasColumnType("INT")
                .HasColumnName("zone_group_id");
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}

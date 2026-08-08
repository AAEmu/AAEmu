namespace AAEmu.Game.Models.Game.Features;

/// <summary>
/// Feature bits inside the 31-byte <c>fset</c> blob of SCInitialConfigPacket (opcode 0x007),
/// version 10.0.2.13.
/// </summary>
/// <remarks>
/// <para>
/// Value == bit index into the blob: <c>byteIndex = value / 8</c>, <c>bitIndex = value % 8</c>
/// (LSB first). Do NOT renumber - these are wire values read straight out of the client.
/// </para>
/// <para>
/// Entries marked "native only" are never exported to the Lua UI table; they are tested
/// directly by client C++ code, which is why they carry no official name.
/// </para>
/// <para>
/// Bytes 1, 8, 10 and 26 are numeric scalars, NOT flags - see <see cref="FeatureSet"/>.
/// Never define a Feature inside those bytes.
/// </para>
/// <para>
/// Three features left the blob in 10.0.2.13 and are now driven by the system-feature table
/// (SCSystemFeatureStateListPacket): returnAccount (274), equipSlotFormulaItemLevel (359),
/// equipSlotBundleEffect (360).
/// </para>
/// </remarks>
public enum Feature
{
    // ---- fset[0] ----
    siege = 0,
    fset_0_1_unknown = 1,  // native only - /family_title command, family appellation (1.2 allowFamilyChanges)
    use_slash_open_chat = 2,
    premium = 4,
    combatResource = 6,

    // ---- fset[4] ----
    nexonPcRoom = 34,
    ranking = 36,
    fset_4_5_unknown = 37,
    ingamecashshop = 38,
    fset_4_7_unknown = 39,  // native only - custom UI / addon Lua loading (1.2 customUiButton)

    // ---- fset[5] ----
    customsaveload = 40,
    fset_5_1_unknown = 41,
    fset_5_2_unknown = 42,  // native only - character & diary transfer
    bm_mileage = 43,
    aaPoint = 44,
    itemSecure = 45,
    secondpass = 46,
    butler = 47,

    // ---- fset[6] ----
    slave_customize = 48,
    pvpModifiySet = 52,
    freeLpRaise = 53,
    itemCapScale = 54,

    // ---- fset[7] ----
    sensitiveOpeartion = 56,
    tailCustomizing = 57,
    fset_7_2_unknown = 58,  // native only - checked in OnLoadingWorldComplete()
    reportSpamMail = 61,
    banishPlayer = 63,

    // ---- fset[9] ----
    housingUcc = 73,
    itemChangeMapping = 74,
    /// <summary>
    /// Alias for bit 74. Client race-create gate for Dwarf/Warborn tests
    /// <c>(uint)fset[8] &amp; 0x400</c> → byte 9 bit 2 → absolute bit 74. Keep enabled for
    /// those races; the Lua export name is itemChangeMapping.
    /// </summary>
    dwarfWarborn = 74,
    mailCoolTime = 75,
    fset_9_6_unknown = 78,  // native only - mate item equip check

    // ---- fset[11] ----
    useUrlLink = 89,
    fset_11_2_unknown = 90,  // native only - doodad descriptor lookup
    auctionPostBuff = 91,
    itemRepairInBag = 92,
    petOnlyEnchantStone = 93,
    questNpcTag = 94,
    houseTaxPrepay = 95,

    // ---- fset[12] ----
    fset_12_0_unknown = 96,
    fset_12_1_unknown = 97,  // native only - item repair cost/slot collector
    arche_pass = 98,
    hud_mail_box_button = 99,
    fastQuestChatBubble = 100,
    /// <summary>
    /// Ancestral ("heir") level progression. Backs the native <c>X2Player:IsEnabledHeirLevel()</c>,
    /// which is <c>(fset[12] &amp; 0x20) != 0 &amp;&amp; content_config heir_start_level (187) &gt; 0</c> -
    /// the client reads the dword at ctx+0x34 (fset[12..15]) and tests bit 5. It gates the whole
    /// level block at the top of the Heir tab: level label, exp bar, level-up button and the four
    /// heir stat lines (CreateHeirLevelSection in x2ui/skill/tab_heir.lua).
    /// Separate from <see cref="useHeirSkill"/> (202), which only gates the tab itself - with this
    /// bit off the tab renders but shows the successor wheel alone and can never gain a level.
    /// </summary>
    heirLevel = 101,
    forbidTransferChar = 102,
    target_equipment_wnd = 103,

    // ---- fset[13] ----
    fset_13_0_unknown = 104,  // native only - movement (CSMoveUnitPacket)
    fset_13_1_unknown = 105,  // native only - skill use / buff removal
    indunPortal = 106,
    fset_13_3_unknown = 107,  // native only - premium service message + mail attachment
    indunDailyLimit = 110,
    rebuildHouse = 111,

    // ---- fset[14] ----
    fset_14_0_unknown = 112,  // native only - login-char unit creation (1.2 useTGOS)
    reportSpammer = 113,
    hero = 114,
    marketPrice = 115,
    buyPremiuminSelChar = 117,

    // ---- fset[17] ----
    fset_17_0_unknown = 136,  // native only - npctype:// chat link
    fset_17_1_unknown = 137,  // native only - faction / trial chat channels
    expeditionWar = 138,
    freeResurrectionInPlace = 139,
    expeditionLevel = 140,
    itemEvolving = 141,
    premiumUserServer = 142,
    show_instance_in_hud = 143,

    // ---- fset[18] ----
    account_attendance = 144,
    event_center_event_info = 145,
    ui_avi = 146,
    shopOnUI = 147,
    itemLookConvertInBag = 148,
    squad = 149,
    expeditionSummon = 151,

    // ---- fset[19] ----
    heroBonus = 152,
    fset_19_2_unknown = 154,
    hairTwoTone = 156,
    socketChange = 157,
    mate_type_summon = 158,
    permissionZone = 159,

    // ---- fset[20] ----
    lootGacha = 160,
    itemEvolvingReRoll = 161,
    fset_20_2_unknown = 162,
    eloRating = 163,
    chronicle_info = 164,
    fset_20_5_unknown = 165,
    packageDemolish = 166,
    reportBadUser = 167,

    // ---- fset[21] ----
    restrictFollow = 168,
    socketExtract = 169,
    itemlookExtract = 170,
    useCharacterListPage = 171,
    renameExpeditionByItem = 172,
    eventWebLink = 175,

    // ---- fset[22] ----
    bless_uthstin = 176,
    vehicleZoneSimulation = 177,
    itemSmelting = 178,
    protectPvp = 179,
    characterInfoLivingPoint = 180,
    useForceAttack = 181,
    reportBadWordUser = 182,
    use_web_help = 183,

    // ---- fset[24] ----
    event_center_content_schedule = 194,
    fset_24_3_unknown = 195,  // native only - housing builder rotation
    auctionPartialBuy = 196,
    equipSlotEnchantment = 197,
    loadingTipOfDay = 198,
    fset_24_7_unknown = 199,  // native only - ui_eventProfile content info

    // ---- fset[25] ----
    itemGradeEnchant = 200,
    fset_25_1_unknown = 201,  // native only - Bless Uthstin page copy/expand
    useHeirSkill = 202,
    fset_25_3_unknown = 203,  // native only - notice / event board record
    event_center_today_assignment = 204,
    chatRace = 205,
    factionMigrateLimit = 206,
    mateAggressive = 207,

    // ---- fset[27] ----
    useCosplayLooksSlot = 220,
    uccUploadBlock = 221,
    archePassMissionAccount = 222,
    survey_form = 223,

    // ---- fset[28] ----
    use_palos_shop = 224,
    chatLanguageFilter = 225,
    blockRename = 226,
    use_character_privacy = 227,
    show_premium_hud = 228,
    specialty_trade_info_ui = 229,
    fset_28_6_unknown = 230,  // native only - multilingual auction search
    block_trade_by_nft = 231,

    // ---- fset[29] ----
    block_joint_raid = 232,
    fset_29_1_unknown = 233,
    blockSpendableGamePoint = 234,
    blockFamilyContents = 235,
    useCraftOrder = 236,
    use_web_diary = 237,
    use_web_messenger = 238,
    use_web_wiki = 239,

    // ---- fset[30] ----
    freeDemolishHouse = 241,
    notGainLeaderShipPoint = 242,
}

//! Opcode tables generated from the 1.2 (`client_12_r208022`) `CSOffsets.cs`, `SCOffsets.cs`,
//! `PPOffsets.cs`, `GLOffsets.cs` and `LGOffsets.cs`. Names are kept identical to the C# constants
//! so they can be grepped across both code bases. `0xfff` marks packets the C# has no 1.2 opcode
//! for (several share it, so `name_of` returns the first).
#![allow(non_upper_case_globals, dead_code)]

/// `CSOffsets`
pub mod cs {
    pub const X2EnterWorldPacket: u16 = 0x000;
    pub const CSLeaveWorldPacket: u16 = 0x001;
    pub const CSCancelLeaveWorldPacket: u16 = 0x002;
    pub const CSCreateExpeditionPacket: u16 = 0x004;
    pub const CSChangeExpeditionSponsorPacket: u16 = 0xfff;
    pub const CSChangeExpeditionRolePolicyPacket: u16 = 0x006;
    pub const CSChangeExpeditionMemberRolePacket: u16 = 0x007;
    pub const CSChangeExpeditionOwnerPacket: u16 = 0x008;
    pub const CSRenameExpeditionPacket: u16 = 0x009;
    pub const CSDismissExpeditionPacket: u16 = 0x00b;
    pub const CSInviteToExpeditionPacket: u16 = 0x00c;
    pub const CSReplyExpeditionInvitationPacket: u16 = 0x00d;
    pub const CSLeaveExpeditionPacket: u16 = 0x00e;
    pub const CSKickFromExpeditionPacket: u16 = 0x00f;
    pub const CSUpdateDominionTaxRatePacket: u16 = 0x012;
    pub const CSFactionImmigrationInvitePacket: u16 = 0x015;
    pub const CSFactionImmigrationInviteReplyPacket: u16 = 0x016;
    pub const CSFactionImmigrateToOriginPacket: u16 = 0x017;
    pub const CSFactionKickToOriginPacket: u16 = 0x018;
    pub const CSFactionDeclareHostilePacket: u16 = 0x019;
    pub const CSFamilyInviteMemberPacket: u16 = 0x01a;
    pub const CSFamilyReplyInvitationPacket: u16 = 0x01b;
    pub const CSFamilyLeavePacket: u16 = 0x01c;
    pub const CSFamilyKickPacket: u16 = 0x01d;
    pub const CSFamilyChangeTitlePacket: u16 = 0x01e;
    pub const CSFamilyChangeOwnerPacket: u16 = 0x01f;
    pub const CSListCharacterPacket: u16 = 0x020;
    pub const CSRefreshInCharacterListPacket: u16 = 0x021;
    pub const CSCreateCharacterPacket: u16 = 0x022;
    pub const CSEditCharacterPacket: u16 = 0x023;
    pub const CSDeleteCharacterPacket: u16 = 0x024;
    pub const CSSelectCharacterPacket: u16 = 0x025;
    pub const CSSpawnCharacterPacket: u16 = 0x026;
    pub const CSCancelCharacterDeletePacket: u16 = 0x027;
    pub const CSNotifyInGamePacket: u16 = 0x029;
    pub const CSNotifyInGameCompletedPacket: u16 = 0x02a;
    pub const CSEditorGameModePacket: u16 = 0x02b;
    pub const CSChangeTargetPacket: u16 = 0x02c;
    pub const CSRequestCharBriefPacket: u16 = 0x02d;
    pub const CSSpawnSlavePacket: u16 = 0x02e;
    pub const CSDespawnSlavePacket: u16 = 0x02f;
    pub const CSDestroySlavePacket: u16 = 0x030;
    pub const CSBindSlavePacket: u16 = 0x031;
    pub const CSDiscardSlavePacket: u16 = 0x032;
    pub const CSChangeSlaveTargetPacket: u16 = 0xfff;
    pub const CSChangeSlaveNamePacket: u16 = 0x034;
    pub const CSRepairSlaveItemsPacket: u16 = 0x035;
    pub const CSTurretStatePacket: u16 = 0x036;
    pub const CSChangeSlaveEquipmentPacket: u16 = 0x037;
    pub const CSDestroyItemPacket: u16 = 0x038;
    pub const CSSplitBagItemPacket: u16 = 0x039;
    pub const CSSwapItemsPacket: u16 = 0x03a;
    pub const CSRepairSingleEquipmentPacket: u16 = 0x03c;
    pub const CSRepairAllEquipmentsPacket: u16 = 0x03d;
    pub const CSSplitCofferItemPacket: u16 = 0x03f;
    pub const CSSwapCofferItemsPacket: u16 = 0x040;
    pub const CSExpandSlotsPacket: u16 = 0x041;
    pub const CSSellBackpackGoodsPacket: u16 = 0x042;
    pub const CSSpecialtyRatioPacket: u16 = 0x043;
    pub const CSListSpecialtyGoodsPacket: u16 = 0x044;
    pub const CSBuySpecialtyItemPacket: u16 = 0xfff;
    pub const CSSpecialtyRecordLoadPacket: u16 = 0xfff;
    pub const CSDepositMoneyPacket: u16 = 0x047;
    pub const CSWithdrawMoneyPacket: u16 = 0x048;
    pub const CSConvertItemLookPacket: u16 = 0x049;
    pub const CSItemSecurePacket: u16 = 0x04a;
    pub const CSItemUnsecurePacket: u16 = 0x04b;
    pub const CSEquipmentsSecurePacket: u16 = 0x04c;
    pub const CSEquipmentsUnsecurePacket: u16 = 0x04d;
    pub const CSResurrectCharacterPacket: u16 = 0x04e;
    pub const CSSetForceAttackPacket: u16 = 0x04f;
    pub const CSChallengeDuelPacket: u16 = 0x050;
    pub const CSStartDuelPacket: u16 = 0x051;
    pub const CSStartSkillPacket: u16 = 0x052;
    pub const CSStopCastingPacket: u16 = 0x054;
    pub const CSRemoveBuffPacket: u16 = 0x055;
    pub const CSConstructHouseTaxPacket: u16 = 0x056;
    pub const CSCreateHousePacket: u16 = 0x057;
    pub const CSDecorateHousePacket: u16 = 0x058;
    pub const CSChangeHouseNamePacket: u16 = 0x059;
    pub const CSChangeHousePermissionPacket: u16 = 0x05a;
    pub const CSChangeHousePayPacket: u16 = 0xfff;
    pub const CSRequestHouseTaxPacket: u16 = 0x05c;
    pub const CSAllowHousingRecoverPacket: u16 = 0x05d;
    pub const CSSellHousePacket: u16 = 0x05e;
    pub const CSSellHouseCancelPacket: u16 = 0x05f;
    pub const CSBuyHousePacket: u16 = 0x060;
    pub const CSJoinUserChatChannelPacket: u16 = 0x061;
    pub const CSLeaveChatChannelPacket: u16 = 0x062;
    pub const CSSendChatMessagePacket: u16 = 0x063;
    pub const CSConsoleCmdUsedPacket: u16 = 0x064;
    pub const CSInteractNPCPacket: u16 = 0x065;
    pub const CSInteractNPCEndPacket: u16 = 0x066;
    pub const CSBoardingTransferPacket: u16 = 0x067;
    pub const CSStartInteractionPacket: u16 = 0x068;
    pub const CSSelectInteractionExPacket: u16 = 0x06b;
    pub const CSCofferInteractionPacket: u16 = 0x06c;
    pub const CSCriminalLockedPacket: u16 = 0x06e;
    pub const CSReplyImprisonOrTrialPacket: u16 = 0x06f;
    pub const CSSkipFinalStatementPacket: u16 = 0x070;
    pub const CSReplyInviteJuryPacket: u16 = 0x071;
    pub const CSJurySummonedPacket: u16 = 0x072;
    pub const CSJuryEndTestimonyPacket: u16 = 0x073;
    pub const CSCancelTrialPacket: u16 = 0x074;
    pub const CSJuryVerdictPacket: u16 = 0x075;
    pub const CSReportCrimePacket: u16 = 0x076;
    pub const CSJoinTrialAudiencePacket: u16 = 0x077;
    pub const CSLeaveTrialAudiencePacket: u16 = 0x078;
    pub const CSRequestJuryWaitingNumberPacket: u16 = 0x079;
    pub const CSInviteToTeamPacket: u16 = 0x07a;
    pub const CSInviteAreaToTeamPacket: u16 = 0x07b;
    pub const CSReplyToJoinTeamPacket: u16 = 0x07c;
    pub const CSLeaveTeamPacket: u16 = 0x07d;
    pub const CSKickTeamMemberPacket: u16 = 0x07e;
    pub const CSMakeTeamOwnerPacket: u16 = 0x07f;
    pub const CSSetTeamOfficerPacket: u16 = 0xfff;
    pub const CSConvertToRaidTeamPacket: u16 = 0x080;
    pub const CSMoveTeamMemberPacket: u16 = 0x081;
    pub const CSChangeLootingRulePacket: u16 = 0x083;
    pub const CSDismissTeamPacket: u16 = 0x084;
    pub const CSSetTeamMemberRolePacket: u16 = 0x085;
    pub const CSSetOverHeadMarkerPacket: u16 = 0x086;
    pub const CSSetPingPosPacket: u16 = 0x087;
    pub const CSAskRiskyTeamActionPacket: u16 = 0x088;
    pub const CSMoveUnitPacket: u16 = 0x089;
    pub const CSSkillControllerStatePacket: u16 = 0x08a;
    pub const CSCreateSkillControllerPacket: u16 = 0x08b;
    pub const CSActiveWeaponChangedPacket: u16 = 0x08c;
    pub const CSChangeItemLookPacket: u16 = 0xfff;
    pub const CSLootOpenBagPacket: u16 = 0x08e;
    pub const CSLootItemPacket: u16 = 0x08f;
    pub const CSLootCloseBagPacket: u16 = 0x090;
    pub const CSLootDicePacket: u16 = 0x091;
    pub const CSLearnSkillPacket: u16 = 0x092;
    pub const CSLearnBuffPacket: u16 = 0x093;
    pub const CSResetSkillsPacket: u16 = 0x094;
    pub const CSSwapAbilityPacket: u16 = 0x096;
    pub const CSSendMailPacket: u16 = 0x098;
    pub const CSListMailPacket: u16 = 0x09a;
    pub const CSListMailContinuePacket: u16 = 0x09b;
    pub const CSReadMailPacket: u16 = 0x09c;
    pub const CSTakeAttachmentItemPacket: u16 = 0x09d;
    pub const CSTakeAttachmentMoneyPacket: u16 = 0x09e;
    pub const CSTakeAttachmentSequentially: u16 = 0x09f;
    pub const CSPayChargeMoneyPacket: u16 = 0x0a0;
    pub const CSDeleteMailPacket: u16 = 0x0a1;
    pub const CSReportSpamPacket: u16 = 0x0a3;
    pub const CSReturnMailPacket: u16 = 0xfff;
    pub const CSRemoveMatePacket: u16 = 0x0a4;
    pub const CSChangeMateTargetPacket: u16 = 0x0a5;
    pub const CSChangeMateNamePacket: u16 = 0x0a6;
    pub const CSMountMatePacket: u16 = 0x0a7;
    pub const CSUnMountMatePacket: u16 = 0x0a8;
    pub const CSChangeMateEquipmentPacket: u16 = 0x0a9;
    pub const CSChangeMateUserStatePacket: u16 = 0x0aa;
    pub const CSExpressEmotionPacket: u16 = 0x0ad;
    pub const CSBuyItemsPacket: u16 = 0x0ae;
    pub const CSBuyCoinItemPacket: u16 = 0x0af;
    pub const CSSellItemsPacket: u16 = 0x0b0;
    pub const CSListSoldItemPacket: u16 = 0x0b1;
    pub const CSBuyPriestBuffPacket: u16 = 0x0b2;
    pub const CSUseTeleportPacket: u16 = 0x0b3;
    pub const CSTeleportEndedPacket: u16 = 0x0b4;
    pub const CSRepairPetItemsPacket: u16 = 0x0b5;
    pub const CSUpdateActionSlotPacket: u16 = 0x0b6;
    pub const CSAuctionPostPacket: u16 = 0x0b7;
    pub const CSAuctionSearchPacket: u16 = 0x0b8;
    pub const CSBidAuctionPacket: u16 = 0x0b9;
    pub const CSCancelAuctionPacket: u16 = 0x0ba;
    pub const CSAuctionMyBidListPacket: u16 = 0x0bb;
    pub const CSAuctionLowestPricePacket: u16 = 0x0bc;
    pub const CSRollDicePacket: u16 = 0x0bd;
    pub const CSHangPacket: u16 = 0x0cb;
    pub const CSUnhangPacket: u16 = 0x0cc;
    pub const CSUnbondDoodadPacket: u16 = 0x0cd;
    pub const CSCompletedCinemaPacket: u16 = 0x0ce;
    pub const CSStartedCinemaPacket: u16 = 0x0cf;
    pub const CSRequestPermissionToPlayCinemaForDirectingMode: u16 = 0x0d0;
    pub const CSStartQuestContextPacket: u16 = 0x0d5;
    pub const CSCompleteQuestContextPacket: u16 = 0x0d6;
    pub const CSDropQuestContextPacket: u16 = 0x0d7;
    pub const CSResetQuestContextPacket: u16 = 0xfff;
    pub const CSAcceptCheatQuestContextPacket: u16 = 0xfff;
    pub const CSQuestTalkMadePacket: u16 = 0x0da;
    pub const CSQuestStartWithPacket: u16 = 0x0db;
    pub const CSTryQuestCompleteAsLetItDonePacket: u16 = 0x0dd;
    pub const CSUsePortalPacket: u16 = 0x0de;
    pub const CSDeletePortalPacket: u16 = 0x0df;
    pub const CSInstanceLoadedPacket: u16 = 0x0e0;
    pub const CSApplyToInstantGamePacket: u16 = 0x0e1;
    pub const CSCancelInstantGamePacket: u16 = 0x0e2;
    pub const CSJoinInstantGamePacket: u16 = 0x0e3;
    pub const CSEnteredInstantGameWorldPacket: u16 = 0x0e4;
    pub const CSLeaveInstantGamePacket: u16 = 0x0e5;
    pub const CSCreateDoodadPacket: u16 = 0x0e6;
    pub const CSSaveDoodadUccStringPacket: u16 = 0xfff;
    pub const CSNaviTeleportPacket: u16 = 0x0e7;
    pub const CSNaviOpenPortalPacket: u16 = 0x0e8;
    pub const CSChangeDoodadPhasePacket: u16 = 0x0e9;
    pub const CSNaviOpenBountyPacket: u16 = 0x0ea;
    pub const CSChangeDoodadDataPacket: u16 = 0x0eb;
    pub const CSStartTradePacket: u16 = 0x0ec;
    pub const CSCanStartTradePacket: u16 = 0x0ed;
    pub const CSCannotStartTradePacket: u16 = 0x0ee;
    pub const CSCancelTradePacket: u16 = 0x0ef;
    pub const CSPutupTradeItemPacket: u16 = 0x0f0;
    pub const CSPutupTradeMoneyPacket: u16 = 0x0f1;
    pub const CSTakedownTradeItemPacket: u16 = 0x0f2;
    pub const CSTradeLockPacket: u16 = 0x0f3;
    pub const CSTradeOkPacket: u16 = 0x0f4;
    pub const CSSaveTutorialPacket: u16 = 0x0f5;
    pub const CSSetLogicDoodadPacket: u16 = 0x0f6;
    pub const CSCleanupLogicLinkPacket: u16 = 0x0f7;
    pub const CSExecuteCraft: u16 = 0x0f8;
    pub const CSChangeAppellationPacket: u16 = 0x0f9;
    pub const CSCreateShipyardPacket: u16 = 0x0fc;
    pub const CSRestartMainQuestPacket: u16 = 0x0fd;
    pub const CSSetLpManageCharacterPacket: u16 = 0x0fe;
    pub const CSUpgradeExpertLimitPacket: u16 = 0x0ff;
    pub const CSDowngradeExpertLimitPacket: u16 = 0x100;
    pub const CSExpandExpertPacket: u16 = 0x101;
    pub const CSSearchListPacket: u16 = 0xfff;
    pub const CSAddFriendPacket: u16 = 0x104;
    pub const CSDeleteFriendPacket: u16 = 0x105;
    pub const CSCharDetailPacket: u16 = 0x106;
    pub const CSAddBlockedUserPacket: u16 = 0x107;
    pub const CSDeleteBlockedUserPacket: u16 = 0x108;
    pub const CSRequestCommonFarmList: u16 = 0x10b;
    pub const CSNotifySubZonePacket: u16 = 0x112;
    pub const CSResturnAddrsPacket: u16 = 0x115;
    pub const CSRequestUIDataPacket: u16 = 0x117;
    pub const CSSaveUIDataPacket: u16 = 0x118;
    pub const CSBroadcastVisualOptionPacket: u16 = 0x119;
    pub const CSRestrictCheckPacket: u16 = 0x11a;
    pub const CSICSMenuListPacket: u16 = 0x11b;
    pub const CSICSGoodsListPacket: u16 = 0x11c;
    pub const CSICSBuyGoodPacket: u16 = 0x11d;
    pub const CSICSMoneyRequestPacket: u16 = 0x11e;
    pub const CSSendUserMusicPacket: u16 = 0x121;
    pub const CSSaveUserMusicNotesPacket: u16 = 0x122;
    pub const CSRequestMusicNotesPacket: u16 = 0x123;
    pub const CSEndMusicPacket: u16 = 0x124;
    pub const CSExitBeautySalonPacket: u16 = 0x12c;
    pub const CSBeautyshopDataPacket: u16 = 0x12d;
    pub const CSEnterBeautySalonPacket: u16 = 0x12e;
    pub const CSRankCharacterPacket: u16 = 0x12f;
    pub const CSRequestSecondPasswordKeyTablesPacket: u16 = 0x125;
    pub const CSRequestSpecialtyCurrentPacket: u16 = 0x131;
    pub const CSIdleStatusPacket: u16 = 0x132;
    pub const CSThisTimeUnpackItemPacket: u16 = 0x134;
    pub const CSPremiumServiceBuyPacket: u16 = 0x135;
    pub const CSPremiumServiceListPacket: u16 = 0x136;
    pub const CSTakeAllAttachmentItemPacket: u16 = 0x139;
    pub const CSTakeScheduleItemPacket: u16 = 0x13b;
    pub const CSPremiumServiceMsgPacket: u16 = 0x13c;
    pub const CSUnknownInstancePacket: u16 = 0x13e;
    pub const CSSetupSecondPassword: u16 = 0x140;
    pub const CSUpdateNationalTaxRatePacket: u16 = 0xfff;
    pub const CSSetCraftingPayPacket: u16 = 0xfff;

    /// `(opcode, name)` pairs, for logging unknown / not-yet-ported packets.
    pub const NAMES: &[(u16, &str)] = &[
        (0x000, "X2EnterWorldPacket"),
        (0x001, "CSLeaveWorldPacket"),
        (0x002, "CSCancelLeaveWorldPacket"),
        (0x004, "CSCreateExpeditionPacket"),
        (0xfff, "CSChangeExpeditionSponsorPacket"),
        (0x006, "CSChangeExpeditionRolePolicyPacket"),
        (0x007, "CSChangeExpeditionMemberRolePacket"),
        (0x008, "CSChangeExpeditionOwnerPacket"),
        (0x009, "CSRenameExpeditionPacket"),
        (0x00b, "CSDismissExpeditionPacket"),
        (0x00c, "CSInviteToExpeditionPacket"),
        (0x00d, "CSReplyExpeditionInvitationPacket"),
        (0x00e, "CSLeaveExpeditionPacket"),
        (0x00f, "CSKickFromExpeditionPacket"),
        (0x012, "CSUpdateDominionTaxRatePacket"),
        (0x015, "CSFactionImmigrationInvitePacket"),
        (0x016, "CSFactionImmigrationInviteReplyPacket"),
        (0x017, "CSFactionImmigrateToOriginPacket"),
        (0x018, "CSFactionKickToOriginPacket"),
        (0x019, "CSFactionDeclareHostilePacket"),
        (0x01a, "CSFamilyInviteMemberPacket"),
        (0x01b, "CSFamilyReplyInvitationPacket"),
        (0x01c, "CSFamilyLeavePacket"),
        (0x01d, "CSFamilyKickPacket"),
        (0x01e, "CSFamilyChangeTitlePacket"),
        (0x01f, "CSFamilyChangeOwnerPacket"),
        (0x020, "CSListCharacterPacket"),
        (0x021, "CSRefreshInCharacterListPacket"),
        (0x022, "CSCreateCharacterPacket"),
        (0x023, "CSEditCharacterPacket"),
        (0x024, "CSDeleteCharacterPacket"),
        (0x025, "CSSelectCharacterPacket"),
        (0x026, "CSSpawnCharacterPacket"),
        (0x027, "CSCancelCharacterDeletePacket"),
        (0x029, "CSNotifyInGamePacket"),
        (0x02a, "CSNotifyInGameCompletedPacket"),
        (0x02b, "CSEditorGameModePacket"),
        (0x02c, "CSChangeTargetPacket"),
        (0x02d, "CSRequestCharBriefPacket"),
        (0x02e, "CSSpawnSlavePacket"),
        (0x02f, "CSDespawnSlavePacket"),
        (0x030, "CSDestroySlavePacket"),
        (0x031, "CSBindSlavePacket"),
        (0x032, "CSDiscardSlavePacket"),
        (0xfff, "CSChangeSlaveTargetPacket"),
        (0x034, "CSChangeSlaveNamePacket"),
        (0x035, "CSRepairSlaveItemsPacket"),
        (0x036, "CSTurretStatePacket"),
        (0x037, "CSChangeSlaveEquipmentPacket"),
        (0x038, "CSDestroyItemPacket"),
        (0x039, "CSSplitBagItemPacket"),
        (0x03a, "CSSwapItemsPacket"),
        (0x03c, "CSRepairSingleEquipmentPacket"),
        (0x03d, "CSRepairAllEquipmentsPacket"),
        (0x03f, "CSSplitCofferItemPacket"),
        (0x040, "CSSwapCofferItemsPacket"),
        (0x041, "CSExpandSlotsPacket"),
        (0x042, "CSSellBackpackGoodsPacket"),
        (0x043, "CSSpecialtyRatioPacket"),
        (0x044, "CSListSpecialtyGoodsPacket"),
        (0xfff, "CSBuySpecialtyItemPacket"),
        (0xfff, "CSSpecialtyRecordLoadPacket"),
        (0x047, "CSDepositMoneyPacket"),
        (0x048, "CSWithdrawMoneyPacket"),
        (0x049, "CSConvertItemLookPacket"),
        (0x04a, "CSItemSecurePacket"),
        (0x04b, "CSItemUnsecurePacket"),
        (0x04c, "CSEquipmentsSecurePacket"),
        (0x04d, "CSEquipmentsUnsecurePacket"),
        (0x04e, "CSResurrectCharacterPacket"),
        (0x04f, "CSSetForceAttackPacket"),
        (0x050, "CSChallengeDuelPacket"),
        (0x051, "CSStartDuelPacket"),
        (0x052, "CSStartSkillPacket"),
        (0x054, "CSStopCastingPacket"),
        (0x055, "CSRemoveBuffPacket"),
        (0x056, "CSConstructHouseTaxPacket"),
        (0x057, "CSCreateHousePacket"),
        (0x058, "CSDecorateHousePacket"),
        (0x059, "CSChangeHouseNamePacket"),
        (0x05a, "CSChangeHousePermissionPacket"),
        (0xfff, "CSChangeHousePayPacket"),
        (0x05c, "CSRequestHouseTaxPacket"),
        (0x05d, "CSAllowHousingRecoverPacket"),
        (0x05e, "CSSellHousePacket"),
        (0x05f, "CSSellHouseCancelPacket"),
        (0x060, "CSBuyHousePacket"),
        (0x061, "CSJoinUserChatChannelPacket"),
        (0x062, "CSLeaveChatChannelPacket"),
        (0x063, "CSSendChatMessagePacket"),
        (0x064, "CSConsoleCmdUsedPacket"),
        (0x065, "CSInteractNPCPacket"),
        (0x066, "CSInteractNPCEndPacket"),
        (0x067, "CSBoardingTransferPacket"),
        (0x068, "CSStartInteractionPacket"),
        (0x06b, "CSSelectInteractionExPacket"),
        (0x06c, "CSCofferInteractionPacket"),
        (0x06e, "CSCriminalLockedPacket"),
        (0x06f, "CSReplyImprisonOrTrialPacket"),
        (0x070, "CSSkipFinalStatementPacket"),
        (0x071, "CSReplyInviteJuryPacket"),
        (0x072, "CSJurySummonedPacket"),
        (0x073, "CSJuryEndTestimonyPacket"),
        (0x074, "CSCancelTrialPacket"),
        (0x075, "CSJuryVerdictPacket"),
        (0x076, "CSReportCrimePacket"),
        (0x077, "CSJoinTrialAudiencePacket"),
        (0x078, "CSLeaveTrialAudiencePacket"),
        (0x079, "CSRequestJuryWaitingNumberPacket"),
        (0x07a, "CSInviteToTeamPacket"),
        (0x07b, "CSInviteAreaToTeamPacket"),
        (0x07c, "CSReplyToJoinTeamPacket"),
        (0x07d, "CSLeaveTeamPacket"),
        (0x07e, "CSKickTeamMemberPacket"),
        (0x07f, "CSMakeTeamOwnerPacket"),
        (0xfff, "CSSetTeamOfficerPacket"),
        (0x080, "CSConvertToRaidTeamPacket"),
        (0x081, "CSMoveTeamMemberPacket"),
        (0x083, "CSChangeLootingRulePacket"),
        (0x084, "CSDismissTeamPacket"),
        (0x085, "CSSetTeamMemberRolePacket"),
        (0x086, "CSSetOverHeadMarkerPacket"),
        (0x087, "CSSetPingPosPacket"),
        (0x088, "CSAskRiskyTeamActionPacket"),
        (0x089, "CSMoveUnitPacket"),
        (0x08a, "CSSkillControllerStatePacket"),
        (0x08b, "CSCreateSkillControllerPacket"),
        (0x08c, "CSActiveWeaponChangedPacket"),
        (0xfff, "CSChangeItemLookPacket"),
        (0x08e, "CSLootOpenBagPacket"),
        (0x08f, "CSLootItemPacket"),
        (0x090, "CSLootCloseBagPacket"),
        (0x091, "CSLootDicePacket"),
        (0x092, "CSLearnSkillPacket"),
        (0x093, "CSLearnBuffPacket"),
        (0x094, "CSResetSkillsPacket"),
        (0x096, "CSSwapAbilityPacket"),
        (0x098, "CSSendMailPacket"),
        (0x09a, "CSListMailPacket"),
        (0x09b, "CSListMailContinuePacket"),
        (0x09c, "CSReadMailPacket"),
        (0x09d, "CSTakeAttachmentItemPacket"),
        (0x09e, "CSTakeAttachmentMoneyPacket"),
        (0x09f, "CSTakeAttachmentSequentially"),
        (0x0a0, "CSPayChargeMoneyPacket"),
        (0x0a1, "CSDeleteMailPacket"),
        (0x0a3, "CSReportSpamPacket"),
        (0xfff, "CSReturnMailPacket"),
        (0x0a4, "CSRemoveMatePacket"),
        (0x0a5, "CSChangeMateTargetPacket"),
        (0x0a6, "CSChangeMateNamePacket"),
        (0x0a7, "CSMountMatePacket"),
        (0x0a8, "CSUnMountMatePacket"),
        (0x0a9, "CSChangeMateEquipmentPacket"),
        (0x0aa, "CSChangeMateUserStatePacket"),
        (0x0ad, "CSExpressEmotionPacket"),
        (0x0ae, "CSBuyItemsPacket"),
        (0x0af, "CSBuyCoinItemPacket"),
        (0x0b0, "CSSellItemsPacket"),
        (0x0b1, "CSListSoldItemPacket"),
        (0x0b2, "CSBuyPriestBuffPacket"),
        (0x0b3, "CSUseTeleportPacket"),
        (0x0b4, "CSTeleportEndedPacket"),
        (0x0b5, "CSRepairPetItemsPacket"),
        (0x0b6, "CSUpdateActionSlotPacket"),
        (0x0b7, "CSAuctionPostPacket"),
        (0x0b8, "CSAuctionSearchPacket"),
        (0x0b9, "CSBidAuctionPacket"),
        (0x0ba, "CSCancelAuctionPacket"),
        (0x0bb, "CSAuctionMyBidListPacket"),
        (0x0bc, "CSAuctionLowestPricePacket"),
        (0x0bd, "CSRollDicePacket"),
        (0x0cb, "CSHangPacket"),
        (0x0cc, "CSUnhangPacket"),
        (0x0cd, "CSUnbondDoodadPacket"),
        (0x0ce, "CSCompletedCinemaPacket"),
        (0x0cf, "CSStartedCinemaPacket"),
        (0x0d0, "CSRequestPermissionToPlayCinemaForDirectingMode"),
        (0x0d5, "CSStartQuestContextPacket"),
        (0x0d6, "CSCompleteQuestContextPacket"),
        (0x0d7, "CSDropQuestContextPacket"),
        (0xfff, "CSResetQuestContextPacket"),
        (0xfff, "CSAcceptCheatQuestContextPacket"),
        (0x0da, "CSQuestTalkMadePacket"),
        (0x0db, "CSQuestStartWithPacket"),
        (0x0dd, "CSTryQuestCompleteAsLetItDonePacket"),
        (0x0de, "CSUsePortalPacket"),
        (0x0df, "CSDeletePortalPacket"),
        (0x0e0, "CSInstanceLoadedPacket"),
        (0x0e1, "CSApplyToInstantGamePacket"),
        (0x0e2, "CSCancelInstantGamePacket"),
        (0x0e3, "CSJoinInstantGamePacket"),
        (0x0e4, "CSEnteredInstantGameWorldPacket"),
        (0x0e5, "CSLeaveInstantGamePacket"),
        (0x0e6, "CSCreateDoodadPacket"),
        (0xfff, "CSSaveDoodadUccStringPacket"),
        (0x0e7, "CSNaviTeleportPacket"),
        (0x0e8, "CSNaviOpenPortalPacket"),
        (0x0e9, "CSChangeDoodadPhasePacket"),
        (0x0ea, "CSNaviOpenBountyPacket"),
        (0x0eb, "CSChangeDoodadDataPacket"),
        (0x0ec, "CSStartTradePacket"),
        (0x0ed, "CSCanStartTradePacket"),
        (0x0ee, "CSCannotStartTradePacket"),
        (0x0ef, "CSCancelTradePacket"),
        (0x0f0, "CSPutupTradeItemPacket"),
        (0x0f1, "CSPutupTradeMoneyPacket"),
        (0x0f2, "CSTakedownTradeItemPacket"),
        (0x0f3, "CSTradeLockPacket"),
        (0x0f4, "CSTradeOkPacket"),
        (0x0f5, "CSSaveTutorialPacket"),
        (0x0f6, "CSSetLogicDoodadPacket"),
        (0x0f7, "CSCleanupLogicLinkPacket"),
        (0x0f8, "CSExecuteCraft"),
        (0x0f9, "CSChangeAppellationPacket"),
        (0x0fc, "CSCreateShipyardPacket"),
        (0x0fd, "CSRestartMainQuestPacket"),
        (0x0fe, "CSSetLpManageCharacterPacket"),
        (0x0ff, "CSUpgradeExpertLimitPacket"),
        (0x100, "CSDowngradeExpertLimitPacket"),
        (0x101, "CSExpandExpertPacket"),
        (0xfff, "CSSearchListPacket"),
        (0x104, "CSAddFriendPacket"),
        (0x105, "CSDeleteFriendPacket"),
        (0x106, "CSCharDetailPacket"),
        (0x107, "CSAddBlockedUserPacket"),
        (0x108, "CSDeleteBlockedUserPacket"),
        (0x10b, "CSRequestCommonFarmList"),
        (0x112, "CSNotifySubZonePacket"),
        (0x115, "CSResturnAddrsPacket"),
        (0x117, "CSRequestUIDataPacket"),
        (0x118, "CSSaveUIDataPacket"),
        (0x119, "CSBroadcastVisualOptionPacket"),
        (0x11a, "CSRestrictCheckPacket"),
        (0x11b, "CSICSMenuListPacket"),
        (0x11c, "CSICSGoodsListPacket"),
        (0x11d, "CSICSBuyGoodPacket"),
        (0x11e, "CSICSMoneyRequestPacket"),
        (0x121, "CSSendUserMusicPacket"),
        (0x122, "CSSaveUserMusicNotesPacket"),
        (0x123, "CSRequestMusicNotesPacket"),
        (0x124, "CSEndMusicPacket"),
        (0x12c, "CSExitBeautySalonPacket"),
        (0x12d, "CSBeautyshopDataPacket"),
        (0x12e, "CSEnterBeautySalonPacket"),
        (0x12f, "CSRankCharacterPacket"),
        (0x125, "CSRequestSecondPasswordKeyTablesPacket"),
        (0x131, "CSRequestSpecialtyCurrentPacket"),
        (0x132, "CSIdleStatusPacket"),
        (0x134, "CSThisTimeUnpackItemPacket"),
        (0x135, "CSPremiumServiceBuyPacket"),
        (0x136, "CSPremiumServiceListPacket"),
        (0x139, "CSTakeAllAttachmentItemPacket"),
        (0x13b, "CSTakeScheduleItemPacket"),
        (0x13c, "CSPremiumServiceMsgPacket"),
        (0x13e, "CSUnknownInstancePacket"),
        (0x140, "CSSetupSecondPassword"),
        (0xfff, "CSUpdateNationalTaxRatePacket"),
        (0xfff, "CSSetCraftingPayPacket"),
    ];
}

/// `SCOffsets`
pub mod sc {
    pub const X2EnterWorldResponsePacket: u16 = 0x000;
    pub const SCReconnectAuthPacket: u16 = 0x001;
    pub const SCPrepareLeaveWorldPacket: u16 = 0x002;
    pub const SCLeaveWorldGrantedPacket: u16 = 0x003;
    pub const SCLeaveWorldCanceledPacket: u16 = 0x004;
    pub const SCWorldQueuePacket: u16 = 0x005;
    pub const SCInitialConfigPacket: u16 = 0x006;
    pub const SCTrionConfigPacket: u16 = 0x007;
    pub const SCFactionListPacket: u16 = 0x008;
    pub const SCFactionRelationListPacket: u16 = 0x009;
    pub const SCExpeditionRolePolicyListPacket: u16 = 0x00b;
    pub const SCExpeditionRolePolicyChangedPacket: u16 = 0x00c;
    pub const SCExpeditionRoleChangedPacket: u16 = 0x00d;
    pub const SCExpeditionOwnerChangedPacket: u16 = 0x00e;
    pub const SCExpeditionShowRenameUIPacket: u16 = 0x00f;
    pub const SCNationShowRenameUIPacket: u16 = 0x010;
    pub const SCFactionRenamedPacket: u16 = 0x011;
    pub const SCFactionRetryRenamePacket: u16 = 0x012;
    pub const SCFactionCreatedPacket: u16 = 0x013;
    pub const SCExpeditionSponsorChangedPacket: u16 = 0x014;
    pub const SCExpeditionDismissedPacket: u16 = 0x015;
    pub const SCExpeditionMemberListPacket: u16 = 0x016;
    pub const SCExpeditionMemberStatusChangedPacket: u16 = 0x017;
    pub const SCFactionOwnerChangedPacket: u16 = 0x018;
    pub const SCFactionSetRelationStatePacket: u16 = 0x019;
    pub const SCExpeditionInvitationPacket: u16 = 0x01a;
    pub const SCUnitFactionChangedPacket: u16 = 0x01b;
    pub const SCUnitExpeditionChangedPacket: u16 = 0x01c;
    pub const SCDominionDataPacket: u16 = 0x01d;
    pub const SCDominionDeletedPacket: u16 = 0x01e;
    pub const SCDominionOwnerChangedPacket: u16 = 0x01f;
    pub const SCDominionTaxRatePacket: u16 = 0x020;
    pub const SCNationalTaxRatePacket: u16 = 0x021;
    pub const SCNationalMonumentChangedPacket: u16 = 0x022;
    pub const SCDominionTaxBalancedPacket: u16 = 0x023;
    pub const CDominionStartUnkPacket: u16 = 0x024;
    pub const SCDominionEndUnkPacket: u16 = 0x025;
    pub const SCFactionIndependencePacket: u16 = 0x026;
    pub const SCFactionImmigrateInvitePacket: u16 = 0x027;
    pub const SCFactionImmigrateInviteResultPacket: u16 = 0x028;
    pub const SCFactionImmigrateToOriginResultPacket: u16 = 0x029;
    pub const SCFactionKickToOriginResultPacket: u16 = 0x02a;
    pub const SCFactionDeclareHostileResultPacket: u16 = 0x02b;
    pub const SCFactionRelationExpiredPacket: u16 = 0x02c;
    pub const SCFamilyInvitationPacket: u16 = 0x02d;
    pub const SCFamilyCreatedPacket: u16 = 0x02e;
    pub const SCFamilyRemovedPacket: u16 = 0x02f;
    pub const SCFamilyDescPacket: u16 = 0x030;
    pub const SCFamilyMemberAddedPacket: u16 = 0x031;
    pub const SCFamilyMemberRemovedPacket: u16 = 0x032;
    pub const SCFamilyOwnerChangedPacket: u16 = 0x033;
    pub const SCFamilyTitleChangedPacket: u16 = 0x034;
    pub const SCFamilyMemberNameChangedPacket: u16 = 0x035;
    pub const SCFamilyTitlePacket: u16 = 0x036;
    pub const SCFamilyMemberOnlinePacket: u16 = 0x037;
    pub const SCCreateCharacterResponsePacket: u16 = 0x038;
    pub const SCDeleteCharacterResponsePacket: u16 = 0x039;
    pub const SCEditCharacterResponsePacket: u16 = 0x03a;
    pub const SCCharacterDeletedPacket: u16 = 0x03b;
    pub const SCCancelCharacterDeleteResponsePacket: u16 = 0x03c;
    pub const SCCharacterCreationFailedPacket: u16 = 0x03d;
    pub const SCCharacterListPacket: u16 = 0x03e;
    pub const SCRaceCongestionPacket: u16 = 0x03f;
    pub const SCCharacterStatePacket: u16 = 0x040;
    pub const SCNotifyResurrectionPacket: u16 = 0x041;
    pub const SCCharacterResurrectedPacket: u16 = 0x042;
    pub const SCForceAttackSetPacket: u16 = 0x043;
    pub const SCCharacterLaborPowerChangedPacket: u16 = 0x045;
    pub const SCBmPointPacket: u16 = 0x046;
    pub const SCAddActionPointPacket: u16 = 0x047;
    pub const SCLpManagedPacket: u16 = 0x048;
    pub const SCCharacterBoundPacket: u16 = 0x049;
    pub const SCCooldownsPacket: u16 = 0x04a;
    pub const SCCharacterInvenInitPacket: u16 = 0x04b;
    pub const SCCharacterInvenContentsPacket: u16 = 0x04c;
    pub const SCInvenExpandedPacket: u16 = 0x04d;
    pub const SCFriendsPacket: u16 = 0x04e;
    pub const SCSearchListPacket: u16 = 0x04f;
    pub const SCAddFriendPacket: u16 = 0x050;
    pub const SCDeleteFriendPacket: u16 = 0x051;
    pub const SCFriendStatusChangedPacket: u16 = 0x052;
    pub const SCCharDetailPacket: u16 = 0x053;
    pub const SCBlockedUsersPacket: u16 = 0x054;
    pub const SCAddBlockedUserPacket: u16 = 0x055;
    pub const SCDeleteBlockedUserPacket: u16 = 0x056;
    pub const SCLoginCharInfoHousePacket: u16 = 0x057;
    pub const SCCharBriefPacket: u16 = 0x058;
    pub const SCPlaytimePacket: u16 = 0x059;
    pub const SCCharacterRenamedPacket: u16 = 0x05a;
    pub const SCCharacterPortalsPacket: u16 = 0x05b;
    pub const SCCharacterReturnDistrictsPacket: u16 = 0x05c;
    pub const SCPortalInfoSavedPacket: u16 = 0x05d;
    pub const SCPortalDeletedPacket: u16 = 0x05e;
    pub const SCUnitPortalUsedPacket: u16 = 0x060;
    pub const SCSlaveCreatedPacket: u16 = 0x061;
    pub const SCSlaveRemovedPacket: u16 = 0x062;
    pub const SCSlaveDespawnPacket: u16 = 0x063;
    pub const SCSlaveBoundPacket: u16 = 0x064;
    pub const SCQuestListPacket: u16 = 0x065;
    pub const SCMySlavePacket: u16 = 0x066;
    pub const SCEscapeSlavePacket: u16 = 0x067;
    pub const SCSlaveEquipmentChangedPacket: u16 = 0x068;
    pub const SCUnitStatePacket: u16 = 0x069;
    pub const SCUnitsRemovedPacket: u16 = 0x06a;
    pub const SCUnitMovementsPacket: u16 = 0x06b;
    pub const SCOneUnitMovementPacket: u16 = 0x06c;
    pub const SCSkillControllerStatePacket: u16 = 0x06d;
    pub const SCUnitOfflinePacket: u16 = 0x06e;
    pub const SCActiveWeaponChangedPacket: u16 = 0x06f;
    pub const SCUnitNameChangedPacket: u16 = 0x070;
    pub const SCUnitDeathPacket: u16 = 0x071;
    pub const SCTeleportUnitPacket: u16 = 0x072;
    pub const SCBlinkUnitPacket: u16 = 0x073;
    pub const SCKnockBackUnitPacket: u16 = 0x074;
    pub const SCUnitAttachedPacket: u16 = 0x076;
    pub const SCUnitDetachedPacket: u16 = 0x077;
    pub const SCUnitInvisiblePacket: u16 = 0x078;
    pub const SCUnitImpulsePacket: u16 = 0x079;
    pub const SCUnitFlyingStateChangedPacket: u16 = 0x07a;
    pub const SCNpcInteractionSkillListPacket: u16 = 0x07b;
    pub const SCNpcInteractionEndedByZonePacket: u16 = 0x07c;
    pub const SCWorldInteractionSkillListPacket: u16 = 0x07e;
    pub const SCWorldInteractionCanceledPacket: u16 = 0x07f;
    pub const SCNpcInteractionStatusChangedPacket: u16 = 0x080;
    pub const SCVegetationCutdowningPacket: u16 = 0x081;
    pub const SCNpcFriendshipListPacket: u16 = 0x082;
    pub const SCNpcFriendshipChangedPacket: u16 = 0x083;
    pub const SCTargetChangedPacket: u16 = 0x084;
    pub const SCCombatEngagedPacket: u16 = 0x085;
    pub const SCCombatClearedPacket: u16 = 0x086;
    pub const SCCvFCombatRelationshipPacket: u16 = 0x087;
    pub const SCFvFCombatRelationshipPacket: u16 = 0x088;
    pub const SCCombatFirstHitPacket: u16 = 0x089;
    pub const SCDumpCombatStatPacket: u16 = 0x08a;
    pub const SCDuelChallengedPacket: u16 = 0x08b;
    pub const SCDuelStartCountdownPacket: u16 = 0x08c;
    pub const SCDuelStartedPacket: u16 = 0x08d;
    pub const SCDuelEndedPacket: u16 = 0x08e;
    pub const SCDuelStatePacket: u16 = 0x08f;
    pub const SCItemTaskSuccessPacket: u16 = 0x090;
    pub const SCItemTaskNotifyPacket: u16 = 0x091;
    pub const SCItemDetailUpdatedPacket: u16 = 0x093;
    pub const SCUnitEquipmentsChangedPacket: u16 = 0x094;
    pub const SCUnitEquipmentIdsPacket: u16 = 0x095;
    pub const SCCofferContentsUpdatePacket: u16 = 0x096;
    pub const SCItemAcquisitionPacket: u16 = 0x097;
    pub const SCSyncItemLifespanPacket: u16 = 0x098;
    pub const SCSpecialtyRatioPacket: u16 = 0x099;
    pub const SCSpecialtyGoodsPacket: u16 = 0x09a;
    pub const SCSpecialtyRecordsPacket: u16 = 0x09b;
    pub const SCGradeEnchantResultPacket: u16 = 0x09c;
    pub const SCItemSocketingLunagemResultPacket: u16 = 0x09d;
    pub const SCItemSocketingLunastoneResultPacket: u16 = 0x09e;
    pub const SCGradeEnchantBroadcastPacket: u16 = 0x09f;
    pub const SCUnknownPacket: u16 = 0x0a0;
    pub const SCSkillStartedPacket: u16 = 0x0a1;
    pub const SCSkillFiredPacket: u16 = 0x0a2;
    pub const SCSkillEndedPacket: u16 = 0x0a3;
    pub const SCSkillStoppedPacket: u16 = 0x0a4;
    pub const SCCastingStoppedPacket: u16 = 0x0a5;
    pub const SCCastingDelayedPacket: u16 = 0x0a6;
    pub const SCUnitDamagedPacket: u16 = 0x0a7;
    pub const SCUnitHealedPacket: u16 = 0x0a8;
    pub const SCUnitTrackedPacket: u16 = 0x0a9;
    pub const SCBombUpdatedPacket: u16 = 0x0aa;
    pub const SCCombatTextPacket: u16 = 0x0ab;
    pub const SCSkillCooldownResetPacket: u16 = 0x0ad;
    pub const SCPlotEventPacket: u16 = 0x0ae;
    pub const SCPlotEndedPacket: u16 = 0x0af;
    pub const SCPlotProcessingTimePacket: u16 = 0x0b0;
    pub const SCPlotCastingStoppedPacket: u16 = 0x0b1;
    pub const SCPlotChannelingStoppedPacket: u16 = 0x0b2;
    pub const SCEnvDamagePacket: u16 = 0x0b3;
    pub const SCOnOffSnowPacket: u16 = 0x0b4;
    pub const SCUnkPacket: u16 = 0x0b5;
    pub const SCBuffCreatedPacket: u16 = 0x0b6;
    pub const SCBuffRemovedPacket: u16 = 0x0b7;
    pub const SCBuffUpdatedPacket: u16 = 0x0b9;
    pub const SCUnitPointsPacket: u16 = 0x0ba;
    pub const SCUnitBountyMoneyPacket: u16 = 0x0bb;
    pub const SCHouseStatePacket: u16 = 0x0bc;
    pub const SCHouseBuildProgressPacket: u16 = 0x0bd;
    pub const SCHousePermissionChangedPacket: u16 = 0x0be;
    pub const SCHouseBuildPayChangedPacket: u16 = 0x0bf;
    pub const SCHouseDemolishedPacket: u16 = 0x0c0;
    pub const SCMyHousePacket: u16 = 0x0c1;
    pub const SCMyHouseRemovedPacket: u16 = 0x0c2;
    pub const SCHouseFarmPacket: u16 = 0x0c3;
    pub const SCHouseTaxInfoPacket: u16 = 0x0c4;
    pub const SCConstructHouseTaxPacket: u16 = 0x0c5;
    pub const SCHousingRecoverTogglePacket: u16 = 0x0c6;
    pub const SCHouseSetForSalePacket: u16 = 0x0c8;
    pub const SCHouseResetForSalePacket: u16 = 0x0c9;
    pub const SCHouseSoldPacket: u16 = 0x0ca;
    pub const SCHouseOwnerNameChangedPacket: u16 = 0x0cb;
    pub const SCJoinedChatChannelPacket: u16 = 0x0cc;
    pub const SCLeavedChatChannelPacket: u16 = 0x0cd;
    pub const SCChatMessagePacket: u16 = 0x0ce;
    pub const SCNpcChatMessagePacket: u16 = 0x0cf;
    pub const SCNoticeMessagePacket: u16 = 0x0d0;
    pub const SCChatFailedPacket: u16 = 0x0d1;
    pub const SCChatLocalizedMessagePacket: u16 = 0x0d2;
    pub const SCChatSpamDelayPacket: u16 = 0x0d3;
    pub const SCAskToJoinTeamPacket: u16 = 0x0d4;
    pub const SCAskToJoinTeamAreaPacket: u16 = 0x0d5;
    pub const SCJoinedTeamPacket: u16 = 0x0d6;
    pub const SCRejectedTeamPacket: u16 = 0x0d7;
    pub const SCLeavedTeamPacket: u16 = 0x0d8;
    pub const SCTeamDismissedPacket: u16 = 0x0d9;
    pub const SCTeamMemberJoinedPacket: u16 = 0x0da;
    pub const SCTeamMemberLeavedPacket: u16 = 0x0db;
    pub const SCTeamMemberDisconnectedPacket: u16 = 0x0dc;
    pub const SCTeamOwnerChangedPacket: u16 = 0x0dd;
    pub const SCTeamMemberRoleChangedPacket: u16 = 0x0de;
    pub const SCTeamBecameRaidTeamPacket: u16 = 0x0df;
    pub const SCTeamMemberMovedPacket: u16 = 0x0e0;
    pub const SCTeamLootingRuleChangedPacket: u16 = 0x0e2;
    pub const SCRefreshTeamMemberPacket: u16 = 0x0e3;
    pub const SCTeamRemoteMembersExPacket: u16 = 0x0e4;
    pub const SCTeamAreaInvitedPacket: u16 = 0x0e5;
    pub const SCOverHeadMarkerSetPacket: u16 = 0x0e6;
    pub const SCTeamPingPosPacket: u16 = 0x0e7;
    pub const SCTeamAckRiskyActionPacket: u16 = 0x0e8;
    pub const SCSiegeStatePacket: u16 = 0x0e9;
    pub const SCSiegeDeclaredPacket: u16 = 0x0ea;
    pub const SCSiegeReinforcePacket: u16 = 0x0eb;
    pub const SCSiegeMemberPacket: u16 = 0x0ec;
    pub const SCSiegeAlertPacket: u16 = 0x0ed;
    pub const SCConflictZoneStatePacket: u16 = 0x0ee;
    pub const SCConflictZoneHonorPointSumPacket: u16 = 0x0ef;
    pub const SCTimeOfDayPacket: u16 = 0x0f0;
    pub const SCDetailedTimeOfDayPacket: u16 = 0x0f1;
    pub const SCQuestsPacket: u16 = 0x0f2;
    pub const SCCompletedQuestsPacket: u16 = 0x0f3;
    pub const SCCraftItemUnlockPacket: u16 = 0x0f5;
    pub const SCLootableStatePacket: u16 = 0x0f6;
    pub const SCUnitLootingStatePacket: u16 = 0x0f7;
    pub const SCLootBagDataPacket: u16 = 0x0f8;
    pub const SCLootItemTookPacket: u16 = 0x0f9;
    pub const SCLootItemFailedPacket: u16 = 0x0fa;
    pub const SCLootDicePacket: u16 = 0x0fb;
    pub const SCLootDiceNotifyPacket: u16 = 0x0fc;
    pub const SCLootDiceSummaryPacket: u16 = 0x0fd;
    pub const SCExpChangedPacket: u16 = 0x0fe;
    pub const SCAbilityExpChangedPacket: u16 = 0x0ff;
    pub const SCRecoverableExpPacket: u16 = 0x100;
    pub const SCMileageChangedPacket: u16 = 0x101;
    pub const SCLevelChangedPacket: u16 = 0x102;
    pub const SCUnitModelPostureChangedPacket: u16 = 0x103;
    pub const SCSkillLearnedPacket: u16 = 0x105;
    pub const SCSkillUpgradedPacket: u16 = 0x106;
    pub const SCBuffLearnedPacket: u16 = 0x107;
    pub const SCSkillsResetPacket: u16 = 0x108;
    pub const SCAbilitySwappedPacket: u16 = 0x109;
    pub const SCErrorMsgPacket: u16 = 0x10a;
    pub const SCDoodadCreatedPacket: u16 = 0x10c;
    pub const SCDoodadRemovedPacket: u16 = 0x10d;
    pub const SCDoodadChangedPacket: u16 = 0x10e;
    pub const SCDoodadPhaseChangedPacket: u16 = 0x10f;
    pub const SCDoodadPuzzleScenePacket: u16 = 0x110;
    pub const SCDoodadQuestAcceptPacket: u16 = 0x111;
    pub const SCDoodadsCreatedPacket: u16 = 0x112;
    pub const SCDoodadsRemovedPacket: u16 = 0x113;
    pub const SCDoodadOriginatorPacket: u16 = 0x114;
    pub const SCMailFailedPacket: u16 = 0x115;
    pub const SCCountUnreadMailPacket: u16 = 0x116;
    pub const SCMailSentPacket: u16 = 0x117;
    pub const SCGotMailPacket: u16 = 0x118;
    pub const SCMailListPacket: u16 = 0x119;
    pub const SCMailListEndPacket: u16 = 0x11a;
    pub const SCMailBodyPacket: u16 = 0x11b;
    pub const SCMailReceiverOpenedPacket: u16 = 0x11c;
    pub const SCAttachmentTakenPacket: u16 = 0x11d;
    pub const SCChargeMoneyPaidPacket: u16 = 0x11e;
    pub const SCMailDeletedPacket: u16 = 0x11f;
    pub const SCSpamReportedPacket: u16 = 0x120;
    pub const SCMailReturnedPacket: u16 = 0x121;
    pub const SCMailStatusUpdatedPacket: u16 = 0x122;
    pub const SCMailRemovedPacket: u16 = 0x123;
    pub const SCMineAmountPacket: u16 = 0x124;
    pub const SCMateSpawnedPacket: u16 = 0x125;
    pub const SCMateEquipmentChangedPacket: u16 = 0x126;
    pub const SCMateEquipmentExpiredPacket: u16 = 0x127;
    pub const SCUnkPacket4: u16 = 0x128;
    pub const SCEmotionExpressedPacket: u16 = 0x12b;
    pub const SCSoldItemListPacket: u16 = 0x12c;
    pub const SCActionSlotsPacket: u16 = 0x12d;
    pub const SCAuctionPostedPacket: u16 = 0x12e;
    pub const SCAuctionSearchedPacket: u16 = 0x12f;
    pub const SCAuctionLowestPricePacket: u16 = 0x130;
    pub const SCAuctionBidPacket: u16 = 0x131;
    pub const SCAuctionCanceledPacket: u16 = 0x132;
    pub const SCAuctionMessagePacket: u16 = 0x133;
    pub const SCDiceValuePacket: u16 = 0x134;
    pub const SCNpcSpawnerPacket: u16 = 0x135;
    pub const SCActabilityPacket: u16 = 0x136;
    pub const SCHungPacket: u16 = 0x137;
    pub const SCUnhungPacket: u16 = 0x138;
    pub const SCBondDoodadPacket: u16 = 0x139;
    pub const SCUnbondDoodadPacket: u16 = 0x13a;
    pub const SCPlaySequencePacket: u16 = 0x13b;
    pub const SCGimmicksCreatedPacket: u16 = 0x13c;
    pub const SCGimmicksRemovedPacket: u16 = 0x13d;
    pub const SCGimmickMovementPacket: u16 = 0x13e;
    pub const SCGimmickJointsBrokenPacket: u16 = 0x13f;
    pub const SCGimmickResetJointsPacket: u16 = 0x140;
    pub const SCGimmickGraspedPacket: u16 = 0x141;
    pub const SCWorldRayCastingResultPacket: u16 = 0x142;
    pub const SCWorldAimPointPacket: u16 = 0x143;
    pub const SCWorldAABBPacket: u16 = 0x144;
    pub const SCUnkPacket5: u16 = 0x145;
    pub const SCQuestContextFailedPacket: u16 = 0x146;
    pub const SCQuestContextStartedPacket: u16 = 0x147;
    pub const SCQuestUnitReqFailedPacket: u16 = 0x148;
    pub const SCQuestContextUpdatedPacket: u16 = 0x149;
    pub const CSQuestAcceptConditionalPacket: u16 = 0x14a;
    pub const SCQuestContextCompletedPacket: u16 = 0x14b;
    pub const SCQuestContextResetPacket: u16 = 0x14c;
    pub const SCDoodadCompleteQuestPacket: u16 = 0x14d;
    pub const SCQuestRewardedByMailPacket: u16 = 0x14e;
    pub const SCQuestMailSentPacket: u16 = 0x150;
    pub const SCScheduleItemSentPacket: u16 = 0x151;
    pub const SCSoundAreaEventPacket: u16 = 0x152;
    pub const SCAreaChatBubblePacket: u16 = 0x153;
    pub const SCChatBubblePacket: u16 = 0x154;
    pub const SCAreaTeamMessagePacket: u16 = 0x155;
    pub const SCDoodadSoundPacket: u16 = 0x156;
    pub const SCDoodadPhaseMsgPacket: u16 = 0x157;
    pub const SCDoodadUccStringPacket: u16 = 0x158;
    pub const SCDoodadUccDataPacket: u16 = 0x15a;
    pub const SCUccCharacterNameLoadedPacket: u16 = 0x15b;
    pub const SCNaviTeleportPacket: u16 = 0x15c;
    pub const SCItemUccDataChangedPacket: u16 = 0x15d;
    pub const SCCanStartTradePacket: u16 = 0x15e;
    pub const SCCannotStartTradePacket: u16 = 0x15f;
    pub const SCTradeStartedPacket: u16 = 0x160;
    pub const SCTradeCanceledPacket: u16 = 0x161;
    pub const SCTradeItemPutupPacket: u16 = 0x162;
    pub const SCOtherTradeItemPutupPacket: u16 = 0x163;
    pub const SCTradeMoneyPutupPacket: u16 = 0x164;
    pub const SCOtherTradeMoneyPutupPacket: u16 = 0x165;
    pub const SCTradeItemTookdownPacket: u16 = 0x166;
    pub const SCOtherTradeItemTookdownPacket: u16 = 0x167;
    pub const SCTradeOkUpdatePacket: u16 = 0x168;
    pub const SCTradeLockUpdatePacket: u16 = 0x169;
    pub const SCTradeMadePacket: u16 = 0x16a;
    pub const SCTowerDefListPacket: u16 = 0x16b;
    pub const SCTowerDefStartPacket: u16 = 0x16c;
    pub const SCTowerDefEndPacket: u16 = 0x16d;
    pub const SCTowerDefWaveStartPacket: u16 = 0x16e;
    pub const SCCrimeChangedPacket: u16 = 0x16f;
    pub const SCCriminalArrestedPacket: u16 = 0x170;
    pub const SCAskImprisonOrTrialPacket: u16 = 0x171;
    pub const SCInviteJuryPacket: u16 = 0x172;
    pub const SCSummonJuryPacket: u16 = 0x173;
    pub const SCJuryBeSeatedPacket: u16 = 0x174;
    pub const SCSummonDefendantPacket: u16 = 0x175;
    pub const SCCrimeDataPacket: u16 = 0x176;
    pub const SCCrimeRecordsPacket: u16 = 0x177;
    pub const SCChangeTrialStatePacket: u16 = 0x178;
    pub const SCChangeJuryOkCountPacket: u16 = 0x179;
    pub const SCChangeJuryVerdictCountPacket: u16 = 0x17a;
    pub const SCTrialWaitStatusPacket: u16 = 0x17b;
    pub const SCJuryWaitStatusPacket: u16 = 0x17c;
    pub const SCRulingStatusPacket: u16 = 0x17d;
    pub const SCRulingClosedPacket: u16 = 0x17e;
    pub const SCTrialAudienceJoinedPacket: u16 = 0x17f;
    pub const SCTrialAudienceLeftPacket: u16 = 0x180;
    pub const SCTrialInfoPacket: u16 = 0x181;
    pub const SCJuryWaitingNumberPacket: u16 = 0x182;
    pub const SCTrialCanceledPacket: u16 = 0x183;
    pub const SCBotSuspectReportedPacket: u16 = 0x184;
    pub const SCBotSuspectArrestedPacket: u16 = 0x185;
    pub const SCSuspectGoingBotTrial: u16 = 0x186;
    pub const SCUnderWaterPacket: u16 = 0x187;
    pub const SCCharacterGamePointsPacket: u16 = 0x188;
    pub const SCGamePointChangedPacket: u16 = 0x189;
    pub const SCJuryPointChangedPacket: u16 = 0x18a;
    pub const SCAppliedToInstantGamePacket: u16 = 0x18b;
    pub const SCCancelInstantGamePacket: u16 = 0x18c;
    pub const SCInstantGameStatePacket: u16 = 0x18d;
    pub const SCInviteToInstantGamePacket: u16 = 0x18e;
    pub const SCInstantGameInviteTimeoutPacket: u16 = 0x18f;
    pub const SCInstantGameJoinedPacket: u16 = 0x190;
    pub const SCInstantGameStartPacket: u16 = 0x191;
    pub const SCInstantGameEndPacket: u16 = 0x192;
    pub const SCInstantGameAddPointPacket: u16 = 0x193;
    pub const SCInstantGameKillPacket: u16 = 0x194;
    pub const SCInstantGameKillstreakPacket: u16 = 0x195;
    pub const SCInstantGameUnkPacket: u16 = 0x196;
    pub const SCLoadInstancePacket: u16 = 0x197;
    pub const SCProcessingInstancePacket: u16 = 0x198;
    pub const SCGmCommandPacket: u16 = 0x199;
    pub const SCGmDumpInventoryPacket: u16 = 0x19a;
    pub const SCGmDumpEquipmentPacket: u16 = 0x19b;
    pub const SCGmDumpQuestsPacket: u16 = 0x19c;
    pub const SCGmDumpCompletedQuestsPacket: u16 = 0x19d;
    pub const SCGmDumpNpcPacket: u16 = 0x19e;
    pub const SCUnkPacket8: u16 = 0x19f;
    pub const SCKickedPacket: u16 = 0x1a0;
    pub const SCAccountWarnedPacket: u16 = 0x1a1;
    pub const SCAboxTeleportPacket: u16 = 0x1a2;
    pub const SCDelayedTaskOnInGameNotifyPacket: u16 = 0x1a3;
    pub const SCAppellationsPacket: u16 = 0x1a4;
    pub const SCAppellationGainedPacket: u16 = 0x1a5;
    pub const SCAppellationChangedPacket: u16 = 0x1a6;
    pub const SCTutorialSavedPacket: u16 = 0x1a7;
    pub const SCEmblemUploadedPacket: u16 = 0x1a8;
    pub const SCEmblemDownloadedPacket: u16 = 0x1a9;
    pub const SCShipyardStatePacket: u16 = 0x1aa;
    pub const SCSlaveStatePacket: u16 = 0x1ab;
    pub const SCMateStatePacket: u16 = 0x1ac;
    pub const SCDumpCombatInfoPacket: u16 = 0x1ad;
    pub const SCUccCheckecPacket: u16 = 0x1ae;
    pub const SCShowQuestAreaPacket: u16 = 0x1af;
    pub const SCShowCommonFarmPacket: u16 = 0x1b0;
    pub const SCResponseCommonFarmListPacket: u16 = 0x1b1;
    pub const SCTelescopeToggledPacket: u16 = 0x1b2;
    pub const SCTelescopeUnitsPacket: u16 = 0x1b3;
    pub const SCTransferTelescopeToggledPacket: u16 = 0x1b4;
    pub const SCTransferTelescopeUnitsPacket: u16 = 0x1b5;
    pub const SCSchoolOfFishFinderToggledPacket: u16 = 0x1b6;
    pub const SCSchoolOfFishDoodadsPacket: u16 = 0x1b7;
    pub const SCShowDemoModeUiPacket: u16 = 0x1b8;
    pub const SCDemoCharResetItemPacket: u16 = 0x1b9;
    pub const SCDemoCharResetLocPacket: u16 = 0x1ba;
    pub const SCDemoResetActionSlotPacket: u16 = 0x1bb;
    pub const SCSetBreathPacket: u16 = 0x1bc;
    pub const SCAggroTargetChanged: u16 = 0x1bd;
    pub const SCDoodadReqBattleFieldPacket: u16 = 0x1be;
    pub const SCCraftFailedPacket: u16 = 0x1bf;
    pub const SCExpertLimitModifiedPacket: u16 = 0x1c0;
    pub const SCExpertExpandedPacket: u16 = 0x1c1;
    pub const SCAccountInfoPacket: u16 = 0x1c2;
    pub const SCAiDebugPacket: u16 = 0x1c3;
    pub const SCAiAggroPacket: u16 = 0x1c4;
    pub const SCHSRequestPacket: u16 = 0x1c5;
    pub const SCHackGuardRetAddrsRequestPacket: u16 = 0x1c6;
    pub const SCUnitLocationPacket: u16 = 0x1c7;
    pub const SCRestrictInfoPacket: u16 = 0x1c8;
    pub const SCIsUnitInFarmPacket: u16 = 0x1c9;
    pub const SCResponseUIDataPacket: u16 = 0x1ca;
    pub const SCUnitVisualOptionsPacket: u16 = 0x1cb;
    pub const SCNotifyUIMessagePacket: u16 = 0x1cc;
    pub const SCRefreshInCharacterListPacket: u16 = 0x1cd;
    pub const SCResultRestrictCheckPacket: u16 = 0x1ce;
    pub const SCICSMenuListPacket: u16 = 0x1cf;
    pub const SCICSGoodListPacket: u16 = 0x1d0;
    pub const SCICSGoodDetailPacket: u16 = 0x1d1;
    pub const SCICSCheckTimePacket: u16 = 0x1d2;
    pub const SCICSBuyResultPacket: u16 = 0x1d3;
    pub const SCICSSyncGoodPacket: u16 = 0x1d4;
    pub const SCICSCashPointPacket: u16 = 0x1d6;
    pub const SCICSExchangeRatioPacket: u16 = 0x1d7;
    pub const SCPremiumServiceListPacket: u16 = 0x1d8;
    pub const SCBountyListPacket: u16 = 0x1d9;
    pub const SCSetBountyPermittedPacket: u16 = 0x1da;
    pub const SCSetBountyDonePacket: u16 = 0x1db;
    pub const SCBountyPaidPacket: u16 = 0x1dc;
    pub const SCInvitationCanceledPacket: u16 = 0x1dd;
    pub const SCPlaySoundPacket: u16 = 0x1de;
    pub const SCSendUserMusicPacket: u16 = 0x1df;
    pub const SCUserNoteLoadedPacket: u16 = 0x1e0;
    pub const SCPauseUserMusicPacket: u16 = 0x1e1;
    pub const SCGetSlotCountPacket: u16 = 0x1e2;
    pub const SCSecondPassSettedPacket: u16 = 0x1e3;
    pub const SCSecondPassKeyTablesPacket: u16 = 0x1e4;
    pub const SCSecondPassChangedPacket: u16 = 0x1e5;
    pub const SCSecondPassCheckedPacket: u16 = 0x1e6;
    pub const SCSecondPassClearedPacket: u16 = 0x1e7;
    pub const SCSecondPassCheckOverFailedPacket: u16 = 0x1e8;
    pub const SCSecondPassAccountLockedPacket: u16 = 0x1e9;
    pub const SCSecondPassCreatedPacket: u16 = 0x1ea;
    pub const SCScheduleItemUpdatePacket: u16 = 0x1eb;
    pub const SCScheduledEventStartedPacket: u16 = 0x1ec;
    pub const SCScheduledEventEndedPacket: u16 = 0x1ed;
    pub const SCMouldListPacket: u16 = 0x1ee;
    pub const SCMouldAskedPacket: u16 = 0x1ef;
    pub const SCMouldTakenPacket: u16 = 0x1f0;
    pub const SCToggleBeautyshopResponsePacket: u16 = 0x1f1;
    pub const SCCharacterGenderAndModelModifiedPacket: u16 = 0x1f3;
    pub const SCSpecialtyCurrentPacket: u16 = 0x1f4;
    pub const SCUnitIdleStatusPacket: u16 = 0x1f5;
    pub const SCRaceCondPacket: u16 = 0x1f6;
    pub const SCRacingPacket: u16 = 0x1f7;
    pub const SCRacingResultPacket: u16 = 0x1f8;
    pub const SCRankRewardMailPacket: u16 = 0x1f9;
    pub const SCRankCharacterPacket: u16 = 0x1fa;
    pub const SCRankSnapshotPacket: u16 = 0x1fb;
    pub const SCAchievementsPacket: u16 = 0x1fc;
    pub const SCAchievementChangedPacket: u16 = 0x1fd;
    pub const SCAchievementCompletedPacket: u16 = 0x1fe;
    pub const SCAchievementResetedPacket: u16 = 0x1ff;
    pub const SCAchievementItemSentPacket: u16 = 0x200;
    pub const SCUpdatePremiumPointPacket: u16 = 0x201;
    pub const SCPremiumPointChangedPacket: u16 = 0x202;
    pub const SCUnitGmModeChangedPacket: u16 = 0x203;
    pub const SCUnitPvPPointsChangedPacket: u16 = 0x204;
    pub const SCRankAlarmPacket: u16 = 0x205;
    pub const SCUnkPacket15: u16 = 0x206;
    pub const SCUnkPacket16: u16 = 0x207;
    pub const SCUnkPacket17: u16 = 0x208;
    pub const SCUnkCharacterAddStatPacket: u16 = 0x209;
    pub const SCUnlockCurrencySlotPacket: u16 = 0x20a;
    pub const SCUnkPacket20: u16 = 0x20b;
    pub const SCUnkPacket21: u16 = 0x20c;
    pub const SCUnkPacket22: u16 = 0x20d;
    pub const SCUnkPacket23: u16 = 0x20e;
    pub const SCUnkPacket24: u16 = 0x20f;
    pub const SCUnkPacket28: u16 = 0x210;
    pub const SCWorldMessagePacket: u16 = 0x211;
    pub const SCUnkPacket25: u16 = 0x212;
    pub const SCUnkPacket26: u16 = 0x213;
    pub const SCUnkPacket27: u16 = 0x214;
    pub const SCAccountAttributeConfigPacket: u16 = 0x215;
    pub const SCIdleKickPacket: u16 = 0x216;
    pub const SCLevelRestrictionConfigPacket: u16 = 0x217;

    /// `(opcode, name)` pairs, for logging unknown / not-yet-ported packets.
    pub const NAMES: &[(u16, &str)] = &[
        (0x000, "X2EnterWorldResponsePacket"),
        (0x001, "SCReconnectAuthPacket"),
        (0x002, "SCPrepareLeaveWorldPacket"),
        (0x003, "SCLeaveWorldGrantedPacket"),
        (0x004, "SCLeaveWorldCanceledPacket"),
        (0x005, "SCWorldQueuePacket"),
        (0x006, "SCInitialConfigPacket"),
        (0x007, "SCTrionConfigPacket"),
        (0x008, "SCFactionListPacket"),
        (0x009, "SCFactionRelationListPacket"),
        (0x00b, "SCExpeditionRolePolicyListPacket"),
        (0x00c, "SCExpeditionRolePolicyChangedPacket"),
        (0x00d, "SCExpeditionRoleChangedPacket"),
        (0x00e, "SCExpeditionOwnerChangedPacket"),
        (0x00f, "SCExpeditionShowRenameUIPacket"),
        (0x010, "SCNationShowRenameUIPacket"),
        (0x011, "SCFactionRenamedPacket"),
        (0x012, "SCFactionRetryRenamePacket"),
        (0x013, "SCFactionCreatedPacket"),
        (0x014, "SCExpeditionSponsorChangedPacket"),
        (0x015, "SCExpeditionDismissedPacket"),
        (0x016, "SCExpeditionMemberListPacket"),
        (0x017, "SCExpeditionMemberStatusChangedPacket"),
        (0x018, "SCFactionOwnerChangedPacket"),
        (0x019, "SCFactionSetRelationStatePacket"),
        (0x01a, "SCExpeditionInvitationPacket"),
        (0x01b, "SCUnitFactionChangedPacket"),
        (0x01c, "SCUnitExpeditionChangedPacket"),
        (0x01d, "SCDominionDataPacket"),
        (0x01e, "SCDominionDeletedPacket"),
        (0x01f, "SCDominionOwnerChangedPacket"),
        (0x020, "SCDominionTaxRatePacket"),
        (0x021, "SCNationalTaxRatePacket"),
        (0x022, "SCNationalMonumentChangedPacket"),
        (0x023, "SCDominionTaxBalancedPacket"),
        (0x024, "CDominionStartUnkPacket"),
        (0x025, "SCDominionEndUnkPacket"),
        (0x026, "SCFactionIndependencePacket"),
        (0x027, "SCFactionImmigrateInvitePacket"),
        (0x028, "SCFactionImmigrateInviteResultPacket"),
        (0x029, "SCFactionImmigrateToOriginResultPacket"),
        (0x02a, "SCFactionKickToOriginResultPacket"),
        (0x02b, "SCFactionDeclareHostileResultPacket"),
        (0x02c, "SCFactionRelationExpiredPacket"),
        (0x02d, "SCFamilyInvitationPacket"),
        (0x02e, "SCFamilyCreatedPacket"),
        (0x02f, "SCFamilyRemovedPacket"),
        (0x030, "SCFamilyDescPacket"),
        (0x031, "SCFamilyMemberAddedPacket"),
        (0x032, "SCFamilyMemberRemovedPacket"),
        (0x033, "SCFamilyOwnerChangedPacket"),
        (0x034, "SCFamilyTitleChangedPacket"),
        (0x035, "SCFamilyMemberNameChangedPacket"),
        (0x036, "SCFamilyTitlePacket"),
        (0x037, "SCFamilyMemberOnlinePacket"),
        (0x038, "SCCreateCharacterResponsePacket"),
        (0x039, "SCDeleteCharacterResponsePacket"),
        (0x03a, "SCEditCharacterResponsePacket"),
        (0x03b, "SCCharacterDeletedPacket"),
        (0x03c, "SCCancelCharacterDeleteResponsePacket"),
        (0x03d, "SCCharacterCreationFailedPacket"),
        (0x03e, "SCCharacterListPacket"),
        (0x03f, "SCRaceCongestionPacket"),
        (0x040, "SCCharacterStatePacket"),
        (0x041, "SCNotifyResurrectionPacket"),
        (0x042, "SCCharacterResurrectedPacket"),
        (0x043, "SCForceAttackSetPacket"),
        (0x045, "SCCharacterLaborPowerChangedPacket"),
        (0x046, "SCBmPointPacket"),
        (0x047, "SCAddActionPointPacket"),
        (0x048, "SCLpManagedPacket"),
        (0x049, "SCCharacterBoundPacket"),
        (0x04a, "SCCooldownsPacket"),
        (0x04b, "SCCharacterInvenInitPacket"),
        (0x04c, "SCCharacterInvenContentsPacket"),
        (0x04d, "SCInvenExpandedPacket"),
        (0x04e, "SCFriendsPacket"),
        (0x04f, "SCSearchListPacket"),
        (0x050, "SCAddFriendPacket"),
        (0x051, "SCDeleteFriendPacket"),
        (0x052, "SCFriendStatusChangedPacket"),
        (0x053, "SCCharDetailPacket"),
        (0x054, "SCBlockedUsersPacket"),
        (0x055, "SCAddBlockedUserPacket"),
        (0x056, "SCDeleteBlockedUserPacket"),
        (0x057, "SCLoginCharInfoHousePacket"),
        (0x058, "SCCharBriefPacket"),
        (0x059, "SCPlaytimePacket"),
        (0x05a, "SCCharacterRenamedPacket"),
        (0x05b, "SCCharacterPortalsPacket"),
        (0x05c, "SCCharacterReturnDistrictsPacket"),
        (0x05d, "SCPortalInfoSavedPacket"),
        (0x05e, "SCPortalDeletedPacket"),
        (0x060, "SCUnitPortalUsedPacket"),
        (0x061, "SCSlaveCreatedPacket"),
        (0x062, "SCSlaveRemovedPacket"),
        (0x063, "SCSlaveDespawnPacket"),
        (0x064, "SCSlaveBoundPacket"),
        (0x065, "SCQuestListPacket"),
        (0x066, "SCMySlavePacket"),
        (0x067, "SCEscapeSlavePacket"),
        (0x068, "SCSlaveEquipmentChangedPacket"),
        (0x069, "SCUnitStatePacket"),
        (0x06a, "SCUnitsRemovedPacket"),
        (0x06b, "SCUnitMovementsPacket"),
        (0x06c, "SCOneUnitMovementPacket"),
        (0x06d, "SCSkillControllerStatePacket"),
        (0x06e, "SCUnitOfflinePacket"),
        (0x06f, "SCActiveWeaponChangedPacket"),
        (0x070, "SCUnitNameChangedPacket"),
        (0x071, "SCUnitDeathPacket"),
        (0x072, "SCTeleportUnitPacket"),
        (0x073, "SCBlinkUnitPacket"),
        (0x074, "SCKnockBackUnitPacket"),
        (0x076, "SCUnitAttachedPacket"),
        (0x077, "SCUnitDetachedPacket"),
        (0x078, "SCUnitInvisiblePacket"),
        (0x079, "SCUnitImpulsePacket"),
        (0x07a, "SCUnitFlyingStateChangedPacket"),
        (0x07b, "SCNpcInteractionSkillListPacket"),
        (0x07c, "SCNpcInteractionEndedByZonePacket"),
        (0x07e, "SCWorldInteractionSkillListPacket"),
        (0x07f, "SCWorldInteractionCanceledPacket"),
        (0x080, "SCNpcInteractionStatusChangedPacket"),
        (0x081, "SCVegetationCutdowningPacket"),
        (0x082, "SCNpcFriendshipListPacket"),
        (0x083, "SCNpcFriendshipChangedPacket"),
        (0x084, "SCTargetChangedPacket"),
        (0x085, "SCCombatEngagedPacket"),
        (0x086, "SCCombatClearedPacket"),
        (0x087, "SCCvFCombatRelationshipPacket"),
        (0x088, "SCFvFCombatRelationshipPacket"),
        (0x089, "SCCombatFirstHitPacket"),
        (0x08a, "SCDumpCombatStatPacket"),
        (0x08b, "SCDuelChallengedPacket"),
        (0x08c, "SCDuelStartCountdownPacket"),
        (0x08d, "SCDuelStartedPacket"),
        (0x08e, "SCDuelEndedPacket"),
        (0x08f, "SCDuelStatePacket"),
        (0x090, "SCItemTaskSuccessPacket"),
        (0x091, "SCItemTaskNotifyPacket"),
        (0x093, "SCItemDetailUpdatedPacket"),
        (0x094, "SCUnitEquipmentsChangedPacket"),
        (0x095, "SCUnitEquipmentIdsPacket"),
        (0x096, "SCCofferContentsUpdatePacket"),
        (0x097, "SCItemAcquisitionPacket"),
        (0x098, "SCSyncItemLifespanPacket"),
        (0x099, "SCSpecialtyRatioPacket"),
        (0x09a, "SCSpecialtyGoodsPacket"),
        (0x09b, "SCSpecialtyRecordsPacket"),
        (0x09c, "SCGradeEnchantResultPacket"),
        (0x09d, "SCItemSocketingLunagemResultPacket"),
        (0x09e, "SCItemSocketingLunastoneResultPacket"),
        (0x09f, "SCGradeEnchantBroadcastPacket"),
        (0x0a0, "SCUnknownPacket"),
        (0x0a1, "SCSkillStartedPacket"),
        (0x0a2, "SCSkillFiredPacket"),
        (0x0a3, "SCSkillEndedPacket"),
        (0x0a4, "SCSkillStoppedPacket"),
        (0x0a5, "SCCastingStoppedPacket"),
        (0x0a6, "SCCastingDelayedPacket"),
        (0x0a7, "SCUnitDamagedPacket"),
        (0x0a8, "SCUnitHealedPacket"),
        (0x0a9, "SCUnitTrackedPacket"),
        (0x0aa, "SCBombUpdatedPacket"),
        (0x0ab, "SCCombatTextPacket"),
        (0x0ad, "SCSkillCooldownResetPacket"),
        (0x0ae, "SCPlotEventPacket"),
        (0x0af, "SCPlotEndedPacket"),
        (0x0b0, "SCPlotProcessingTimePacket"),
        (0x0b1, "SCPlotCastingStoppedPacket"),
        (0x0b2, "SCPlotChannelingStoppedPacket"),
        (0x0b3, "SCEnvDamagePacket"),
        (0x0b4, "SCOnOffSnowPacket"),
        (0x0b5, "SCUnkPacket"),
        (0x0b6, "SCBuffCreatedPacket"),
        (0x0b7, "SCBuffRemovedPacket"),
        (0x0b9, "SCBuffUpdatedPacket"),
        (0x0ba, "SCUnitPointsPacket"),
        (0x0bb, "SCUnitBountyMoneyPacket"),
        (0x0bc, "SCHouseStatePacket"),
        (0x0bd, "SCHouseBuildProgressPacket"),
        (0x0be, "SCHousePermissionChangedPacket"),
        (0x0bf, "SCHouseBuildPayChangedPacket"),
        (0x0c0, "SCHouseDemolishedPacket"),
        (0x0c1, "SCMyHousePacket"),
        (0x0c2, "SCMyHouseRemovedPacket"),
        (0x0c3, "SCHouseFarmPacket"),
        (0x0c4, "SCHouseTaxInfoPacket"),
        (0x0c5, "SCConstructHouseTaxPacket"),
        (0x0c6, "SCHousingRecoverTogglePacket"),
        (0x0c8, "SCHouseSetForSalePacket"),
        (0x0c9, "SCHouseResetForSalePacket"),
        (0x0ca, "SCHouseSoldPacket"),
        (0x0cb, "SCHouseOwnerNameChangedPacket"),
        (0x0cc, "SCJoinedChatChannelPacket"),
        (0x0cd, "SCLeavedChatChannelPacket"),
        (0x0ce, "SCChatMessagePacket"),
        (0x0cf, "SCNpcChatMessagePacket"),
        (0x0d0, "SCNoticeMessagePacket"),
        (0x0d1, "SCChatFailedPacket"),
        (0x0d2, "SCChatLocalizedMessagePacket"),
        (0x0d3, "SCChatSpamDelayPacket"),
        (0x0d4, "SCAskToJoinTeamPacket"),
        (0x0d5, "SCAskToJoinTeamAreaPacket"),
        (0x0d6, "SCJoinedTeamPacket"),
        (0x0d7, "SCRejectedTeamPacket"),
        (0x0d8, "SCLeavedTeamPacket"),
        (0x0d9, "SCTeamDismissedPacket"),
        (0x0da, "SCTeamMemberJoinedPacket"),
        (0x0db, "SCTeamMemberLeavedPacket"),
        (0x0dc, "SCTeamMemberDisconnectedPacket"),
        (0x0dd, "SCTeamOwnerChangedPacket"),
        (0x0de, "SCTeamMemberRoleChangedPacket"),
        (0x0df, "SCTeamBecameRaidTeamPacket"),
        (0x0e0, "SCTeamMemberMovedPacket"),
        (0x0e2, "SCTeamLootingRuleChangedPacket"),
        (0x0e3, "SCRefreshTeamMemberPacket"),
        (0x0e4, "SCTeamRemoteMembersExPacket"),
        (0x0e5, "SCTeamAreaInvitedPacket"),
        (0x0e6, "SCOverHeadMarkerSetPacket"),
        (0x0e7, "SCTeamPingPosPacket"),
        (0x0e8, "SCTeamAckRiskyActionPacket"),
        (0x0e9, "SCSiegeStatePacket"),
        (0x0ea, "SCSiegeDeclaredPacket"),
        (0x0eb, "SCSiegeReinforcePacket"),
        (0x0ec, "SCSiegeMemberPacket"),
        (0x0ed, "SCSiegeAlertPacket"),
        (0x0ee, "SCConflictZoneStatePacket"),
        (0x0ef, "SCConflictZoneHonorPointSumPacket"),
        (0x0f0, "SCTimeOfDayPacket"),
        (0x0f1, "SCDetailedTimeOfDayPacket"),
        (0x0f2, "SCQuestsPacket"),
        (0x0f3, "SCCompletedQuestsPacket"),
        (0x0f5, "SCCraftItemUnlockPacket"),
        (0x0f6, "SCLootableStatePacket"),
        (0x0f7, "SCUnitLootingStatePacket"),
        (0x0f8, "SCLootBagDataPacket"),
        (0x0f9, "SCLootItemTookPacket"),
        (0x0fa, "SCLootItemFailedPacket"),
        (0x0fb, "SCLootDicePacket"),
        (0x0fc, "SCLootDiceNotifyPacket"),
        (0x0fd, "SCLootDiceSummaryPacket"),
        (0x0fe, "SCExpChangedPacket"),
        (0x0ff, "SCAbilityExpChangedPacket"),
        (0x100, "SCRecoverableExpPacket"),
        (0x101, "SCMileageChangedPacket"),
        (0x102, "SCLevelChangedPacket"),
        (0x103, "SCUnitModelPostureChangedPacket"),
        (0x105, "SCSkillLearnedPacket"),
        (0x106, "SCSkillUpgradedPacket"),
        (0x107, "SCBuffLearnedPacket"),
        (0x108, "SCSkillsResetPacket"),
        (0x109, "SCAbilitySwappedPacket"),
        (0x10a, "SCErrorMsgPacket"),
        (0x10c, "SCDoodadCreatedPacket"),
        (0x10d, "SCDoodadRemovedPacket"),
        (0x10e, "SCDoodadChangedPacket"),
        (0x10f, "SCDoodadPhaseChangedPacket"),
        (0x110, "SCDoodadPuzzleScenePacket"),
        (0x111, "SCDoodadQuestAcceptPacket"),
        (0x112, "SCDoodadsCreatedPacket"),
        (0x113, "SCDoodadsRemovedPacket"),
        (0x114, "SCDoodadOriginatorPacket"),
        (0x115, "SCMailFailedPacket"),
        (0x116, "SCCountUnreadMailPacket"),
        (0x117, "SCMailSentPacket"),
        (0x118, "SCGotMailPacket"),
        (0x119, "SCMailListPacket"),
        (0x11a, "SCMailListEndPacket"),
        (0x11b, "SCMailBodyPacket"),
        (0x11c, "SCMailReceiverOpenedPacket"),
        (0x11d, "SCAttachmentTakenPacket"),
        (0x11e, "SCChargeMoneyPaidPacket"),
        (0x11f, "SCMailDeletedPacket"),
        (0x120, "SCSpamReportedPacket"),
        (0x121, "SCMailReturnedPacket"),
        (0x122, "SCMailStatusUpdatedPacket"),
        (0x123, "SCMailRemovedPacket"),
        (0x124, "SCMineAmountPacket"),
        (0x125, "SCMateSpawnedPacket"),
        (0x126, "SCMateEquipmentChangedPacket"),
        (0x127, "SCMateEquipmentExpiredPacket"),
        (0x128, "SCUnkPacket4"),
        (0x12b, "SCEmotionExpressedPacket"),
        (0x12c, "SCSoldItemListPacket"),
        (0x12d, "SCActionSlotsPacket"),
        (0x12e, "SCAuctionPostedPacket"),
        (0x12f, "SCAuctionSearchedPacket"),
        (0x130, "SCAuctionLowestPricePacket"),
        (0x131, "SCAuctionBidPacket"),
        (0x132, "SCAuctionCanceledPacket"),
        (0x133, "SCAuctionMessagePacket"),
        (0x134, "SCDiceValuePacket"),
        (0x135, "SCNpcSpawnerPacket"),
        (0x136, "SCActabilityPacket"),
        (0x137, "SCHungPacket"),
        (0x138, "SCUnhungPacket"),
        (0x139, "SCBondDoodadPacket"),
        (0x13a, "SCUnbondDoodadPacket"),
        (0x13b, "SCPlaySequencePacket"),
        (0x13c, "SCGimmicksCreatedPacket"),
        (0x13d, "SCGimmicksRemovedPacket"),
        (0x13e, "SCGimmickMovementPacket"),
        (0x13f, "SCGimmickJointsBrokenPacket"),
        (0x140, "SCGimmickResetJointsPacket"),
        (0x141, "SCGimmickGraspedPacket"),
        (0x142, "SCWorldRayCastingResultPacket"),
        (0x143, "SCWorldAimPointPacket"),
        (0x144, "SCWorldAABBPacket"),
        (0x145, "SCUnkPacket5"),
        (0x146, "SCQuestContextFailedPacket"),
        (0x147, "SCQuestContextStartedPacket"),
        (0x148, "SCQuestUnitReqFailedPacket"),
        (0x149, "SCQuestContextUpdatedPacket"),
        (0x14a, "CSQuestAcceptConditionalPacket"),
        (0x14b, "SCQuestContextCompletedPacket"),
        (0x14c, "SCQuestContextResetPacket"),
        (0x14d, "SCDoodadCompleteQuestPacket"),
        (0x14e, "SCQuestRewardedByMailPacket"),
        (0x150, "SCQuestMailSentPacket"),
        (0x151, "SCScheduleItemSentPacket"),
        (0x152, "SCSoundAreaEventPacket"),
        (0x153, "SCAreaChatBubblePacket"),
        (0x154, "SCChatBubblePacket"),
        (0x155, "SCAreaTeamMessagePacket"),
        (0x156, "SCDoodadSoundPacket"),
        (0x157, "SCDoodadPhaseMsgPacket"),
        (0x158, "SCDoodadUccStringPacket"),
        (0x15a, "SCDoodadUccDataPacket"),
        (0x15b, "SCUccCharacterNameLoadedPacket"),
        (0x15c, "SCNaviTeleportPacket"),
        (0x15d, "SCItemUccDataChangedPacket"),
        (0x15e, "SCCanStartTradePacket"),
        (0x15f, "SCCannotStartTradePacket"),
        (0x160, "SCTradeStartedPacket"),
        (0x161, "SCTradeCanceledPacket"),
        (0x162, "SCTradeItemPutupPacket"),
        (0x163, "SCOtherTradeItemPutupPacket"),
        (0x164, "SCTradeMoneyPutupPacket"),
        (0x165, "SCOtherTradeMoneyPutupPacket"),
        (0x166, "SCTradeItemTookdownPacket"),
        (0x167, "SCOtherTradeItemTookdownPacket"),
        (0x168, "SCTradeOkUpdatePacket"),
        (0x169, "SCTradeLockUpdatePacket"),
        (0x16a, "SCTradeMadePacket"),
        (0x16b, "SCTowerDefListPacket"),
        (0x16c, "SCTowerDefStartPacket"),
        (0x16d, "SCTowerDefEndPacket"),
        (0x16e, "SCTowerDefWaveStartPacket"),
        (0x16f, "SCCrimeChangedPacket"),
        (0x170, "SCCriminalArrestedPacket"),
        (0x171, "SCAskImprisonOrTrialPacket"),
        (0x172, "SCInviteJuryPacket"),
        (0x173, "SCSummonJuryPacket"),
        (0x174, "SCJuryBeSeatedPacket"),
        (0x175, "SCSummonDefendantPacket"),
        (0x176, "SCCrimeDataPacket"),
        (0x177, "SCCrimeRecordsPacket"),
        (0x178, "SCChangeTrialStatePacket"),
        (0x179, "SCChangeJuryOkCountPacket"),
        (0x17a, "SCChangeJuryVerdictCountPacket"),
        (0x17b, "SCTrialWaitStatusPacket"),
        (0x17c, "SCJuryWaitStatusPacket"),
        (0x17d, "SCRulingStatusPacket"),
        (0x17e, "SCRulingClosedPacket"),
        (0x17f, "SCTrialAudienceJoinedPacket"),
        (0x180, "SCTrialAudienceLeftPacket"),
        (0x181, "SCTrialInfoPacket"),
        (0x182, "SCJuryWaitingNumberPacket"),
        (0x183, "SCTrialCanceledPacket"),
        (0x184, "SCBotSuspectReportedPacket"),
        (0x185, "SCBotSuspectArrestedPacket"),
        (0x186, "SCSuspectGoingBotTrial"),
        (0x187, "SCUnderWaterPacket"),
        (0x188, "SCCharacterGamePointsPacket"),
        (0x189, "SCGamePointChangedPacket"),
        (0x18a, "SCJuryPointChangedPacket"),
        (0x18b, "SCAppliedToInstantGamePacket"),
        (0x18c, "SCCancelInstantGamePacket"),
        (0x18d, "SCInstantGameStatePacket"),
        (0x18e, "SCInviteToInstantGamePacket"),
        (0x18f, "SCInstantGameInviteTimeoutPacket"),
        (0x190, "SCInstantGameJoinedPacket"),
        (0x191, "SCInstantGameStartPacket"),
        (0x192, "SCInstantGameEndPacket"),
        (0x193, "SCInstantGameAddPointPacket"),
        (0x194, "SCInstantGameKillPacket"),
        (0x195, "SCInstantGameKillstreakPacket"),
        (0x196, "SCInstantGameUnkPacket"),
        (0x197, "SCLoadInstancePacket"),
        (0x198, "SCProcessingInstancePacket"),
        (0x199, "SCGmCommandPacket"),
        (0x19a, "SCGmDumpInventoryPacket"),
        (0x19b, "SCGmDumpEquipmentPacket"),
        (0x19c, "SCGmDumpQuestsPacket"),
        (0x19d, "SCGmDumpCompletedQuestsPacket"),
        (0x19e, "SCGmDumpNpcPacket"),
        (0x19f, "SCUnkPacket8"),
        (0x1a0, "SCKickedPacket"),
        (0x1a1, "SCAccountWarnedPacket"),
        (0x1a2, "SCAboxTeleportPacket"),
        (0x1a3, "SCDelayedTaskOnInGameNotifyPacket"),
        (0x1a4, "SCAppellationsPacket"),
        (0x1a5, "SCAppellationGainedPacket"),
        (0x1a6, "SCAppellationChangedPacket"),
        (0x1a7, "SCTutorialSavedPacket"),
        (0x1a8, "SCEmblemUploadedPacket"),
        (0x1a9, "SCEmblemDownloadedPacket"),
        (0x1aa, "SCShipyardStatePacket"),
        (0x1ab, "SCSlaveStatePacket"),
        (0x1ac, "SCMateStatePacket"),
        (0x1ad, "SCDumpCombatInfoPacket"),
        (0x1ae, "SCUccCheckecPacket"),
        (0x1af, "SCShowQuestAreaPacket"),
        (0x1b0, "SCShowCommonFarmPacket"),
        (0x1b1, "SCResponseCommonFarmListPacket"),
        (0x1b2, "SCTelescopeToggledPacket"),
        (0x1b3, "SCTelescopeUnitsPacket"),
        (0x1b4, "SCTransferTelescopeToggledPacket"),
        (0x1b5, "SCTransferTelescopeUnitsPacket"),
        (0x1b6, "SCSchoolOfFishFinderToggledPacket"),
        (0x1b7, "SCSchoolOfFishDoodadsPacket"),
        (0x1b8, "SCShowDemoModeUiPacket"),
        (0x1b9, "SCDemoCharResetItemPacket"),
        (0x1ba, "SCDemoCharResetLocPacket"),
        (0x1bb, "SCDemoResetActionSlotPacket"),
        (0x1bc, "SCSetBreathPacket"),
        (0x1bd, "SCAggroTargetChanged"),
        (0x1be, "SCDoodadReqBattleFieldPacket"),
        (0x1bf, "SCCraftFailedPacket"),
        (0x1c0, "SCExpertLimitModifiedPacket"),
        (0x1c1, "SCExpertExpandedPacket"),
        (0x1c2, "SCAccountInfoPacket"),
        (0x1c3, "SCAiDebugPacket"),
        (0x1c4, "SCAiAggroPacket"),
        (0x1c5, "SCHSRequestPacket"),
        (0x1c6, "SCHackGuardRetAddrsRequestPacket"),
        (0x1c7, "SCUnitLocationPacket"),
        (0x1c8, "SCRestrictInfoPacket"),
        (0x1c9, "SCIsUnitInFarmPacket"),
        (0x1ca, "SCResponseUIDataPacket"),
        (0x1cb, "SCUnitVisualOptionsPacket"),
        (0x1cc, "SCNotifyUIMessagePacket"),
        (0x1cd, "SCRefreshInCharacterListPacket"),
        (0x1ce, "SCResultRestrictCheckPacket"),
        (0x1cf, "SCICSMenuListPacket"),
        (0x1d0, "SCICSGoodListPacket"),
        (0x1d1, "SCICSGoodDetailPacket"),
        (0x1d2, "SCICSCheckTimePacket"),
        (0x1d3, "SCICSBuyResultPacket"),
        (0x1d4, "SCICSSyncGoodPacket"),
        (0x1d6, "SCICSCashPointPacket"),
        (0x1d7, "SCICSExchangeRatioPacket"),
        (0x1d8, "SCPremiumServiceListPacket"),
        (0x1d9, "SCBountyListPacket"),
        (0x1da, "SCSetBountyPermittedPacket"),
        (0x1db, "SCSetBountyDonePacket"),
        (0x1dc, "SCBountyPaidPacket"),
        (0x1dd, "SCInvitationCanceledPacket"),
        (0x1de, "SCPlaySoundPacket"),
        (0x1df, "SCSendUserMusicPacket"),
        (0x1e0, "SCUserNoteLoadedPacket"),
        (0x1e1, "SCPauseUserMusicPacket"),
        (0x1e2, "SCGetSlotCountPacket"),
        (0x1e3, "SCSecondPassSettedPacket"),
        (0x1e4, "SCSecondPassKeyTablesPacket"),
        (0x1e5, "SCSecondPassChangedPacket"),
        (0x1e6, "SCSecondPassCheckedPacket"),
        (0x1e7, "SCSecondPassClearedPacket"),
        (0x1e8, "SCSecondPassCheckOverFailedPacket"),
        (0x1e9, "SCSecondPassAccountLockedPacket"),
        (0x1ea, "SCSecondPassCreatedPacket"),
        (0x1eb, "SCScheduleItemUpdatePacket"),
        (0x1ec, "SCScheduledEventStartedPacket"),
        (0x1ed, "SCScheduledEventEndedPacket"),
        (0x1ee, "SCMouldListPacket"),
        (0x1ef, "SCMouldAskedPacket"),
        (0x1f0, "SCMouldTakenPacket"),
        (0x1f1, "SCToggleBeautyshopResponsePacket"),
        (0x1f3, "SCCharacterGenderAndModelModifiedPacket"),
        (0x1f4, "SCSpecialtyCurrentPacket"),
        (0x1f5, "SCUnitIdleStatusPacket"),
        (0x1f6, "SCRaceCondPacket"),
        (0x1f7, "SCRacingPacket"),
        (0x1f8, "SCRacingResultPacket"),
        (0x1f9, "SCRankRewardMailPacket"),
        (0x1fa, "SCRankCharacterPacket"),
        (0x1fb, "SCRankSnapshotPacket"),
        (0x1fc, "SCAchievementsPacket"),
        (0x1fd, "SCAchievementChangedPacket"),
        (0x1fe, "SCAchievementCompletedPacket"),
        (0x1ff, "SCAchievementResetedPacket"),
        (0x200, "SCAchievementItemSentPacket"),
        (0x201, "SCUpdatePremiumPointPacket"),
        (0x202, "SCPremiumPointChangedPacket"),
        (0x203, "SCUnitGmModeChangedPacket"),
        (0x204, "SCUnitPvPPointsChangedPacket"),
        (0x205, "SCRankAlarmPacket"),
        (0x206, "SCUnkPacket15"),
        (0x207, "SCUnkPacket16"),
        (0x208, "SCUnkPacket17"),
        (0x209, "SCUnkCharacterAddStatPacket"),
        (0x20a, "SCUnlockCurrencySlotPacket"),
        (0x20b, "SCUnkPacket20"),
        (0x20c, "SCUnkPacket21"),
        (0x20d, "SCUnkPacket22"),
        (0x20e, "SCUnkPacket23"),
        (0x20f, "SCUnkPacket24"),
        (0x210, "SCUnkPacket28"),
        (0x211, "SCWorldMessagePacket"),
        (0x212, "SCUnkPacket25"),
        (0x213, "SCUnkPacket26"),
        (0x214, "SCUnkPacket27"),
        (0x215, "SCAccountAttributeConfigPacket"),
        (0x216, "SCIdleKickPacket"),
        (0x217, "SCLevelRestrictionConfigPacket"),
    ];
}

/// `PPOffsets`
pub mod pp {
    pub const ChangeStatePacket: u16 = 0x000;
    pub const FinishStatePacket: u16 = 0x001;
    pub const FlushMsgsPacket: u16 = 0x002;
    pub const UpdatePhysicsTimePacket: u16 = 0x004;
    pub const BeginUpdateObjPacket: u16 = 0x005;
    pub const EndUpdateObjPacket: u16 = 0x006;
    pub const BeginBindObjPacket: u16 = 0x007;
    pub const EndBindObjPacket: u16 = 0x008;
    pub const UnbindPredictedObjPacket: u16 = 0x009;
    pub const RemoveStaticObjPacket: u16 = 0x00a;
    pub const VoiceDataPacket: u16 = 0x00b;
    pub const UpdateAspectPacket: u16 = 0x00c;
    pub const SetAspectProfilePacket: u16 = 0x00d;
    pub const PartialAspectPacket: u16 = 0x00e;
    pub const SetGameTypePacket: u16 = 0x00f;
    pub const ChangeCVarPacket: u16 = 0x010;
    pub const EntityClassRegistrationPacket: u16 = 0x011;
    pub const PingPacket: u16 = 0x012;
    pub const PongPacket: u16 = 0x013;
    pub const PacketSeqChange: u16 = 0x014;
    pub const FastPingPacket: u16 = 0x015;
    pub const FastPongPacket: u16 = 0x016;

    /// `(opcode, name)` pairs, for logging unknown / not-yet-ported packets.
    pub const NAMES: &[(u16, &str)] = &[
        (0x000, "ChangeStatePacket"),
        (0x001, "FinishStatePacket"),
        (0x002, "FlushMsgsPacket"),
        (0x004, "UpdatePhysicsTimePacket"),
        (0x005, "BeginUpdateObjPacket"),
        (0x006, "EndUpdateObjPacket"),
        (0x007, "BeginBindObjPacket"),
        (0x008, "EndBindObjPacket"),
        (0x009, "UnbindPredictedObjPacket"),
        (0x00a, "RemoveStaticObjPacket"),
        (0x00b, "VoiceDataPacket"),
        (0x00c, "UpdateAspectPacket"),
        (0x00d, "SetAspectProfilePacket"),
        (0x00e, "PartialAspectPacket"),
        (0x00f, "SetGameTypePacket"),
        (0x010, "ChangeCVarPacket"),
        (0x011, "EntityClassRegistrationPacket"),
        (0x012, "PingPacket"),
        (0x013, "PongPacket"),
        (0x014, "PacketSeqChange"),
        (0x015, "FastPingPacket"),
        (0x016, "FastPongPacket"),
    ];
}

/// `GLOffsets`
pub mod gl {
    pub const GLRegisterGameServerPacket: u16 = 0x000;
    pub const GLPlayerEnterPacket: u16 = 0x001;
    pub const GLPlayerReconnectPacket: u16 = 0x002;
    pub const GLRequestInfoPacket: u16 = 0x003;
    pub const GLGameServerLoadPacket: u16 = 0x004;

    /// `(opcode, name)` pairs, for logging unknown / not-yet-ported packets.
    pub const NAMES: &[(u16, &str)] = &[
        (0x000, "GLRegisterGameServerPacket"),
        (0x001, "GLPlayerEnterPacket"),
        (0x002, "GLPlayerReconnectPacket"),
        (0x003, "GLRequestInfoPacket"),
        (0x004, "GLGameServerLoadPacket"),
    ];
}

/// `LGOffsets`
pub mod lg {
    pub const LGRegisterGameServerPacket: u16 = 0x000;
    pub const LGPlayerEnterPacket: u16 = 0x001;
    pub const LGPlayerReconnectPacket: u16 = 0x002;
    pub const LGRequestInfoPacket: u16 = 0x003;

    /// `(opcode, name)` pairs, for logging unknown / not-yet-ported packets.
    pub const NAMES: &[(u16, &str)] = &[
        (0x000, "LGRegisterGameServerPacket"),
        (0x001, "LGPlayerEnterPacket"),
        (0x002, "LGPlayerReconnectPacket"),
        (0x003, "LGRequestInfoPacket"),
    ];
}

/// Looks up the C# constant name for an opcode in one of the [`NAMES`](cs::NAMES) tables.
pub fn name_of(table: &[(u16, &'static str)], opcode: u16) -> Option<&'static str> {
    table.iter().find(|(op, _)| *op == opcode).map(|(_, n)| *n)
}

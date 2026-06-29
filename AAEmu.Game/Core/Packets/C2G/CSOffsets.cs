namespace AAEmu.Game.Core.Packets.C2G;

public static class CSOffsets
{
    // All opcodes here are updated for version client_12_r208022
    // World
    public const ushort X2EnterWorldPacket = 0x000;
    public const ushort CSAesXorKeyPacket = 0x047; // 10.0.2.13 RSA key reply (observed C2S opcode after CTJoin; 1.2 had it elsewhere)
    public const ushort CSLeaveWorldPacket = 0x001;
    public const ushort CSCancelLeaveWorldPacket = 0x002;
    public const ushort CSCreateExpeditionPacket = 0x004;
    public const ushort CSChangeExpeditionSponsorPacket = 0x005; // TODO : this packet seems like it has been removed.
    public const ushort CSChangeExpeditionRolePolicyPacket = 0x006;
    public const ushort CSChangeExpeditionMemberRolePacket = 0x007;
    public const ushort CSChangeExpeditionOwnerPacket = 0x008;
    public const ushort CSRenameExpeditionPacket = 0xFFF;
    public const ushort CSDismissExpeditionPacket = 0x00B;
    public const ushort CSInviteToExpeditionPacket = 0x00C;
    public const ushort CSReplyExpeditionInvitationPacket = 0x00D;
    public const ushort CSLeaveExpeditionPacket = 0x00E;
    public const ushort CSKickFromExpeditionPacket = 0x00F;
    // 0x10 unk packet
    public const ushort CSUpdateDominionTaxRatePacket = 0x01A;
    public const ushort CSFactionImmigrationInvitePacket = 0xFFF;
    public const ushort CSFactionImmigrationInviteReplyPacket = 0xFFF;
    public const ushort CSFactionImmigrateToOriginPacket = 0xFFF;
    public const ushort CSFactionKickToOriginPacket = 0xFFF;
    public const ushort CSFactionDeclareHostilePacket = 0xFFF;
    public const ushort CSFamilyInviteMemberPacket = 0x03D;
    public const ushort CSFamilyReplyInvitationPacket = 0x03E;
    public const ushort CSFamilyLeavePacket = 0x03F;
    public const ushort CSFamilyKickPacket = 0x040;
    public const ushort CSFamilyChangeTitlePacket = 0x041;
    public const ushort CSFamilyChangeOwnerPacket = 0x042;
    public const ushort CSListCharacterPacket = 0xFFF;
    public const ushort CSRefreshInCharacterListPacket = 0x048;
    public const ushort CSCreateCharacterPacket = 0x049;
    public const ushort CSEditCharacterPacket = 0x04A;
    public const ushort CSDeleteCharacterPacket = 0x04B;
    public const ushort CSSelectCharacterPacket = 0x04C;
    public const ushort CSCheckRaceCongestionPacket = 0x04D;
    public const ushort CSSpawnCharacterPacket = 0x04E;
    public const ushort CSCancelCharacterDeletePacket = 0x04F;
    public const ushort CSNotifyInGamePacket = 0x051;
    public const ushort CSNotifyInGameCompletedPacket = 0x052;
    public const ushort CSEditorGameModePacket = 0x053;
    public const ushort CSChangeTargetPacket = 0x054;
    public const ushort CSRequestCharBriefPacket = 0x055;
    public const ushort CSSpawnSlavePacket = 0x05B;
    public const ushort CSDespawnSlavePacket = 0x05C;
    public const ushort CSDestroySlavePacket = 0x05D;
    public const ushort CSBindSlavePacket = 0x05E;
    public const ushort CSDiscardSlavePacket = 0x05F;
    public const ushort CSChangeSlaveTargetPacket = 0x060; // TODO: this packet is not in the offsets
    public const ushort CSChangeSlaveNamePacket = 0x061;
    public const ushort CSRepairSlaveItemsPacket = 0x062;
    public const ushort CSTurretStatePacket = 0x063;
    public const ushort CSChangeSlaveEquipmentPacket = 0x064;
    public const ushort CSDestroyItemPacket = 0x065;
    public const ushort CSSplitBagItemPacket = 0x066;
    public const ushort CSSwapItemsPacket = 0x067;
    public const ushort CSRepairSingleEquipmentPacket = 0x069;
    public const ushort CSRepairAllEquipmentsPacket = 0x06A;
    public const ushort CSSplitCofferItemPacket = 0x06C;
    public const ushort CSSwapCofferItemsPacket = 0x06D;
    public const ushort CSExpandSlotsPacket = 0x06E;
    public const ushort CSSellBackpackGoodsPacket = 0x06F;
    public const ushort CSSpecialtyRatioPacket = 0x070;
    public const ushort CSListSpecialtyGoodsPacket = 0x071;
    public const ushort CSBuySpecialtyItemPacket = 0x072; // TODO: this packet is not in the offsets
    public const ushort CSSpecialtyRecordLoadPacket = 0x075; // TODO: this packet is not in the offsets
    public const ushort CSDepositMoneyPacket = 0x076;
    public const ushort CSWithdrawMoneyPacket = 0x077;
    public const ushort CSConvertItemLookPacket = 0x078;
    public const ushort CSItemSecurePacket = 0x07B;
    public const ushort CSItemUnsecurePacket = 0x07C;
    public const ushort CSEquipmentsSecurePacket = 0x07D;
    public const ushort CSEquipmentsUnsecurePacket = 0x07E;
    public const ushort CSResurrectCharacterPacket = 0x082;
    public const ushort CSSetForceAttackPacket = 0xFFF;
    public const ushort CSChallengeDuelPacket = 0x084;
    public const ushort CSStartDuelPacket = 0x085;
    public const ushort CSStartSkillPacket = 0x086;
    public const ushort CSStopCastingPacket = 0x088;
    public const ushort CSRemoveBuffPacket = 0x08A;
    public const ushort CSConstructHouseTaxPacket = 0x08B;
    public const ushort CSCreateHousePacket = 0x08C;
    public const ushort CSDecorateHousePacket = 0x08D;
    public const ushort CSChangeHouseNamePacket = 0x08E;
    public const ushort CSChangeHousePermissionPacket = 0x08F;
    public const ushort CSChangeHousePayPacket = 0xFFF; // TODO: this packet is not in the offsets
    public const ushort CSRequestHouseTaxPacket = 0x090;
    // 0x5c unk packet
    public const ushort CSAllowHousingRecoverPacket = 0x092;
    public const ushort CSSellHousePacket = 0x093;
    public const ushort CSSellHouseCancelPacket = 0x094;
    public const ushort CSBuyHousePacket = 0x095;
    public const ushort CSJoinUserChatChannelPacket = 0x096;
    public const ushort CSLeaveChatChannelPacket = 0x097;
    public const ushort CSSendChatMessagePacket = 0x098;
    public const ushort CSConsoleCmdUsedPacket = 0x09A;
    public const ushort CSInteractNPCPacket = 0x09B;
    public const ushort CSInteractNPCEndPacket = 0x09C;
    public const ushort CSBoardingTransferPacket = 0x09D;
    public const ushort CSStartInteractionPacket = 0x09E;
    public const ushort CSSelectInteractionExPacket = 0x0A1;
    public const ushort CSCofferInteractionPacket = 0x0A2;
    public const ushort CSCriminalLockedPacket = 0x0A5;
    public const ushort CSReplyImprisonOrTrialPacket = 0x0A6;
    public const ushort CSSkipFinalStatementPacket = 0x0A7;
    public const ushort CSReplyInviteJuryPacket = 0x0A8;
    public const ushort CSJurySummonedPacket = 0x0A9;
    public const ushort CSJuryEndTestimonyPacket = 0x0AA;
    public const ushort CSCancelTrialPacket = 0x0AB;
    public const ushort CSJuryVerdictPacket = 0x0AC;
    public const ushort CSReportCrimePacket = 0x0AD;
    public const ushort CSJoinTrialAudiencePacket = 0x0AE;
    public const ushort CSLeaveTrialAudiencePacket = 0x0AF;
    public const ushort CSRequestJuryWaitingNumberPacket = 0x0B0;
    public const ushort CSInviteToTeamPacket = 0x0B1;
    public const ushort CSInviteAreaToTeamPacket = 0x0B2;
    public const ushort CSReplyToJoinTeamPacket = 0x0B3;
    public const ushort CSLeaveTeamPacket = 0x0B4;
    public const ushort CSKickTeamMemberPacket = 0x0B5;
    public const ushort CSMakeTeamOwnerPacket = 0x0B6;
    public const ushort CSSetTeamOfficerPacket = 0xFFF; // TODO: this packet is not in the offsets 
    public const ushort CSConvertToRaidTeamPacket = 0x0B7;
    public const ushort CSMoveTeamMemberPacket = 0x0B8;
    public const ushort CSChangeLootingRulePacket = 0x0BA;
    public const ushort CSDismissTeamPacket = 0x0BB;
    public const ushort CSSetTeamMemberRolePacket = 0x0BC;
    public const ushort CSSetOverHeadMarkerPacket = 0x0BD;
    public const ushort CSSetPingPosPacket = 0x0BE;
    public const ushort CSAskRiskyTeamActionPacket = 0x0BF;
    public const ushort CSMoveUnitPacket = 0x0C8;
    public const ushort CSSkillControllerStatePacket = 0x0C9;
    public const ushort CSCreateSkillControllerPacket = 0x0CA;
    public const ushort CSActiveWeaponChangedPacket = 0x0CB;
    public const ushort CSChangeItemLookPacket = 0xFFF; // TODO: this packet is not in the offsets 
    public const ushort CSLootOpenBagPacket = 0x0CE;
    public const ushort CSLootItemPacket = 0x0CF;
    public const ushort CSLootCloseBagPacket = 0x0D0;
    public const ushort CSLootDicePacket = 0x0D1;
    public const ushort CSLearnSkillPacket = 0x0D2;
    public const ushort CSLearnBuffPacket = 0x0D3;
    public const ushort CSResetSkillsPacket = 0x0D4;
    public const ushort CSSwapAbilityPacket = 0x0D5;
    public const ushort CSSendMailPacket = 0x0DB;
    public const ushort CSListMailPacket = 0x0DC;
    public const ushort CSListMailContinuePacket = 0x0DD;
    public const ushort CSReadMailPacket = 0x0DE;
    public const ushort CSTakeAttachmentItemPacket = 0x0DF;
    public const ushort CSTakeAttachmentMoneyPacket = 0x0E0;
    // 0x9f unk packet
    public const ushort CSTakeAttachmentSequentially = 0x0E1;
    public const ushort CSPayChargeMoneyPacket = 0x0E2;
    public const ushort CSDeleteMailPacket = 0x0E3;
    public const ushort CSReportSpamPacket = 0xFFF;
    public const ushort CSReturnMailPacket = 0x0E4; // TODO: this packet is not in the offsets 
    public const ushort CSRemoveMatePacket = 0x0E5;
    public const ushort CSChangeMateTargetPacket = 0x0E6;
    public const ushort CSChangeMateNamePacket = 0x0E7;
    public const ushort CSMountMatePacket = 0x0E8;
    public const ushort CSUnMountMatePacket = 0x0E9;
    public const ushort CSChangeMateEquipmentPacket = 0x0EA;
    public const ushort CSChangeMateUserStatePacket = 0x0EB;
    // 0xab unk packet
    // 0xac unk packet
    public const ushort CSExpressEmotionPacket = 0x0EE;
    public const ushort CSBuyItemsPacket = 0x0F0;
    public const ushort CSBuyCoinItemPacket = 0xFFF;
    public const ushort CSSellItemsPacket = 0x0F2;
    public const ushort CSListSoldItemPacket = 0x0F3;
    public const ushort CSBuyPriestBuffPacket = 0xFFF;
    public const ushort CSUseTeleportPacket = 0xFFF;
    public const ushort CSTeleportEndedPacket = 0x0F5;
    public const ushort CSRepairPetItemsPacket = 0x0F6;
    public const ushort CSUpdateActionSlotPacket = 0x0F7;
    public const ushort CSAuctionPostPacket = 0x0F8;
    public const ushort CSAuctionSearchPacket = 0x0F9;
    public const ushort CSBidAuctionPacket = 0x0FA;
    public const ushort CSCancelAuctionPacket = 0x0FB;
    public const ushort CSAuctionMyBidListPacket = 0x0FC;
    public const ushort CSAuctionLowestPricePacket = 0x0FD;
    public const ushort CSRollDicePacket = 0x0FF;
    //0xbf CSRequestNpcSpawnerList
    //0xc8 CSRemoveAllFieldSlaves
    //0xc9 CSAddFieldSlave
    public const ushort CSHangPacket = 0x10D;
    public const ushort CSUnhangPacket = 0x10E;
    public const ushort CSUnbondDoodadPacket = 0x10F;
    public const ushort CSCompletedCinemaPacket = 0x110;
    public const ushort CSStartedCinemaPacket = 0x111;
    public const ushort CSRequestPermissionToPlayCinemaForDirectingMode = 0xFFF;
    //0xd1 CSEditorRemoveGimmickPacket
    //0xd2 CSEditorAddGimmickPacket
    //0xd3 CSInteractGimmickPacket
    //0xd4 CSWorldRayCastingPacket
    public const ushort CSStartQuestContextPacket = 0x117;
    public const ushort CSCompleteQuestContextPacket = 0x118;
    public const ushort CSDropQuestContextPacket = 0x119;
    public const ushort CSResetQuestContextPacket = 0x11A; // TODO: this packet is not in the offsets 
    public const ushort CSAcceptCheatQuestContextPacket = 0x11B; // TODO: this packet is not in the offsets 
    public const ushort CSQuestTalkMadePacket = 0x11C;
    public const ushort CSQuestStartWithPacket = 0x11D;
    public const ushort CSTryQuestCompleteAsLetItDonePacket = 0x11F;
    public const ushort CSUsePortalPacket = 0x123;
    public const ushort CSDeletePortalPacket = 0x124;
    public const ushort CSInstanceLoadedPacket = 0x125;
    public const ushort CSApplyToInstantGamePacket = 0x126;
    public const ushort CSCancelInstantGamePacket = 0x127;
    public const ushort CSJoinInstantGamePacket = 0xFFF;
    public const ushort CSEnteredInstantGameWorldPacket = 0xFFF;
    public const ushort CSLeaveInstantGamePacket = 0x12A;
    public const ushort CSCreateDoodadPacket = 0x131;
    public const ushort CSSaveDoodadUccStringPacket = 0xFFF; // TODO: this packet is not in the offsets 
    public const ushort CSNaviTeleportPacket = 0x132;
    public const ushort CSNaviOpenPortalPacket = 0x133;
    public const ushort CSChangeDoodadPhasePacket = 0x134;
    public const ushort CSNaviOpenBountyPacket = 0xFFF;
    public const ushort CSChangeDoodadDataPacket = 0x135;
    public const ushort CSStartTradePacket = 0x139;
    public const ushort CSCanStartTradePacket = 0x13A;
    public const ushort CSCannotStartTradePacket = 0x13B;
    public const ushort CSCancelTradePacket = 0x13C;
    public const ushort CSPutupTradeItemPacket = 0x13D;
    public const ushort CSPutupTradeMoneyPacket = 0x13E;
    public const ushort CSTakedownTradeItemPacket = 0x13F;
    public const ushort CSTradeLockPacket = 0x140;
    public const ushort CSTradeOkPacket = 0x141;
    public const ushort CSSaveTutorialPacket = 0x142;
    public const ushort CSSetLogicDoodadPacket = 0x143;
    public const ushort CSCleanupLogicLinkPacket = 0x144;
    public const ushort CSExecuteCraft = 0x145;
    public const ushort CSChangeAppellationPacket = 0x14C;
    public const ushort CSCreateShipyardPacket = 0x14E;
    public const ushort CSRestartMainQuestPacket = 0x14F;
    public const ushort CSSetLpManageCharacterPacket = 0x150; // 10.0.2.13 CS_SET_LP_MANAGE_CHARACTER (336)
    public const ushort CSUpgradeExpertLimitPacket = 0x151;
    public const ushort CSDowngradeExpertLimitPacket = 0x152;
    public const ushort CSExpandExpertPacket = 0x153;
    public const ushort CSSearchListPacket = 0x157; // TODO: this packet is not in the offsets 
    public const ushort CSAddFriendPacket = 0xFFF;
    public const ushort CSDeleteFriendPacket = 0x159;
    public const ushort CSCharDetailPacket = 0x15A;
    public const ushort CSAddBlockedUserPacket = 0x15B;
    public const ushort CSDeleteBlockedUserPacket = 0x15C;
    public const ushort CSRequestCommonFarmList = 0xFFF;
    public const ushort CSNotifySubZonePacket = 0x165;
    public const ushort CSResturnAddrsPacket = 0x168;
    public const ushort CSRequestUIDataPacket = 0x16D;
    public const ushort CSSaveUIDataPacket = 0x16E;
    public const ushort CSBroadcastVisualOptionPacket = 0x16F;
    public const ushort CSRestrictCheckPacket = 0x172;
    public const ushort CSICSMenuListPacket = 0x173;
    public const ushort CSICSGoodsListPacket = 0xFFF;
    public const ushort CSICSBuyGoodPacket = 0xFFF;
    public const ushort CSICSMoneyRequestPacket = 0x175;
    public const ushort CSSendUserMusicPacket = 0x177;
    public const ushort CSSaveUserMusicNotesPacket = 0x178;
    public const ushort CSRequestMusicNotesPacket = 0x179;
    public const ushort CSEndMusicPacket = 0xFFF; // tentative name
    public const ushort CSExitBeautySalonPacket = 0xFFF;
    public const ushort CSBeautyshopDataPacket = 0x185;
    public const ushort CSEnterBeautySalonPacket = 0xFFF;
    public const ushort CSRankCharacterPacket = 0xFFF;
    public const ushort CSRequestSecondPasswordKeyTablesPacket = 0x17F;
    // 0x130 CSRankSnapshotPacket
    public const ushort CSRequestSpecialtyCurrentPacket = 0xFFF;
    public const ushort CSIdleStatusPacket = 0x18E;
    // 0x133 CSChangeAutoUseAAPointPacket
    public const ushort CSThisTimeUnpackItemPacket = 0x190;
    public const ushort CSPremiumServiceBuyPacket = 0x191;
    public const ushort CSPremiumServiceListPacket = 0x192;
    // 0x137 CSICSBuyAAPointPacket
    // 0x138 CSRequestTencentFatigueInfoPacket
    public const ushort CSTakeAllAttachmentItemPacket = 0x194;
    // 0x13a unk packet
    // 0x13b unk packet
    public const ushort CSPremiumServiceMsgPacket = 0x197;
    // 0x13d unk packet
    // 0x13e unk packet
    public const ushort CSUnknownInstancePacket = 0xFFF;
    // 0x13f unk packet
    public const ushort CSSetupSecondPassword = 0xFFF;
    // 0x141 unk packet
    // 0x142 unk packet

    // no such packets
    public const ushort CSUpdateNationalTaxRatePacket = 0xFFF;
    public const ushort CSSetCraftingPayPacket = 0xFFF;
}

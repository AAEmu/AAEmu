namespace AAEmu.Game.Core.Packets.C2G
{
    public static class CSOffsets
    {
        // All opcodes here are updated for version client_12_r208022
        // World
        public const ushort X2EnterWorldPacket = 0x000;
        public const ushort CSLeaveWorldPacket = 0x001;
        public const ushort CSCancelLeaveWorldPacket = 0x002;
        public const ushort CSCreateExpeditionPacket = 0x004;
        public const ushort CSChangeExpeditionSponsorPacket = 0xfff; // TODO : this packet seems like it has been removed.
        public const ushort CSChangeExpeditionRolePolicyPacket = 0x006;
        public const ushort CSChangeExpeditionMemberRolePacket = 0x007;
        public const ushort CSChangeExpeditionOwnerPacket = 0x008;
        public const ushort CSRenameExpeditionPacket = 0x009;
        public const ushort CSDismissExpeditionPacket = 0x00b;
        public const ushort CSInviteToExpeditionPacket = 0x00c;
        public const ushort CSReplyExpeditionInvitationPacket = 0x00d;
        public const ushort CSLeaveExpeditionPacket = 0x00e;
        public const ushort CSKickFromExpeditionPacket = 0x00f;
        // 0x10 unk packet
        public const ushort CSUpdateDominionTaxRatePacket = 0x012;
        public const ushort CSFactionImmigrationInvitePacket = 0x015;
        public const ushort CSFactionImmigrationInviteReplyPacket = 0x016;
        public const ushort CSFactionImmigrateToOriginPacket = 0x017;
        public const ushort CSFactionKickToOriginPacket = 0x018;
        public const ushort CSFactionDeclareHostilePacket = 0x019;
        public const ushort CSFamilyInviteMemberPacket = 0x01a;
        public const ushort CSFamilyReplyInvitationPacket = 0x01b;
        public const ushort CSFamilyLeavePacket = 0x01c;
        public const ushort CSFamilyKickPacket = 0x01d;
        public const ushort CSFamilyChangeTitlePacket = 0x01e;
        public const ushort CSFamilyChangeOwnerPacket = 0x01f;
        public const ushort CSListCharacterPacket = 0x020;
        public const ushort CSRefreshInCharacterListPacket = 0x021;
        public const ushort CSCreateCharacterPacket = 0x022;
        public const ushort CSEditCharacterPacket = 0x023;
        public const ushort CSDeleteCharacterPacket = 0x024;
        public const ushort CSSelectCharacterPacket = 0x025;
        public const ushort CSSpawnCharacterPacket = 0x026;
        public const ushort CSCancelCharacterDeletePacket = 0x027;
        public const ushort CSNotifyInGamePacket = 0x029;
        public const ushort CSNotifyInGameCompletedPacket = 0x02a;
        public const ushort CSEditorGameModePacket = 0x02b;
        public const ushort CSChangeTargetPacket = 0x02c;
        public const ushort CSRequestCharBriefPacket = 0x02d;
        public const ushort CSSpawnSlavePacket = 0x02e;
        public const ushort CSDespawnSlavePacket = 0x02f;
        public const ushort CSDestroySlavePacket = 0x030;
        public const ushort CSBindSlavePacket = 0x031;
        public const ushort CSDiscardSlavePacket = 0x032;
        public const ushort CSChangeSlaveTargetPacket = 0xfff; // TODO: this packet is not in the offsets
        public const ushort CSChangeSlaveNamePacket = 0x034;
        public const ushort CSRepairSlaveItemsPacket = 0x035;
        public const ushort CSTurretStatePacket = 0x036;
        public const ushort CSChangeSlaveEquipmentPacket = 0x037;
        public const ushort CSDestroyItemPacket = 0x038;
        public const ushort CSSplitBagItemPacket = 0x039;
        public const ushort CSSwapItemsPacket = 0x03a;
        public const ushort CSRepairSingleEquipmentPacket = 0x03c;
        public const ushort CSRepairAllEquipmentsPacket = 0x03d;
        public const ushort CSSplitCofferItemPacket = 0x03f;
        public const ushort CSSwapCofferItemsPacket = 0x040;
        public const ushort CSExpandSlotsPacket = 0x041;
        public const ushort CSSellBackpackGoodsPacket = 0x042;
        public const ushort CSSpecialtyRatioPacket = 0x043;
        public const ushort CSListSpecialtyGoodsPacket = 0x044;
        public const ushort CSBuySpecialtyItemPacket = 0xfff; // TODO: this packet is not in the offsets
        public const ushort CSSpecialtyRecordLoadPacket = 0xfff; // TODO: this packet is not in the offsets
        public const ushort CSDepositMoneyPacket = 0x047;
        public const ushort CSWithdrawMoneyPacket = 0x048;
        public const ushort CSConvertItemLookPacket = 0x049;
        public const ushort CSItemSecurePacket = 0x04a;
        public const ushort CSItemUnsecurePacket = 0x04b;
        public const ushort CSEquipmentsSecurePacket = 0x04c;
        public const ushort CSEquipmentsUnsecurePacket = 0x04d;
        public const ushort CSResurrectCharacterPacket = 0x04e;
        public const ushort CSSetForceAttackPacket = 0x04f;
        public const ushort CSChallengeDuelPacket = 0x050;
        public const ushort CSStartDuelPacket = 0x051;
        public const ushort CSStartSkillPacket = 0x052;
        public const ushort CSStopCastingPacket = 0x054;
        public const ushort CSRemoveBuffPacket = 0x055;
        public const ushort CSConstructHouseTaxPacket = 0x056;
        public const ushort CSCreateHousePacket = 0x057;
        public const ushort CSDecorateHousePacket = 0x058;
        public const ushort CSChangeHouseNamePacket = 0x059;
        public const ushort CSChangeHousePermissionPacket = 0x05a;
        public const ushort CSChangeHousePayPacket = 0xfff; // TODO: this packet is not in the offsets
        public const ushort CSRequestHouseTaxPacket = 0x05c;
        // 0x5c unk packet
        public const ushort CSAllowHousingRecoverPacket = 0x05d;
        public const ushort CSSellHousePacket = 0x05e;
        public const ushort CSSellHouseCancelPacket = 0x05f;
        public const ushort CSBuyHousePacket = 0x060;
        public const ushort CSJoinUserChatChannelPacket = 0x061;
        public const ushort CSLeaveChatChannelPacket = 0x062;
        public const ushort CSSendChatMessagePacket = 0x063;
        public const ushort CSConsoleCmdUsedPacket = 0x064;
        public const ushort CSInteractNPCPacket = 0x065;
        public const ushort CSInteractNPCEndPacket = 0x066;
        public const ushort CSBoardingTransferPacket = 0x067;
        public const ushort CSStartInteractionPacket = 0x068;
        public const ushort CSSelectInteractionExPacket = 0x06b;
        public const ushort CSCofferInteractionPacket = 0x06c;
        public const ushort CSCriminalLockedPacket = 0x06e;
        public const ushort CSReplyImprisonOrTrialPacket = 0x06f;
        public const ushort CSSkipFinalStatementPacket = 0x070;
        public const ushort CSReplyInviteJuryPacket = 0x071;
        public const ushort CSJurySummonedPacket = 0x072;
        public const ushort CSJuryEndTestimonyPacket = 0x073;
        public const ushort CSCancelTrialPacket = 0x074;
        public const ushort CSJuryVerdictPacket = 0x075;
        public const ushort CSReportCrimePacket = 0x076;
        public const ushort CSJoinTrialAudiencePacket = 0x077;
        public const ushort CSLeaveTrialAudiencePacket = 0x078;
        public const ushort CSRequestJuryWaitingNumberPacket = 0x079;
        public const ushort CSInviteToTeamPacket = 0x07a;
        public const ushort CSInviteAreaToTeamPacket = 0x07b;
        public const ushort CSReplyToJoinTeamPacket = 0x07c;
        public const ushort CSLeaveTeamPacket = 0x07d;
        public const ushort CSKickTeamMemberPacket = 0x07e;
        public const ushort CSMakeTeamOwnerPacket = 0x07f;
        public const ushort CSSetTeamOfficerPacket = 0xfff; // TODO: this packet is not in the offsets 
        public const ushort CSConvertToRaidTeamPacket = 0x080;
        public const ushort CSMoveTeamMemberPacket = 0x081;
        public const ushort CSChangeLootingRulePacket = 0x083;
        public const ushort CSDismissTeamPacket = 0x084;
        public const ushort CSSetTeamMemberRolePacket = 0x085;
        public const ushort CSSetOverHeadMarkerPacket = 0x086;
        public const ushort CSSetPingPosPacket = 0x087;
        public const ushort CSAskRiskyTeamActionPacket = 0x088;
        public const ushort CSMoveUnitPacket = 0x089;
        public const ushort CSSkillControllerStatePacket = 0x08a;
        public const ushort CSCreateSkillControllerPacket = 0x08b;
        public const ushort CSActiveWeaponChangedPacket = 0x08c;
        public const ushort CSChangeItemLookPacket = 0xfff; // TODO: this packet is not in the offsets 
        public const ushort CSLootOpenBagPacket = 0x08e;
        public const ushort CSLootItemPacket = 0x08f;
        public const ushort CSLootCloseBagPacket = 0x090;
        public const ushort CSLootDicePacket = 0x091;
        public const ushort CSLearnSkillPacket = 0x092;
        public const ushort CSLearnBuffPacket = 0x093;
        public const ushort CSResetSkillsPacket = 0x094;
        public const ushort CSSwapAbilityPacket = 0x096;
        public const ushort CSSendMailPacket = 0x098;
        public const ushort CSListMailPacket = 0x09a;
        public const ushort CSListMailContinuePacket = 0x09b;
        public const ushort CSReadMailPacket = 0x09c;
        public const ushort CSTakeAttachmentItemPacket = 0x09d;
        public const ushort CSTakeAttachmentMoneyPacket = 0x09e;
        // 0x9f unk packet
        public const ushort CSTakeAttachmentSequentially = 0x09f;
        public const ushort CSPayChargeMoneyPacket = 0x0a0;
        public const ushort CSDeleteMailPacket = 0x0a1;
        public const ushort CSReportSpamPacket = 0x0a3;
        public const ushort CSReturnMailPacket = 0xfff; // TODO: this packet is not in the offsets 
        public const ushort CSRemoveMatePacket = 0x0a4;
        public const ushort CSChangeMateTargetPacket = 0x0a5;
        public const ushort CSChangeMateNamePacket = 0x0a6;
        public const ushort CSMountMatePacket = 0x0a7;
        public const ushort CSUnMountMatePacket = 0x0a8;
        public const ushort CSChangeMateEquipmentPacket = 0x0a9;
        public const ushort CSChangeMateUserStatePacket = 0x0aa;
        // 0xab unk packet
        // 0xac unk packet
        public const ushort CSExpressEmotionPacket = 0x0ad;
        public const ushort CSBuyItemsPacket = 0x0ae;
        public const ushort CSBuyCoinItemPacket = 0x0af;
        public const ushort CSSellItemsPacket = 0x0b0;
        public const ushort CSListSoldItemPacket = 0x0b1;
        public const ushort CSBuyPriestBuffPacket = 0x0b2;
        public const ushort CSUseTeleportPacket = 0x0b3;
        public const ushort CSTeleportEndedPacket = 0x0b4;
        public const ushort CSRepairPetItemsPacket = 0x0b5;
        public const ushort CSUpdateActionSlotPacket = 0x0b6;
        public const ushort CSAuctionPostPacket = 0x0b7;
        public const ushort CSAuctionSearchPacket = 0x0b8;
        public const ushort CSBidAuctionPacket = 0x0b9;
        public const ushort CSCancelAuctionPacket = 0x0ba;
        public const ushort CSAuctionMyBidListPacket = 0x0bb;
        public const ushort CSAuctionLowestPricePacket = 0x0bc;
        public const ushort CSRollDicePacket = 0x0bd;
        //0xbf CSRequestNpcSpawnerList
        //0xc8 CSRemoveAllFieldSlaves
        //0xc9 CSAddFieldSlave
        public const ushort CSHangPacket = 0x0cb;
        public const ushort CSUnhangPacket = 0x0cc;
        public const ushort CSUnbondDoodadPacket = 0x0cd;
        public const ushort CSCompletedCinemaPacket = 0x0ce;
        public const ushort CSStartedCinemaPacket = 0x0cf;
        //0xd0 CSRequestPermissionToPlayCinemaForDirectingMode
        //0xd1 CSEditorRemoveGimmickPacket
        //0xd2 CSEditorAddGimmickPacket
        //0xd3 CSInteractGimmickPacket
        //0xd4 CSWorldRayCastingPacket
        public const ushort CSStartQuestContextPacket = 0x0d5;
        public const ushort CSCompleteQuestContextPacket = 0x0d6;
        public const ushort CSDropQuestContextPacket = 0x0d7;
        public const ushort CSResetQuestContextPacket = 0xfff; // TODO: this packet is not in the offsets 
        public const ushort CSAcceptCheatQuestContextPacket = 0xfff; // TODO: this packet is not in the offsets 
        public const ushort CSQuestTalkMadePacket = 0x0da;
        public const ushort CSQuestStartWithPacket = 0x0db;
        public const ushort CSTryQuestCompleteAsLetItDonePacket = 0x0dd;
        public const ushort CSUsePortalPacket = 0x0de;
        public const ushort CSDeletePortalPacket = 0x0df;
        public const ushort CSInstanceLoadedPacket = 0x0e0;
        public const ushort CSApplyToInstantGamePacket = 0x0e1;
        public const ushort CSCancelInstantGamePacket = 0x0e2;
        public const ushort CSJoinInstantGamePacket = 0x0e3;
        public const ushort CSEnteredInstantGameWorldPacket = 0x0e4;
        public const ushort CSLeaveInstantGamePacket = 0x0e5;
        public const ushort CSCreateDoodadPacket = 0x0e6;
        public const ushort CSSaveDoodadUccStringPacket = 0xfff; // TODO: this packet is not in the offsets 
        public const ushort CSNaviTeleportPacket = 0x0e7;
        public const ushort CSNaviOpenPortalPacket = 0x0e8;
        public const ushort CSChangeDoodadPhasePacket = 0x0e9;
        public const ushort CSNaviOpenBountyPacket = 0x0ea;
        public const ushort CSChangeDoodadDataPacket = 0x0eb;
        public const ushort CSStartTradePacket = 0x0ec;
        public const ushort CSCanStartTradePacket = 0x0ed;
        public const ushort CSCannotStartTradePacket = 0x0ee;
        public const ushort CSCancelTradePacket = 0x0ef;
        public const ushort CSPutupTradeItemPacket = 0x0f0;
        public const ushort CSPutupTradeMoneyPacket = 0x0f1;
        public const ushort CSTakedownTradeItemPacket = 0x0f2;
        public const ushort CSTradeLockPacket = 0x0f3;
        public const ushort CSTradeOkPacket = 0x0f4;
        public const ushort CSSaveTutorialPacket = 0x0f5;
        public const ushort CSSetLogicDoodadPacket = 0x0f6;
        public const ushort CSCleanupLogicLinkPacket = 0x0f7;
        public const ushort CSExecuteCraft = 0x0f8;
        public const ushort CSChangeAppellationPacket = 0x0f9;
        public const ushort CSCreateShipyardPacket = 0x0fc;
        public const ushort CSRestartMainQuestPacket = 0x0fd;
        public const ushort CSSetLpManageCharacterPacket = 0x0fe;
        public const ushort CSUpgradeExpertLimitPacket = 0x0ff;
        public const ushort CSDowngradeExpertLimitPacket = 0x100;
        public const ushort CSExpandExpertPacket = 0x101;
        public const ushort CSSearchListPacket = 0xfff; // TODO: this packet is not in the offsets 
        public const ushort CSAddFriendPacket = 0x104;
        public const ushort CSDeleteFriendPacket = 0x105;
        public const ushort CSCharDetailPacket = 0x106;
        public const ushort CSAddBlockedUserPacket = 0x107;
        public const ushort CSDeleteBlockedUserPacket = 0x108;
        public const ushort CSNotifySubZonePacket = 0x112;
        public const ushort CSResturnAddrsPacket = 0x115;
        public const ushort CSRequestUIDataPacket = 0x117;
        public const ushort CSSaveUIDataPacket = 0x118;
        public const ushort CSBroadcastVisualOptionPacket = 0x119;
        public const ushort CSRestrictCheckPacket = 0x11a;
        public const ushort CSICSMenuListPacket = 0x11b;
        public const ushort CSICSGoodsListPacket = 0x11c;
        public const ushort CSICSBuyGoodPacket = 0x11d;
        public const ushort CSICSMoneyRequestPacket = 0x11e;
        // 0x12e CSEnterBeautySalonPacket
        public const ushort CSRankCharacterPacket = 0x12F;
        public const ushort CSRequestSecondPasswordKeyTablesPacket = 0x125;
        public const ushort CSRankSnapshotPacket = 0x130;
        // 0x131 unk packet
        public const ushort CSIdleStatusPacket = 0x132;
        // 0x133 CSChangeAutoUseAAPointPacket
        public const ushort CSThisTimeUnpackItemPacket = 0x134;
        public const ushort CSPremiumServiceBuyPacket = 0x135;
        public const ushort CSPremiumServiceListPacket = 0x136;
        // 0x137 CSICSBuyAAPointPacket
        // 0x138 CSRequestTencentFatigueInfoPacket
        // 0x139 CSTakeAllAttachmentItemPacket
        // 0x13a unk packet
        // 0x13b unk packet
        public const ushort CSPremiumServieceMsgPacket = 0x13c;
        // 0x13d unk packet
        // 0x13e unk packet
        // 0x13f unk packet
        public const ushort CSSetupSecondPassword = 0x140;
        // 0x141 unk packet
        // 0x142 unk packet

        // no such packets
        public const ushort CSUpdateNationalTaxRatePacket = 0xfff;
        public const ushort CSSetCraftingPayPacket = 0xfff;
    }
}

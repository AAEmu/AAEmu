using System;
using System.Net;
using AAEmu.Commons.Network.Type;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Packets.C2G;
using AAEmu.Game.Core.Packets.Proxy;
using AAEmu.Game.Models;
using NLog;

namespace AAEmu.Game.Core.Network.Game
{
    public class GameNetwork : Singleton<GameNetwork>
    {
        private Server _server;
        private GameProtocolHandler _handler;
        private static Logger _log = LogManager.GetCurrentClassLogger();

        private GameNetwork()
        {
            _handler = new GameProtocolHandler();

            // World
            RegisterPacket(0x000, 1, typeof(X2EnterWorldPacket));
            RegisterPacket(0x001, 1, typeof(CSLeaveWorldPacket));
            RegisterPacket(0x002, 1, typeof(CSCancelLeaveWorldPacket));
            RegisterPacket(0x004, 1, typeof(CSCreateExpeditionPacket));
            //            RegisterPacket(0x005, 1, typeof(CSChangeExpeditionSponsorPacket)); TODO : this packet seems like it has been removed.
            RegisterPacket(0x006, 1, typeof(CSChangeExpeditionRolePolicyPacket));
            RegisterPacket(0x007, 1, typeof(CSChangeExpeditionMemberRolePacket));
            RegisterPacket(0x008, 1, typeof(CSChangeExpeditionOwnerPacket));
            RegisterPacket(0x009, 1, typeof(CSRenameExpeditionPacket));
            RegisterPacket(0x00b, 1, typeof(CSDismissExpeditionPacket));
            RegisterPacket(0x00c, 1, typeof(CSInviteToExpeditionPacket));
            RegisterPacket(0x00d, 1, typeof(CSReplyExpeditionInvitationPacket));
            RegisterPacket(0x00e, 1, typeof(CSLeaveExpeditionPacket));
            RegisterPacket(0x00f, 1, typeof(CSKickFromExpeditionPacket));
            // 0x10 unk packet
            RegisterPacket(0x012, 1, typeof(CSUpdateDominionTaxRatePacket));

            RegisterPacket(0x015, 1, typeof(CSFactionImmigrationInvitePacket));
            RegisterPacket(0x016, 1, typeof(CSFactionImmigrationInviteReplyPacket));
            RegisterPacket(0x017, 1, typeof(CSFactionImmigrateToOriginPacket));
            RegisterPacket(0x018, 1, typeof(CSFactionKickToOriginPacket));
            RegisterPacket(0x019, 1, typeof(CSFactionDeclareHostilePacket));
            RegisterPacket(0x01a, 1, typeof(CSFamilyInviteMemberPacket));
            RegisterPacket(0x01b, 1, typeof(CSFamilyReplyInvitationPacket));
            RegisterPacket(0x01c, 1, typeof(CSFamilyLeavePacket));
            RegisterPacket(0x01d, 1, typeof(CSFamilyKickPacket));
            RegisterPacket(0x01e, 1, typeof(CSFamilyChangeTitlePacket));
            RegisterPacket(0x01f, 1, typeof(CSFamilyChangeOwnerPacket));
            RegisterPacket(0x020, 1, typeof(CSListCharacterPacket));
            RegisterPacket(0x021, 1, typeof(CSRefreshInCharacterListPacket));
            RegisterPacket(0x022, 1, typeof(CSCreateCharacterPacket));
            RegisterPacket(0x023, 1, typeof(CSEditCharacterPacket));
            RegisterPacket(0x024, 1, typeof(CSDeleteCharacterPacket));
            RegisterPacket(0x025, 1, typeof(CSSelectCharacterPacket));
            RegisterPacket(0x026, 1, typeof(CSSpawnCharacterPacket));
            RegisterPacket(0x027, 1, typeof(CSCancelCharacterDeletePacket));
            RegisterPacket(0x029, 1, typeof(CSNotifyInGamePacket));
            RegisterPacket(0x02a, 1, typeof(CSNotifyInGameCompletedPacket));
            RegisterPacket(0x02b, 1, typeof(CSEditorGameModePacket));
            RegisterPacket(0x02c, 1, typeof(CSChangeTargetPacket));
            RegisterPacket(0x02d, 1, typeof(CSRequestCharBriefPacket));
            RegisterPacket(0x02e, 1, typeof(CSSpawnSlavePacket));
            RegisterPacket(0x02f, 1, typeof(CSDespawnSlavePacket));
            RegisterPacket(0x030, 1, typeof(CSDestroySlavePacket));
            RegisterPacket(0x031, 1, typeof(CSBindSlavePacket));
            RegisterPacket(0x032, 1, typeof(CSDiscardSlavePacket));
            //            RegisterPacket(0x031, 1, typeof(CSChangeSlaveTargetPacket)); TODO: this packet is not in the offsets
            RegisterPacket(0x034, 1, typeof(CSChangeSlaveNamePacket));
            RegisterPacket(0x035, 1, typeof(CSRepairSlaveItemsPacket));
            RegisterPacket(0x036, 1, typeof(CSTurretStatePacket));
            RegisterPacket(0x037, 1, typeof(CSChangeSlaveEquipmentPacket));
            RegisterPacket(0x038, 1, typeof(CSDestroyItemPacket));
            RegisterPacket(0x039, 1, typeof(CSSplitBagItemPacket));
            RegisterPacket(0x03a, 1, typeof(CSSwapItemsPacket));
            RegisterPacket(0x03c, 1, typeof(CSRepairSingleEquipmentPacket));
            RegisterPacket(0x03d, 1, typeof(CSRepairAllEquipmentsPacket));
            RegisterPacket(0x03f, 1, typeof(CSSplitCofferItemPacket));
            RegisterPacket(0x040, 1, typeof(CSSwapCofferItemsPacket));
            RegisterPacket(0x041, 1, typeof(CSExpandSlotsPacket));
            RegisterPacket(0x042, 1, typeof(CSSellBackpackGoodsPacket));
            RegisterPacket(0x043, 1, typeof(CSSpecialtyRatioPacket));
            RegisterPacket(0x044, 1, typeof(CSListSpecialtyGoodsPacket));
            //            RegisterPacket(0x043, 1, typeof(CSBuySpecialtyItemPacket)); TODO: this packet is not in the offsets
            //            RegisterPacket(0x044, 1, typeof(CSSpecialtyRecordLoadPacket)); TODO: this packet is not in the offsets
            RegisterPacket(0x047, 1, typeof(CSDepositMoneyPacket));
            RegisterPacket(0x048, 1, typeof(CSWithdrawMoneyPacket));
            RegisterPacket(0x049, 1, typeof(CSConvertItemLookPacket));
            RegisterPacket(0x04a, 1, typeof(CSItemSecurePacket));
            RegisterPacket(0x04b, 1, typeof(CSItemUnsecurePacket));
            RegisterPacket(0x04c, 1, typeof(CSEquipmentsSecurePacket));
            RegisterPacket(0x04d, 1, typeof(CSEquipmentsUnsecurePacket));
            RegisterPacket(0x04e, 1, typeof(CSResurrectCharacterPacket));
            RegisterPacket(0x04f, 1, typeof(CSSetForceAttackPacket));
            RegisterPacket(0x050, 1, typeof(CSChallengeDuelPacket));
            RegisterPacket(0x051, 1, typeof(CSStartDuelPacket));
            RegisterPacket(0x052, 1, typeof(CSStartSkillPacket));
            RegisterPacket(0x054, 1, typeof(CSStopCastingPacket));
            RegisterPacket(0x055, 1, typeof(CSRemoveBuffPacket));
            RegisterPacket(0x056, 1, typeof(CSConstructHouseTaxPacket));
            RegisterPacket(0x057, 1, typeof(CSCreateHousePacket));
            RegisterPacket(0x058, 1, typeof(CSDecorateHousePacket));
            RegisterPacket(0x059, 1, typeof(CSChangeHouseNamePacket));
            RegisterPacket(0x05a, 1, typeof(CSChangeHousePermissionPacket));
            //              RegisterPacket(0x05b, 1, typeof(CSChangeHousePayPacket)); TODO: this packet is not in the offsets
            RegisterPacket(0x05c, 1, typeof(CSRequestHouseTaxPacket));
            // 0x5c unk packet
            RegisterPacket(0x05d, 1, typeof(CSAllowHousingRecoverPacket));
            RegisterPacket(0x05e, 1, typeof(CSSellHousePacket));
            RegisterPacket(0x05f, 1, typeof(CSSellHouseCancelPacket));
            RegisterPacket(0x060, 1, typeof(CSBuyHousePacket));
            RegisterPacket(0x061, 1, typeof(CSJoinUserChatChannelPacket));
            RegisterPacket(0x062, 1, typeof(CSLeaveChatChannelPacket));
            RegisterPacket(0x063, 1, typeof(CSSendChatMessagePacket));
            RegisterPacket(0x064, 1, typeof(CSConsoleCmdUsedPacket));
            RegisterPacket(0x065, 1, typeof(CSInteractNPCPacket));
            RegisterPacket(0x066, 1, typeof(CSInteractNPCEndPacket));
            RegisterPacket(0x067, 1, typeof(CSBoardingTransferPacket));
            RegisterPacket(0x068, 1, typeof(CSStartInteractionPacket));
            RegisterPacket(0x06b, 1, typeof(CSSelectInteractionExPacket));
            RegisterPacket(0x06c, 1, typeof(CSCofferInteractionPacket));
            RegisterPacket(0x06e, 1, typeof(CSCriminalLockedPacket));
            RegisterPacket(0x06f, 1, typeof(CSReplyImprisonOrTrialPacket));
            RegisterPacket(0x070, 1, typeof(CSSkipFinalStatementPacket));
            RegisterPacket(0x071, 1, typeof(CSReplyInviteJuryPacket));
            RegisterPacket(0x072, 1, typeof(CSJurySummonedPacket));
            RegisterPacket(0x073, 1, typeof(CSJuryEndTestimonyPacket));
            RegisterPacket(0x074, 1, typeof(CSCancelTrialPacket));
            RegisterPacket(0x075, 1, typeof(CSJuryVerdictPacket));
            RegisterPacket(0x076, 1, typeof(CSReportCrimePacket));
            RegisterPacket(0x077, 1, typeof(CSJoinTrialAudiencePacket));
            RegisterPacket(0x078, 1, typeof(CSLeaveTrialAudiencePacket));
            RegisterPacket(0x079, 1, typeof(CSRequestJuryWaitingNumberPacket));
            RegisterPacket(0x07a, 1, typeof(CSInviteToTeamPacket));
            RegisterPacket(0x07b, 1, typeof(CSInviteAreaToTeamPacket));
            RegisterPacket(0x07c, 1, typeof(CSReplyToJoinTeamPacket));
            RegisterPacket(0x07d, 1, typeof(CSLeaveTeamPacket));
            RegisterPacket(0x07e, 1, typeof(CSKickTeamMemberPacket));
            RegisterPacket(0x07f, 1, typeof(CSMakeTeamOwnerPacket));
            //            RegisterPacket(0x07e, 1, typeof(CSSetTeamOfficerPacket)); TODO: this packet is not in the offsets 
            RegisterPacket(0x080, 1, typeof(CSConvertToRaidTeamPacket));
            RegisterPacket(0x081, 1, typeof(CSMoveTeamMemberPacket));
            RegisterPacket(0x083, 1, typeof(CSChangeLootingRulePacket));
            RegisterPacket(0x084, 1, typeof(CSDismissTeamPacket));
            RegisterPacket(0x085, 1, typeof(CSSetTeamMemberRolePacket));
            RegisterPacket(0x086, 1, typeof(CSSetOverHeadMarkerPacket));
            RegisterPacket(0x087, 1, typeof(CSSetPingPosPacket));
            RegisterPacket(0x088, 1, typeof(CSAskRiskyTeamActionPacket));
            RegisterPacket(0x089, 1, typeof(CSMoveUnitPacket));
            RegisterPacket(0x08a, 1, typeof(CSSkillControllerStatePacket));
            RegisterPacket(0x08b, 1, typeof(CSCreateSkillControllerPacket));

            //            RegisterPacket(0x08d, 1, typeof(CSChangeItemLookPacket)); TODO: this packet is not in the offsets 
            RegisterPacket(0x08e, 1, typeof(CSLootOpenBagPacket));
            RegisterPacket(0x08f, 1, typeof(CSLootItemTookPacket));

            RegisterPacket(0x090, 1, typeof(CSLootCloseBagPacket));
            RegisterPacket(0x091, 1, typeof(CSLootDicePacket));
            RegisterPacket(0x092, 1, typeof(CSLearnSkillPacket));
            RegisterPacket(0x093, 1, typeof(CSLearnBuffPacket));
            RegisterPacket(0x094, 1, typeof(CSResetSkillsPacket));
            RegisterPacket(0x096, 1, typeof(CSSwapAbilityPacket));
            RegisterPacket(0x098, 1, typeof(CSSendMailPacket));
            RegisterPacket(0x09a, 1, typeof(CSListMailPacket));
            RegisterPacket(0x09b, 1, typeof(CSListMailContinuePacket));
            RegisterPacket(0x09c, 1, typeof(CSReadMailPacket));
            RegisterPacket(0x09d, 1, typeof(CSTakeAttachmentItemPacket));
            RegisterPacket(0x09e, 1, typeof(CSTakeAttachmentMoneyPacket));
            // 0x9f unk packet
            RegisterPacket(0x0a0, 1, typeof(CSPayChargeMoneyPacket));
            RegisterPacket(0x0a1, 1, typeof(CSDeleteMailPacket));
            RegisterPacket(0x0a3, 1, typeof(CSReportSpamPacket));
            //            RegisterPacket(0x0a1, 1, typeof(CSReturnMailPacket)); TODO: this packet is not in the offsets 
            RegisterPacket(0x0a4, 1, typeof(CSRemoveMatePacket));
            RegisterPacket(0x0a5, 1, typeof(CSChangeMateTargetPacket));
            RegisterPacket(0x0a6, 1, typeof(CSChangeMateNamePacket));
            RegisterPacket(0x0a7, 1, typeof(CSMountMatePacket));
            RegisterPacket(0x0a8, 1, typeof(CSUnMountMatePacket));
            RegisterPacket(0x0a9, 1, typeof(CSChangeMateEquipmentPacket));
            RegisterPacket(0x0aa, 1, typeof(CSChangeMateUserStatePacket));
            // 0xab unk packet
            // 0xac unk packet
            RegisterPacket(0x0ad, 1, typeof(CSExpressEmotionPacket));
            RegisterPacket(0x0ae, 1, typeof(CSBuyItemsPacket));
            RegisterPacket(0x0af, 1, typeof(CSBuyCoinItemPacket));
            RegisterPacket(0x0b0, 1, typeof(CSSellItemsPacket));
            RegisterPacket(0x0b1, 1, typeof(CSListSoldItemPacket));
            RegisterPacket(0x0b2, 1, typeof(CSBuyPriestBuffPacket));
            RegisterPacket(0x0b3, 1, typeof(CSUseTeleportPacket));
            RegisterPacket(0x0b4, 1, typeof(CSTeleportEndedPacket));
            RegisterPacket(0x0b5, 1, typeof(CSRepairPetItemsPacket));
            RegisterPacket(0x0b6, 1, typeof(CSUpdateActionSlotPacket));
            RegisterPacket(0x0b7, 1, typeof(CSAuctionPostPacket));
            RegisterPacket(0x0b8, 1, typeof(CSAuctionSearchPacket));
            RegisterPacket(0x0b9, 1, typeof(CSBidAuctionPacket));
            RegisterPacket(0x0ba, 1, typeof(CSCancelAuctionPacket));
            RegisterPacket(0x0bb, 1, typeof(CSAuctionMyBidListPacket));
            RegisterPacket(0x0bc, 1, typeof(CSAuctionLowestPricePacket));
            RegisterPacket(0x0bd, 1, typeof(CSRollDicePacket));
            //0xbf CSRequestNpcSpawnerList


            //0xc8 CSRemoveAllFieldSlaves
            //0xc9 CSAddFieldSlave
            RegisterPacket(0x0cb, 1, typeof(CSHangPacket));
            RegisterPacket(0x0cc, 1, typeof(CSUnhangPacket));

            RegisterPacket(0x0ce, 1, typeof(CSCompletedCinemaPacket));
            RegisterPacket(0x0cf, 1, typeof(CSStartedCinemaPacket));
            //0xd0 CSRequestPermissionToPlayCinemaForDirectingMode
            //0xd1 CSEditorRemoveGimmickPacket
            //0xd2 CSEditorAddGimmickPacket
            //0xd3 CSInteractGimmickPacket
            //0xd4 CSWorldRayCastingPacket
            RegisterPacket(0x0d5, 1, typeof(CSStartQuestContextPacket));
            RegisterPacket(0x0d6, 1, typeof(CSCompleteQuestContextPacket));
            RegisterPacket(0x0d7, 1, typeof(CSDropQuestContextPacket));
            //            RegisterPacket(0x0d4, 1, typeof(CSResetQuestContextPacket)); TODO: this packet is not in the offsets 
            //            RegisterPacket(0x0d5, 1, typeof(CSAcceptCheatQuestContextPacket)); TODO: this packet is not in the offsets 
            RegisterPacket(0x0da, 1, typeof(CSQuestTalkMadePacket));
            RegisterPacket(0x0db, 1, typeof(CSQuestStartWithPacket));
            RegisterPacket(0x0dd, 1, typeof(CSTryQuestCompleteAsLetItDonePacket));
            RegisterPacket(0x0de, 1, typeof(CSUsePortalPacket));
            RegisterPacket(0x0df, 1, typeof(CSDeletePortalPacket));
            RegisterPacket(0x0e0, 1, typeof(CSInstanceLoadedPacket));
            RegisterPacket(0x0e1, 1, typeof(CSApplyToInstantGamePacket));
            RegisterPacket(0x0e2, 1, typeof(CSCancelInstantGamePacket));
            RegisterPacket(0x0e3, 1, typeof(CSJoinInstantGamePacket));
            RegisterPacket(0x0e4, 1, typeof(CSEnteredInstantGameWorldPacket));
            RegisterPacket(0x0e5, 1, typeof(CSLeaveInstantGamePacket));
            RegisterPacket(0x0e6, 1, typeof(CSCreateDoodadPacket));
            //            RegisterPacket(0x0e3, 1, typeof(CSSaveDoodadUccStringPacket)); TODO: this packet is not in the offsets 
            RegisterPacket(0x0e7, 1, typeof(CSNaviTeleportPacket));
            RegisterPacket(0x0e8, 1, typeof(CSNaviOpenPortalPacket));
            RegisterPacket(0x0e9, 1, typeof(CSChangeDoodadPhasePacket));
            RegisterPacket(0x0ea, 1, typeof(CSNaviOpenBountyPacket));
            RegisterPacket(0x0eb, 1, typeof(CSChangeDoodadDataPacket));
            RegisterPacket(0x0ec, 1, typeof(CSStartTradePacket));
            RegisterPacket(0x0ed, 1, typeof(CSCanStartTradePacket));
            RegisterPacket(0x0ee, 1, typeof(CSCannotStartTradePacket));
            RegisterPacket(0x0ef, 1, typeof(CSCancelTradePacket));
            RegisterPacket(0x0f0, 1, typeof(CSPutupTradeItemPacket));
            RegisterPacket(0x0f1, 1, typeof(CSPutupTradeMoneyPacket));
            RegisterPacket(0x0f2, 1, typeof(CSTakedownTradeItemPacket));
            RegisterPacket(0x0f3, 1, typeof(CSTradeLockPacket));
            RegisterPacket(0x0f4, 1, typeof(CSTradeOkPacket));
            RegisterPacket(0x0f5, 1, typeof(CSSaveTutorialPacket));
            RegisterPacket(0x0f6, 1, typeof(CSSetLogicDoodadPacket));
            RegisterPacket(0x0f7, 1, typeof(CSCleanupLogicLinkPacket));
            RegisterPacket(0x0f8, 1, typeof(CSExecuteCraft));
            RegisterPacket(0x0f9, 1, typeof(CSChangeAppellationPacket));
            RegisterPacket(0x0fc, 1, typeof(CSCreateShipyardPacket));
            RegisterPacket(0x0fd, 1, typeof(CSRestartMainQuestPacket));
            RegisterPacket(0x0fe, 1, typeof(CSSetLpManageCharacterPacket));
            RegisterPacket(0x0ff, 1, typeof(CSUpgradeExpertLimitPacket));
            RegisterPacket(0x100, 1, typeof(CSDowngradeExpertLimitPacket));
            RegisterPacket(0x101, 1, typeof(CSExpandExpertPacket));
            //            RegisterPacket(0x100, 1, typeof(CSSearchListPacket)); TODO: this packet is not in the offsets 
            RegisterPacket(0x104, 1, typeof(CSAddFriendPacket));
            RegisterPacket(0x105, 1, typeof(CSDeleteFriendPacket));
            RegisterPacket(0x106, 1, typeof(CSCharDetailPacket));
            RegisterPacket(0x107, 1, typeof(CSAddBlockedUserPacket));
            RegisterPacket(0x108, 1, typeof(CSDeleteBlockedUserPacket));
            RegisterPacket(0x112, 1, typeof(CSNotifySubZonePacket));
            RegisterPacket(0x115, 1, typeof(CSResturnAddrsPacket));
            RegisterPacket(0x117, 1, typeof(CSRequestUIDataPacket));
            RegisterPacket(0x118, 1, typeof(CSSaveUIDataPacket));
            RegisterPacket(0x119, 1, typeof(CSBroadcastVisualOptionPacket));
            RegisterPacket(0x11a, 1, typeof(CSRestrictCheckPacket));
            RegisterPacket(0x11b, 1, typeof(CSICSMenuListPacket));
            RegisterPacket(0x11c, 1, typeof(CSICSGoodsListPacket));
            RegisterPacket(0x11d, 1, typeof(CSICSBuyGoodPacket));
            RegisterPacket(0x11e, 1, typeof(CSICSMoneyRequestPacket));
            // 0x12e CSEnterBeautySalonPacket
            RegisterPacket(0x12F, 1, typeof(CSRankCharacterPacket));
            RegisterPacket(0x125, 1, typeof(CSRequestSecondPasswordKeyTablesPacket));
            // 0x130 CSRankSnapshotPacket
            // 0x131 unk packet
            RegisterPacket(0x132, 1, typeof(CSIdleStatusPacket));
            // 0x133 CSChangeAutoUseAAPointPacket
            RegisterPacket(0x134, 1, typeof(CSThisTimeUnpackItemPacket));
            RegisterPacket(0x135, 1, typeof(CSPremiumServiceBuyPacket));
            RegisterPacket(0x136, 1, typeof(CSPremiumServiceListPacket));
            // 0x137 CSICSBuyAAPointPacket
            // 0x138 CSRequestTencentFatigueInfoPacket
            // 0x139 CSTakeAllAttachmentItemPacket
            // 0x13a unk packet
            // 0x13b unk packet
            RegisterPacket(0x13c, 1, typeof(CSPremiumServieceMsgPacket));
            // 0x13d unk packet
            // 0x13e unk packet
            // 0x13f unk packet
            RegisterPacket(0x140, 1, typeof(CSSetupSecondPassword));
            // 0x141 unk packet
            // 0x142 unk packet

            // Proxy
            RegisterPacket(0x000, 2, typeof(ChangeStatePacket));
            RegisterPacket(0x001, 2, typeof(FinishStatePacket));
            RegisterPacket(0x002, 2, typeof(FlushMsgsPacket));
            RegisterPacket(0x004, 2, typeof(UpdatePhysicsTimePacket));
            RegisterPacket(0x005, 2, typeof(BeginUpdateObjPacket));
            RegisterPacket(0x006, 2, typeof(EndUpdateObjPacket));
            RegisterPacket(0x007, 2, typeof(BeginBindObjPacket));
            RegisterPacket(0x008, 2, typeof(EndBindObjPacket));
            RegisterPacket(0x009, 2, typeof(UnbindPredictedObjPacket));
            RegisterPacket(0x00A, 2, typeof(RemoveStaticObjPacket));
            RegisterPacket(0x00B, 2, typeof(VoiceDataPacket));
            RegisterPacket(0x00C, 2, typeof(UpdateAspectPacket));
            RegisterPacket(0x00D, 2, typeof(SetAspectProfilePacket));
            RegisterPacket(0x00E, 2, typeof(PartialAspectPacket));
            RegisterPacket(0x00F, 2, typeof(SetGameTypePacket));
            RegisterPacket(0x010, 2, typeof(ChangeCVarPacket));
            RegisterPacket(0x011, 2, typeof(EntityClassRegistrationPacket));
            RegisterPacket(0x012, 2, typeof(PingPacket));
            RegisterPacket(0x013, 2, typeof(PongPacket));
            RegisterPacket(0x014, 2, typeof(PacketSeqChange));
            RegisterPacket(0x015, 2, typeof(FastPingPacket));
            RegisterPacket(0x016, 2, typeof(FastPongPacket));
        }

        public void Start()
        {
            var config = AppConfiguration.Instance.Network;
            _server = new Server(new IPEndPoint(config.Host.Equals("*") ? IPAddress.Any : IPAddress.Parse(config.Host), config.Port), 10);
            _server.SetHandler(_handler);
            _server.Start();

            _log.Info("Network started");
        }

        public void Stop()
        {
            if (_server.IsStarted)
                _server.Stop();

            _log.Info("Network stoped");
        }

        private void RegisterPacket(uint type, byte level, Type classType)
        {
            _handler.RegisterPacket(type, level, classType);
        }
    }
}

using System;
using System.Net;
using AAEmu.Commons.Network.Core;
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
            RegisterPacket(CSOffsets.X2EnterWorldPacket, 1, typeof(X2EnterWorldPacket)); // level = 1
            // double _01_&_05_
            // пакет для дешифрации
            RegisterPacket(CSOffsets.CSAesXorKeyPacket, 1, typeof(CSAesXorKeyPacket));         // level = 1
            RegisterPacket(CSOffsets.CSAesXorKey_05_Packet, 5, typeof(CSAesXorKey_05_Packet)); // level = 5
            //
            RegisterPacket(CSOffsets.CSResturnAddrsPacket, 1, typeof(CSResturnAddrsPacket)); // level = 1
            RegisterPacket(CSOffsets.CSResturnAddrs_05_Packet, 5, typeof(CSResturnAddrs_05_Packet)); // level = 5
            RegisterPacket(CSOffsets.CSHgResponsePacket, 1, typeof(CSHgResponsePacket)); // level = 1
            RegisterPacket(CSOffsets.CSHgResponse_05_Packet, 5, typeof(CSHgResponse_05_Packet)); // level = 5

            RegisterPacket(CSOffsets.CSLeaveWorldPacket, 5, typeof(CSLeaveWorldPacket));
            RegisterPacket(CSOffsets.CSCancelLeaveWorldPacket, 5, typeof(CSCancelLeaveWorldPacket));
            RegisterPacket(CSOffsets.CSCreateExpeditionPacket, 5, typeof(CSCreateExpeditionPacket));
            //RegisterPacket(0x005, 5, typeof(CSChangeExpeditionSponsorPacket)); // TODO: this packet seems like it has been removed.
            RegisterPacket(CSOffsets.CSChangeExpeditionRolePolicyPacket, 5, typeof(CSChangeExpeditionRolePolicyPacket));
            RegisterPacket(CSOffsets.CSChangeExpeditionMemberRolePacket, 5, typeof(CSChangeExpeditionMemberRolePacket));
            RegisterPacket(CSOffsets.CSChangeExpeditionOwnerPacket, 5, typeof(CSChangeExpeditionOwnerPacket));
            RegisterPacket(CSOffsets.CSRenameExpeditionPacket, 5, typeof(CSRenameExpeditionPacket));
            RegisterPacket(CSOffsets.CSDismissExpeditionPacket, 5, typeof(CSDismissExpeditionPacket));
            RegisterPacket(CSOffsets.CSInviteToExpeditionPacket, 5, typeof(CSInviteToExpeditionPacket));
            RegisterPacket(CSOffsets.CSReplyExpeditionInvitationPacket, 5, typeof(CSReplyExpeditionInvitationPacket));
            RegisterPacket(CSOffsets.CSLeaveExpeditionPacket, 5, typeof(CSLeaveExpeditionPacket));
            RegisterPacket(CSOffsets.CSKickFromExpeditionPacket, 5, typeof(CSKickFromExpeditionPacket));
            // 0x10 unk packet
            RegisterPacket(CSOffsets.CSUpdateDominionTaxRatePacket, 5, typeof(CSUpdateDominionTaxRatePacket));
            RegisterPacket(CSOffsets.CSFactionImmigrationInvitePacket, 5, typeof(CSFactionImmigrationInvitePacket));
            RegisterPacket(CSOffsets.CSFactionImmigrationInviteReplyPacket, 5, typeof(CSFactionImmigrationInviteReplyPacket));
            RegisterPacket(CSOffsets.CSFactionImmigrateToOriginPacket, 5, typeof(CSFactionImmigrateToOriginPacket));
            RegisterPacket(CSOffsets.CSFactionKickToOriginPacket, 5, typeof(CSFactionKickToOriginPacket));
            RegisterPacket(CSOffsets.CSFactionDeclareHostilePacket, 5, typeof(CSFactionDeclareHostilePacket));
            RegisterPacket(CSOffsets.CSFamilyInviteMemberPacket, 5, typeof(CSFamilyInviteMemberPacket));
            RegisterPacket(CSOffsets.CSFamilyReplyInvitationPacket, 5, typeof(CSFamilyReplyInvitationPacket));
            RegisterPacket(CSOffsets.CSFamilyLeavePacket, 5, typeof(CSFamilyLeavePacket));
            RegisterPacket(CSOffsets.CSFamilyKickPacket, 5, typeof(CSFamilyKickPacket));
            RegisterPacket(CSOffsets.CSFamilyChangeTitlePacket, 5, typeof(CSFamilyChangeTitlePacket));
            RegisterPacket(CSOffsets.CSFamilyChangeOwnerPacket, 5, typeof(CSFamilyChangeOwnerPacket));
            RegisterPacket(CSOffsets.CSListCharacterPacket, 5, typeof(CSListCharacterPacket));
            RegisterPacket(CSOffsets.CSRefreshInCharacterListPacket, 5, typeof(CSRefreshInCharacterListPacket));
            RegisterPacket(CSOffsets.CSCreateCharacterPacket, 5, typeof(CSCreateCharacterPacket));
            RegisterPacket(CSOffsets.CSEditCharacterPacket, 5, typeof(CSEditCharacterPacket));
            RegisterPacket(CSOffsets.CSDeleteCharacterPacket, 5, typeof(CSDeleteCharacterPacket));
            RegisterPacket(CSOffsets.CSSelectCharacterPacket, 5, typeof(CSSelectCharacterPacket));
            RegisterPacket(CSOffsets.CSSpawnCharacterPacket, 5, typeof(CSSpawnCharacterPacket));
            RegisterPacket(CSOffsets.CSCancelCharacterDeletePacket, 5, typeof(CSCancelCharacterDeletePacket));
            RegisterPacket(CSOffsets.CSNotifyInGamePacket, 5, typeof(CSNotifyInGamePacket));
            RegisterPacket(CSOffsets.CSNotifyInGameCompletedPacket, 5, typeof(CSNotifyInGameCompletedPacket));
            RegisterPacket(CSOffsets.CSEditorGameModePacket, 5, typeof(CSEditorGameModePacket));
            RegisterPacket(CSOffsets.CSChangeTargetPacket, 5, typeof(CSChangeTargetPacket));
            RegisterPacket(CSOffsets.CSRequestCharBriefPacket, 5, typeof(CSRequestCharBriefPacket));
            RegisterPacket(CSOffsets.CSSpawnSlavePacket, 5, typeof(CSSpawnSlavePacket));
            RegisterPacket(CSOffsets.CSDespawnSlavePacket, 5, typeof(CSDespawnSlavePacket));
            RegisterPacket(CSOffsets.CSDestroySlavePacket, 5, typeof(CSDestroySlavePacket));
            RegisterPacket(CSOffsets.CSBindSlavePacket, 5, typeof(CSBindSlavePacket));
            RegisterPacket(CSOffsets.CSDiscardSlavePacket, 5, typeof(CSDiscardSlavePacket));
            //RegisterPacket(0x031, 5, typeof(CSChangeSlaveTargetPacket)); TODO: the this packet is not in the offsets
            RegisterPacket(CSOffsets.CSChangeSlaveNamePacket, 5, typeof(CSChangeSlaveNamePacket));
            RegisterPacket(CSOffsets.CSRepairSlaveItemsPacket, 5, typeof(CSRepairSlaveItemsPacket));
            RegisterPacket(CSOffsets.CSTurretStatePacket, 5, typeof(CSTurretStatePacket));
            RegisterPacket(CSOffsets.CSChangeSlaveEquipmentPacket, 5, typeof(CSChangeSlaveEquipmentPacket));
            RegisterPacket(CSOffsets.CSDestroyItemPacket, 5, typeof(CSDestroyItemPacket));
            RegisterPacket(CSOffsets.CSSplitBagItemPacket, 5, typeof(CSSplitBagItemPacket));
            RegisterPacket(CSOffsets.CSSwapItemsPacket, 5, typeof(CSSwapItemsPacket));
            RegisterPacket(CSOffsets.CSRepairSingleEquipmentPacket, 5, typeof(CSRepairSingleEquipmentPacket));
            RegisterPacket(CSOffsets.CSRepairAllEquipmentsPacket, 5, typeof(CSRepairAllEquipmentsPacket));
            RegisterPacket(CSOffsets.CSSplitCofferItemPacket, 5, typeof(CSSplitCofferItemPacket));
            RegisterPacket(CSOffsets.CSSwapCofferItemsPacket, 5, typeof(CSSwapCofferItemsPacket));
            RegisterPacket(CSOffsets.CSExpandSlotsPacket, 5, typeof(CSExpandSlotsPacket));
            RegisterPacket(CSOffsets.CSSellBackpackGoodsPacket, 5, typeof(CSSellBackpackGoodsPacket));
            RegisterPacket(CSOffsets.CSSpecialtyRatioPacket, 5, typeof(CSSpecialtyRatioPacket));
            RegisterPacket(CSOffsets.CSListSpecialtyGoodsPacket, 5, typeof(CSListSpecialtyGoodsPacket));
            //RegisterPacket(0x043, 5, typeof(CSBuySpecialtyItemPacket)); TODO: this packet is not in the offsets
            //RegisterPacket(0x044, 5, typeof(CSSpecialtyRecordLoadPacket)); TODO: this packet is not in the offsets
            RegisterPacket(CSOffsets.CSDepositMoneyPacket, 5, typeof(CSDepositMoneyPacket));
            RegisterPacket(CSOffsets.CSWithdrawMoneyPacket, 5, typeof(CSWithdrawMoneyPacket));
            RegisterPacket(CSOffsets.CSConvertItemLookPacket, 5, typeof(CSConvertItemLookPacket));
            RegisterPacket(CSOffsets.CSItemSecurePacket, 5, typeof(CSItemSecurePacket));
            RegisterPacket(CSOffsets.CSItemUnsecurePacket, 5, typeof(CSItemUnsecurePacket));
            RegisterPacket(CSOffsets.CSEquipmentsSecurePacket, 5, typeof(CSEquipmentsSecurePacket));
            RegisterPacket(CSOffsets.CSEquipmentsUnsecurePacket, 5, typeof(CSEquipmentsUnsecurePacket));
            RegisterPacket(CSOffsets.CSResurrectCharacterPacket, 5, typeof(CSResurrectCharacterPacket));
            RegisterPacket(CSOffsets.CSSetForceAttackPacket, 5, typeof(CSSetForceAttackPacket));
            RegisterPacket(CSOffsets.CSChallengeDuelPacket, 5, typeof(CSChallengeDuelPacket));
            RegisterPacket(CSOffsets.CSStartDuelPacket, 5, typeof(CSStartDuelPacket));
            RegisterPacket(CSOffsets.CSStartSkillPacket, 5, typeof(CSStartSkillPacket));
            RegisterPacket(CSOffsets.CSStopCastingPacket, 5, typeof(CSStopCastingPacket));
            RegisterPacket(CSOffsets.CSRemoveBuffPacket, 5, typeof(CSRemoveBuffPacket));
            RegisterPacket(CSOffsets.CSConstructHouseTaxPacket, 5, typeof(CSConstructHouseTaxPacket));
            RegisterPacket(CSOffsets.CSCreateHousePacket, 5, typeof(CSCreateHousePacket));
            RegisterPacket(CSOffsets.CSDecorateHousePacket, 5, typeof(CSDecorateHousePacket));
            RegisterPacket(CSOffsets.CSChangeHouseNamePacket, 5, typeof(CSChangeHouseNamePacket));
            RegisterPacket(CSOffsets.CSChangeHousePermissionPacket, 5, typeof(CSChangeHousePermissionPacket));
            //RegisterPacket(0x05b, 5, typeof(CSChangeHousePayPacket)); TODO: this packet is not in the offsets
            RegisterPacket(CSOffsets.CSRequestHouseTaxPacket, 5, typeof(CSRequestHouseTaxPacket));
            // 0x5c unk packet
            RegisterPacket(CSOffsets.CSAllowHousingRecoverPacket, 5, typeof(CSAllowHousingRecoverPacket));
            RegisterPacket(CSOffsets.CSSellHousePacket, 5, typeof(CSSellHousePacket));
            RegisterPacket(CSOffsets.CSSellHouseCancelPacket, 5, typeof(CSSellHouseCancelPacket));
            RegisterPacket(CSOffsets.CSBuyHousePacket, 5, typeof(CSBuyHousePacket));
            RegisterPacket(CSOffsets.CSJoinUserChatChannelPacket, 5, typeof(CSJoinUserChatChannelPacket));
            RegisterPacket(CSOffsets.CSLeaveChatChannelPacket, 5, typeof(CSLeaveChatChannelPacket));
            RegisterPacket(CSOffsets.CSSendChatMessagePacket, 5, typeof(CSSendChatMessagePacket));
            RegisterPacket(CSOffsets.CSConsoleCmdUsedPacket, 5, typeof(CSConsoleCmdUsedPacket));
            RegisterPacket(CSOffsets.CSInteractNPCPacket, 5, typeof(CSInteractNPCPacket));
            RegisterPacket(CSOffsets.CSInteractNPCEndPacket, 5, typeof(CSInteractNPCEndPacket));
            RegisterPacket(CSOffsets.CSBoardingTransferPacket, 5, typeof(CSBoardingTransferPacket));
            RegisterPacket(CSOffsets.CSStartInteractionPacket, 5, typeof(CSStartInteractionPacket));
            RegisterPacket(CSOffsets.CSSelectInteractionExPacket, 5, typeof(CSSelectInteractionExPacket));
            RegisterPacket(CSOffsets.CSCofferInteractionPacket, 5, typeof(CSCofferInteractionPacket));
            RegisterPacket(CSOffsets.CSCriminalLockedPacket, 5, typeof(CSCriminalLockedPacket));
            RegisterPacket(CSOffsets.CSReplyImprisonOrTrialPacket, 5, typeof(CSReplyImprisonOrTrialPacket));
            RegisterPacket(CSOffsets.CSSkipFinalStatementPacket, 5, typeof(CSSkipFinalStatementPacket));
            RegisterPacket(CSOffsets.CSReplyInviteJuryPacket, 5, typeof(CSReplyInviteJuryPacket));
            RegisterPacket(CSOffsets.CSJurySummonedPacket, 5, typeof(CSJurySummonedPacket));
            RegisterPacket(CSOffsets.CSJuryEndTestimonyPacket, 5, typeof(CSJuryEndTestimonyPacket));
            RegisterPacket(CSOffsets.CSCancelTrialPacket, 5, typeof(CSCancelTrialPacket));
            RegisterPacket(CSOffsets.CSJuryVerdictPacket, 5, typeof(CSJuryVerdictPacket));
            RegisterPacket(CSOffsets.CSReportCrimePacket, 5, typeof(CSReportCrimePacket));
            RegisterPacket(CSOffsets.CSJoinTrialAudiencePacket, 5, typeof(CSJoinTrialAudiencePacket));
            RegisterPacket(CSOffsets.CSLeaveTrialAudiencePacket, 5, typeof(CSLeaveTrialAudiencePacket));
            RegisterPacket(CSOffsets.CSRequestJuryWaitingNumberPacket, 5, typeof(CSRequestJuryWaitingNumberPacket));
            RegisterPacket(CSOffsets.CSInviteToTeamPacket, 5, typeof(CSInviteToTeamPacket));
            RegisterPacket(CSOffsets.CSInviteAreaToTeamPacket, 5, typeof(CSInviteAreaToTeamPacket));
            RegisterPacket(CSOffsets.CSReplyToJoinTeamPacket, 5, typeof(CSReplyToJoinTeamPacket));
            RegisterPacket(CSOffsets.CSLeaveTeamPacket, 5, typeof(CSLeaveTeamPacket));
            RegisterPacket(CSOffsets.CSKickTeamMemberPacket, 5, typeof(CSKickTeamMemberPacket));
            RegisterPacket(CSOffsets.CSMakeTeamOwnerPacket, 5, typeof(CSMakeTeamOwnerPacket));
            //RegisterPacket(0x07e, 5, typeof(CSSetTeamOfficerPacket)); TODO: this packet is not in the offsets 
            RegisterPacket(CSOffsets.CSConvertToRaidTeamPacket, 5, typeof(CSConvertToRaidTeamPacket));
            RegisterPacket(CSOffsets.CSMoveTeamMemberPacket, 5, typeof(CSMoveTeamMemberPacket));
            RegisterPacket(CSOffsets.CSChangeLootingRulePacket, 5, typeof(CSChangeLootingRulePacket));
            RegisterPacket(CSOffsets.CSDismissTeamPacket, 5, typeof(CSDismissTeamPacket));
            RegisterPacket(CSOffsets.CSSetTeamMemberRolePacket, 5, typeof(CSSetTeamMemberRolePacket));
            RegisterPacket(CSOffsets.CSSetOverHeadMarkerPacket, 5, typeof(CSSetOverHeadMarkerPacket));
            RegisterPacket(CSOffsets.CSSetPingPosPacket, 5, typeof(CSSetPingPosPacket));
            RegisterPacket(CSOffsets.CSAskRiskyTeamActionPacket, 5, typeof(CSAskRiskyTeamActionPacket));
            RegisterPacket(CSOffsets.CSMoveUnitPacket, 5, typeof(CSMoveUnitPacket));
            RegisterPacket(CSOffsets.CSSkillControllerStatePacket, 5, typeof(CSSkillControllerStatePacket));
            RegisterPacket(CSOffsets.CSCreateSkillControllerPacket, 5, typeof(CSCreateSkillControllerPacket));
            RegisterPacket(CSOffsets.CSActiveWeaponChangedPacket, 5, typeof(CSActiveWeaponChangedPacket));
            //RegisterPacket(0x08d, 5, typeof(CSChangeItemLookPacket)); TODO: this packet is not in the offsets 
            RegisterPacket(CSOffsets.CSLootOpenBagPacket, 5, typeof(CSLootOpenBagPacket));
            RegisterPacket(CSOffsets.CSLootItemPacket, 5, typeof(CSLootItemPacket));
            RegisterPacket(CSOffsets.CSLootCloseBagPacket, 5, typeof(CSLootCloseBagPacket));
            RegisterPacket(CSOffsets.CSLootDicePacket, 5, typeof(CSLootDicePacket));
            RegisterPacket(CSOffsets.CSLearnSkillPacket, 5, typeof(CSLearnSkillPacket));
            RegisterPacket(CSOffsets.CSLearnBuffPacket, 5, typeof(CSLearnBuffPacket));
            RegisterPacket(CSOffsets.CSResetSkillsPacket, 5, typeof(CSResetSkillsPacket));
            RegisterPacket(CSOffsets.CSSwapAbilityPacket, 5, typeof(CSSwapAbilityPacket));
            RegisterPacket(CSOffsets.CSSendMailPacket, 5, typeof(CSSendMailPacket));
            RegisterPacket(CSOffsets.CSListMailPacket, 5, typeof(CSListMailPacket));
            RegisterPacket(CSOffsets.CSListMailContinuePacket, 5, typeof(CSListMailContinuePacket));
            RegisterPacket(CSOffsets.CSReadMailPacket, 5, typeof(CSReadMailPacket));
            RegisterPacket(CSOffsets.CSTakeAttachmentItemPacket, 5, typeof(CSTakeAttachmentItemPacket));
            RegisterPacket(CSOffsets.CSTakeAttachmentMoneyPacket, 5, typeof(CSTakeAttachmentMoneyPacket));
            // 0x9f unk packet
            RegisterPacket(CSOffsets.CSTakeAttachmentSequentiallyPacket, 5, typeof(CSTakeAttachmentSequentiallyPacket));
            RegisterPacket(CSOffsets.CSPayChargeMoneyPacket, 5, typeof(CSPayChargeMoneyPacket));
            RegisterPacket(CSOffsets.CSDeleteMailPacket, 5, typeof(CSDeleteMailPacket));
            RegisterPacket(CSOffsets.CSReportSpamPacket, 5, typeof(CSReportSpamPacket));
            //RegisterPacket(0x0a1, 5, typeof(CSReturnMailPacket)); TODO: this packet is not in the offsets 
            RegisterPacket(CSOffsets.CSRemoveMatePacket, 5, typeof(CSRemoveMatePacket));
            RegisterPacket(CSOffsets.CSChangeMateTargetPacket, 5, typeof(CSChangeMateTargetPacket));
            RegisterPacket(CSOffsets.CSChangeMateNamePacket, 5, typeof(CSChangeMateNamePacket));
            RegisterPacket(CSOffsets.CSMountMatePacket, 5, typeof(CSMountMatePacket));
            RegisterPacket(CSOffsets.CSUnMountMatePacket, 5, typeof(CSUnMountMatePacket));
            RegisterPacket(CSOffsets.CSChangeMateEquipmentPacket, 5, typeof(CSChangeMateEquipmentPacket));
            RegisterPacket(CSOffsets.CSChangeMateUserStatePacket, 5, typeof(CSChangeMateUserStatePacket));
            // 0xab unk packet
            // 0xac unk packet
            RegisterPacket(CSOffsets.CSExpressEmotionPacket, 5, typeof(CSExpressEmotionPacket));
            RegisterPacket(CSOffsets.CSBuyItemsPacket, 5, typeof(CSBuyItemsPacket));
            RegisterPacket(CSOffsets.CSBuyCoinItemPacket, 5, typeof(CSBuyCoinItemPacket));
            RegisterPacket(CSOffsets.CSSellItemsPacket, 5, typeof(CSSellItemsPacket));
            RegisterPacket(CSOffsets.CSListSoldItemPacket, 5, typeof(CSListSoldItemPacket));
            RegisterPacket(CSOffsets.CSBuyPriestBuffPacket, 5, typeof(CSBuyPriestBuffPacket));
            RegisterPacket(CSOffsets.CSUseTeleportPacket, 5, typeof(CSUseTeleportPacket));
            RegisterPacket(CSOffsets.CSTeleportEndedPacket, 5, typeof(CSTeleportEndedPacket));
            RegisterPacket(CSOffsets.CSRepairPetItemsPacket, 5, typeof(CSRepairPetItemsPacket));
            RegisterPacket(CSOffsets.CSUpdateActionSlotPacket, 5, typeof(CSUpdateActionSlotPacket));
            RegisterPacket(CSOffsets.CSAuctionPostPacket, 5, typeof(CSAuctionPostPacket));
            RegisterPacket(CSOffsets.CSAuctionSearchPacket, 5, typeof(CSAuctionSearchPacket));
            RegisterPacket(CSOffsets.CSBidAuctionPacket, 5, typeof(CSBidAuctionPacket));
            RegisterPacket(CSOffsets.CSCancelAuctionPacket, 5, typeof(CSCancelAuctionPacket));
            RegisterPacket(CSOffsets.CSAuctionMyBidListPacket, 5, typeof(CSAuctionMyBidListPacket));
            RegisterPacket(CSOffsets.CSAuctionLowestPricePacket, 5, typeof(CSAuctionLowestPricePacket));
            RegisterPacket(CSOffsets.CSRollDicePacket, 5, typeof(CSRollDicePacket));
            //0xbf CSRequestNpcSpawnerList
            //0xc8 CSRemoveAllFieldSlaves
            //0xc9 CSAddFieldSlave
            RegisterPacket(CSOffsets.CSHangPacket, 5, typeof(CSHangPacket));
            RegisterPacket(CSOffsets.CSUnhangPacket, 5, typeof(CSUnhangPacket));
            RegisterPacket(CSOffsets.CSUnbondDoodadPacket, 5, typeof(CSUnbondDoodadPacket));
            RegisterPacket(CSOffsets.CSCompletedCinemaPacket, 5, typeof(CSCompletedCinemaPacket));
            RegisterPacket(CSOffsets.CSStartedCinemaPacket, 5, typeof(CSStartedCinemaPacket));
            //0xd0 CSRequestPermissionToPlayCinemaForDirectingMode
            //0xd1 CSEditorRemoveGimmickPacket
            //0xd2 CSEditorAddGimmickPacket
            //0xd3 CSInteractGimmickPacket
            //0xd4 CSWorldRayCastingPacket
            RegisterPacket(CSOffsets.CSStartQuestContextPacket, 5, typeof(CSStartQuestContextPacket));
            RegisterPacket(CSOffsets.CSCompleteQuestContextPacket, 5, typeof(CSCompleteQuestContextPacket));
            RegisterPacket(CSOffsets.CSDropQuestContextPacket, 5, typeof(CSDropQuestContextPacket));
            //RegisterPacket(0x0d4, 5, typeof(CSResetQuestContextPacket)); TODO: this packet is not in the offsets 
            //RegisterPacket(0x0d5, 5, typeof(CSAcceptCheatQuestContextPacket)); TODO: this packet is not in the offsets 
            RegisterPacket(CSOffsets.CSQuestTalkMadePacket, 5, typeof(CSQuestTalkMadePacket));
            RegisterPacket(CSOffsets.CSQuestStartWithPacket, 5, typeof(CSQuestStartWithPacket));
            RegisterPacket(CSOffsets.CSTryQuestCompleteAsLetItDonePacket, 5, typeof(CSTryQuestCompleteAsLetItDonePacket));
            RegisterPacket(CSOffsets.CSUsePortalPacket, 5, typeof(CSUsePortalPacket));
            RegisterPacket(CSOffsets.CSDeletePortalPacket, 5, typeof(CSDeletePortalPacket));
            RegisterPacket(CSOffsets.CSInstanceLoadedPacket, 5, typeof(CSInstanceLoadedPacket));
            RegisterPacket(CSOffsets.CSApplyToInstantGamePacket, 5, typeof(CSApplyToInstantGamePacket));
            RegisterPacket(CSOffsets.CSCancelInstantGamePacket, 5, typeof(CSCancelInstantGamePacket));
            RegisterPacket(CSOffsets.CSJoinInstantGamePacket, 5, typeof(CSJoinInstantGamePacket));
            RegisterPacket(CSOffsets.CSEnteredInstantGameWorldPacket, 5, typeof(CSEnteredInstantGameWorldPacket));
            RegisterPacket(CSOffsets.CSLeaveInstantGamePacket, 5, typeof(CSLeaveInstantGamePacket));
            RegisterPacket(CSOffsets.CSCreateDoodadPacket, 5, typeof(CSCreateDoodadPacket));
            //RegisterPacket(0x0e3, 5, typeof(CSSaveDoodadUccStringPacket)); TODO: this packet is not in the offsets 
            RegisterPacket(CSOffsets.CSNaviTeleportPacket, 5, typeof(CSNaviTeleportPacket));
            RegisterPacket(CSOffsets.CSNaviOpenPortalPacket, 5, typeof(CSNaviOpenPortalPacket));
            RegisterPacket(CSOffsets.CSChangeDoodadPhasePacket, 5, typeof(CSChangeDoodadPhasePacket));
            RegisterPacket(CSOffsets.CSNaviOpenBountyPacket, 5, typeof(CSNaviOpenBountyPacket));
            RegisterPacket(CSOffsets.CSChangeDoodadDataPacket, 5, typeof(CSChangeDoodadDataPacket));
            RegisterPacket(CSOffsets.CSStartTradePacket, 5, typeof(CSStartTradePacket));
            RegisterPacket(CSOffsets.CSCanStartTradePacket, 5, typeof(CSCanStartTradePacket));
            RegisterPacket(CSOffsets.CSCannotStartTradePacket, 5, typeof(CSCannotStartTradePacket));
            RegisterPacket(CSOffsets.CSCancelTradePacket, 5, typeof(CSCancelTradePacket));
            RegisterPacket(CSOffsets.CSPutupTradeItemPacket, 5, typeof(CSPutupTradeItemPacket));
            RegisterPacket(CSOffsets.CSPutupTradeMoneyPacket, 5, typeof(CSPutupTradeMoneyPacket));
            RegisterPacket(CSOffsets.CSTakedownTradeItemPacket, 5, typeof(CSTakedownTradeItemPacket));
            RegisterPacket(CSOffsets.CSTradeLockPacket, 5, typeof(CSTradeLockPacket));
            RegisterPacket(CSOffsets.CSTradeOkPacket, 5, typeof(CSTradeOkPacket));
            RegisterPacket(CSOffsets.CSSaveTutorialPacket, 5, typeof(CSSaveTutorialPacket));
            RegisterPacket(CSOffsets.CSSetLogicDoodadPacket, 5, typeof(CSSetLogicDoodadPacket));
            RegisterPacket(CSOffsets.CSCleanupLogicLinkPacket, 5, typeof(CSCleanupLogicLinkPacket));
            RegisterPacket(CSOffsets.CSExecuteCraftPacket, 5, typeof(CSExecuteCraftPacket));
            RegisterPacket(CSOffsets.CSChangeAppellationPacket, 5, typeof(CSChangeAppellationPacket));
            RegisterPacket(CSOffsets.CSCreateShipyardPacket, 5, typeof(CSCreateShipyardPacket));
            RegisterPacket(CSOffsets.CSRestartMainQuestPacket, 5, typeof(CSRestartMainQuestPacket));
            RegisterPacket(CSOffsets.CSSetLpManageCharacterPacket, 5, typeof(CSSetLpManageCharacterPacket));
            RegisterPacket(CSOffsets.CSUpgradeExpertLimitPacket, 5, typeof(CSUpgradeExpertLimitPacket));
            RegisterPacket(CSOffsets.CSDowngradeExpertLimitPacket, 5, typeof(CSDowngradeExpertLimitPacket));
            RegisterPacket(CSOffsets.CSExpandExpertPacket, 5, typeof(CSExpandExpertPacket));
            //RegisterPacket(0x100, 5, typeof(CSSearchListPacket)); TODO: this packet is not in the offsets 
            RegisterPacket(CSOffsets.CSAddFriendPacket, 5, typeof(CSAddFriendPacket));
            RegisterPacket(CSOffsets.CSDeleteFriendPacket, 5, typeof(CSDeleteFriendPacket));
            RegisterPacket(CSOffsets.CSCharDetailPacket, 5, typeof(CSCharDetailPacket));
            RegisterPacket(CSOffsets.CSAddBlockedUserPacket, 5, typeof(CSAddBlockedUserPacket));
            RegisterPacket(CSOffsets.CSDeleteBlockedUserPacket, 5, typeof(CSDeleteBlockedUserPacket));
            RegisterPacket(CSOffsets.CSNotifySubZonePacket, 5, typeof(CSNotifySubZonePacket));
            RegisterPacket(CSOffsets.CSRequestUIDataPacket, 5, typeof(CSRequestUIDataPacket));
            RegisterPacket(CSOffsets.CSSaveUIDataPacket, 5, typeof(CSSaveUIDataPacket));
            RegisterPacket(CSOffsets.CSBroadcastVisualOptionPacket, 5, typeof(CSBroadcastVisualOptionPacket));
            RegisterPacket(CSOffsets.CSRestrictCheckPacket, 5, typeof(CSRestrictCheckPacket));
            RegisterPacket(CSOffsets.CSICSMenuListPacket, 5, typeof(CSICSMenuListPacket));
            RegisterPacket(CSOffsets.CSICSGoodsListPacket, 5, typeof(CSICSGoodsListPacket));
            RegisterPacket(CSOffsets.CSICSBuyGoodPacket, 5, typeof(CSICSBuyGoodPacket));
            RegisterPacket(CSOffsets.CSICSMoneyRequestPacket, 5, typeof(CSICSMoneyRequestPacket));
            // 0x12e CSEnterBeautySalonPacket
            RegisterPacket(CSOffsets.CSRankCharacterPacket, 5, typeof(CSRankCharacterPacket));
            RegisterPacket(CSOffsets.CSRequestSecondPasswordKeyTablesPacket, 5, typeof(CSRequestSecondPasswordKeyTablesPacket));
            // 0x130 CSRankSnapshotPacket
            // 0x131 unk packet
            RegisterPacket(CSOffsets.CSIdleStatusPacket, 5, typeof(CSIdleStatusPacket));
            // 0x133 CSChangeAutoUseAAPointPacket
            RegisterPacket(CSOffsets.CSThisTimeUnpackItemPacket, 5, typeof(CSThisTimeUnpackItemPacket));
            RegisterPacket(CSOffsets.CSPremiumServiceBuyPacket, 5, typeof(CSPremiumServiceBuyPacket));
            RegisterPacket(CSOffsets.CSPremiumServiceListPacket, 5, typeof(CSPremiumServiceListPacket));
            // 0x137 CSICSBuyAAPointPacket
            // 0x138 CSRequestTencentFatigueInfoPacket
            // 0x139 CSTakeAllAttachmentItemPacket
            // 0x13a unk packet
            // 0x13b unk packet
            RegisterPacket(CSOffsets.CSPremiumServieceMsgPacket, 5, typeof(CSPremiumServieceMsgPacket));
            // 0x13d unk packet
            // 0x13e unk packet
            // 0x13f unk packet
            RegisterPacket(CSOffsets.CSSetupSecondPasswordPacket, 5, typeof(CSSetupSecondPasswordPacket));
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
            _server = new Server(config.Host.Equals("*") ? IPAddress.Any : IPAddress.Parse(config.Host), config.Port, _handler);
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

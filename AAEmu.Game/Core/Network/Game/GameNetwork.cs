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
            RegisterPacket(CSOffsets.X2EnterWorldPacket, 1, typeof(X2EnterWorldPacket));
            RegisterPacket(CSOffsets.CSLeaveWorldPacket, 1, typeof(CSLeaveWorldPacket));
            RegisterPacket(CSOffsets.CSCancelLeaveWorldPacket, 1, typeof(CSCancelLeaveWorldPacket));
            RegisterPacket(CSOffsets.CSCreateExpeditionPacket, 1, typeof(CSCreateExpeditionPacket));
            //RegisterPacket(0x005, 1, typeof(CSChangeExpeditionSponsorPacket)); // TODO: this packet seems like it has been removed.
            RegisterPacket(CSOffsets.CSChangeExpeditionRolePolicyPacket, 1, typeof(CSChangeExpeditionRolePolicyPacket));
            RegisterPacket(CSOffsets.CSChangeExpeditionMemberRolePacket, 1, typeof(CSChangeExpeditionMemberRolePacket));
            RegisterPacket(CSOffsets.CSChangeExpeditionOwnerPacket, 1, typeof(CSChangeExpeditionOwnerPacket));
            RegisterPacket(CSOffsets.CSRenameExpeditionPacket, 1, typeof(CSRenameExpeditionPacket));
            RegisterPacket(CSOffsets.CSDismissExpeditionPacket, 1, typeof(CSDismissExpeditionPacket));
            RegisterPacket(CSOffsets.CSInviteToExpeditionPacket, 1, typeof(CSInviteToExpeditionPacket));
            RegisterPacket(CSOffsets.CSReplyExpeditionInvitationPacket, 1, typeof(CSReplyExpeditionInvitationPacket));
            RegisterPacket(CSOffsets.CSLeaveExpeditionPacket, 1, typeof(CSLeaveExpeditionPacket));
            RegisterPacket(CSOffsets.CSKickFromExpeditionPacket, 1, typeof(CSKickFromExpeditionPacket));
            // 0x10 unk packet
            RegisterPacket(CSOffsets.CSUpdateDominionTaxRatePacket, 1, typeof(CSUpdateDominionTaxRatePacket));
            RegisterPacket(CSOffsets.CSFactionImmigrationInvitePacket, 1, typeof(CSFactionImmigrationInvitePacket));
            RegisterPacket(CSOffsets.CSFactionImmigrationInviteReplyPacket, 1, typeof(CSFactionImmigrationInviteReplyPacket));
            RegisterPacket(CSOffsets.CSFactionImmigrateToOriginPacket, 1, typeof(CSFactionImmigrateToOriginPacket));
            RegisterPacket(CSOffsets.CSFactionKickToOriginPacket, 1, typeof(CSFactionKickToOriginPacket));
            RegisterPacket(CSOffsets.CSFactionDeclareHostilePacket, 1, typeof(CSFactionDeclareHostilePacket));
            RegisterPacket(CSOffsets.CSFamilyInviteMemberPacket, 1, typeof(CSFamilyInviteMemberPacket));
            RegisterPacket(CSOffsets.CSFamilyReplyInvitationPacket, 1, typeof(CSFamilyReplyInvitationPacket));
            RegisterPacket(CSOffsets.CSFamilyLeavePacket, 1, typeof(CSFamilyLeavePacket));
            RegisterPacket(CSOffsets.CSFamilyKickPacket, 1, typeof(CSFamilyKickPacket));
            RegisterPacket(CSOffsets.CSFamilyChangeTitlePacket, 1, typeof(CSFamilyChangeTitlePacket));
            RegisterPacket(CSOffsets.CSFamilyChangeOwnerPacket, 1, typeof(CSFamilyChangeOwnerPacket));
            RegisterPacket(CSOffsets.CSListCharacterPacket, 1, typeof(CSListCharacterPacket));
            RegisterPacket(CSOffsets.CSRefreshInCharacterListPacket, 1, typeof(CSRefreshInCharacterListPacket));
            RegisterPacket(CSOffsets.CSCreateCharacterPacket, 1, typeof(CSCreateCharacterPacket));
            RegisterPacket(CSOffsets.CSEditCharacterPacket, 1, typeof(CSEditCharacterPacket));
            RegisterPacket(CSOffsets.CSDeleteCharacterPacket, 1, typeof(CSDeleteCharacterPacket));
            RegisterPacket(CSOffsets.CSSelectCharacterPacket, 1, typeof(CSSelectCharacterPacket));
            RegisterPacket(CSOffsets.CSSpawnCharacterPacket, 1, typeof(CSSpawnCharacterPacket));
            RegisterPacket(CSOffsets.CSCancelCharacterDeletePacket, 1, typeof(CSCancelCharacterDeletePacket));
            RegisterPacket(CSOffsets.CSNotifyInGamePacket, 1, typeof(CSNotifyInGamePacket));
            RegisterPacket(CSOffsets.CSNotifyInGameCompletedPacket, 1, typeof(CSNotifyInGameCompletedPacket));
            RegisterPacket(CSOffsets.CSEditorGameModePacket, 1, typeof(CSEditorGameModePacket));
            RegisterPacket(CSOffsets.CSChangeTargetPacket, 1, typeof(CSChangeTargetPacket));
            RegisterPacket(CSOffsets.CSRequestCharBriefPacket, 1, typeof(CSRequestCharBriefPacket));
            RegisterPacket(CSOffsets.CSSpawnSlavePacket, 1, typeof(CSSpawnSlavePacket));
            RegisterPacket(CSOffsets.CSDespawnSlavePacket, 1, typeof(CSDespawnSlavePacket));
            RegisterPacket(CSOffsets.CSDestroySlavePacket, 1, typeof(CSDestroySlavePacket));
            RegisterPacket(CSOffsets.CSBindSlavePacket, 1, typeof(CSBindSlavePacket));
            RegisterPacket(CSOffsets.CSDiscardSlavePacket, 1, typeof(CSDiscardSlavePacket));
            //RegisterPacket(0x031, 1, typeof(CSChangeSlaveTargetPacket)); TODO: the this packet is not in the offsets
            RegisterPacket(CSOffsets.CSChangeSlaveNamePacket, 1, typeof(CSChangeSlaveNamePacket));
            RegisterPacket(CSOffsets.CSRepairSlaveItemsPacket, 1, typeof(CSRepairSlaveItemsPacket));
            RegisterPacket(CSOffsets.CSTurretStatePacket, 1, typeof(CSTurretStatePacket));
            RegisterPacket(CSOffsets.CSChangeSlaveEquipmentPacket, 1, typeof(CSChangeSlaveEquipmentPacket));
            RegisterPacket(CSOffsets.CSDestroyItemPacket, 1, typeof(CSDestroyItemPacket));
            RegisterPacket(CSOffsets.CSSplitBagItemPacket, 1, typeof(CSSplitBagItemPacket));
            RegisterPacket(CSOffsets.CSSwapItemsPacket, 1, typeof(CSSwapItemsPacket));
            RegisterPacket(CSOffsets.CSRepairSingleEquipmentPacket, 1, typeof(CSRepairSingleEquipmentPacket));
            RegisterPacket(CSOffsets.CSRepairAllEquipmentsPacket, 1, typeof(CSRepairAllEquipmentsPacket));
            RegisterPacket(CSOffsets.CSSplitCofferItemPacket, 1, typeof(CSSplitCofferItemPacket));
            RegisterPacket(CSOffsets.CSSwapCofferItemsPacket, 1, typeof(CSSwapCofferItemsPacket));
            RegisterPacket(CSOffsets.CSExpandSlotsPacket, 1, typeof(CSExpandSlotsPacket));
            RegisterPacket(CSOffsets.CSSellBackpackGoodsPacket, 1, typeof(CSSellBackpackGoodsPacket));
            RegisterPacket(CSOffsets.CSSpecialtyRatioPacket, 1, typeof(CSSpecialtyRatioPacket));
            RegisterPacket(CSOffsets.CSListSpecialtyGoodsPacket, 1, typeof(CSListSpecialtyGoodsPacket));
            //RegisterPacket(0x043, 1, typeof(CSBuySpecialtyItemPacket)); TODO: this packet is not in the offsets
            //RegisterPacket(0x044, 1, typeof(CSSpecialtyRecordLoadPacket)); TODO: this packet is not in the offsets
            RegisterPacket(CSOffsets.CSDepositMoneyPacket, 1, typeof(CSDepositMoneyPacket));
            RegisterPacket(CSOffsets.CSWithdrawMoneyPacket, 1, typeof(CSWithdrawMoneyPacket));
            RegisterPacket(CSOffsets.CSConvertItemLookPacket, 1, typeof(CSConvertItemLookPacket));
            RegisterPacket(CSOffsets.CSItemSecurePacket, 1, typeof(CSItemSecurePacket));
            RegisterPacket(CSOffsets.CSItemUnsecurePacket, 1, typeof(CSItemUnsecurePacket));
            RegisterPacket(CSOffsets.CSEquipmentsSecurePacket, 1, typeof(CSEquipmentsSecurePacket));
            RegisterPacket(CSOffsets.CSEquipmentsUnsecurePacket, 1, typeof(CSEquipmentsUnsecurePacket));
            RegisterPacket(CSOffsets.CSResurrectCharacterPacket, 1, typeof(CSResurrectCharacterPacket));
            RegisterPacket(CSOffsets.CSSetForceAttackPacket, 1, typeof(CSSetForceAttackPacket));
            RegisterPacket(CSOffsets.CSChallengeDuelPacket, 1, typeof(CSChallengeDuelPacket));
            RegisterPacket(CSOffsets.CSStartDuelPacket, 1, typeof(CSStartDuelPacket));
            RegisterPacket(CSOffsets.CSStartSkillPacket, 1, typeof(CSStartSkillPacket));
            RegisterPacket(CSOffsets.CSStopCastingPacket, 1, typeof(CSStopCastingPacket));
            RegisterPacket(CSOffsets.CSRemoveBuffPacket, 1, typeof(CSRemoveBuffPacket));
            RegisterPacket(CSOffsets.CSConstructHouseTaxPacket, 1, typeof(CSConstructHouseTaxPacket));
            RegisterPacket(CSOffsets.CSCreateHousePacket, 1, typeof(CSCreateHousePacket));
            RegisterPacket(CSOffsets.CSDecorateHousePacket, 1, typeof(CSDecorateHousePacket));
            RegisterPacket(CSOffsets.CSChangeHouseNamePacket, 1, typeof(CSChangeHouseNamePacket));
            RegisterPacket(CSOffsets.CSChangeHousePermissionPacket, 1, typeof(CSChangeHousePermissionPacket));
            //RegisterPacket(0x05b, 1, typeof(CSChangeHousePayPacket)); TODO: this packet is not in the offsets
            RegisterPacket(CSOffsets.CSRequestHouseTaxPacket, 1, typeof(CSRequestHouseTaxPacket));
            // 0x5c unk packet
            RegisterPacket(CSOffsets.CSAllowHousingRecoverPacket, 1, typeof(CSAllowHousingRecoverPacket));
            RegisterPacket(CSOffsets.CSSellHousePacket, 1, typeof(CSSellHousePacket));
            RegisterPacket(CSOffsets.CSSellHouseCancelPacket, 1, typeof(CSSellHouseCancelPacket));
            RegisterPacket(CSOffsets.CSBuyHousePacket, 1, typeof(CSBuyHousePacket));
            RegisterPacket(CSOffsets.CSJoinUserChatChannelPacket, 1, typeof(CSJoinUserChatChannelPacket));
            RegisterPacket(CSOffsets.CSLeaveChatChannelPacket, 1, typeof(CSLeaveChatChannelPacket));
            RegisterPacket(CSOffsets.CSSendChatMessagePacket, 1, typeof(CSSendChatMessagePacket));
            RegisterPacket(CSOffsets.CSConsoleCmdUsedPacket, 1, typeof(CSConsoleCmdUsedPacket));
            RegisterPacket(CSOffsets.CSInteractNPCPacket, 1, typeof(CSInteractNPCPacket));
            RegisterPacket(CSOffsets.CSInteractNPCEndPacket, 1, typeof(CSInteractNPCEndPacket));
            RegisterPacket(CSOffsets.CSBoardingTransferPacket, 1, typeof(CSBoardingTransferPacket));
            RegisterPacket(CSOffsets.CSStartInteractionPacket, 1, typeof(CSStartInteractionPacket));
            RegisterPacket(CSOffsets.CSSelectInteractionExPacket, 1, typeof(CSSelectInteractionExPacket));
            RegisterPacket(CSOffsets.CSCofferInteractionPacket, 1, typeof(CSCofferInteractionPacket));
            RegisterPacket(CSOffsets.CSCriminalLockedPacket, 1, typeof(CSCriminalLockedPacket));
            RegisterPacket(CSOffsets.CSReplyImprisonOrTrialPacket, 1, typeof(CSReplyImprisonOrTrialPacket));
            RegisterPacket(CSOffsets.CSSkipFinalStatementPacket, 1, typeof(CSSkipFinalStatementPacket));
            RegisterPacket(CSOffsets.CSReplyInviteJuryPacket, 1, typeof(CSReplyInviteJuryPacket));
            RegisterPacket(CSOffsets.CSJurySummonedPacket, 1, typeof(CSJurySummonedPacket));
            RegisterPacket(CSOffsets.CSJuryEndTestimonyPacket, 1, typeof(CSJuryEndTestimonyPacket));
            RegisterPacket(CSOffsets.CSCancelTrialPacket, 1, typeof(CSCancelTrialPacket));
            RegisterPacket(CSOffsets.CSJuryVerdictPacket, 1, typeof(CSJuryVerdictPacket));
            RegisterPacket(CSOffsets.CSReportCrimePacket, 1, typeof(CSReportCrimePacket));
            RegisterPacket(CSOffsets.CSJoinTrialAudiencePacket, 1, typeof(CSJoinTrialAudiencePacket));
            RegisterPacket(CSOffsets.CSLeaveTrialAudiencePacket, 1, typeof(CSLeaveTrialAudiencePacket));
            RegisterPacket(CSOffsets.CSRequestJuryWaitingNumberPacket, 1, typeof(CSRequestJuryWaitingNumberPacket));
            RegisterPacket(CSOffsets.CSInviteToTeamPacket, 1, typeof(CSInviteToTeamPacket));
            RegisterPacket(CSOffsets.CSInviteAreaToTeamPacket, 1, typeof(CSInviteAreaToTeamPacket));
            RegisterPacket(CSOffsets.CSReplyToJoinTeamPacket, 1, typeof(CSReplyToJoinTeamPacket));
            RegisterPacket(CSOffsets.CSLeaveTeamPacket, 1, typeof(CSLeaveTeamPacket));
            RegisterPacket(CSOffsets.CSKickTeamMemberPacket, 1, typeof(CSKickTeamMemberPacket));
            RegisterPacket(CSOffsets.CSMakeTeamOwnerPacket, 1, typeof(CSMakeTeamOwnerPacket));
            //RegisterPacket(0x07e, 1, typeof(CSSetTeamOfficerPacket)); TODO: this packet is not in the offsets 
            RegisterPacket(CSOffsets.CSConvertToRaidTeamPacket, 1, typeof(CSConvertToRaidTeamPacket));
            RegisterPacket(CSOffsets.CSMoveTeamMemberPacket, 1, typeof(CSMoveTeamMemberPacket));
            RegisterPacket(CSOffsets.CSChangeLootingRulePacket, 1, typeof(CSChangeLootingRulePacket));
            RegisterPacket(CSOffsets.CSDismissTeamPacket, 1, typeof(CSDismissTeamPacket));
            RegisterPacket(CSOffsets.CSSetTeamMemberRolePacket, 1, typeof(CSSetTeamMemberRolePacket));
            RegisterPacket(CSOffsets.CSSetOverHeadMarkerPacket, 1, typeof(CSSetOverHeadMarkerPacket));
            RegisterPacket(CSOffsets.CSSetPingPosPacket, 1, typeof(CSSetPingPosPacket));
            RegisterPacket(CSOffsets.CSAskRiskyTeamActionPacket, 1, typeof(CSAskRiskyTeamActionPacket));
            RegisterPacket(CSOffsets.CSMoveUnitPacket, 1, typeof(CSMoveUnitPacket));
            RegisterPacket(CSOffsets.CSSkillControllerStatePacket, 1, typeof(CSSkillControllerStatePacket));
            RegisterPacket(CSOffsets.CSCreateSkillControllerPacket, 1, typeof(CSCreateSkillControllerPacket));
            RegisterPacket(CSOffsets.CSActiveWeaponChangedPacket, 1, typeof(CSActiveWeaponChangedPacket));
            //RegisterPacket(0x08d, 1, typeof(CSChangeItemLookPacket)); TODO: this packet is not in the offsets 
            RegisterPacket(CSOffsets.CSLootOpenBagPacket, 1, typeof(CSLootOpenBagPacket));
            RegisterPacket(CSOffsets.CSLootItemPacket, 1, typeof(CSLootItemPacket));
            RegisterPacket(CSOffsets.CSLootCloseBagPacket, 1, typeof(CSLootCloseBagPacket));
            RegisterPacket(CSOffsets.CSLootDicePacket, 1, typeof(CSLootDicePacket));
            RegisterPacket(CSOffsets.CSLearnSkillPacket, 1, typeof(CSLearnSkillPacket));
            RegisterPacket(CSOffsets.CSLearnBuffPacket, 1, typeof(CSLearnBuffPacket));
            RegisterPacket(CSOffsets.CSResetSkillsPacket, 1, typeof(CSResetSkillsPacket));
            RegisterPacket(CSOffsets.CSSwapAbilityPacket, 1, typeof(CSSwapAbilityPacket));
            RegisterPacket(CSOffsets.CSSendMailPacket, 1, typeof(CSSendMailPacket));
            RegisterPacket(CSOffsets.CSListMailPacket, 1, typeof(CSListMailPacket));
            RegisterPacket(CSOffsets.CSListMailContinuePacket, 1, typeof(CSListMailContinuePacket));
            RegisterPacket(CSOffsets.CSReadMailPacket, 1, typeof(CSReadMailPacket));
            RegisterPacket(CSOffsets.CSTakeAttachmentItemPacket, 1, typeof(CSTakeAttachmentItemPacket));
            RegisterPacket(CSOffsets.CSTakeAttachmentMoneyPacket, 1, typeof(CSTakeAttachmentMoneyPacket));
            // 0x9f unk packet
            RegisterPacket(CSOffsets.CSTakeAttachmentSequentially, 1, typeof(CSTakeAttachmentSequentially));
            RegisterPacket(CSOffsets.CSPayChargeMoneyPacket, 1, typeof(CSPayChargeMoneyPacket));
            RegisterPacket(CSOffsets.CSDeleteMailPacket, 1, typeof(CSDeleteMailPacket));
            RegisterPacket(CSOffsets.CSReportSpamPacket, 1, typeof(CSReportSpamPacket));
            //RegisterPacket(0x0a1, 1, typeof(CSReturnMailPacket)); TODO: this packet is not in the offsets 
            RegisterPacket(CSOffsets.CSRemoveMatePacket, 1, typeof(CSRemoveMatePacket));
            RegisterPacket(CSOffsets.CSChangeMateTargetPacket, 1, typeof(CSChangeMateTargetPacket));
            RegisterPacket(CSOffsets.CSChangeMateNamePacket, 1, typeof(CSChangeMateNamePacket));
            RegisterPacket(CSOffsets.CSMountMatePacket, 1, typeof(CSMountMatePacket));
            RegisterPacket(CSOffsets.CSUnMountMatePacket, 1, typeof(CSUnMountMatePacket));
            RegisterPacket(CSOffsets.CSChangeMateEquipmentPacket, 1, typeof(CSChangeMateEquipmentPacket));
            RegisterPacket(CSOffsets.CSChangeMateUserStatePacket, 1, typeof(CSChangeMateUserStatePacket));
            // 0xab unk packet
            // 0xac unk packet
            RegisterPacket(CSOffsets.CSExpressEmotionPacket, 1, typeof(CSExpressEmotionPacket));
            RegisterPacket(CSOffsets.CSBuyItemsPacket, 1, typeof(CSBuyItemsPacket));
            RegisterPacket(CSOffsets.CSBuyCoinItemPacket, 1, typeof(CSBuyCoinItemPacket));
            RegisterPacket(CSOffsets.CSSellItemsPacket, 1, typeof(CSSellItemsPacket));
            RegisterPacket(CSOffsets.CSListSoldItemPacket, 1, typeof(CSListSoldItemPacket));
            RegisterPacket(CSOffsets.CSBuyPriestBuffPacket, 1, typeof(CSBuyPriestBuffPacket));
            RegisterPacket(CSOffsets.CSUseTeleportPacket, 1, typeof(CSUseTeleportPacket));
            RegisterPacket(CSOffsets.CSTeleportEndedPacket, 1, typeof(CSTeleportEndedPacket));
            RegisterPacket(CSOffsets.CSRepairPetItemsPacket, 1, typeof(CSRepairPetItemsPacket));
            RegisterPacket(CSOffsets.CSUpdateActionSlotPacket, 1, typeof(CSUpdateActionSlotPacket));
            RegisterPacket(CSOffsets.CSAuctionPostPacket, 1, typeof(CSAuctionPostPacket));
            RegisterPacket(CSOffsets.CSAuctionSearchPacket, 1, typeof(CSAuctionSearchPacket));
            RegisterPacket(CSOffsets.CSBidAuctionPacket, 1, typeof(CSBidAuctionPacket));
            RegisterPacket(CSOffsets.CSCancelAuctionPacket, 1, typeof(CSCancelAuctionPacket));
            RegisterPacket(CSOffsets.CSAuctionMyBidListPacket, 1, typeof(CSAuctionMyBidListPacket));
            RegisterPacket(CSOffsets.CSAuctionLowestPricePacket, 1, typeof(CSAuctionLowestPricePacket));
            RegisterPacket(CSOffsets.CSRollDicePacket, 1, typeof(CSRollDicePacket));
            //0xbf CSRequestNpcSpawnerList
            //0xc8 CSRemoveAllFieldSlaves
            //0xc9 CSAddFieldSlave
            RegisterPacket(CSOffsets.CSHangPacket, 1, typeof(CSHangPacket));
            RegisterPacket(CSOffsets.CSUnhangPacket, 1, typeof(CSUnhangPacket));
            RegisterPacket(CSOffsets.CSUnbondDoodadPacket, 1, typeof(CSUnbondDoodadPacket));
            RegisterPacket(CSOffsets.CSCompletedCinemaPacket, 1, typeof(CSCompletedCinemaPacket));
            RegisterPacket(CSOffsets.CSStartedCinemaPacket, 1, typeof(CSStartedCinemaPacket));
            //0xd0 CSRequestPermissionToPlayCinemaForDirectingMode
            //0xd1 CSEditorRemoveGimmickPacket
            //0xd2 CSEditorAddGimmickPacket
            //0xd3 CSInteractGimmickPacket
            //0xd4 CSWorldRayCastingPacket
            RegisterPacket(CSOffsets.CSStartQuestContextPacket, 1, typeof(CSStartQuestContextPacket));
            RegisterPacket(CSOffsets.CSCompleteQuestContextPacket, 1, typeof(CSCompleteQuestContextPacket));
            RegisterPacket(CSOffsets.CSDropQuestContextPacket, 1, typeof(CSDropQuestContextPacket));
            //RegisterPacket(0x0d4, 1, typeof(CSResetQuestContextPacket)); TODO: this packet is not in the offsets 
            //RegisterPacket(0x0d5, 1, typeof(CSAcceptCheatQuestContextPacket)); TODO: this packet is not in the offsets 
            RegisterPacket(CSOffsets.CSQuestTalkMadePacket, 1, typeof(CSQuestTalkMadePacket));
            RegisterPacket(CSOffsets.CSQuestStartWithPacket, 1, typeof(CSQuestStartWithPacket));
            RegisterPacket(CSOffsets.CSTryQuestCompleteAsLetItDonePacket, 1, typeof(CSTryQuestCompleteAsLetItDonePacket));
            RegisterPacket(CSOffsets.CSUsePortalPacket, 1, typeof(CSUsePortalPacket));
            RegisterPacket(CSOffsets.CSDeletePortalPacket, 1, typeof(CSDeletePortalPacket));
            RegisterPacket(CSOffsets.CSInstanceLoadedPacket, 1, typeof(CSInstanceLoadedPacket));
            RegisterPacket(CSOffsets.CSApplyToInstantGamePacket, 1, typeof(CSApplyToInstantGamePacket));
            RegisterPacket(CSOffsets.CSCancelInstantGamePacket, 1, typeof(CSCancelInstantGamePacket));
            RegisterPacket(CSOffsets.CSJoinInstantGamePacket, 1, typeof(CSJoinInstantGamePacket));
            RegisterPacket(CSOffsets.CSEnteredInstantGameWorldPacket, 1, typeof(CSEnteredInstantGameWorldPacket));
            RegisterPacket(CSOffsets.CSLeaveInstantGamePacket, 1, typeof(CSLeaveInstantGamePacket));
            RegisterPacket(CSOffsets.CSCreateDoodadPacket, 1, typeof(CSCreateDoodadPacket));
            //RegisterPacket(0x0e3, 1, typeof(CSSaveDoodadUccStringPacket)); TODO: this packet is not in the offsets 
            RegisterPacket(CSOffsets.CSNaviTeleportPacket, 1, typeof(CSNaviTeleportPacket));
            RegisterPacket(CSOffsets.CSNaviOpenPortalPacket, 1, typeof(CSNaviOpenPortalPacket));
            RegisterPacket(CSOffsets.CSChangeDoodadPhasePacket, 1, typeof(CSChangeDoodadPhasePacket));
            RegisterPacket(CSOffsets.CSNaviOpenBountyPacket, 1, typeof(CSNaviOpenBountyPacket));
            RegisterPacket(CSOffsets.CSChangeDoodadDataPacket, 1, typeof(CSChangeDoodadDataPacket));
            RegisterPacket(CSOffsets.CSStartTradePacket, 1, typeof(CSStartTradePacket));
            RegisterPacket(CSOffsets.CSCanStartTradePacket, 1, typeof(CSCanStartTradePacket));
            RegisterPacket(CSOffsets.CSCannotStartTradePacket, 1, typeof(CSCannotStartTradePacket));
            RegisterPacket(CSOffsets.CSCancelTradePacket, 1, typeof(CSCancelTradePacket));
            RegisterPacket(CSOffsets.CSPutupTradeItemPacket, 1, typeof(CSPutupTradeItemPacket));
            RegisterPacket(CSOffsets.CSPutupTradeMoneyPacket, 1, typeof(CSPutupTradeMoneyPacket));
            RegisterPacket(CSOffsets.CSTakedownTradeItemPacket, 1, typeof(CSTakedownTradeItemPacket));
            RegisterPacket(CSOffsets.CSTradeLockPacket, 1, typeof(CSTradeLockPacket));
            RegisterPacket(CSOffsets.CSTradeOkPacket, 1, typeof(CSTradeOkPacket));
            RegisterPacket(CSOffsets.CSSaveTutorialPacket, 1, typeof(CSSaveTutorialPacket));
            RegisterPacket(CSOffsets.CSSetLogicDoodadPacket, 1, typeof(CSSetLogicDoodadPacket));
            RegisterPacket(CSOffsets.CSCleanupLogicLinkPacket, 1, typeof(CSCleanupLogicLinkPacket));
            RegisterPacket(CSOffsets.CSExecuteCraft, 1, typeof(CSExecuteCraft));
            RegisterPacket(CSOffsets.CSChangeAppellationPacket, 1, typeof(CSChangeAppellationPacket));
            RegisterPacket(CSOffsets.CSCreateShipyardPacket, 1, typeof(CSCreateShipyardPacket));
            RegisterPacket(CSOffsets.CSRestartMainQuestPacket, 1, typeof(CSRestartMainQuestPacket));
            RegisterPacket(CSOffsets.CSSetLpManageCharacterPacket, 1, typeof(CSSetLpManageCharacterPacket));
            RegisterPacket(CSOffsets.CSUpgradeExpertLimitPacket, 1, typeof(CSUpgradeExpertLimitPacket));
            RegisterPacket(CSOffsets.CSDowngradeExpertLimitPacket, 1, typeof(CSDowngradeExpertLimitPacket));
            RegisterPacket(CSOffsets.CSExpandExpertPacket, 1, typeof(CSExpandExpertPacket));
            //RegisterPacket(0x100, 1, typeof(CSSearchListPacket)); TODO: this packet is not in the offsets 
            RegisterPacket(CSOffsets.CSAddFriendPacket, 1, typeof(CSAddFriendPacket));
            RegisterPacket(CSOffsets.CSDeleteFriendPacket, 1, typeof(CSDeleteFriendPacket));
            RegisterPacket(CSOffsets.CSCharDetailPacket, 1, typeof(CSCharDetailPacket));
            RegisterPacket(CSOffsets.CSAddBlockedUserPacket, 1, typeof(CSAddBlockedUserPacket));
            RegisterPacket(CSOffsets.CSDeleteBlockedUserPacket, 1, typeof(CSDeleteBlockedUserPacket));
            RegisterPacket(CSOffsets.CSNotifySubZonePacket, 1, typeof(CSNotifySubZonePacket));
            RegisterPacket(CSOffsets.CSResturnAddrsPacket, 1, typeof(CSResturnAddrsPacket));
            RegisterPacket(CSOffsets.CSRequestUIDataPacket, 1, typeof(CSRequestUIDataPacket));
            RegisterPacket(CSOffsets.CSSaveUIDataPacket, 1, typeof(CSSaveUIDataPacket));
            RegisterPacket(CSOffsets.CSBroadcastVisualOptionPacket, 1, typeof(CSBroadcastVisualOptionPacket));
            RegisterPacket(CSOffsets.CSRestrictCheckPacket, 1, typeof(CSRestrictCheckPacket));
            RegisterPacket(CSOffsets.CSICSMenuListPacket, 1, typeof(CSICSMenuListPacket));
            RegisterPacket(CSOffsets.CSICSGoodsListPacket, 1, typeof(CSICSGoodsListPacket));
            RegisterPacket(CSOffsets.CSICSBuyGoodPacket, 1, typeof(CSICSBuyGoodPacket));
            RegisterPacket(CSOffsets.CSICSMoneyRequestPacket, 1, typeof(CSICSMoneyRequestPacket));
            // 0x12e CSEnterBeautySalonPacket
            RegisterPacket(CSOffsets.CSRankCharacterPacket, 1, typeof(CSRankCharacterPacket));
            RegisterPacket(CSOffsets.CSRequestSecondPasswordKeyTablesPacket, 1, typeof(CSRequestSecondPasswordKeyTablesPacket));
            RegisterPacket(CSOffsets.CSRankSnapshotPacket, 1, typeof(CSRankSnapshotPacket));
            // 0x131 unk packet
            RegisterPacket(CSOffsets.CSIdleStatusPacket, 1, typeof(CSIdleStatusPacket));
            // 0x133 CSChangeAutoUseAAPointPacket
            RegisterPacket(CSOffsets.CSThisTimeUnpackItemPacket, 1, typeof(CSThisTimeUnpackItemPacket));
            RegisterPacket(CSOffsets.CSPremiumServiceBuyPacket, 1, typeof(CSPremiumServiceBuyPacket));
            RegisterPacket(CSOffsets.CSPremiumServiceListPacket, 1, typeof(CSPremiumServiceListPacket));
            // 0x137 CSICSBuyAAPointPacket
            // 0x138 CSRequestTencentFatigueInfoPacket
            // 0x139 CSTakeAllAttachmentItemPacket
            // 0x13a unk packet
            // 0x13b unk packet
            RegisterPacket(CSOffsets.CSPremiumServieceMsgPacket, 1, typeof(CSPremiumServieceMsgPacket));
            // 0x13d unk packet
            // 0x13e unk packet
            // 0x13f unk packet
            RegisterPacket(CSOffsets.CSSetupSecondPassword, 1, typeof(CSSetupSecondPassword));
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

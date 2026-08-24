using System.Net;

using AAEmu.Commons.Network.Core;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Packets.C2G;
using AAEmu.Game.Core.Packets.Proxy;
using AAEmu.Game.Models;

using NLog;

namespace AAEmu.Game.Core.Network.Game;

public class GameNetwork : Singleton<GameNetwork>
{
    private Server _server;
    private readonly GameProtocolHandler _handler;
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private GameNetwork()
    {
        _handler = new GameProtocolHandler();

        // World
        RegisterPacket(CSOffsets.X2EnterWorldPacket, 1, typeof(X2EnterWorldPacket));
        RegisterPacket(CSOffsets.CSAesXorKeyPacket, 1, typeof(CSAesXorKeyPacket));
        RegisterPacket(CSOffsets.CSReentryCheckPacket, 1, typeof(CSReentryCheckPacket));
        RegisterPacket(CSOffsets.CSLeaveWorldPacket, 1, typeof(CSLeaveWorldPacket));
        RegisterPacket(CSOffsets.CSCancelLeaveWorldPacket, 1, typeof(CSCancelLeaveWorldPacket));
        RegisterPacket(CSOffsets.CSCreateExpeditionPacket, 1, typeof(CSCreateExpeditionPacket));
        RegisterPacket(CSOffsets.CSChangeExpeditionSponsorPacket, 1, typeof(CSChangeExpeditionSponsorPacket));
        RegisterPacket(CSOffsets.CSChangeExpeditionRolePolicyPacket, 1, typeof(CSChangeExpeditionRolePolicyPacket));
        RegisterPacket(CSOffsets.CSChangeExpeditionMemberRolePacket, 1, typeof(CSChangeExpeditionMemberRolePacket));
        RegisterPacket(CSOffsets.CSChangeExpeditionOwnerPacket, 1, typeof(CSChangeExpeditionOwnerPacket));
        RegisterPacket(CSOffsets.CSRenameExpeditionPacket, 1, typeof(CSRenameExpeditionPacket));
        RegisterPacket(CSOffsets.CSDismissExpeditionPacket, 1, typeof(CSDismissExpeditionPacket));
        RegisterPacket(CSOffsets.CSInviteToExpeditionPacket, 1, typeof(CSInviteToExpeditionPacket));
        RegisterPacket(CSOffsets.CSReplyExpeditionInvitationPacket, 1, typeof(CSReplyExpeditionInvitationPacket));
        RegisterPacket(CSOffsets.CSLeaveExpeditionPacket, 1, typeof(CSLeaveExpeditionPacket));
        RegisterPacket(CSOffsets.CSKickFromExpeditionPacket, 1, typeof(CSKickFromExpeditionPacket));
        RegisterPacket(CSOffsets.CSDeclareExpeditionWarPacket, 1, typeof(CSDeclareExpeditionWarPacket));
        // 0x10 unk packet
        RegisterPacket(CSOffsets.CSUpdateDominionTaxRatePacket, 1, typeof(CSUpdateDominionTaxRatePacket));
        RegisterPacket(CSOffsets.CSFactionMobilizationOrderPacket, 1, typeof(CSFactionMobilizationOrderPacket));
        RegisterPacket(CSOffsets.CSFamilyInviteMemberPacket, 1, typeof(CSFamilyInviteMemberPacket));
        RegisterPacket(CSOffsets.CSFamilyReplyInvitationPacket, 1, typeof(CSFamilyReplyInvitationPacket));
        RegisterPacket(CSOffsets.CSFamilyLeavePacket, 1, typeof(CSFamilyLeavePacket));
        RegisterPacket(CSOffsets.CSFamilyKickPacket, 1, typeof(CSFamilyKickPacket));
        RegisterPacket(CSOffsets.CSFamilyChangeTitlePacket, 1, typeof(CSFamilyChangeTitlePacket));
        RegisterPacket(CSOffsets.CSFamilyChangeOwnerPacket, 1, typeof(CSFamilyChangeOwnerPacket));
        RegisterPacket(CSOffsets.CSRefreshInCharacterListPacket, 1, typeof(CSRefreshInCharacterListPacket));
        RegisterPacket(CSOffsets.CSCreateCharacterPacket, 1, typeof(CSCreateCharacterPacket));
        RegisterPacket(CSOffsets.CSEditCharacterPacket, 1, typeof(CSEditCharacterPacket));
        RegisterPacket(CSOffsets.CSDeleteCharacterPacket, 1, typeof(CSDeleteCharacterPacket));
        RegisterPacket(CSOffsets.CSSelectCharacterPacket, 1, typeof(CSSelectCharacterPacket));
        RegisterPacket(CSOffsets.CSCheckRaceCongestionPacket, 1, typeof(CSCheckRaceCongestionPacket));
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
        RegisterPacket(CSOffsets.CSChangeSlaveTargetPacket, 1, typeof(CSChangeSlaveTargetPacket));
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
        RegisterPacket(CSOffsets.CSBuySpecialtyItemPacket, 1, typeof(CSBuySpecialtyItemPacket));
        RegisterPacket(CSOffsets.CSSpecialtyRecordLoadPacket, 1, typeof(CSSpecialtyRecordLoadPacket));
        RegisterPacket(CSOffsets.CSDepositMoneyPacket, 1, typeof(CSDepositMoneyPacket));
        RegisterPacket(CSOffsets.CSWithdrawMoneyPacket, 1, typeof(CSWithdrawMoneyPacket));
        RegisterPacket(CSOffsets.CSConvertItemLookPacket, 1, typeof(CSConvertItemLookPacket));
        RegisterPacket(CSOffsets.CSItemSecurePacket, 1, typeof(CSItemSecurePacket));
        RegisterPacket(CSOffsets.CSItemUnsecurePacket, 1, typeof(CSItemUnsecurePacket));
        RegisterPacket(CSOffsets.CSEquipmentsSecurePacket, 1, typeof(CSEquipmentsSecurePacket));
        RegisterPacket(CSOffsets.CSEquipmentsUnsecurePacket, 1, typeof(CSEquipmentsUnsecurePacket));
        RegisterPacket(CSOffsets.CSResurrectCharacterPacket, 1, typeof(CSResurrectCharacterPacket));
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
        RegisterPacket(CSOffsets.CSRequestHouseTaxPacket, 1, typeof(CSRequestHouseTaxPacket));
        RegisterPacket(CSOffsets.CSPrepayHouseTaxPacket, 1, typeof(CSPrepayHouseTaxPacket));
        // 0x5c unk packet
        RegisterPacket(CSOffsets.CSAllowHousingRecoverPacket, 1, typeof(CSAllowHousingRecoverPacket));
        RegisterPacket(CSOffsets.CSSellHousePacket, 1, typeof(CSSellHousePacket));
        RegisterPacket(CSOffsets.CSSellHouseCancelPacket, 1, typeof(CSSellHouseCancelPacket));
        RegisterPacket(CSOffsets.CSBuyHousePacket, 1, typeof(CSBuyHousePacket));
        RegisterPacket(CSOffsets.CSJoinUserChatChannelPacket, 1, typeof(CSJoinUserChatChannelPacket));
        RegisterPacket(CSOffsets.CSLeaveChatChannelPacket, 1, typeof(CSLeaveChatChannelPacket));
        RegisterPacket(CSOffsets.CSSendChatMessagePacket, 1, typeof(CSSendChatMessagePacket));
        RegisterPacket(CSOffsets.CSConsoleCmdUsedPacket, 1, typeof(CSConsoleCmdUsedPacket));
        RegisterPacket(CSOffsets.CSGmCommandPacket, 1, typeof(CSGmCommandPacket));
        RegisterPacket(CSOffsets.CSGmNoticePacket, 1, typeof(CSGmNoticePacket));
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
        RegisterPacket(CSOffsets.CSMakeTeamOfficerPacket, 1, typeof(CSMakeTeamOfficerPacket));
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
        RegisterPacket(CSOffsets.CSReturnMailPacket, 1, typeof(CSReturnMailPacket));
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
        RegisterPacket(CSOffsets.CSDoodadPurchaseItemPacket, 1, typeof(CSDoodadPurchaseItemPacket));
        RegisterPacket(CSOffsets.CSSellItemsPacket, 1, typeof(CSSellItemsPacket));
        RegisterPacket(CSOffsets.CSListSoldItemPacket, 1, typeof(CSListSoldItemPacket));
        RegisterPacket(CSOffsets.CSTeleportEndedPacket, 1, typeof(CSTeleportEndedPacket));
        RegisterPacket(CSOffsets.CSRepairPetItemsPacket, 1, typeof(CSRepairPetItemsPacket));
        RegisterPacket(CSOffsets.CSUpdateActionSlotPacket, 1, typeof(CSUpdateActionSlotPacket));
        RegisterPacket(CSOffsets.CSAuctionPostPacket, 1, typeof(CSAuctionPostPacket));
        RegisterPacket(CSOffsets.CSAuctionSearchPacket, 1, typeof(CSAuctionSearchPacket));
        RegisterPacket(CSOffsets.CSBidAuctionPacket, 1, typeof(CSBidAuctionPacket));
        RegisterPacket(CSOffsets.CSCancelAuctionPacket, 1, typeof(CSCancelAuctionPacket));
        RegisterPacket(CSOffsets.CSAuctionMyBidListPacket, 1, typeof(CSAuctionMyBidListPacket));
        RegisterPacket(CSOffsets.CSAuctionLowestPricePacket, 1, typeof(CSAuctionLowestPricePacket));
        RegisterPacket(CSOffsets.CSSearchAuctionSoldRecordPacket, 1, typeof(CSSearchAuctionSoldRecordPacket));
        RegisterPacket(CSOffsets.CSRollDicePacket, 1, typeof(CSRollDicePacket));
        //0xbf CSRequestNpcSpawnerList
        //0xc8 CSRemoveAllFieldSlaves
        //0xc9 CSAddFieldSlave
        RegisterPacket(CSOffsets.CSHangPacket, 1, typeof(CSHangPacket));
        RegisterPacket(CSOffsets.CSUnhangPacket, 1, typeof(CSUnhangPacket));
        RegisterPacket(CSOffsets.CSUnbondDoodadPacket, 1, typeof(CSUnbondDoodadPacket));
        RegisterPacket(CSOffsets.CSCompletedCinemaPacket, 1, typeof(CSCompletedCinemaPacket));
        RegisterPacket(CSOffsets.CSStartedCinemaPacket, 1, typeof(CSStartedCinemaPacket));
        RegisterPacket(CSOffsets.CSRequestPermissionToPlayCinemaForDirectingMode, 1, typeof(CSRequestPermissionToPlayCinemaForDirectingMode));
        RegisterPacket(CSOffsets.CSWorldRayCastingPacket, 1, typeof(CSWorldRayCastingPacket));
        //0xd1 CSEditorRemoveGimmickPacket
        //0xd2 CSEditorAddGimmickPacket
        //0xd3 CSInteractGimmickPacket
        //0xd4 CSWorldRayCastingPacket
        RegisterPacket(CSOffsets.CSStartQuestContextPacket, 1, typeof(CSStartQuestContextPacket));
        RegisterPacket(CSOffsets.CSCompleteQuestContextPacket, 1, typeof(CSCompleteQuestContextPacket));
        RegisterPacket(CSOffsets.CSDropQuestContextPacket, 1, typeof(CSDropQuestContextPacket));
        RegisterPacket(CSOffsets.CSResetQuestContextPacket, 1, typeof(CSResetQuestContextPacket));
        RegisterPacket(CSOffsets.CSAcceptCheatQuestContextPacket, 1, typeof(CSAcceptCheatQuestContextPacket));
        RegisterPacket(CSOffsets.CSQuestTalkMadePacket, 1, typeof(CSQuestTalkMadePacket));
        RegisterPacket(CSOffsets.CSQuestStartWithPacket, 1, typeof(CSQuestStartWithPacket));
        RegisterPacket(CSOffsets.CSTryQuestCompleteAsLetItDonePacket, 1, typeof(CSTryQuestCompleteAsLetItDonePacket));
        RegisterPacket(CSOffsets.CSUsePortalPacket, 1, typeof(CSUsePortalPacket));
        RegisterPacket(CSOffsets.CSDeletePortalPacket, 1, typeof(CSDeletePortalPacket));
        RegisterPacket(CSOffsets.CSInstanceLoadedPacket, 1, typeof(CSInstanceLoadedPacket));
        RegisterPacket(CSOffsets.CSApplyToInstantGamePacket, 1, typeof(CSApplyToInstantGamePacket));
        RegisterPacket(CSOffsets.CSCancelInstantGamePacket, 1, typeof(CSCancelInstantGamePacket));
        RegisterPacket(CSOffsets.CSInvitationAnswerPacket, 1, typeof(CSInvitationAnswerPacket));
        RegisterPacket(CSOffsets.CSLeaveInstantGamePacket, 1, typeof(CSLeaveInstantGamePacket));
        RegisterPacket(CSOffsets.CSReentryReponsePacket, 1, typeof(CSReentryReponsePacket));
        RegisterPacket(CSOffsets.CSPickBuffInstantGamePacket, 1, typeof(CSPickBuffInstantGamePacket));
        RegisterPacket(CSOffsets.CSCreateDoodadPacket, 1, typeof(CSCreateDoodadPacket));
        RegisterPacket(CSOffsets.CSNaviTeleportPacket, 1, typeof(CSNaviTeleportPacket));
        RegisterPacket(CSOffsets.CSNaviOpenPortalPacket, 1, typeof(CSNaviOpenPortalPacket));
        RegisterPacket(CSOffsets.CSChangeDoodadPhasePacket, 1, typeof(CSChangeDoodadPhasePacket));
        RegisterPacket(CSOffsets.CSChangeDoodadDataPacket, 1, typeof(CSChangeDoodadDataPacket));
        RegisterPacket(CSOffsets.CSDoodadItemChangerPacket, 1, typeof(CSDoodadItemChangerPacket));
        RegisterPacket(CSOffsets.CSDoodadQuestNotiPacket, 1, typeof(CSDoodadQuestNotiPacket));
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
        RegisterPacket(CSOffsets.CSSearchListPacket, 1, typeof(CSSearchListPacket));
        RegisterPacket(CSOffsets.CSDeleteFriendPacket, 1, typeof(CSDeleteFriendPacket));
        RegisterPacket(CSOffsets.CSCharDetailPacket, 1, typeof(CSCharDetailPacket));
        RegisterPacket(CSOffsets.CSAddBlockedUserPacket, 1, typeof(CSAddBlockedUserPacket));
        RegisterPacket(CSOffsets.CSDeleteBlockedUserPacket, 1, typeof(CSDeleteBlockedUserPacket));
        RegisterPacket(CSOffsets.CSShowCommonFarmAreaPacket, 1, typeof(CSShowCommonFarmAreaPacket));
        RegisterPacket(CSOffsets.CSShowQuestAreaPacket, 1, typeof(CSShowQuestAreaPacket));
        RegisterPacket(CSOffsets.CSRequestCommonFarmList, 1, typeof(CSRequestCommonFarmList));
        RegisterPacket(CSOffsets.CSPlaceCommonFarmPacket, 1, typeof(CSPlaceCommonFarmPacket));
        RegisterPacket(CSOffsets.CSPlaceAreaSpheresPacket, 1, typeof(CSPlaceAreaSpheresPacket));
        RegisterPacket(CSOffsets.CSNotifySubZonePacket, 1, typeof(CSNotifySubZonePacket));
        RegisterPacket(CSOffsets.CSResturnAddrsPacket, 1, typeof(CSResturnAddrsPacket));
        RegisterPacket(CSOffsets.CSItemUccPacket, 1, typeof(CSItemUccPacket));
        RegisterPacket(CSOffsets.CSRequestUIDataPacket, 1, typeof(CSRequestUIDataPacket));
        RegisterPacket(CSOffsets.CSSaveUIDataPacket, 1, typeof(CSSaveUIDataPacket));
        RegisterPacket(CSOffsets.CSBroadcastVisualOptionPacket, 1, typeof(CSBroadcastVisualOptionPacket));
        RegisterPacket(CSOffsets.CSBroadcastOpenEquipInfoPacket, 1, typeof(CSBroadcastOpenEquipInfoPacket));
        RegisterPacket(CSOffsets.CSRestrictCheckPacket, 1, typeof(CSRestrictCheckPacket));
        RegisterPacket(CSOffsets.CSICSMenuListPacket, 1, typeof(CSICSMenuListPacket));
        RegisterPacket(CSOffsets.CSICSBuyGoodPacket, 1, typeof(CSICSBuyGoodPacket));
        RegisterPacket(CSOffsets.CSICSMoneyRequestPacket, 1, typeof(CSICSMoneyRequestPacket));
        RegisterPacket(CSOffsets.CSSaveUserMusicNotesPacket, 1, typeof(CSSaveUserMusicNotesPacket));
        RegisterPacket(CSOffsets.CSRequestMusicNotesPacket, 1, typeof(CSRequestMusicNotesPacket));
        RegisterPacket(CSOffsets.CSSendUserMusicPacket, 1, typeof(CSSendUserMusicPacket));

        RegisterPacket(CSOffsets.CSBeautyshopDataPacket, 1, typeof(CSBeautyshopDataPacket));
        RegisterPacket(CSOffsets.CSBeautyshopBypassPacket, 1, typeof(CSBeautyshopBypassPacket));
        RegisterPacket(CSOffsets.CSSpecialtyPacket, 1, typeof(CSSpecialtyPacket));
        RegisterPacket(CSOffsets.CSSpecialtyCurrentLoadPacket, 1, typeof(CSSpecialtyCurrentLoadPacket));
        RegisterPacket(CSOffsets.CSRankRewardSnapshotPacket, 1, typeof(CSRankRewardSnapshotPacket));
        RegisterPacket(CSOffsets.CSRankSnapshotPacket, 1, typeof(CSRankSnapshotPacket));

        RegisterPacket(CSOffsets.CSRequestSecondPasswordKeyTablesPacket, 1, typeof(CSRequestSecondPasswordKeyTablesPacket));
        RegisterPacket(CSOffsets.CSCreateSecondPasswordPacket, 1, typeof(CSCreateSecondPasswordPacket));
        RegisterPacket(CSOffsets.CSCheckSecondPasswordPacket, 1, typeof(CSCheckSecondPasswordPacket));
        RegisterPacket(CSOffsets.CSClearSecondPasswordPacket, 1, typeof(CSClearSecondPasswordPacket));
        RegisterPacket(CSOffsets.CSChangeSecondPasswordPacket, 1, typeof(CSChangeSecondPasswordPacket));
        // 0x130 CSRankSnapshotPacket
        RegisterPacket(CSOffsets.CSIdleStatusPacket, 1, typeof(CSIdleStatusPacket));
        // 0x133 CSChangeAutoUseAAPointPacket
        RegisterPacket(CSOffsets.CSThisTimeUnpackItemPacket, 1, typeof(CSThisTimeUnpackItemPacket));
        RegisterPacket(CSOffsets.CSPremiumServiceBuyPacket, 1, typeof(CSPremiumServiceBuyPacket));
        RegisterPacket(CSOffsets.CSPremiumServiceListPacket, 1, typeof(CSPremiumServiceListPacket));
        RegisterPacket(CSOffsets.CSICSBuyAAPointPacket, 1, typeof(CSICSBuyAAPointPacket));
        // 0x137 CSICSBuyAAPointPacket
        // 0x138 CSRequestTencentFatigueInfoPacket
        RegisterPacket(CSOffsets.CSTakeAllAttachmentItemPacket, 1, typeof(CSTakeAllAttachmentItemPacket));
        RegisterPacket(CSOffsets.CSTakeScheduleItemPacket, 1, typeof(CSTakeScheduleItemPacket));
        // 0x13a unk packet
        // 0x13b unk packet
        RegisterPacket(CSOffsets.CSPremiumServiceMsgPacket, 1, typeof(CSPremiumServiceMsgPacket));
        RegisterPacket(CSOffsets.CSRequestSysInstanceIndexPacket, 1, typeof(CSRequestSysInstanceIndexPacket));
        RegisterPacket(CSOffsets.CSEnterSysInstancePacket, 1, typeof(CSEnterSysInstancePacket));
        // 0x13d unk packet
        // 0x13e unk packet
        // 0x13f unk packet
        // 0x141 unk packet
        // 0x142 unk packet

        // Returns 10.0.2.13 in-world (Phase 1 UI / monitor / unnamed stubs)
        RegisterPacket(CSOffsets.CSProtectSensitiveOperation, 1, typeof(CSProtectSensitiveOperation));
        RegisterPacket(CSOffsets.CSHeroAbstainPacket, 1, typeof(CSHeroAbstainPacket));
        RegisterPacket(CSOffsets.CSRebuildHouseTaxInfoPacket, 1, typeof(CSRebuildHouseTaxInfoPacket));
        RegisterPacket(CSOffsets.CSInstantTimePacket, 1, typeof(CSInstantTimePacket));
        RegisterPacket(CSOffsets.CSHeroRankingListPacket, 1, typeof(CSHeroRankingListPacket));
        RegisterPacket(CSOffsets.CSQuizResponsePacket, 1, typeof(CSQuizResponsePacket));
        RegisterPacket(CSOffsets.CSIndunDirectTelPacket, 1, typeof(CSIndunDirectTelPacket));
        RegisterPacket(CSOffsets.CSVoteReputationPacket, 1, typeof(CSVoteReputationPacket));
        RegisterPacket(CSOffsets.CSSecurityReportPacket, 1, typeof(CSSecurityReportPacket));
        RegisterPacket(CSOffsets.CSRequestMonitorNpcsInfoPacket, 1, typeof(CSRequestMonitorNpcsInfoPacket));
        RegisterPacket(CSOffsets.CSRaidRecruitListPacket, 1, typeof(CSRaidRecruitListPacket));
        RegisterPacket(CSOffsets.CSUIContentTogglePacket, 1, typeof(CSUIContentTogglePacket));
        RegisterPacket(CSOffsets.CSReopenRandomBoxRefreshPacket, 1, typeof(CSReopenRandomBoxRefreshPacket));
        RegisterPacket(CSOffsets.CSFriendAcceptPacket, 1, typeof(CSFriendAcceptPacket));
        RegisterPacket(CSOffsets.CSFriendCancelPacket, 1, typeof(CSFriendCancelPacket));
        RegisterPacket(CSOffsets.CSInvokeItemSelectiveItemEffectPacket, 1, typeof(CSInvokeItemSelectiveItemEffectPacket));
        RegisterPacket(CSOffsets.CSCharacterPrivacyStatusUpdatePacket, 1, typeof(CSCharacterPrivacyStatusUpdatePacket));

        // Recovered in the 10.0.2.13 CS audit -- layouts taken from the client's own
        RegisterPacket(CSOffsets.CSRequestDominionDataPacket, 1, typeof(CSRequestDominionDataPacket));
        RegisterPacket(CSOffsets.CSFactionIssuanceOfMobilizationOrderPacket, 1, typeof(CSFactionIssuanceOfMobilizationOrderPacket));
        RegisterPacket(CSOffsets.CSRefreshBotCheckInfoPacket, 1, typeof(CSRefreshBotCheckInfoPacket));
        RegisterPacket(CSOffsets.CSAnswerBotCheckInfoPacket, 1, typeof(CSAnswerBotCheckInfoPacket));
        RegisterPacket(CSOffsets.CSReportSpammerPacket, 1, typeof(CSReportSpammerPacket));
        RegisterPacket(CSOffsets.CSTeamHandOverOwnerResponsePacket, 1, typeof(CSTeamHandOverOwnerResponsePacket));
        RegisterPacket(CSOffsets.CSTeamOwnerOfferResponsePacket, 1, typeof(CSTeamOwnerOfferResponsePacket));
        RegisterPacket(CSOffsets.CSChangeClientNpcTargetPacket, 1, typeof(CSChangeClientNpcTargetPacket));
        RegisterPacket(CSOffsets.CSRemoveClientNpcPacket, 1, typeof(CSRemoveClientNpcPacket));
        RegisterPacket(CSOffsets.CSRemoveAllDoodadFromCell, 1, typeof(CSRemoveAllDoodadFromCell));
        RegisterPacket(CSOffsets.CSAddDoodadToCellEndedPacket, 1, typeof(CSAddDoodadToCellEndedPacket));
        RegisterPacket(CSOffsets.CSSetDoodadTimeAccel, 1, typeof(CSSetDoodadTimeAccel));
        RegisterPacket(CSOffsets.CSRemoveAllFieldSlaves, 1, typeof(CSRemoveAllFieldSlaves));
        RegisterPacket(CSOffsets.CSEditorRemoveGimmickPacket, 1, typeof(CSEditorRemoveGimmickPacket));
        RegisterPacket(CSOffsets.CSInteractGimmickPacket, 1, typeof(CSInteractGimmickPacket));
        RegisterPacket(CSOffsets.CSRequestTodayAssignmentPacket, 1, typeof(CSRequestTodayAssignmentPacket));
        RegisterPacket(CSOffsets.CSResetTodayAssignmentPacket, 1, typeof(CSResetTodayAssignmentPacket));
        RegisterPacket(CSOffsets.CSEnterInstantGamePacket, 1, typeof(CSEnterInstantGamePacket));
        RegisterPacket(CSOffsets.CSInstantLeaveUserListRequest, 1, typeof(CSInstantLeaveUserListRequest));
        RegisterPacket(CSOffsets.CSBanVoteRequestPacket, 1, typeof(CSBanVoteRequestPacket));
        RegisterPacket(CSOffsets.CSRemoveAreaSpheresPacket, 1, typeof(CSRemoveAreaSpheresPacket));
        RegisterPacket(CSOffsets.CSRemoveCommonFarmsPacket, 1, typeof(CSRemoveCommonFarmsPacket));
        RegisterPacket(CSOffsets.CSPauseUserMusicPacket, 1, typeof(CSPauseUserMusicPacket));
        RegisterPacket(CSOffsets.CSLeaveBeautyshopPacket, 1, typeof(CSLeaveBeautyshopPacket));
        RegisterPacket(CSOffsets.CSRankPersonalDataPacket, 1, typeof(CSRankPersonalDataPacket));
        RegisterPacket(CSOffsets.CSChangeAutoUseAAPointPacket, 1, typeof(CSChangeAutoUseAAPointPacket));
        RegisterPacket(CSOffsets.CSRequestSkipClientDrivenIndunPacket, 1, typeof(CSRequestSkipClientDrivenIndunPacket));
        RegisterPacket(CSOffsets.CSCancelSensitiveOperationVerify, 1, typeof(CSCancelSensitiveOperationVerify));
        RegisterPacket(CSOffsets.CSHeroCandidateListPacket, 1, typeof(CSHeroCandidateListPacket));
        RegisterPacket(CSOffsets.CSHeroVotingPacket, 1, typeof(CSHeroVotingPacket));
        RegisterPacket(CSOffsets.CSRepresentCharacter, 1, typeof(CSRepresentCharacter));
        RegisterPacket(CSOffsets.CSFriendRequestPacket, 1, typeof(CSFriendRequestPacket));
        RegisterPacket(CSOffsets.CSReopenRandomBoxGetItemPacket, 1, typeof(CSReopenRandomBoxGetItemPacket));

        RegisterPacket(CSOffsets.CSExpeditionWarKillScorePacket, 1, typeof(CSExpeditionWarKillScorePacket));
        RegisterPacket(CSOffsets.CSRequestExpeditionHistoriesPacket, 1, typeof(CSRequestExpeditionHistoriesPacket));
        RegisterPacket(CSOffsets.CSReqExpdWarHistoriesPacket, 1, typeof(CSReqExpdWarHistoriesPacket));
        RegisterPacket(CSOffsets.CSCancelExpeditionProtectionPacket, 1, typeof(CSCancelExpeditionProtectionPacket));
        RegisterPacket(CSOffsets.CSExpeditionBuffUnitPacket, 1, typeof(CSExpeditionBuffUnitPacket));
        RegisterPacket(CSOffsets.CSShowResidentZoneGroupsPacket, 1, typeof(CSShowResidentZoneGroupsPacket));
        RegisterPacket(CSOffsets.CSResidentBalanceAllPacket, 1, typeof(CSResidentBalanceAllPacket));
        RegisterPacket(CSOffsets.CSFactionRelationHistoryGetPacket, 1, typeof(CSFactionRelationHistoryGetPacket));
        RegisterPacket(CSOffsets.CSFactionRelationCountGetPacket, 1, typeof(CSFactionRelationCountGetPacket));
        RegisterPacket(CSOffsets.CSExpeditionNoticeUpatePacket, 1, typeof(CSExpeditionNoticeUpatePacket));
        RegisterPacket(CSOffsets.CSExpeditionRecruitmentsGetPacket, 1, typeof(CSExpeditionRecruitmentsGetPacket));
        RegisterPacket(CSOffsets.CSExpeditionRecruitmentDelPacket, 1, typeof(CSExpeditionRecruitmentDelPacket));
        RegisterPacket(CSOffsets.CSExpeditionApplicantsGetPacket, 1, typeof(CSExpeditionApplicantsGetPacket));
        RegisterPacket(CSOffsets.CSExpeditionApplicantAddPacket, 1, typeof(CSExpeditionApplicantAddPacket));
        RegisterPacket(CSOffsets.CSExpeditionApplicantAcceptPacket, 1, typeof(CSExpeditionApplicantAcceptPacket));
        RegisterPacket(CSOffsets.CSExpeditionApplicantRejectPacket, 1, typeof(CSExpeditionApplicantRejectPacket));
        RegisterPacket(CSOffsets.CSExpeditionSummonGetPacket, 1, typeof(CSExpeditionSummonGetPacket));
        RegisterPacket(CSOffsets.CSExpeditionSummonReplyPacket, 1, typeof(CSExpeditionSummonReplyPacket));
        RegisterPacket(CSOffsets.CSFamilyNameSetPacket, 1, typeof(CSFamilyNameSetPacket));
        RegisterPacket(CSOffsets.CSFamilyNoticeSetPacket, 1, typeof(CSFamilyNoticeSetPacket));
        RegisterPacket(CSOffsets.CSFamilyIncreaseMemberPacket, 1, typeof(CSFamilyIncreaseMemberPacket));
        RegisterPacket(CSOffsets.CSResetVisualRacePacket, 1, typeof(CSResetVisualRacePacket));
        RegisterPacket(CSOffsets.CSTeamTestRaidCreatePacket, 1, typeof(CSTeamTestRaidCreatePacket));
        RegisterPacket(CSOffsets.CSTeamJointInfoPacket, 1, typeof(CSTeamJointInfoPacket));
        RegisterPacket(CSOffsets.CSTeamSummonGetPacket, 1, typeof(CSTeamSummonGetPacket));
        RegisterPacket(CSOffsets.CSTeamSummonReplyPacket, 1, typeof(CSTeamSummonReplyPacket));
        RegisterPacket(CSOffsets.CSFollowRespPacket, 1, typeof(CSFollowRespPacket));
        RegisterPacket(CSOffsets.CSExpandAbilitySetSlotPacket, 1, typeof(CSExpandAbilitySetSlotPacket));
        RegisterPacket(CSOffsets.CSHeroDropoutComebackAccept, 1, typeof(CSHeroDropoutComebackAccept));
        RegisterPacket(CSOffsets.CSChangeDiceBidRulePacket, 1, typeof(CSChangeDiceBidRulePacket));
        RegisterPacket(CSOffsets.CSGetDoodadManikinSkin, 1, typeof(CSGetDoodadManikinSkin));
        RegisterPacket(CSOffsets.CSTodayAssignmentAcceptAllPacket, 1, typeof(CSTodayAssignmentAcceptAllPacket));
        RegisterPacket(CSOffsets.CSCleanupGardenPacket, 1, typeof(CSCleanupGardenPacket));
        RegisterPacket(CSOffsets.CSSearchCraftOrderPacket, 1, typeof(CSSearchCraftOrderPacket));
        RegisterPacket(CSOffsets.CSUpdateFavoriteCraftsPacket, 1, typeof(CSUpdateFavoriteCraftsPacket));
        RegisterPacket(CSOffsets.CSRunZoneCommand, 1, typeof(CSRunZoneCommand));
        RegisterPacket(CSOffsets.CSICSBuyCountRequestPacket, 1, typeof(CSICSBuyCountRequestPacket));
        RegisterPacket(CSOffsets.CSEnsembleAcceptPacket, 1, typeof(CSEnsembleAcceptPacket));
        RegisterPacket(CSOffsets.CSEnsembleRejectPacket, 1, typeof(CSEnsembleRejectPacket));
        RegisterPacket(CSOffsets.CSRankRankerAppearance, 1, typeof(CSRankRankerAppearance));
        RegisterPacket(CSOffsets.CSAntibotTransferWorldPacket, 1, typeof(CSAntibotTransferWorldPacket));
        RegisterPacket(CSOffsets.CSLoadAccountAttendancePacket, 1, typeof(CSLoadAccountAttendancePacket));
        RegisterPacket(CSOffsets.CSAddReportBadUser, 1, typeof(CSAddReportBadUser));
        RegisterPacket(CSOffsets.CSRequestBadUserList, 1, typeof(CSRequestBadUserList));
        RegisterPacket(CSOffsets.CSReportBadwordUser, 1, typeof(CSReportBadwordUser));
        RegisterPacket(CSOffsets.CSReportSpamMailPacket, 1, typeof(CSReportSpamMailPacket));
        RegisterPacket(CSOffsets.CSRevenueSanction, 1, typeof(CSRevenueSanction));
        RegisterPacket(CSOffsets.CSRequestEventInfoCountPacket, 1, typeof(CSRequestEventInfoCountPacket));
        RegisterPacket(CSOffsets.CSRequestEventMainInfoPacket, 1, typeof(CSRequestEventMainInfoPacket));
        RegisterPacket(CSOffsets.CSBlessUthstinExtendMaxStatsPacket, 1, typeof(CSBlessUthstinExtendMaxStatsPacket));
        RegisterPacket(CSOffsets.CSBlessUthstinExpandPagePacket, 1, typeof(CSBlessUthstinExpandPagePacket));
        RegisterPacket(CSOffsets.CSHeirLevlUpPacket, 1, typeof(CSHeirLevlUpPacket));
        RegisterPacket(CSOffsets.CSActivateHeirSkillPacket, 1, typeof(CSActivateHeirSkillPacket));
        RegisterPacket(CSOffsets.CSResetHeirSkillPacket, 1, typeof(CSResetHeirSkillPacket));
        RegisterPacket(CSOffsets.CSDepartToForeignServerPacket, 1, typeof(CSDepartToForeignServerPacket));
        RegisterPacket(CSOffsets.CSArrivedFromAbroadPacket, 1, typeof(CSArrivedFromAbroadPacket));
        RegisterPacket(CSOffsets.CSRaidRecruitDelPacket, 1, typeof(CSRaidRecruitDelPacket));
        RegisterPacket(CSOffsets.CSEquipSlotReinforceLevelUpPacket, 1, typeof(CSEquipSlotReinforceLevelUpPacket));
        RegisterPacket(CSOffsets.CSRequestSquadListPacket, 1, typeof(CSRequestSquadListPacket));
        RegisterPacket(CSOffsets.CSCreateSquadPacket, 1, typeof(CSCreateSquadPacket));
        RegisterPacket(CSOffsets.CSDisbandSquadPacket, 1, typeof(CSDisbandSquadPacket));
        RegisterPacket(CSOffsets.CSReadySquadPacket, 1, typeof(CSReadySquadPacket));
        RegisterPacket(CSOffsets.CSJoinSquadMemberPacket, 1, typeof(CSJoinSquadMemberPacket));
        RegisterPacket(CSOffsets.CSRefuseSquadInvitation, 1, typeof(CSRefuseSquadInvitation));
        RegisterPacket(CSOffsets.CSLeaveSquadMemberPacket, 1, typeof(CSLeaveSquadMemberPacket));
        RegisterPacket(CSOffsets.CSInviteSquadMemberPacket, 1, typeof(CSInviteSquadMemberPacket));
        RegisterPacket(CSOffsets.CSApplySquadMatchingPacket, 1, typeof(CSApplySquadMatchingPacket));
        RegisterPacket(CSOffsets.CSChangeSquadMemberRolePacket, 1, typeof(CSChangeSquadMemberRolePacket));
        RegisterPacket(CSOffsets.CSExpelSquadMemberPacket, 1, typeof(CSExpelSquadMemberPacket));
        RegisterPacket(CSOffsets.CSIgnoreMinGameSizePacket, 1, typeof(CSIgnoreMinGameSizePacket));
        RegisterPacket(CSOffsets.CSDelegateSquadLeaderPacket, 1, typeof(CSDelegateSquadLeaderPacket));
        RegisterPacket(CSOffsets.CSChangeSquadOpenTypePacket, 1, typeof(CSChangeSquadOpenTypePacket));
        RegisterPacket(CSOffsets.CSSiegeRaidRegisterListRequestPacket, 1, typeof(CSSiegeRaidRegisterListRequestPacket));
        RegisterPacket(CSOffsets.CSSiegeRaidTeamInfoRequest, 1, typeof(CSSiegeRaidTeamInfoRequest));
        RegisterPacket(CSOffsets.CSAllSiegeRaidTeamInfoRequest, 1, typeof(CSAllSiegeRaidTeamInfoRequest));
        RegisterPacket(CSOffsets.CSNextSiegeInfoPacket, 1, typeof(CSNextSiegeInfoPacket));
        RegisterPacket(CSOffsets.CSTakeReturnAccountItemPacket, 1, typeof(CSTakeReturnAccountItemPacket));
        RegisterPacket(CSOffsets.CSUnbindButlerPacket, 1, typeof(CSUnbindButlerPacket));
        RegisterPacket(CSOffsets.CSExpandButlerUsableSlotPacket, 1, typeof(CSExpandButlerUsableSlotPacket));
        RegisterPacket(CSOffsets.CSChangeButlerNamePacket, 1, typeof(CSChangeButlerNamePacket));
        RegisterPacket(CSOffsets.CSArchePassUpgradePacket, 1, typeof(CSArchePassUpgradePacket));
        RegisterPacket(CSOffsets.CSShowCurrentWorld, 1, typeof(CSShowCurrentWorld));
        RegisterPacket(CSOffsets.CSContentRosterSavePacket, 1, typeof(CSContentRosterSavePacket));
        RegisterPacket(CSOffsets.CSRandomShopInfoRefreshPacket, 1, typeof(CSRandomShopInfoRefreshPacket));
        RegisterPacket(CSOffsets.CSSelectInstanceDifficultPacket, 1, typeof(CSSelectInstanceDifficultPacket));

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

        Logger.Info("Network started");
    }

    public void Stop()
    {
        if (_server?.IsStarted ?? false)
            _server.Stop();

        Logger.Info("Network stopped");
    }

    private void RegisterPacket(uint type, byte level, Type classType)
    {
        _handler.RegisterPacket(type, level, classType);
    }
}

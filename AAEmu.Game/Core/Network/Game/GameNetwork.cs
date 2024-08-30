using System;
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
    private GameProtocolHandler _handler;
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

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

        RegisterPacket(CSOffsets.CSTodayAssignmentPacket, 5, typeof(CSTodayAssignmentPacket));
        //RegisterPacket(CSOffsets.CSRequestSkipClientDrivenIndunPacket, 5, typeof(CSRequestSkipClientDrivenIndunPacket));
        //RegisterPacket(CSOffsets.CSRemoveClientNpcPacket, 5, typeof(CSRemoveClientNpcPacket));
        RegisterPacket(CSOffsets.CSMoveUnitPacket, 5, typeof(CSMoveUnitPacket));
        RegisterPacket(CSOffsets.CSCofferInteractionPacket, 5, typeof(CSCofferInteractionPacket));
        RegisterPacket(CSOffsets.CSRequestCommonFarmListPacket, 5, typeof(CSRequestCommonFarmListPacket));
        RegisterPacket(CSOffsets.CSChallengeDuelPacket, 5, typeof(CSChallengeDuelPacket));
        RegisterPacket(CSOffsets.CSStartDuelPacket, 5, typeof(CSStartDuelPacket));
        //RegisterPacket(CSOffsets.CSHeroRankingListPacket, 5, typeof(CSHeroRankingListPacket));
        //RegisterPacket(CSOffsets.CSHeroCandidateListPacket, 5, typeof(CSHeroCandidateListPacket));
        //RegisterPacket(CSOffsets.CSHeroAbstainPacket, 5, typeof(CSHeroAbstainPacket));
        //RegisterPacket(CSOffsets.CSHeroVotingPacket, 5, typeof(CSHeroVotingPacket));
        RegisterPacket(CSOffsets.CSConvertItemLookPacket, 5, typeof(CSConvertItemLookPacket));
        //RegisterPacket(CSOffsets.CSConvertItemLook2Packet, 5, typeof(CSConvertItemLook2Packet));
        RegisterPacket(CSOffsets.CSSetPingPosPacket, 5, typeof(CSSetPingPosPacket));
        //RegisterPacket(CSOffsets.CSUpdateExploredRegionPacket, 5, typeof(CSUpdateExploredRegionPacket));
        RegisterPacket(CSOffsets.CSICSMoneyRequestPacket, 5, typeof(CSICSMoneyRequestPacket));
        RegisterPacket(CSOffsets.CSPremiumServiceBuyPacket, 5, typeof(CSPremiumServiceBuyPacket));
        //RegisterPacket(CSOffsets.CSSetVisiblePremiumServicePacket, 5, typeof(CSSetVisiblePremiumServicePacket));
        //RegisterPacket(CSOffsets.CSAddReputationPacket, 5, typeof(CSAddReputationPacket));
        //RegisterPacket(CSOffsets.CSUnknown0x80Packet, 5, typeof(CSUnknown0x80Packet));
        RegisterPacket(CSOffsets.CSGetResidentDescPacket, 5, typeof(CSGetResidentDescPacket));
        RegisterPacket(CSOffsets.CSRefreshResidentMembersPacket, 5, typeof(CSRefreshResidentMembersPacket));
        RegisterPacket(CSOffsets.CSGetResidentZoneListPacket, 5, typeof(CSGetResidentZoneListPacket));
        RegisterPacket(CSOffsets.CSResidentFireNuonsArrowPacket, 5, typeof(CSResidentFireNuonsArrowPacket));
        //RegisterPacket(CSOffsets.CSUseBlessUthstinInitStatsPacket, 5, typeof(CSUseBlessUthstinInitStatsPacket));
        //RegisterPacket(CSOffsets.CSUseBlessUthstinExtendMaxStatsPacket, 5, typeof(CSUseBlessUthstinExtendMaxStatsPacket));
        //RegisterPacket(CSOffsets.CSBlessUthstinUseApplyStatsItemPacket, 5, typeof(CSBlessUthstinUseApplyStatsItemPacket));
        //RegisterPacket(CSOffsets.CSBlessUthstinApplyStatsPacket, 5, typeof(CSBlessUthstinApplyStatsPacket));
        RegisterPacket(CSOffsets.CSEventCenterAddAttendancePacket, 5, typeof(CSEventCenterAddAttendancePacket));
        RegisterPacket(CSOffsets.CSRequestGameEventInfoPacket, 5, typeof(CSRequestGameEventInfoPacket));
        //RegisterPacket(CSOffsets.CSUnknown0x0cbPacket, 5, typeof(CSUnknown0x0cbPacket));
        //RegisterPacket(CSOffsets.CSUnknown0x0aPacket, 5, typeof(CSUnknown0x0aPacket));
        RegisterPacket(CSOffsets.CSChangeMateNamePacket, 5, typeof(CSChangeMateNamePacket));
        //RegisterPacket(CSOffsets.CSSendNationMemberCountListPacket, 5, typeof(CSSendNationMemberCountListPacket));
        RegisterPacket(CSOffsets.CSNationSendExpeditionImmigrationAcceptRejectPacket, 5, typeof(CSNationSendExpeditionImmigrationAcceptRejectPacket));
        //RegisterPacket(CSOffsets.CSSendExpeditionImmigrationListPacket, 5, typeof(CSSendExpeditionImmigrationListPacket));
        //RegisterPacket(CSOffsets.CSSendRelationFriendPacket, 5, typeof(CSSendRelationFriendPacket));
        //RegisterPacket(CSOffsets.CSSendRelationVotePacket, 5, typeof(CSSendRelationVotePacket));
        //RegisterPacket(CSOffsets.CSSendNationInfoSetPacket, 5, typeof(CSSendNationInfoSetPacket));
        RegisterPacket(CSOffsets.CSRankCharacterPacket, 5, typeof(CSRankCharacterPacket));
        //RegisterPacket(CSOffsets.CSRankSnapshotPacket, 5, typeof(CSRankSnapshotPacket));
        RegisterPacket(CSOffsets.CSHeroRequestRankDataPacket, 5, typeof(CSHeroRequestRankDataPacket));
        //RegisterPacket(CSOffsets.CSGetRankerInformationPacket, 5, typeof(CSGetRankerInformationPacket));
        //RegisterPacket(CSOffsets.CSRequestRankerAppearancePacket, 5, typeof(CSRequestRankerAppearancePacket));
        //RegisterPacket(CSOffsets.CSRequestSecondPassKeyTablesPacket, 5, typeof(CSRequestSecondPassKeyTablesPacket));
        //RegisterPacket(CSOffsets.CSCreateSecondPassPacket, 5, typeof(CSCreateSecondPassPacket));
        //RegisterPacket(CSOffsets.CSChangeSecondPassPacket, 5, typeof(CSChangeSecondPassPacket));
        //RegisterPacket(CSOffsets.CSClearSecondPassPacket, 5, typeof(CSClearSecondPassPacket));
        //RegisterPacket(CSOffsets.CSCheckSecondPassPacket, 5, typeof(CSCheckSecondPassPacket));
        RegisterPacket(CSOffsets.CSReplyImprisonOrTrialPacket, 5, typeof(CSReplyImprisonOrTrialPacket));
        RegisterPacket(CSOffsets.CSSkipFinalStatementPacket, 5, typeof(CSSkipFinalStatementPacket));
        RegisterPacket(CSOffsets.CSReplyInviteJuryPacket, 5, typeof(CSReplyInviteJuryPacket));
        RegisterPacket(CSOffsets.CSJurySummonedPacket, 5, typeof(CSJurySummonedPacket));
        RegisterPacket(CSOffsets.CSJuryEndTestimonyPacket, 5, typeof(CSJuryEndTestimonyPacket));
        RegisterPacket(CSOffsets.CSCancelTrialPacket, 5, typeof(CSCancelTrialPacket));
        //RegisterPacket(CSOffsets.CSJurySentencePacket, 5, typeof(CSJurySentencePacket));
        RegisterPacket(CSOffsets.CSReportCrimePacket, 5, typeof(CSReportCrimePacket));
        RegisterPacket(CSOffsets.CSRequestJuryWaitingNumberPacket, 5, typeof(CSRequestJuryWaitingNumberPacket));
        //RegisterPacket(CSOffsets.CSRequestSetBountyPacket, 5, typeof(CSRequestSetBountyPacket));
        //RegisterPacket(CSOffsets.CSUpdateBountyPacket, 5, typeof(CSUpdateBountyPacket));
        //RegisterPacket(CSOffsets.CSTrialReportBadUserPacket, 5, typeof(CSTrialReportBadUserPacket));
        //RegisterPacket(CSOffsets.CSTrialRequestBadUserListPacket, 5, typeof(CSTrialRequestBadUserListPacket));
        //RegisterPacket(CSOffsets.CSsUnknown0x146Packet, 5, typeof(CSsUnknown0x146Packet));
        RegisterPacket(CSOffsets.CSSendUserMusicPacket, 5, typeof(CSSendUserMusicPacket));
        RegisterPacket(CSOffsets.CSSaveUserMusicNotesPacket, 5, typeof(CSSaveUserMusicNotesPacket));
        RegisterPacket(CSOffsets.CSRequestMusicNotesPacket, 5, typeof(CSRequestMusicNotesPacket));
        RegisterPacket(CSOffsets.CSPauseUserMusicPacket, 5, typeof(CSPauseUserMusicPacket));
        //RegisterPacket(CSOffsets.CSUnknown0x5ePacket, 5, typeof(CSUnknown0x5ePacket));
        //RegisterPacket(CSOffsets.CSBagHandleSelectiveItemsPacket, 5, typeof(CSBagHandleSelectiveItemsPacket));
        RegisterPacket(CSOffsets.CSSkillControllerStatePacket, 5, typeof(CSSkillControllerStatePacket));
        RegisterPacket(CSOffsets.CSMountMatePacket, 5, typeof(CSMountMatePacket));
        RegisterPacket(CSOffsets.CSLeaveWorldPacket, 5, typeof(CSLeaveWorldPacket));
        RegisterPacket(CSOffsets.CSCancelLeaveWorldPacket, 5, typeof(CSCancelLeaveWorldPacket));
        RegisterPacket(CSOffsets.CSRequestSpecialtyCurrentPacket, 5, typeof(CSRequestSpecialtyCurrentPacket));
        RegisterPacket(CSOffsets.CSIdleStatusPacket, 5, typeof(CSIdleStatusPacket));
        //RegisterPacket(CSOffsets.CSChangeClientNpcTargetPacket, 5, typeof(CSChangeClientNpcTargetPacket));
        RegisterPacket(CSOffsets.CSCompletedCinemaPacket, 5, typeof(CSCompletedCinemaPacket));
        //RegisterPacket(CSOffsets.CSCheckDemoModePacket, 5, typeof(CSCheckDemoModePacket));
        //RegisterPacket(CSOffsets.CSDemoCharResetPacket, 5, typeof(CSDemoCharResetPacket));
        RegisterPacket(CSOffsets.CSConsoleCmdUsedPacket, 5, typeof(CSConsoleCmdUsedPacket));
        RegisterPacket(CSOffsets.CSEditorGameModePacket, 5, typeof(CSEditorGameModePacket));
        RegisterPacket(CSOffsets.CSTeleportEndedPacket, 5, typeof(CSTeleportEndedPacket));
        //RegisterPacket(CSOffsets.CSInteractGimmickPacket, 5, typeof(CSInteractGimmickPacket));
        //RegisterPacket(CSOffsets.CSWorldRaycastingPacket, 5, typeof(CSWorldRaycastingPacket));
        RegisterPacket(CSOffsets.CSOpenExpeditionImmigrationRequestPacket, 5, typeof(CSOpenExpeditionImmigrationRequestPacket));
        RegisterPacket(CSOffsets.CSNationGetNationNamePacket, 5, typeof(CSNationGetNationNamePacket));
        RegisterPacket(CSOffsets.CSRefreshInCharacterListPacket, 5, typeof(CSRefreshInCharacterListPacket));
        RegisterPacket(CSOffsets.CSDeleteCharacterPacket, 5, typeof(CSDeleteCharacterPacket));
        RegisterPacket(CSOffsets.CSCancelCharacterDeletePacket, 5, typeof(CSCancelCharacterDeletePacket));
        RegisterPacket(CSOffsets.CSSelectCharacterPacket, 5, typeof(CSSelectCharacterPacket));
        RegisterPacket(CSOffsets.CSCharacterConnectionRestrictPacket, 5, typeof(CSCharacterConnectionRestrictPacket));
        RegisterPacket(CSOffsets.CSNotifyInGamePacket, 5, typeof(CSNotifyInGamePacket));
        RegisterPacket(CSOffsets.CSNotifyInGameCompletedPacket, 5, typeof(CSNotifyInGameCompletedPacket));
        RegisterPacket(CSOffsets.CSChangeTargetPacket, 5, typeof(CSChangeTargetPacket));
        //RegisterPacket(CSOffsets.CSUnknown0x8bPacket, 5, typeof(CSUnknown0x8bPacket));
        //RegisterPacket(CSOffsets.CSGetSiegeAuctionBidCurrencyPacket, 5, typeof(CSGetSiegeAuctionBidCurrencyPacket));
        RegisterPacket(CSOffsets.CSResurrectCharacterPacket, 5, typeof(CSResurrectCharacterPacket));
        RegisterPacket(CSOffsets.CSCriminalLockedPacket, 5, typeof(CSCriminalLockedPacket));
        RegisterPacket(CSOffsets.CSExpressEmotionPacket, 5, typeof(CSExpressEmotionPacket));
        RegisterPacket(CSOffsets.CSUnhangPacket, 5, typeof(CSUnhangPacket));
        RegisterPacket(CSOffsets.CSChangeAppellationPacket, 5, typeof(CSChangeAppellationPacket));
        RegisterPacket(CSOffsets.CSStartedCinemaPacket, 5, typeof(CSStartedCinemaPacket));
        RegisterPacket(CSOffsets.CSBroadcastVisualOptionPacket, 5, typeof(CSBroadcastVisualOptionPacket));
        RegisterPacket(CSOffsets.CSBroadcastOpenEquipInfoPacket, 5, typeof(CSBroadcastOpenEquipInfoPacket));
        RegisterPacket(CSOffsets.CSRestrictCheckPacket, 5, typeof(CSRestrictCheckPacket));
        RegisterPacket(CSOffsets.CSICSMenuListRequestPacket, 5, typeof(CSICSMenuListPacket)); //CSICSMenuListRequestPacket
        RegisterPacket(CSOffsets.CSICSGoodsListRequestPacket, 5, typeof(CSICSGoodsListPacket)); //CSICSGoodsListRequestPacket
        RegisterPacket(CSOffsets.CSICSBuyGoodRequestPacket, 5, typeof(CSICSBuyGoodPacket)); // CSICSBuyGoodRequestPacket
        RegisterPacket(CSOffsets.CSPremiumServiceMsgPacket, 5, typeof(CSPremiumServiceMsgPacket));
        //RegisterPacket(CSOffsets.CSProtectSensitiveOperationPacket, 5, typeof(CSProtectSensitiveOperationPacket));
        //RegisterPacket(CSOffsets.CSCancelSensitiveOperationVerifyPacket, 5, typeof(CSCancelSensitiveOperationVerifyPacket));
        //RegisterPacket(CSOffsets.CSAntibotDataPacket, 5, typeof(CSAntibotDataPacket));
        //RegisterPacket(CSOffsets.CSBuyAaPointPacket, 5, typeof(CSBuyAaPointPacket));
        //RegisterPacket(CSOffsets.CSRequestTencentFatigueInfoPacket, 5, typeof(CSRequestTencentFatigueInfoPacket));
        RegisterPacket(CSOffsets.CSPremiumServiceListPacket, 5, typeof(CSPremiumServiceListPacket));
        //RegisterPacket(CSOffsets.CSRequestSysInstanceIndexPacket, 5, typeof(CSRequestSysInstanceIndexPacket));
        RegisterPacket(CSOffsets.CSQuitResponsePacket, 5, typeof(CSQuitResponsePacket));
        RegisterPacket(CSOffsets.CSSecurityReportPacket, 5, typeof(CSSecurityReportPacket));
        RegisterPacket(CSOffsets.CSEnprotectStubCallResponsePacket, 5, typeof(CSEnprotectStubCallResponsePacket));
        RegisterPacket(CSOffsets.CSRepresentCharacterPacket, 5, typeof(CSRepresentCharacterPacket));
        //RegisterPacket(CSOffsets.CSPacketUnknown0x0aaPacket, 5, typeof(CSPacketUnknown0x0aaPacket));
        RegisterPacket(CSOffsets.CSCheckDemoModePacket, 5, typeof(CSCheckDemoModePacket));
        RegisterPacket(CSOffsets.CSCreateCharacterPacket, 5, typeof(CSCreateCharacterPacket));
        RegisterPacket(CSOffsets.CSEditCharacterPacket, 5, typeof(CSEditCharacterPacket));
        RegisterPacket(CSOffsets.CSSpawnCharacterPacket, 5, typeof(CSSpawnCharacterPacket));
        RegisterPacket(CSOffsets.CSNotifySubZonePacket, 5, typeof(CSNotifySubZonePacket));
        RegisterPacket(CSOffsets.CSCompletedTutorialPacket, 5, typeof(CSCompletedTutorialPacket));
        RegisterPacket(CSOffsets.CSRequestUIDataPacket, 5, typeof(CSRequestUIDataPacket));
        RegisterPacket(CSOffsets.CSSaveUIDataPacket, 5, typeof(CSSaveUIDataPacket));
        RegisterPacket(CSOffsets.CSBeautyShopDataPacket, 5, typeof(CSBeautyshopDataPacket)); // CSBeautyShopDataPacket
        RegisterPacket(CSOffsets.CSDominionUpdateTaxRatePacket, 5, typeof(CSDominionUpdateTaxRatePacket));
        RegisterPacket(CSOffsets.CSDominionUpdateNationalTaxRatePacket, 5, typeof(CSDominionUpdateNationalTaxRatePacket));
        //RegisterPacket(CSOffsets.CSRequestCharacterBriefPacket, 5, typeof(CSRequestCharacterBriefPacket));
        RegisterPacket(CSOffsets.CSExpeditionCreatePacket, 5, typeof(CSExpeditionCreatePacket));
        RegisterPacket(CSOffsets.CSExpeditionChangeRolePolicyPacket, 5, typeof(CSExpeditionChangeRolePolicyPacket));
        RegisterPacket(CSOffsets.CSExpeditionChangeMemberRolePacket, 5, typeof(CSExpeditionChangeMemberRolePacket));
        RegisterPacket(CSOffsets.CSExpeditionChangeOwnerPacket, 5, typeof(CSExpeditionChangeOwnerPacket));
        RegisterPacket(CSOffsets.CSChangeNationOwnerPacket, 5, typeof(CSChangeNationOwnerPacket));
        RegisterPacket(CSOffsets.CSExpeditionRenamePacket, 5, typeof(CSExpeditionRenamePacket));
        RegisterPacket(CSOffsets.CSExpeditionDismissPacket, 5, typeof(CSExpeditionDismissPacket));
        RegisterPacket(CSOffsets.CSExpeditionInvitePacket, 5, typeof(CSExpeditionInvitePacket));
        RegisterPacket(CSOffsets.CSExpeditionLeavePacket, 5, typeof(CSExpeditionLeavePacket));
        RegisterPacket(CSOffsets.CSExpeditionKickPacket, 5, typeof(CSExpeditionKickPacket));
        RegisterPacket(CSOffsets.CSExpeditionBeginnerJoinPacket, 5, typeof(CSExpeditionBeginnerJoinPacket));
        RegisterPacket(CSOffsets.CSDeclareExpeditionWarPacket, 5, typeof(CSDeclareExpeditionWarPacket));
        RegisterPacket(CSOffsets.CSFactionGetDeclarationMoneyPacket, 5, typeof(CSFactionGetDeclarationMoneyPacket));
        //RegisterPacket(CSOffsets.CSUnknown0x0a3Packet, 5, typeof(CSUnknown0x0a3Packet));
        RegisterPacket(CSOffsets.CSFactionGetExpeditionWarHistoryPacket, 5, typeof(CSFactionGetExpeditionWarHistoryPacket));
        RegisterPacket(CSOffsets.CSFactionCancelProtectionPacket, 5, typeof(CSFactionCancelProtectionPacket));
        RegisterPacket(CSOffsets.CSFactionImmigrationInvitePacket, 5, typeof(CSFactionImmigrationInvitePacket));
        RegisterPacket(CSOffsets.CSFactionImmigrationInviteReplyPacket, 5, typeof(CSFactionImmigrationInviteReplyPacket));
        RegisterPacket(CSOffsets.CSFactionImmigrateToOriginPacket, 5, typeof(CSFactionImmigrateToOriginPacket));
        RegisterPacket(CSOffsets.CSFactionKickToOriginPacket, 5, typeof(CSFactionKickToOriginPacket));
        RegisterPacket(CSOffsets.CSFactionMobilizationOrderPacket, 5, typeof(CSFactionMobilizationOrderPacket));
        RegisterPacket(CSOffsets.CSFactionCheckExpeditionExpNextDayPacket, 5, typeof(CSFactionCheckExpeditionExpNextDayPacket));
        RegisterPacket(CSOffsets.CSFactionSetExpeditionLevelUpPacket, 5, typeof(CSFactionSetExpeditionLevelUpPacket));
        RegisterPacket(CSOffsets.CSFactionSetExpeditionMotdPacket, 5, typeof(CSFactionSetExpeditionMotdPacket));
        RegisterPacket(CSOffsets.CSFactionSetMyExpeditionInterestPacket, 5, typeof(CSFactionSetMyExpeditionInterestPacket));
        //RegisterPacket(CSOffsets.CSUnknown0x60Packet, 5, typeof(CSUnknown0x60Packet));
        RegisterPacket(CSOffsets.CSExpeditionReplyInvitationPacket, 5, typeof(CSExpeditionReplyInvitationPacket));
        RegisterPacket(CSOffsets.CSFamilyInviteMemberPacket, 5, typeof(CSFamilyInviteMemberPacket));
        RegisterPacket(CSOffsets.CSFamilyLeavePacket, 5, typeof(CSFamilyLeavePacket));
        RegisterPacket(CSOffsets.CSFamilyKickPacket, 5, typeof(CSFamilyKickPacket));
        RegisterPacket(CSOffsets.CSFamilyChangeTitlePacket, 5, typeof(CSFamilyChangeTitlePacket));
        RegisterPacket(CSOffsets.CSFamilyChangeOwnerPacket, 5, typeof(CSFamilyChangeOwnerPacket));
        //RegisterPacket(CSOffsets.CSFamilySetNamePacket, 5, typeof(CSFamilySetNamePacket));
        //RegisterPacket(CSOffsets.CSFamilySetContentPacket, 5, typeof(CSFamilySetContentPacket));
        //RegisterPacket(CSOffsets.CSFamilyOpenIncreaseMemberPacket, 5, typeof(CSFamilyOpenIncreaseMemberPacket));
        //RegisterPacket(CSOffsets.CSFamilyChangeMemberRolePacket, 5, typeof(CSFamilyChangeMemberRolePacket));
        RegisterPacket(CSOffsets.CSFamilyReplyInvitationPacket, 5, typeof(CSFamilyReplyInvitationPacket));
        RegisterPacket(CSOffsets.CSAddFriendPacket, 5, typeof(CSAddFriendPacket));
        RegisterPacket(CSOffsets.CSDeleteFriendPacket, 5, typeof(CSDeleteFriendPacket));
        RegisterPacket(CSOffsets.CSAddBlockedUserPacket, 5, typeof(CSAddBlockedUserPacket));
        RegisterPacket(CSOffsets.CSDeleteBlockedUserPacket, 5, typeof(CSDeleteBlockedUserPacket));
        RegisterPacket(CSOffsets.CSInviteAreaToTeamPacket, 5, typeof(CSInviteAreaToTeamPacket));
        RegisterPacket(CSOffsets.CSInviteToTeamPacket, 5, typeof(CSInviteToTeamPacket));
        RegisterPacket(CSOffsets.CSReplyToJoinTeamPacket, 5, typeof(CSReplyToJoinTeamPacket));
        RegisterPacket(CSOffsets.CSLeaveTeamPacket, 5, typeof(CSLeaveTeamPacket));
        RegisterPacket(CSOffsets.CSKickTeamMemberPacket, 5, typeof(CSKickTeamMemberPacket));
        RegisterPacket(CSOffsets.CSMakeTeamOwnerPacket, 5, typeof(CSMakeTeamOwnerPacket));
        RegisterPacket(CSOffsets.CSConvertToRaidTeamPacket, 5, typeof(CSConvertToRaidTeamPacket));
        RegisterPacket(CSOffsets.CSMoveTeamMemberPacket, 5, typeof(CSMoveTeamMemberPacket));
        RegisterPacket(CSOffsets.CSDismissTeamPacket, 5, typeof(CSDismissTeamPacket));
        RegisterPacket(CSOffsets.CSSetTeamMemberRolePacket, 5, typeof(CSSetTeamMemberRolePacket));
        RegisterPacket(CSOffsets.CSSetOverHeadMarkerPacket, 5, typeof(CSSetOverHeadMarkerPacket));
        RegisterPacket(CSOffsets.CSAskRiskyTeamActionPacket, 5, typeof(CSAskRiskyTeamActionPacket));
        //RegisterPacket(CSOffsets.CSTeamAcceptHandOverOwnerPacket, 5, typeof(CSTeamAcceptHandOverOwnerPacket));
        //RegisterPacket(CSOffsets.CSTeamAcceptOwnerOfferPacket, 5, typeof(CSTeamAcceptOwnerOfferPacket));
        RegisterPacket(CSOffsets.CSChangeLootingRulePacket, 5, typeof(CSChangeLootingRulePacket));
        //RegisterPacket(CSOffsets.CSRenameCharacterPacket, 5, typeof(CSRenameCharacterPacket));
        RegisterPacket(CSOffsets.CSUpdateActionSlotPacket, 5, typeof(CSUpdateActionSlotPacket));
        RegisterPacket(CSOffsets.CSUsePortalPacket, 5, typeof(CSUsePortalPacket));
        RegisterPacket(CSOffsets.CSUpgradeExpertLimitPacket, 5, typeof(CSUpgradeExpertLimitPacket));
        RegisterPacket(CSOffsets.CSDowngradeExpertLimitPacket, 5, typeof(CSDowngradeExpertLimitPacket));
        RegisterPacket(CSOffsets.CSExpandExpertPacket, 5, typeof(CSExpandExpertPacket));
        //RegisterPacket(CSOffsets.CSEnterSysInstancePacket, 5, typeof(CSEnterSysInstancePacket));
        //RegisterPacket(CSOffsets.CSEndPortalInteractionPacket, 5, typeof(CSEndPortalInteractionPacket));
        RegisterPacket(CSOffsets.CSCreateShipyardPacket, 5, typeof(CSCreateShipyardPacket));
        RegisterPacket(CSOffsets.CSCreateHousePacket, 5, typeof(CSCreateHousePacket));
        RegisterPacket(CSOffsets.CSLeaveBeautyShopPacket, 5, typeof(CSLeaveBeautyShopPacket));
        RegisterPacket(CSOffsets.CSConstructHouseTaxPacket, 5, typeof(CSConstructHouseTaxPacket));
        RegisterPacket(CSOffsets.CSChangeHouseNamePacket, 5, typeof(CSChangeHouseNamePacket));
        RegisterPacket(CSOffsets.CSChangeHousePermissionPacket, 5, typeof(CSChangeHousePermissionPacket));
        RegisterPacket(CSOffsets.CSRequestHouseTaxPacket, 5, typeof(CSRequestHouseTaxPacket));
        RegisterPacket(CSOffsets.CSPerpayHouseTaxPacket, 5, typeof(CSPerpayHouseTaxPacket));
        RegisterPacket(CSOffsets.CSAllowRecoverPacket, 5, typeof(CSAllowHousingRecoverPacket)); // CSAllowRecoverPacket
        RegisterPacket(CSOffsets.CSSellHouseCancelPacket, 5, typeof(CSSellHouseCancelPacket));
        RegisterPacket(CSOffsets.CSDecorateHousePacket, 5, typeof(CSDecorateHousePacket));
        RegisterPacket(CSOffsets.CSSellHousePacket, 5, typeof(CSSellHousePacket));
        RegisterPacket(CSOffsets.CSBuyHousePacket, 5, typeof(CSBuyHousePacket));
        //RegisterPacket(CSOffsets.CSRotateHousePacket, 5, typeof(CSRotateHousePacket));
        RegisterPacket(CSOffsets.CSRemoveMatePacket, 5, typeof(CSRemoveMatePacket));
        RegisterPacket(CSOffsets.CSChangeMateTargetPacket, 5, typeof(CSChangeMateTargetPacket));
        RegisterPacket(CSOffsets.CSChangeMateUserStatePacket, 5, typeof(CSChangeMateUserStatePacket));
        RegisterPacket(CSOffsets.CSSpawnSlavePacket, 5, typeof(CSSpawnSlavePacket));
        RegisterPacket(CSOffsets.CSDespawnSlavePacket, 5, typeof(CSDespawnSlavePacket));
        RegisterPacket(CSOffsets.CSDestroySlavePacket, 5, typeof(CSDestroySlavePacket));
        RegisterPacket(CSOffsets.CSBindSlavePacket, 5, typeof(CSBindSlavePacket));
        RegisterPacket(CSOffsets.CSDiscardSlavePacket, 5, typeof(CSDiscardSlavePacket));
        RegisterPacket(CSOffsets.CSBoardingTransferPacket, 5, typeof(CSBoardingTransferPacket));
        RegisterPacket(CSOffsets.CSTurretStatePacket, 5, typeof(CSTurretStatePacket));
        RegisterPacket(CSOffsets.CSCreateSkillControllerPacket, 5, typeof(CSCreateSkillControllerPacket));
        RegisterPacket(CSOffsets.CSJoinTrialAudiencePacket, 5, typeof(CSJoinTrialAudiencePacket));
        RegisterPacket(CSOffsets.CSLeaveTrialAudiencePacket, 5, typeof(CSLeaveTrialAudiencePacket));
        RegisterPacket(CSOffsets.CSUnMountMatePacket, 5, typeof(CSUnMountMatePacket));
        RegisterPacket(CSOffsets.CSDetachFromDoodadPacket, 5, typeof(CSDetachFromDoodadPacket)); // CSUnbondDoodadPacket
        RegisterPacket(CSOffsets.CSInstanceLoadedPacket, 5, typeof(CSInstanceLoadedPacket));
        RegisterPacket(CSOffsets.CSApplyToInstantGamePacket, 5, typeof(CSApplyToInstantGamePacket));
        RegisterPacket(CSOffsets.CSCancelInstantGamePacket, 5, typeof(CSCancelInstantGamePacket));
        RegisterPacket(CSOffsets.CSJoinInstantGamePacket, 5, typeof(CSJoinInstantGamePacket));
        RegisterPacket(CSOffsets.CSEnteredInstantGameWorldPacket, 5, typeof(CSEnteredInstantGameWorldPacket));
        RegisterPacket(CSOffsets.CSLeaveInstantGamePacket, 5, typeof(CSLeaveInstantGamePacket));
        //RegisterPacket(CSOffsets.CSPickBuffInstantGamePacket, 5, typeof(CSPickBuffInstantGamePacket));
        //RegisterPacket(CSOffsets.CSBattlefieldPickshipPacket, 5, typeof(CSBattlefieldPickshipPacket));
        RegisterPacket(CSOffsets.CSRequestPermissionToPlayCinemaForDirectingModePacket, 5, typeof(CSRequestPermissionToPlayCinemaForDirectingModePacket));
        RegisterPacket(CSOffsets.CSStartQuestContextPacket, 5, typeof(CSStartQuestContextPacket));
        RegisterPacket(CSOffsets.CSCompleteQuestContextPacket, 5, typeof(CSCompleteQuestContextPacket));
        RegisterPacket(CSOffsets.CSDropQuestContextPacket, 5, typeof(CSDropQuestContextPacket));
        RegisterPacket(CSOffsets.CSQuestTalkMadePacket, 5, typeof(CSQuestTalkMadePacket));
        //RegisterPacket(CSOffsets.CSQuestStartWithParamPacket, 5, typeof(CSQuestStartWithParamPacket));
        RegisterPacket(CSOffsets.CSTryQuestCompleteAsLetItDonePacket, 5, typeof(CSTryQuestCompleteAsLetItDonePacket));
        RegisterPacket(CSOffsets.CSRestartMainQuestPacket, 5, typeof(CSRestartMainQuestPacket));
        RegisterPacket(CSOffsets.CSLearnSkillPacket, 5, typeof(CSLearnSkillPacket));
        RegisterPacket(CSOffsets.CSLearnBuffPacket, 5, typeof(CSLearnBuffPacket));
        RegisterPacket(CSOffsets.CSResetSkillsPacket, 5, typeof(CSResetSkillsPacket));
        RegisterPacket(CSOffsets.CSSwapAbilityPacket, 5, typeof(CSSwapAbilityPacket));
        //RegisterPacket(CSOffsets.CSSelectHighAbilityPacket, 5, typeof(CSSelectHighAbilityPacket));
        //RegisterPacket(CSOffsets.CSUnknown0x18dPacket, 5, typeof(CSUnknown0x18dPacket));
        RegisterPacket(CSOffsets.CSRemoveBuffPacket, 5, typeof(CSRemoveBuffPacket));
        RegisterPacket(CSOffsets.CSStopCastingPacket, 5, typeof(CSStopCastingPacket));
        RegisterPacket(CSOffsets.CSDeletePortalPacket, 5, typeof(CSDeletePortalPacket));
        //RegisterPacket(CSOffsets.CSIndunDirectTelPacket, 5, typeof(CSIndunDirectTelPacket));
        RegisterPacket(CSOffsets.CSSetForceAttackPacket, 5, typeof(CSSetForceAttackPacket));
        RegisterPacket(CSOffsets.CSStartSkillPacket, 5, typeof(CSStartSkillPacket));
        //RegisterPacket(CSOffsets.CSUnknown0x122Packet, 5, typeof(CSUnknown0x122Packet));
        RegisterPacket(CSOffsets.CSStopLootingPacket, 5, typeof(CSStopLootingPacket));
        RegisterPacket(CSOffsets.CSCreateDoodadPacket, 5, typeof(CSCreateDoodadPacket));
        RegisterPacket(CSOffsets.CSNaviTeleportPacket, 5, typeof(CSNaviTeleportPacket));
        RegisterPacket(CSOffsets.CSNaviOpenPortalPacket, 5, typeof(CSNaviOpenPortalPacket));
        RegisterPacket(CSOffsets.CSNaviOpenBountyPacket, 5, typeof(CSNaviOpenBountyPacket));
        RegisterPacket(CSOffsets.CSSetLogicDoodadPacket, 5, typeof(CSSetLogicDoodadPacket));
        RegisterPacket(CSOffsets.CSCleanupLogicLinkPacket, 5, typeof(CSCleanupLogicLinkPacket));
        RegisterPacket(CSOffsets.CSSelectInteractionExPacket, 5, typeof(CSSelectInteractionExPacket));
        RegisterPacket(CSOffsets.CSChangeDoodadDataPacket, 5, typeof(CSChangeDoodadDataPacket));
        RegisterPacket(CSOffsets.CSBuyItemsPacket, 5, typeof(CSBuyItemsPacket));
        //RegisterPacket(CSOffsets.CSUnknown0x59Packet, 5, typeof(CSUnknown0x59Packet));
        //RegisterPacket(CSOffsets.CSUnknown0x1a5Packet, 5, typeof(CSUnknown0x1a5Packet));
        //RegisterPacket(CSOffsets.CSUnknown0x30Packet, 5, typeof(CSUnknown0x30Packet));
        RegisterPacket(CSOffsets.CSUnitAttachedPacket, 5, typeof(CSUnitAttachedPacket));
        RegisterPacket(CSOffsets.CSStartInteractionPacket, 5, typeof(CSStartInteractionPacket));
        RegisterPacket(CSOffsets.CSInteractNPCPacket, 5, typeof(CSInteractNPCPacket));
        RegisterPacket(CSOffsets.CSInteractNPCEndPacket, 5, typeof(CSInteractNPCEndPacket));
        RegisterPacket(CSOffsets.CSBeautyShopBypassPacket, 5, typeof(CSBeautyShopBypassPacket));
        RegisterPacket(CSOffsets.CSSpecialtyRatioPacket, 5, typeof(CSSpecialtyRatioPacket));
        RegisterPacket(CSOffsets.CSListSpecialtyGoodsPacket, 5, typeof(CSListSpecialtyGoodsPacket));
        RegisterPacket(CSOffsets.CSJoinUserChatChannelPacket, 5, typeof(CSJoinUserChatChannelPacket));
        RegisterPacket(CSOffsets.CSLeaveChatChannelPacket, 5, typeof(CSLeaveChatChannelPacket));
        RegisterPacket(CSOffsets.CSSendChatMessagePacket, 5, typeof(CSSendChatMessagePacket));
        RegisterPacket(CSOffsets.CSRollDicePacket, 5, typeof(CSRollDicePacket));
        RegisterPacket(CSOffsets.CSSendMailPacket, 5, typeof(CSSendMailPacket));
        RegisterPacket(CSOffsets.CSListMailPacket, 5, typeof(CSListMailPacket));
        RegisterPacket(CSOffsets.CSListMailContinuePacket, 5, typeof(CSListMailContinuePacket));
        RegisterPacket(CSOffsets.CSReadMailPacket, 5, typeof(CSReadMailPacket));
        RegisterPacket(CSOffsets.CSTakeAttachmentMoneyPacket, 5, typeof(CSTakeAttachmentMoneyPacket));
        RegisterPacket(CSOffsets.CSTakeAttachmentSequentiallyPacket, 5, typeof(CSTakeAttachmentSequentiallyPacket));
        RegisterPacket(CSOffsets.CSPayChargeMoneyPacket, 5, typeof(CSPayChargeMoneyPacket));
        RegisterPacket(CSOffsets.CSDeleteMailPacket, 5, typeof(CSDeleteMailPacket));
        RegisterPacket(CSOffsets.CSReportSpamPacket, 5, typeof(CSReportSpamPacket));
        RegisterPacket(CSOffsets.CSReturnMailPacket, 5, typeof(CSReturnMailPacket));
        RegisterPacket(CSOffsets.CSTakeAllAttachmentItemPacket, 5, typeof(CSTakeAllAttachmentItemPacket));
        RegisterPacket(CSOffsets.CSTakeAttachmentItemPacket, 5, typeof(CSTakeAttachmentItemPacket));
        RegisterPacket(CSOffsets.CSActiveWeaponChangedPacket, 5, typeof(CSActiveWeaponChangedPacket));
        //RegisterPacket(CSOffsets.CSUnknown0x0d8Packet, 5, typeof(CSUnknown0x0d8Packet));
        RegisterPacket(CSOffsets.CSRequestExpandAbilitySetSlotPacket, 5, typeof(CSRequestExpandAbilitySetSlotPacket));
        RegisterPacket(CSOffsets.CSSaveAbilitySetPacket, 5, typeof(CSSaveAbilitySetPacket));
        RegisterPacket(CSOffsets.CSDeleteAbilitySetPacket, 5, typeof(CSDeleteAbilitySetPacket));
        RegisterPacket(CSOffsets.CSRepairSlaveItemsPacket, 5, typeof(CSRepairSlaveItemsPacket));
        RegisterPacket(CSOffsets.CSRepairPetItemsPacket, 5, typeof(CSRepairPetItemsPacket));
        //RegisterPacket(CSOffsets.CSFactionIssuanceOfMobilizationOrderPacket, 5, typeof(CSFactionIssuanceOfMobilizationOrderPacket));
        RegisterPacket(CSOffsets.CSGetExpeditionMyRecruitmentsPacket, 5, typeof(CSGetExpeditionMyRecruitmentsPacket));
        RegisterPacket(CSOffsets.CSExpeditionRecruitmentAddPacket, 5, typeof(CSExpeditionRecruitmentAddPacket));
        RegisterPacket(CSOffsets.CSExpeditionRecruitmentDeletePacket, 5, typeof(CSExpeditionRecruitmentDeletePacket));
        RegisterPacket(CSOffsets.CSGetExpeditionApplicantsPacket, 5, typeof(CSGetExpeditionApplicantsPacket));
        RegisterPacket(CSOffsets.CSExpeditionApplicantAddPacket, 5, typeof(CSExpeditionApplicantAddPacket));
        RegisterPacket(CSOffsets.CSExpeditionApplicantDeletePacket, 5, typeof(CSExpeditionApplicantDeletePacket));
        RegisterPacket(CSOffsets.CSExpeditionApplicantAcceptPacket, 5, typeof(CSExpeditionApplicantAcceptPacket));
        RegisterPacket(CSOffsets.CSExpeditionApplicantRejectPacket, 5, typeof(CSExpeditionApplicantRejectPacket));
        RegisterPacket(CSOffsets.CSExpeditionSummonPacket, 5, typeof(CSExpeditionSummonPacket));
        RegisterPacket(CSOffsets.CSExpeditionSummonReplyPacket, 5, typeof(CSExpeditionSummonReplyPacket));
        //RegisterPacket(CSOffsets.CSInstantTimePacket, 5, typeof(CSInstantTimePacket));
        RegisterPacket(CSOffsets.CSSetHouseAllowRecoverPacket, 5, typeof(CSSetHouseAllowRecoverPacket)); // CSAllowHousingRecoverPacket
        //RegisterPacket(CSOffsets.CSRefreshBotCheckInfoPacket, 5, typeof(CSRefreshBotCheckInfoPacket));
        //RegisterPacket(CSOffsets.CSAnswerBotCheckPacket, 5, typeof(CSAnswerBotCheckPacket));
        RegisterPacket(CSOffsets.CSChangeSlaveNamePacket, 5, typeof(CSChangeSlaveNamePacket));
        RegisterPacket(CSOffsets.CSUseTeleportPacket, 5, typeof(CSUseTeleportPacket));
        RegisterPacket(CSOffsets.CSAuctionPostPacket, 5, typeof(CSAuctionPostPacket));
        RegisterPacket(CSOffsets.CSAuctionSearchPacket, 5, typeof(CSAuctionSearchPacket));
        RegisterPacket(CSOffsets.CSAuctionMyBidListPacket, 5, typeof(CSAuctionMyBidListPacket));
        RegisterPacket(CSOffsets.CSAuctionLowestPricePacket, 5, typeof(CSAuctionLowestPricePacket));
        //RegisterPacket(CSOffsets.CSAuctionSearchSoldRecordPacket, 5, typeof(CSAuctionSearchSoldRecordPacket));
        //RegisterPacket(CSOffsets.CSAuctionCancelPacket, 5, typeof(CSAuctionCancelPacket));
        //RegisterPacket(CSOffsets.CSAuctionBidPacket, 5, typeof(CSAuctionBidPacket));
        RegisterPacket(CSOffsets.CSExecuteCraftPacket, 5, typeof(CSExecuteCraftPacket));
        RegisterPacket(CSOffsets.CSSetLpManageCharacterPacket, 5, typeof(CSSetLpManageCharacterPacket));
        RegisterPacket(CSOffsets.CSSetCraftingPayPacket, 5, typeof(CSSetCraftingPayPacket));
        RegisterPacket(CSOffsets.CSDestroyItemPacket, 5, typeof(CSDestroyItemPacket));
        RegisterPacket(CSOffsets.CSSplitBagItemPacket, 5, typeof(CSSplitBagItemPacket));
        RegisterPacket(CSOffsets.CSSwapItemsPacket, 5, typeof(CSSwapItemsPacket));
        RegisterPacket(CSOffsets.CSSplitCofferItemPacket, 5, typeof(CSSplitCofferItemPacket));
        RegisterPacket(CSOffsets.CSSwapCofferItemsPacket, 5, typeof(CSSwapCofferItemsPacket));
        RegisterPacket(CSOffsets.CSExpandSlotsPacket, 5, typeof(CSExpandSlotsPacket));
        RegisterPacket(CSOffsets.CSDepositMoneyPacket, 5, typeof(CSDepositMoneyPacket));
        RegisterPacket(CSOffsets.CSWithdrawMoneyPacket, 5, typeof(CSWithdrawMoneyPacket));
        RegisterPacket(CSOffsets.CSItemSecurePacket, 5, typeof(CSItemSecurePacket));
        RegisterPacket(CSOffsets.CSItemUnsecurePacket, 5, typeof(CSItemUnsecurePacket));
        RegisterPacket(CSOffsets.CSEquipmentsSecurePacket, 5, typeof(CSEquipmentsSecurePacket));
        RegisterPacket(CSOffsets.CSEquipmentsUnsecurePacket, 5, typeof(CSEquipmentsUnsecurePacket));
        RegisterPacket(CSOffsets.CSRepairSingleEquipmentPacket, 5, typeof(CSRepairSingleEquipmentPacket));
        RegisterPacket(CSOffsets.CSRepairAllEquipmentsPacket, 5, typeof(CSRepairAllEquipmentsPacket));
        //RegisterPacket(CSOffsets.CSChangeAutoUseAAPointPacket, 5, typeof(CSChangeAutoUseAAPointPacket));
        RegisterPacket(CSOffsets.CSThisTimeUnpackPacket, 5, typeof(CSThisTimeUnpackItemPacket));
        //RegisterPacket(CSOffsets.CSTakeScheduleItemPacket, 5, typeof(CSTakeScheduleItemPacket));
        RegisterPacket(CSOffsets.CSChangeMateEquipmentPacket, 5, typeof(CSChangeMateEquipmentPacket));
        RegisterPacket(CSOffsets.CSChangeSlaveEquipmentPacket, 5, typeof(CSChangeSlaveEquipmentPacket));
        //RegisterPacket(CSOffsets.CSLoginUccItemsPacket, 5, typeof(CSLoginUccItemsPacket));
        RegisterPacket(CSOffsets.CSLootOpenBagPacket, 5, typeof(CSLootOpenBagPacket));
        RegisterPacket(CSOffsets.CSLootItemPacket, 5, typeof(CSLootItemPacket));
        RegisterPacket(CSOffsets.CSLootCloseBagPacket, 5, typeof(CSLootCloseBagPacket));
        RegisterPacket(CSOffsets.CSLootDicePacket, 5, typeof(CSLootDicePacket));
        RegisterPacket(CSOffsets.CSSellBackpackGoodsPacket, 5, typeof(CSSellBackpackGoodsPacket));
        RegisterPacket(CSOffsets.CSSellItemsPacket, 5, typeof(CSSellItemsPacket));
        RegisterPacket(CSOffsets.CSListSoldItemPacket, 5, typeof(CSListSoldItemPacket));
        //RegisterPacket(CSOffsets.CSSpecialtyCurrentLoadPacket, 5, typeof(CSSpecialtyCurrentLoadPacket));
        RegisterPacket(CSOffsets.CSStartTradePacket, 5, typeof(CSStartTradePacket));
        RegisterPacket(CSOffsets.CSCanStartTradePacket, 5, typeof(CSCanStartTradePacket));
        RegisterPacket(CSOffsets.CSCannotStartTradePacket, 5, typeof(CSCannotStartTradePacket));
        RegisterPacket(CSOffsets.CSCancelTradePacket, 5, typeof(CSCancelTradePacket));
        RegisterPacket(CSOffsets.CSPutupTradeItemPacket, 5, typeof(CSPutupTradeItemPacket));
        RegisterPacket(CSOffsets.CSPutupTradeMoneyPacket, 5, typeof(CSPutupTradeMoneyPacket));
        //RegisterPacket(CSOffsets.CSPutupItemPacket, 5, typeof(CSPutupItemPacket));
        //RegisterPacket(CSOffsets.CSTakedownItemPacket, 5, typeof(CSTakedownItemPacket));
        RegisterPacket(CSOffsets.CSTradeLockPacket, 5, typeof(CSTradeLockPacket));
        RegisterPacket(CSOffsets.CSTradeOkPacket, 5, typeof(CSTradeOkPacket));
        //RegisterPacket(CSOffsets.CSPutupMoneyPacket, 5, typeof(CSPutupMoneyPacket));
        //RegisterPacket(CSOffsets.CSReportSpammer, 5, typeof(CSReportSpammer);

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

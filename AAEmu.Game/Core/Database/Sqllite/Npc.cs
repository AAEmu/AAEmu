using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class Npc
{
    public long? Id { get; set; }

    public string Name { get; set; }

    public long? CharRaceId { get; set; }

    public long? NpcGradeId { get; set; }

    public long? NpcKindId { get; set; }

    public long? Level { get; set; }

    public long? NpcTemplateId { get; set; }

    public long? EquipBodiesId { get; set; }

    public long? EquipClothsId { get; set; }

    public long? EquipWeaponsId { get; set; }

    public long? ModelId { get; set; }

    public long? FactionId { get; set; }

    public byte[] SkillTrainer { get; set; }

    public long? AiFileId { get; set; }

    public byte[] Merchant { get; set; }

    public long? NpcNicknameId { get; set; }

    public byte[] Auctioneer { get; set; }

    public byte[] ShowNameTag { get; set; }

    public byte[] VisibleToCreatorOnly { get; set; }

    public byte[] NoExp { get; set; }

    public long? PetItemId { get; set; }

    public long? BaseSkillId { get; set; }

    public byte[] TrackFriendship { get; set; }

    public byte[] Priest { get; set; }

    public string Comment1 { get; set; }

    public long? NpcTendencyId { get; set; }

    public byte[] Blacksmith { get; set; }

    public byte[] Teleporter { get; set; }

    public double? Opacity { get; set; }

    public byte[] AbilityChanger { get; set; }

    public double? Scale { get; set; }

    public string Comment2 { get; set; }

    public string Comment3 { get; set; }

    public double? SightRangeScale { get; set; }

    public double? SightFovScale { get; set; }

    public long? MilestoneId { get; set; }

    public double? AttackStartRangeScale { get; set; }

    public byte[] Aggression { get; set; }

    public double? ExpMultiplier { get; set; }

    public long? ExpAdder { get; set; }

    public byte[] Stabler { get; set; }

    public byte[] AcceptAggroLink { get; set; }

    public long? RecruitingBattleFieldId { get; set; }

    public double? ReturnDistance { get; set; }

    public long? NpcAiParamId { get; set; }

    public byte[] NonPushableByActor { get; set; }

    public byte[] Banker { get; set; }

    public long? AggroLinkSpecialRuleId { get; set; }

    public double? AggroLinkHelpDist { get; set; }

    public byte[] AggroLinkSightCheck { get; set; }

    public byte[] Expedition { get; set; }

    public long? HonorPoint { get; set; }

    public byte[] Trader { get; set; }

    public byte[] AggroLinkSpecialGuard { get; set; }

    public byte[] AggroLinkSpecialIgnoreNpcAttacker { get; set; }

    public string CommentWear { get; set; }

    public double? AbsoluteReturnDistance { get; set; }

    public byte[] Repairman { get; set; }

    public byte[] ActivateAiAlways { get; set; }

    public string SoState { get; set; }

    public byte[] Specialty { get; set; }

    public long? SoundPackId { get; set; }

    public long? SpecialtyCoinId { get; set; }

    public byte[] UseRangeMod { get; set; }

    public long? NpcPostureSetId { get; set; }

    public long? MateEquipSlotPackId { get; set; }

    public long? MateKindId { get; set; }

    public long? EngageCombatGiveQuestId { get; set; }

    public long? TotalCustomId { get; set; }

    public byte[] NoApplyTotalCustom { get; set; }

    public byte[] BaseSkillStrafe { get; set; }

    public double? BaseSkillDelay { get; set; }

    public long? NpcInteractionSetId { get; set; }

    public byte[] UseAbuserList { get; set; }

    public byte[] ReturnWhenEnterHousingArea { get; set; }

    public byte[] LookConverter { get; set; }

    public byte[] UseDdcmsMountSkill { get; set; }

    public byte[] CrowdEffect { get; set; }

    public double? FxScale { get; set; }

    public byte[] Translate { get; set; }

    public byte[] NoPenalty { get; set; }

    public byte[] ShowFactionTag { get; set; }
}

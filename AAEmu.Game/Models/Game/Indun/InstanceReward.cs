namespace AAEmu.Game.Models.Game.Indun;

/// <summary>Target kinds shipped in <c>instance_rewards.reward_target_type</c>.</summary>
public enum InstanceRewardTargetType
{
    Item,
    EnumCurrency
}

/// <summary>Names come from <c>enum_instance_reward_mail_kinds</c>; no numeric mail-kind is assumed.</summary>
public enum InstanceRewardMailKind
{
    Basic
}

public sealed record InstanceRewardKindDefinition(uint Id, string Name);
public sealed record InstanceRewardMailKindDefinition(uint Id, string Name);

/// <summary>One typed <c>instance_rewards</c> row. W03B bonus rows are intentionally not represented.</summary>
public sealed record InstanceReward(
    uint Id,
    uint InstanceId,
    uint InstanceRewardKindId,
    int StartRange,
    int EndRange,
    int RewardAmount,
    bool UseGameScore,
    uint RewardTargetId,
    InstanceRewardTargetType RewardTargetType,
    bool GiveIgnoreVisitedCount,
    bool ApplyConfig);

/// <summary>One typed <c>instance_reward_mail_texts</c> row for an instance.</summary>
public sealed record InstanceRewardMailText(
    uint Id,
    uint InstanceId,
    string MailSender,
    string MailTitle,
    string MailBody,
    uint MailKindId,
    InstanceRewardMailKind MailKind);

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

/// <summary>One typed <c>instance_rewards</c> row.</summary>
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

/// <summary>
/// One typed <c>instance_reward_bonus_counts</c> row. The catalog proves the association with an
/// authored reward and a real buff, but not a player-visible application; delivery therefore records
/// the grant in the same durable transaction as the mail without mutating character buffs.
/// </summary>
public sealed record InstanceRewardBonusCount(
    uint Id,
    uint InstanceRewardId,
    uint BuffId,
    int Count);

/// <summary>Content rows that cannot be attached to a loaded reward, reported instead of guessed.</summary>
public sealed record InstanceRewardBonusDiagnostics(
    IReadOnlyList<uint> OrphanRewardIds,
    IReadOnlyList<uint> OrphanRowIds);

/// <summary>One typed <c>instance_reward_mail_texts</c> row for an instance.</summary>
public sealed record InstanceRewardMailText(
    uint Id,
    uint InstanceId,
    string MailSender,
    string MailTitle,
    string MailBody,
    uint MailKindId,
    InstanceRewardMailKind MailKind);

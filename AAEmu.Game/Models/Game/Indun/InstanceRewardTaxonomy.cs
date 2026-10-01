namespace AAEmu.Game.Models.Game.Indun;

/// <summary>Target kinds shipped in <c>instance_mini_scoreboards.target_type</c>.</summary>
public enum InstanceMiniScoreboardTargetType
{
    InstanceGainRule
}

/// <summary>Target kinds shipped in <c>instance_gain_rules.target_type</c>.</summary>
public enum InstanceGainRuleTargetType
{
    InstancePointDoodadPhaseChange
}

/// <summary>
/// One typed <c>instance_factions</c> row. It describes how many players make up one ranked side of
/// an instance; a row whose <see cref="MinPlayer"/> equals <see cref="MaxPlayer"/> is a fixed-size team,
/// which is what makes a 1..N reward range a per-player rank rather than an inter-faction order.
/// </summary>
public sealed record InstanceFaction(
    uint Id,
    uint InstanceId,
    uint InstanceFactionPresetId,
    int MinPlayer,
    int MaxPlayer,
    bool ExcludeWhenDevMinimalRecruitment,
    uint SpawnPointIndex);

/// <summary>
/// One typed <c>instance_mini_scoreboards</c> row.
/// <para>
/// This catalog is <b>display and grouping only</b>. It names a scoreboard category, gives it an icon
/// key and an explicit sort order, and points at the gain rules it merges. It carries no score value
/// and no rank, so it can never be used to derive a reward selection value.
/// </para>
/// <para>
/// <see cref="IconId"/> is a catalog key string, not a number, and is never parsed as one.
/// </para>
/// </summary>
public sealed record InstanceMiniScoreboard(
    uint Id,
    uint InstanceId,
    uint TargetId,
    InstanceMiniScoreboardTargetType TargetType,
    string Name,
    int VisibleOrder,
    string IconId,
    uint MergeTargetId);

/// <summary>
/// One typed <c>instance_gain_rules</c> row. It groups point-gaining doodads under a display range so
/// the mini scoreboard can merge them. Like <see cref="InstanceMiniScoreboard"/> it is display and
/// grouping only and is never a score source.
/// </summary>
public sealed record InstanceGainRule(
    uint Id,
    uint InstanceId,
    uint RangeId,
    uint InstanceFactionId,
    uint TargetId,
    InstanceGainRuleTargetType TargetType,
    uint DisplayRangeId);

/// <summary>
/// One typed <c>instance_point_doodad_phase_changes</c> row, the doodad that a gain rule points at.
/// The doodad's authored functions are interaction and hit-detection only, so this row proves where
/// gain rules are collected, not what any player scored.
/// </summary>
public sealed record InstancePointDoodadPhaseChange(
    uint Id,
    uint DoodadAlmightyId,
    uint DoodadFuncGroupId);

/// <summary>
/// How an instance-reward kind's selection value can be established from shipped content.
/// <para>
/// The classification is derived structurally from the evidence, never from the reward-kind name or a
/// literal kind id, so renaming or renumbering content cannot change server behaviour.
/// </para>
/// </summary>
public enum InstanceRewardSelectionClass
{
    /// <summary>No shipped evidence establishes a selection value for this kind on this instance.</summary>
    Unsupported,

    /// <summary>
    /// The instance publishes <c>instance_difficult_infos</c> and an authored reward range contains one
    /// of those difficulties. The selection value is that difficulty.
    /// </summary>
    DifficultyBacked,

    /// <summary>
    /// The instance's zone group publishes <c>indun_rounds</c> and the authored reward ranges for this
    /// kind start at 1 and reach exactly the round count, which is what makes the round index the
    /// selection value. Whether anything triggers the delivery is a separate question.
    /// </summary>
    RoundBacked,

    /// <summary>
    /// The instance publishes fixed-size <c>instance_factions</c> teams and reward ranges spanning
    /// 1..team size, plus a display ranking surface. The band is authored; the score that orders
    /// players into those bands is not.
    /// </summary>
    RankBacked
}

/// <summary>The exact artifact whose absence blocks one classification.</summary>
public enum InstanceRewardBlocker
{
    /// <summary>Nothing blocks this classification.</summary>
    None,

    /// <summary>No <c>instance_difficult_infos</c> row and no round cover for this instance.</summary>
    MissingSelectionSource,

    /// <summary>
    /// The selection source is proven but no <c>indun_action_send_mail_rewards</c> row exists for this
    /// reward kind, so nothing authored reaches the delivery hook.
    /// </summary>
    MissingDeliveryTrigger,

    /// <summary>
    /// Rank bands are authored but no content yields a per-player score to compare, so no ordering
    /// rule can be applied.
    /// </summary>
    MissingScoreSource,

    /// <summary>The instance publishes no <c>instance_rewards</c> row for the requested kind.</summary>
    MissingRewardRows
}

/// <summary>
/// The outcome of classifying one instance/kind pair. <see cref="SelectionValue"/> is populated only
/// where the selection source is proven, and <see cref="DeliverableNow"/> is true only where a proven
/// selection source and an authored trigger both exist.
/// </summary>
public sealed record InstanceRewardSelectionVerdict(
    uint InstanceId,
    uint InstanceRewardKindId,
    string KindName,
    InstanceRewardSelectionClass Classification,
    InstanceRewardBlocker Blocker,
    bool SelectionSourceProven,
    bool DeliveryTriggerAuthored,
    bool DeliverableNow,
    int SelectionValue);

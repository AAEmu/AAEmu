namespace AAEmu.Game.Models.Game.Indun;

/// <summary>
/// Classifies how an instance-reward kind's selection value can be established, and names the exact
/// artifact whose absence blocks it.
/// <para>
/// Every test here is structural: it compares authored reward ranges against authored round counts,
/// authored difficulty rows, or authored team sizes. No reward-kind name and no literal kind id takes
/// part, so a content rename or renumber cannot move a kind between classes.
/// </para>
/// </summary>
public static class InstanceRewardTaxonomyRules
{
    /// <summary>
    /// Classifies one instance/kind pair.
    /// </summary>
    /// <param name="rewards">The authored <c>instance_rewards</c> rows for this instance and kind.</param>
    /// <param name="kindName">Catalog name of the kind, carried through for diagnostics only.</param>
    /// <param name="authoredDifficulties">
    /// The <c>instance_difficult_infos</c> difficulties of this instance, or an empty set.
    /// </param>
    /// <param name="roundCount">The <c>indun_rounds</c> row count of this instance's zone group.</param>
    /// <param name="teamSizes">
    /// The <c>instance_factions</c> team sizes of this instance. Only fixed-size rows (min == max)
    /// count, because an elastic roster cannot define a per-player rank band.
    /// </param>
    /// <param name="hasDisplayRankingSurface">
    /// True when the instance publishes <c>instance_mini_scoreboards</c> or <c>instance_gain_rules</c>.
    /// Presence only; those catalogs carry no score.
    /// </param>
    /// <param name="deliveryTriggerAuthored">
    /// True when an <c>indun_action_send_mail_rewards</c> row exists for this reward kind.
    /// </param>
    /// <param name="runtimeDifficulty">
    /// The difficulty the running copy actually holds, when it has one.
    /// </param>
    public static InstanceRewardSelectionVerdict Classify(
        uint instanceId,
        uint instanceRewardKindId,
        string kindName,
        IReadOnlyList<InstanceReward> rewards,
        IReadOnlyCollection<byte> authoredDifficulties,
        int roundCount,
        IReadOnlyCollection<int> teamSizes,
        bool hasDisplayRankingSurface,
        bool deliveryTriggerAuthored,
        byte? runtimeDifficulty)
    {
        ArgumentNullException.ThrowIfNull(rewards);
        ArgumentNullException.ThrowIfNull(authoredDifficulties);
        ArgumentNullException.ThrowIfNull(teamSizes);

        if (rewards.Count == 0)
        {
            return Verdict(instanceId, instanceRewardKindId, kindName,
                InstanceRewardSelectionClass.Unsupported, InstanceRewardBlocker.MissingRewardRows,
                selectionSourceProven: false, deliveryTriggerAuthored, deliverableNow: false, selectionValue: 0);
        }

        if (TryResolveDifficultyBacked(
                rewards, authoredDifficulties, runtimeDifficulty,
                out var difficultyValue, out var difficultyEstablished))
        {
            var trigger = deliveryTriggerAuthored
                ? InstanceRewardBlocker.None
                : InstanceRewardBlocker.MissingDeliveryTrigger;
            // The classification is provable without a chosen difficulty - the instance does publish
            // them and the authored bands cover them - but provable is not chosen. A verdict that cannot
            // name the run's own difficulty carries no selection value and must not deliver, because the
            // only value available is the lowest authored one, and delivering that hands a run the
            // easiest reward for never having picked a difficulty.
            var blocker = deliveryTriggerAuthored && !difficultyEstablished
                ? InstanceRewardBlocker.MissingSelectionSource
                : trigger;
            return Verdict(instanceId, instanceRewardKindId, kindName,
                InstanceRewardSelectionClass.DifficultyBacked, blocker,
                selectionSourceProven: true, deliveryTriggerAuthored,
                deliverableNow: deliveryTriggerAuthored && difficultyEstablished,
                selectionValue: difficultyEstablished ? difficultyValue : 0);
        }

        if (TryResolveRoundBacked(rewards, roundCount, out var roundValue))
        {
            var trigger = deliveryTriggerAuthored
                ? InstanceRewardBlocker.None
                : InstanceRewardBlocker.MissingDeliveryTrigger;
            return Verdict(instanceId, instanceRewardKindId, kindName,
                InstanceRewardSelectionClass.RoundBacked, trigger,
                selectionSourceProven: true, deliveryTriggerAuthored,
                deliverableNow: false, selectionValue: roundValue);
        }

        if (TryResolveRankBacked(rewards, teamSizes, hasDisplayRankingSurface, out var teamSize))
        {
            return Verdict(instanceId, instanceRewardKindId, kindName,
                InstanceRewardSelectionClass.RankBacked, InstanceRewardBlocker.MissingScoreSource,
                selectionSourceProven: false, deliveryTriggerAuthored,
                deliverableNow: false, selectionValue: 0);
        }

        return Verdict(instanceId, instanceRewardKindId, kindName,
            InstanceRewardSelectionClass.Unsupported, InstanceRewardBlocker.MissingSelectionSource,
            selectionSourceProven: false, deliveryTriggerAuthored, deliverableNow: false, selectionValue: 0);
    }

    /// <summary>
    /// Difficulty-backed when the instance publishes difficulties and an authored range contains one.
    /// The runtime difficulty wins when the copy has one and it lands in a range; otherwise the first
    /// authored difficulty in range is used so the class is still provable on a copy that has not
    /// selected yet.
    /// </summary>
    private static bool TryResolveDifficultyBacked(
        IReadOnlyList<InstanceReward> rewards,
        IReadOnlyCollection<byte> authoredDifficulties,
        byte? runtimeDifficulty,
        out int selectionValue,
        out bool selectionEstablished)
    {
        selectionValue = 0;
        selectionEstablished = false;
        if (authoredDifficulties.Count == 0)
            return false;

        if (runtimeDifficulty is { } chosen &&
            rewards.Any(reward => chosen >= reward.StartRange && chosen <= reward.EndRange))
        {
            selectionValue = chosen;
            selectionEstablished = true;
            return true;
        }

        // Reported for the classification only: it is a statement about content, not about this run,
        // and the caller must not deliver on it. selectionEstablished stays false, which is what stops
        // a copy that never chose a difficulty from being paid the lowest authored one.
        foreach (var difficulty in authoredDifficulties.OrderBy(value => value))
        {
            if (rewards.Any(reward => difficulty >= reward.StartRange && difficulty <= reward.EndRange))
            {
                selectionValue = difficulty;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Round-backed when the zone group publishes rounds and the authored ranges for this kind start at
    /// 1 and reach exactly the round count. That exact match is the proof: it means the reward bands
    /// were authored against the round indices rather than against some other scale.
    /// </summary>
    private static bool TryResolveRoundBacked(IReadOnlyList<InstanceReward> rewards, int roundCount, out int selectionValue)
    {
        selectionValue = 0;
        if (roundCount <= 0)
            return false;

        var minStart = rewards.Min(reward => reward.StartRange);
        var maxEnd = rewards.Max(reward => reward.EndRange);
        if (minStart != 1 || maxEnd != roundCount)
            return false;

        selectionValue = roundCount;
        return true;
    }

    /// <summary>
    /// Rank-backed when the instance publishes fixed-size teams and the authored ranges span 1..team
    /// size. The display surface is recorded but contributes nothing: it proves the content intends a
    /// ranking, not what any player scored.
    /// </summary>
    private static bool TryResolveRankBacked(
        IReadOnlyList<InstanceReward> rewards,
        IReadOnlyCollection<int> teamSizes,
        bool hasDisplayRankingSurface,
        out int selectionValue)
    {
        selectionValue = 0;
        if (!hasDisplayRankingSurface || teamSizes.Count == 0)
            return false;

        var minStart = rewards.Min(reward => reward.StartRange);
        var maxEnd = rewards.Max(reward => reward.EndRange);
        foreach (var teamSize in teamSizes.OrderBy(size => size))
        {
            if (teamSize < 2 || minStart != 1 || maxEnd != teamSize)
                continue;

            selectionValue = teamSize;
            return true;
        }

        return false;
    }

    private static InstanceRewardSelectionVerdict Verdict(
        uint instanceId,
        uint instanceRewardKindId,
        string kindName,
        InstanceRewardSelectionClass classification,
        InstanceRewardBlocker blocker,
        bool selectionSourceProven,
        bool deliveryTriggerAuthored,
        bool deliverableNow,
        int selectionValue) =>
        new(instanceId, instanceRewardKindId, kindName, classification, blocker,
            selectionSourceProven, deliveryTriggerAuthored, deliverableNow, selectionValue);

    /// <summary>
    /// The diagnostic a blocked classification logs. It names the class and the exact missing
    /// artifact so an operator can see which row would unblock it, instead of a generic refusal.
    /// </summary>
    public static string DescribeBlocker(InstanceRewardSelectionVerdict verdict)
    {
        ArgumentNullException.ThrowIfNull(verdict);
        return verdict.Blocker switch
        {
            InstanceRewardBlocker.MissingRewardRows =>
                $"no authored {verdict.KindName} reward row is published for this instance",
            InstanceRewardBlocker.MissingSelectionSource =>
                $"{verdict.KindName} has no authored selection source: the instance publishes no difficulty info, " +
                "no indun_rounds cover matching the reward ranges, and no fixed-size instance_factions team spanning them",
            InstanceRewardBlocker.MissingDeliveryTrigger =>
                $"{verdict.KindName} selection is proven from indun_rounds, but no indun_action_send_mail_rewards row " +
                "authored for this kind, so no trigger reaches the delivery hook; authoring one would be inventing content",
            InstanceRewardBlocker.MissingScoreSource =>
                $"{verdict.KindName} rank bands are authored, but nothing authored yields a per-player score to order " +
                "them: the mini scoreboard and gain rule catalogs are display only and no point doodad function produces a value",
            _ => string.Empty
        };
    }
}

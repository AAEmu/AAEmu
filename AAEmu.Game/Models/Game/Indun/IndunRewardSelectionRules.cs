using AAEmu.Game.Models.Game.Mails;

namespace AAEmu.Game.Models.Game.Indun;

public static class IndunRewardSelectionRules
{
    public static bool TryResolveDifficultySelection(bool hasDifficultyInfo, byte? difficulty, out int selectionValue)
    {
        if (!hasDifficultyInfo || difficulty is null)
        {
            selectionValue = 0;
            return false;
        }

        selectionValue = difficulty.Value;
        return true;
    }

    public static bool TryResolveAuthoredDifficultySelection(
        IReadOnlyList<InstanceReward> rewards,
        bool hasDifficultyInfo,
        byte? difficulty,
        uint instanceRewardKindId,
        out int selectionValue)
    {
        selectionValue = 0;
        if (rewards == null ||
            !TryResolveDifficultySelection(hasDifficultyInfo, difficulty, out var candidate))
            return false;

        if (!rewards.Any(reward => reward.InstanceRewardKindId == instanceRewardKindId &&
                                   candidate >= reward.StartRange && candidate <= reward.EndRange))
            return false;

        selectionValue = candidate;
        return true;
    }

    /// <summary>
    /// Selects every authored row containing the supplied value. Overlapping rows are intentional
    /// content (one letter may contain more than one reward); an empty result is a content error.
    /// </summary>
    public static IReadOnlyList<InstanceReward> Select(
        IReadOnlyList<InstanceReward> rewards,
        uint instanceRewardKindId,
        int selectionValue)
    {
        ArgumentNullException.ThrowIfNull(rewards);
        var matches = rewards
            .Where(reward => reward.InstanceRewardKindId == instanceRewardKindId &&
                             selectionValue >= reward.StartRange && selectionValue <= reward.EndRange)
            .ToArray();
        if (matches.Length == 0)
        {
            throw new InvalidDataException(
                $"instance_rewards has no {instanceRewardKindId} row at selection value {selectionValue}");
        }

        return matches;
    }

    /// <summary>
    /// Keeps only bonus rows attached to the reward rows the same selection chose. A bonus pointing
    /// at any other reward is a catalog error, never a row to discard silently.
    /// </summary>
    public static IReadOnlyList<InstanceRewardBonusCount> SelectBonusCounts(
        IReadOnlyList<InstanceReward> selectedRewards,
        IReadOnlyList<InstanceRewardBonusCount> bonusCounts)
    {
        ArgumentNullException.ThrowIfNull(selectedRewards);
        ArgumentNullException.ThrowIfNull(bonusCounts);
        var selectedIds = selectedRewards.Select(reward => reward.Id).ToHashSet();
        var keys = new HashSet<(uint InstanceRewardId, uint BuffId)>();
        foreach (var bonus in bonusCounts)
        {
            if (bonus.Id == 0 || bonus.InstanceRewardId == 0 || bonus.BuffId == 0 || bonus.Count <= 0)
                throw new InvalidDataException(
                    $"instance_reward_bonus_counts {bonus.Id} is incomplete or has a non-positive count");
            if (!selectedIds.Contains(bonus.InstanceRewardId))
                throw new InvalidDataException(
                    $"instance_reward_bonus_counts {bonus.Id} points at reward {bonus.InstanceRewardId}, which this selection did not choose");
            if (!keys.Add((bonus.InstanceRewardId, bonus.BuffId)))
                throw new InvalidDataException(
                    $"instance_reward_bonus_counts has duplicate reward {bonus.InstanceRewardId} / buff {bonus.BuffId}");
        }

        return bonusCounts
            .OrderBy(bonus => bonus.InstanceRewardId)
            .ThenBy(bonus => bonus.BuffId)
            .ToArray();
    }
}

public static class IndunRewardMailKindRules
{
    public static MailType Map(InstanceRewardMailKind kind) => kind switch
    {
        InstanceRewardMailKind.Basic => MailType.SysExpress,
        _ => throw new InvalidDataException($"Unsupported instance reward mail kind {kind}")
    };
}

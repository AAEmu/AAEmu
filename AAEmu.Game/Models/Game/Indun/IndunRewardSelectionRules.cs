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
}

public static class IndunRewardMailKindRules
{
    public static MailType Map(InstanceRewardMailKind kind) => kind switch
    {
        InstanceRewardMailKind.Basic => MailType.SysExpress,
        _ => throw new InvalidDataException($"Unsupported instance reward mail kind {kind}")
    };
}

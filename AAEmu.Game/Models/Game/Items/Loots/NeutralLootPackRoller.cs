using AAEmu.Game.Core.Managers;
using AAEmu.Game.GameData;

namespace AAEmu.Game.Models.Game.Items.Loots;

public readonly record struct LootPackReward(uint ItemTemplateId, int Count, byte Grade);

public interface INeutralLootPackRoller
{
    bool TryRoll(uint lootPackId, out IReadOnlyList<LootPackReward> rewards);
}

/// <summary>Rolls content loot packs without borrowing character or World reward modifiers.</summary>
public sealed class NeutralLootPackRoller(IItemManager itemManager) : INeutralLootPackRoller
{
    public bool TryRoll(uint lootPackId, out IReadOnlyList<LootPackReward> rewards)
    {
        rewards = [];
        if (lootPackId == 0 || LootGameData.Instance.GetPack(lootPackId) is not { } pack)
            return false;

        return NeutralLootPackRules.TryRoll(
            pack,
            upperExclusive => Random.Shared.NextInt64(upperExclusive),
            itemId => itemManager.GetTemplate(itemId) is { LootQuestId: 0 },
            out rewards);
    }
}

/// <summary>
/// Rolls content probabilities and amounts directly, without character, proficiency, quest,
/// World loot-rate, or gold-rate modifiers. Farmhand packs in the target content use only this subset.
/// </summary>
internal static class NeutralLootPackRules
{
    private const long ProbabilityScale = 10_000_000;

    internal static bool TryRoll(
        LootPack pack,
        Func<long, long> nextRoll,
        Func<uint, bool> isSupportedItem,
        out IReadOnlyList<LootPackReward> rewards)
    {
        var rolled = new List<LootPackReward>();
        rewards = rolled;
        if (pack?.Id is not > 0 || nextRoll == null || isSupportedItem == null ||
            pack.Groups == null || pack.LootsByGroupNo is not { Count: > 0 } ||
            pack.ActabilityGroups is not { Count: 0 } ||
            pack.Groups.Count != pack.LootsByGroupNo.Count)
            return false;

        foreach (var (groupNo, loots) in pack.LootsByGroupNo.OrderBy(entry => entry.Key))
        {
            if (groupNo == 0 || loots is not { Count: > 0 } ||
                !pack.Groups.TryGetValue(groupNo, out var group) ||
                group.PackId != pack.Id || group.GroupNo != groupNo ||
                group.DropRate > ProbabilityScale || group.ItemGradeDistributionId != 0 ||
                group.ZoneGroupId != 0)
                return false;

            if (!TryNext(nextRoll, ProbabilityScale, out var groupRoll))
                return false;
            if ((ulong)groupRoll >= group.DropRate)
                continue;

            long totalWeight = 0;
            foreach (var loot in loots)
            {
                if (loot == null || loot.LootPackId != pack.Id || loot.Group != groupNo ||
                    loot.ItemId == 0 || loot.DropRate == 0 || loot.AlwaysDrop || loot.GradeId != 0 ||
                    loot.MinAmount <= 0 || loot.MaxAmount < loot.MinAmount ||
                    !isSupportedItem(loot.ItemId))
                    return false;
                try
                {
                    totalWeight = checked(totalWeight + loot.DropRate);
                }
                catch (OverflowException)
                {
                    return false;
                }
            }

            if (!TryNext(nextRoll, totalWeight, out var itemRoll))
                return false;
            Loot selected = null;
            long cumulativeWeight = 0;
            foreach (var loot in loots)
            {
                cumulativeWeight += loot.DropRate;
                if (itemRoll < cumulativeWeight)
                {
                    selected = loot;
                    break;
                }
            }

            if (selected == null)
                return false;

            var amountRange = (long)selected.MaxAmount - selected.MinAmount + 1;
            if (!TryNext(nextRoll, amountRange, out var amountRoll))
                return false;
            var amount = checked(selected.MinAmount + (int)amountRoll);
            rolled.Add(new LootPackReward(selected.ItemId, amount, selected.GradeId));
        }

        return true;
    }

    private static bool TryNext(Func<long, long> nextRoll, long upperExclusive, out long value)
    {
        value = 0;
        if (upperExclusive <= 0)
            return false;
        value = nextRoll(upperExclusive);
        return value >= 0 && value < upperExclusive;
    }
}

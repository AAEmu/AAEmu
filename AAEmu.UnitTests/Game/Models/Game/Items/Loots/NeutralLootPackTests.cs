using AAEmu.Game.Models;
using AAEmu.Game.Models.Game.Items.Loots;

namespace AAEmu.UnitTests.Game.Models.Game.Items.Loots;

[NotInParallel]
public sealed class NeutralLootPackTests
{
    [Test]
    public async Task NeutralRoll_IgnoresWorldLootAndGoldMultipliers()
    {
        var pack = CreatePack(new Loot
        {
            Id = 1,
            Group = 1,
            ItemId = 500,
            LootPackId = 9,
            DropRate = 1,
            MinAmount = 2,
            MaxAmount = 2
        });
        var previousLootRate = AppConfiguration.Instance.World.LootRate;
        var previousGoldRate = AppConfiguration.Instance.World.GoldLootMultiplier;
        AppConfiguration.Instance.World.LootRate = 0;
        AppConfiguration.Instance.World.GoldLootMultiplier = 50;
        try
        {
            var valid = NeutralLootPackRules.TryRoll(pack, _ => 0, _ => true, out var rewards);

            await Assert.That(valid).IsTrue();
            await Assert.That(rewards.Count).IsEqualTo(1);
            await Assert.That(rewards[0]).IsEqualTo(new LootPackReward(500, 2, 0));
        }
        finally
        {
            AppConfiguration.Instance.World.LootRate = previousLootRate;
            AppConfiguration.Instance.World.GoldLootMultiplier = previousGoldRate;
        }
    }

    [Test]
    public async Task NeutralRoll_UsesIntegerWeightsAndInclusiveAmountRange()
    {
        var pack = CreatePack(
            new Loot
            {
                Id = 1, Group = 1, ItemId = 100, LootPackId = 9, DropRate = 3,
                MinAmount = 1, MaxAmount = 1
            },
            new Loot
            {
                Id = 2, Group = 1, ItemId = 200, LootPackId = 9, DropRate = 7,
                MinAmount = 4, MaxAmount = 6
            });
        var rolls = new Queue<long>([4_999_999, 3, 2]);

        var valid = NeutralLootPackRules.TryRoll(pack, _ => rolls.Dequeue(), _ => true,
            out var rewards);

        await Assert.That(valid).IsTrue();
        await Assert.That(rewards).IsEquivalentTo([new LootPackReward(200, 6, 0)]);
    }

    [Test]
    public async Task NeutralRoll_RejectsPlayerDependentPackFeatures()
    {
        var pack = CreatePack(new Loot
        {
            Id = 1, Group = 1, ItemId = 100, LootPackId = 9, DropRate = 1,
            MinAmount = 1, MaxAmount = 1
        });
        pack.Groups[1].ItemGradeDistributionId = 2;

        var valid = NeutralLootPackRules.TryRoll(pack, _ => 0, _ => true, out _);

        await Assert.That(valid).IsFalse();
    }

    private static LootPack CreatePack(params Loot[] loots) => new()
    {
        Id = 9,
        GroupCount = 1,
        Loots = [.. loots],
        Groups = new Dictionary<uint, LootGroups>
        {
            [1] = new()
            {
                PackId = 9,
                GroupNo = 1,
                DropRate = 10_000_000
            }
        },
        ActabilityGroups = [],
        LootsByGroupNo = new Dictionary<uint, List<Loot>> { [1] = [.. loots] }
    };
}

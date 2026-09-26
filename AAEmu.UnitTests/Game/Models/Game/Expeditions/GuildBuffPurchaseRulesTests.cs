using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Expeditions;

namespace AAEmu.UnitTests.Game.Models.Game.Expeditions;

public sealed class GuildBuffPurchaseRulesTests
{
    private static ExpeditionBuffTemplate Buff(bool active = true, uint level = 2) =>
        new() { Id = 7, Name = "synthetic", Active = active, ExpeditionLevelId = level };

    private static ExpeditionBuffGrade Grade(
        byte grade = 1,
        uint level = 2,
        int contribution = 10,
        uint itemId = 0,
        int count = 0,
        bool housing = false,
        uint buffId = 7) =>
        new()
        {
            Id = 70,
            ExpeditionBuffId = buffId,
            Grade = grade,
            Contribution = contribution,
            ItemId = itemId,
            Count = count,
            ExpeditionLevelId = level,
            Housing = housing
        };

    [Test]
    public async Task AcceptsContentBackedCostAndEligibility()
    {
        var decision = GuildBuffPurchaseRules.Evaluate(Buff(), Grade(), 0, 2, 0, 10);

        await Assert.That(decision.IsAllowed).IsTrue();
        await Assert.That(decision.Reason).IsEqualTo(GuildBuffPurchaseRejection.None);
    }

    [Test]
    public async Task RejectsInactiveOrWrongCategory()
    {
        var inactive = GuildBuffPurchaseRules.Evaluate(Buff(active: false), Grade(), 0, 2, 0, 10);
        var wrongCategory = GuildBuffPurchaseRules.Evaluate(Buff(), Grade(buffId: 8), 0, 2, 0, 10);

        await Assert.That(inactive.Reason).IsEqualTo(GuildBuffPurchaseRejection.InactiveBuff);
        await Assert.That(wrongCategory.Reason).IsEqualTo(GuildBuffPurchaseRejection.WrongBuff);
    }

    [Test]
    public async Task RejectsSkippedGradeAndInsufficientLevel()
    {
        var skipped = GuildBuffPurchaseRules.Evaluate(Buff(), Grade(grade: 2), 0, 2, 0, 10);
        var lowLevel = GuildBuffPurchaseRules.Evaluate(Buff(level: 3), Grade(level: 2), 0, 2, 0, 10);

        await Assert.That(skipped.Reason).IsEqualTo(GuildBuffPurchaseRejection.OutOfOrder);
        await Assert.That(lowLevel.Reason).IsEqualTo(GuildBuffPurchaseRejection.ExpeditionLevelTooLow);
    }

    [Test]
    public async Task RejectsResidenceAndMalformedItemCost()
    {
        var residence = GuildBuffPurchaseRules.Evaluate(Buff(), Grade(housing: true), 0, 2, 0, 10);
        var negative = GuildBuffPurchaseRules.Evaluate(Buff(), Grade(contribution: -1), 0, 2, 0, 10);
        var itemWithoutCount = GuildBuffPurchaseRules.Evaluate(Buff(), Grade(itemId: 9), 0, 2, 0, 10);
        var countWithoutItem = GuildBuffPurchaseRules.Evaluate(Buff(), Grade(count: 1), 0, 2, 0, 10);

        await Assert.That(residence.Reason).IsEqualTo(GuildBuffPurchaseRejection.ResidenceRequired);
        await Assert.That(negative.Reason).IsEqualTo(GuildBuffPurchaseRejection.InvalidCost);
        await Assert.That(itemWithoutCount.Reason).IsEqualTo(GuildBuffPurchaseRejection.InvalidCost);
        await Assert.That(countWithoutItem.Reason).IsEqualTo(GuildBuffPurchaseRejection.InvalidCost);
    }

    [Test]
    public async Task RejectsInsufficientContributionButAcceptsItemCost()
    {
        var insufficient = GuildBuffPurchaseRules.Evaluate(Buff(), Grade(contribution: 11), 0, 2, 0, 10);
        var itemCost = GuildBuffPurchaseRules.Evaluate(Buff(), Grade(itemId: 9, count: 2), 0, 2, 0, 10);

        await Assert.That(insufficient.Reason).IsEqualTo(GuildBuffPurchaseRejection.InsufficientContribution);
        await Assert.That(itemCost.IsAllowed).IsTrue();
    }
}

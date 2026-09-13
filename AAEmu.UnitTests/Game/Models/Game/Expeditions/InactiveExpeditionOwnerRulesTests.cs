using AAEmu.Game.Models.Game.Expeditions;

namespace AAEmu.UnitTests.Game.Models.Game.Expeditions;

public class InactiveExpeditionOwnerRulesTests
{
    [Test]
    public async Task SelectSuccessor_UsesContributionThenOnlineRecencyAndStableId()
    {
        var now = new DateTime(2026, 9, 13, 12, 0, 0, DateTimeKind.Utc);
        var expedition = new Expedition { OwnerId = 1 };
        expedition.Members =
        [
            Member(1, 5000, now.AddHours(-721)),
            Member(9, 2000, now.AddHours(-2)),
            Member(7, 2000, now.AddHours(-2), true),
            Member(5, 9999, now.AddHours(-169))
        ];

        var selected = InactiveExpeditionOwnerRules.SelectSuccessor(expedition, now,
            TimeSpan.FromHours(720), TimeSpan.FromHours(168), 1000);

        await Assert.That(selected.CharacterId).IsEqualTo(7u);
    }

    [Test]
    public async Task SelectSuccessor_RequiresOwnerThresholdAndEligibleCandidate()
    {
        var now = new DateTime(2026, 9, 13, 12, 0, 0, DateTimeKind.Utc);
        var expedition = new Expedition { OwnerId = 1 };
        expedition.Members = [Member(1, 5000, now.AddHours(-719)), Member(2, 999, now)];

        await Assert.That(InactiveExpeditionOwnerRules.SelectSuccessor(expedition, now,
            TimeSpan.FromHours(720), TimeSpan.FromHours(168), 1000)).IsNull();
    }

    private static ExpeditionMember Member(uint id, uint contribution, DateTime lastLeave, bool online = false) =>
        new() { CharacterId = id, Name = $"Member{id}", ContributionPoint = contribution,
            LastWorldLeaveTime = lastLeave, IsOnline = online };
}

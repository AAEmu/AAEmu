using AAEmu.Game.Models.Game.Quests.Acts;

namespace AAEmu.UnitTests.Game.Models.Game.Quests;

public sealed class QuestActConAcceptItemCleanupTests
{
    [Test]
    [Arguments(0, 1)]
    [Arguments(-1, 1)]
    [Arguments(1, 1)]
    [Arguments(3, 3)]
    public async Task ConsumeAmount_DefaultsToOneWhenUnset(int count, int expected)
    {
        await Assert.That(QuestActConAcceptItem.ResolveConsumeAmount(count)).IsEqualTo(expected);
    }
}

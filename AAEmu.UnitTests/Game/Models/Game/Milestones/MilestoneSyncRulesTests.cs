using AAEmu.Game.Models.Game.Milestones;
using AAEmu.Game.Models.Game.Sagas;
using AAEmu.UnitTests.Game.Models.Game.Sagas;

namespace AAEmu.UnitTests.Game.Models.Game.Milestones;

/// <summary>
/// The wire-safety rule for reusing GF-W13's chronicle update family: the chronicle type space is
/// saga-group-keyed, so a milestone id that collides with a shipped group id must not sync.
/// </summary>
public class MilestoneSyncRulesTests
{
    [Test]
    public async Task ChronicleType_CollidingWithASagaGroupId_IsSuppressed()
    {
        // Synthetic saga catalog whose group id collides with a milestone id.
        var sagaGroups = SagaTestCatalog.Build(
            groups: [(8u, 0u, 130u)],
            members: [(8u, 9001u)]);

        await Assert.That(MilestoneSyncRules.CanSyncChronicleType(8, sagaGroups)).IsFalse();
        // A milestone id no group claims keeps its sync edge.
        await Assert.That(MilestoneSyncRules.CanSyncChronicleType(5001, sagaGroups)).IsTrue();
    }

    [Test]
    public async Task ChronicleType_SurvivesAnEmptySagaCatalog()
    {
        await Assert.That(
            MilestoneSyncRules.CanSyncChronicleType(5001, SagaQuestCatalog.Empty)).IsTrue();
    }
}

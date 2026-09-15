using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.InstantGame;

namespace AAEmu.UnitTests.Game.Models.Game.InstantGame;

public sealed class BattlefieldExpeditionContentTests
{
    [Test]
    public async Task IsExpeditionContent_RequiresConfiguredInstanceUiKindAndSquadUse()
    {
        var eligible = new Battlefield
        {
            InstanceId = 69,
            InstanceRankDetailId = 41,
            InstanceUiKindId = BattlefieldGameData.ExpeditionInstanceUiKindId,
            SquadNotUse = false
        };
        var noInstance = new Battlefield
        {
            InstanceUiKindId = BattlefieldGameData.ExpeditionInstanceUiKindId,
            SquadNotUse = false
        };
        var wrongUiKind = new Battlefield { InstanceId = 69, InstanceUiKindId = 1, SquadNotUse = false };
        var squadDisabled = new Battlefield
        {
            InstanceId = 69,
            InstanceUiKindId = BattlefieldGameData.ExpeditionInstanceUiKindId,
            SquadNotUse = true
        };
        var missingRankDetail = new Battlefield
        {
            InstanceId = 69,
            InstanceUiKindId = BattlefieldGameData.ExpeditionInstanceUiKindId,
            SquadNotUse = false
        };

        await Assert.That(eligible.IsExpeditionContent).IsTrue();
        await Assert.That(eligible.InstanceRankDetailId).IsEqualTo(41u);
        await Assert.That(noInstance.IsExpeditionContent).IsFalse();
        await Assert.That(wrongUiKind.IsExpeditionContent).IsFalse();
        await Assert.That(squadDisabled.IsExpeditionContent).IsFalse();
        await Assert.That(eligible.CanRecordExpeditionHistory).IsTrue();
        await Assert.That(missingRankDetail.CanRecordExpeditionHistory).IsFalse();
    }
}

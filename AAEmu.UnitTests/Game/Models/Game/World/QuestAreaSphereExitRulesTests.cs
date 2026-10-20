using System.Numerics;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.UnitTests.Game.Models.Game.World;

public class QuestAreaSphereExitRulesTests
{
    [Test]
    public async Task SmallerAreaExit_DoesNotClearObjectiveInsideLargerSign()
    {
        var center = new Vector3(10f, 20f, 30f);
        var area = new SphereQuest { Xyz = center, Radius = 3f };
        var sign = new SphereQuest { ComponentId = 100, Xyz = center, Radius = 5f };
        var position = center + new Vector3(4f, 0f, 0f);
        var matchingSigns = QuestAreaSphereExitRules.MatchingSigns([sign], componentId: 100);

        await Assert.That(area.Contains(position)).IsFalse();
        await Assert.That(QuestAreaSphereExitRules.PlayerRemainsInMatchingSign(matchingSigns, position)).IsTrue();
    }

    [Test]
    public async Task OtherObjectiveSign_DoesNotSuppressAreaExit()
    {
        var position = new Vector3(10f, 20f, 30f);
        var unrelatedSign = new SphereQuest { ComponentId = 200, Xyz = position, Radius = 5f };
        var matchingSigns = QuestAreaSphereExitRules.MatchingSigns([unrelatedSign], componentId: 100);

        await Assert.That(matchingSigns).IsEmpty();
        await Assert.That(QuestAreaSphereExitRules.PlayerRemainsInMatchingSign(matchingSigns, position)).IsFalse();
    }
}

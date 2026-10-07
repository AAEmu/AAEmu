using AAEmu.Game.Models.Game.Units;

namespace AAEmu.UnitTests.Game.Models.Game.Units;

public class SlaveAttachedStreamRulesTests
{
    [Test]
    public async Task ShouldFollowHull_RequiresBothUnitsLiveAndAnAttachPoint()
    {
        await Assert.That(SlaveAttachedStreamRules.ShouldFollowHull(1857, 1864, 2)).IsTrue();
        await Assert.That(SlaveAttachedStreamRules.ShouldFollowHull(0, 1864, 2)).IsFalse();
        await Assert.That(SlaveAttachedStreamRules.ShouldFollowHull(1857, 0, 2)).IsFalse();
        await Assert.That(SlaveAttachedStreamRules.ShouldFollowHull(1857, 1864, -1)).IsFalse();
    }

    [Test]
    public async Task ChildObjIdsToFollow_SkipsUnspawnedAndUnattachedChildren()
    {
        // A ship's real equipment set: figurehead, foresail, mainsail — with one child not spawned
        // yet and one that has no attach point, both of which must not be streamed with the hull.
        var children = new (uint ObjId, int AttachPointId)[]
        {
            (1864, 2),
            (0, 3),
            (1866, -1),
            (1867, 5),
        };

        var ids = SlaveAttachedStreamRules.ChildObjIdsToFollow(1857, children).ToArray();

        await Assert.That(ids.Length).IsEqualTo(2);
        await Assert.That(ids[0]).IsEqualTo(1864u);
        await Assert.That(ids[1]).IsEqualTo(1867u);
    }

    [Test]
    public async Task ChildObjIdsToFollow_NoChildrenIsEmpty()
    {
        var ids = SlaveAttachedStreamRules.ChildObjIdsToFollow(1857, []).ToArray();
        await Assert.That(ids.Length).IsEqualTo(0);
    }
}

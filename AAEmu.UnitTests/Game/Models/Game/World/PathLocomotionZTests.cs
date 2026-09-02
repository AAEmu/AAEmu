using System.Numerics;

using AAEmu.Game.Models.Game.World;

namespace AAEmu.UnitTests.Game.Models.Game.World;

public class PathLocomotionZTests
{
    [Test]
    public async Task ShouldUseLerpedMoveHeight_WhenFollowingPathWaypoint()
    {
        var peek = new Vector3(10f, 20f, 100f);
        var moveTarget = new Vector3(10.1f, 20f, 100f);

        var result = PathLocomotionZ.ShouldUseLerpedMoveHeight(
            geoDataMode: true,
            hasZoneBai: false,
            hasPathQueue: true,
            moveTarget: moveTarget,
            pathPeek: peek,
            currentZ: 99f);

        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task ShouldUseLerpedMoveHeight_WhenZoneBaiAndTargetHasZ()
    {
        var result = PathLocomotionZ.ShouldUseLerpedMoveHeight(
            geoDataMode: true,
            hasZoneBai: true,
            hasPathQueue: false,
            moveTarget: new Vector3(100f, 200f, 150f),
            pathPeek: Vector3.Zero,
            currentZ: 148f);

        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task ShouldNotUseLerpedMoveHeight_WhenGeoDataOff()
    {
        var result = PathLocomotionZ.ShouldUseLerpedMoveHeight(
            geoDataMode: false,
            hasZoneBai: true,
            hasPathQueue: true,
            moveTarget: new Vector3(1f, 2f, 3f),
            pathPeek: new Vector3(1f, 2f, 3f),
            currentZ: 3f);

        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task ShouldNotUseLerpedMoveHeight_WhenOutdoorDirectMoveWithoutZoneBai()
    {
        var result = PathLocomotionZ.ShouldUseLerpedMoveHeight(
            geoDataMode: true,
            hasZoneBai: false,
            hasPathQueue: false,
            moveTarget: new Vector3(100f, 200f, 130f),
            pathPeek: Vector3.Zero,
            currentZ: 129f);

        await Assert.That(result).IsFalse();
    }
}

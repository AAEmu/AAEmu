using AAEmu.Game.Models.Game.DoodadObj.Funcs;

namespace AAEmu.UnitTests.Game.Models.Game.DoodadObj;

public class DoodadSpawnPlacementTests
{
    [Test]
    public async Task SourcePlacementUsesDoodadFacing()
    {
        var result = DoodadSpawnPlacement.Resolve(2, 2, 10, 20, 30, MathF.PI / 2, 0, 2, 180);

        await Assert.That(result.HasValue).IsTrue();
        await Assert.That(result.Value.X).IsEqualTo(10).Within(0.001f);
        await Assert.That(result.Value.Y).IsEqualTo(22).Within(0.001f);
        await Assert.That(result.Value.Z).IsEqualTo(30);
        await Assert.That(result.Value.Yaw).IsEqualTo(MathF.PI * 1.5f).Within(0.001f);
    }

    [Test]
    public async Task WorldOrientationDoesNotInheritDoodadFacing()
    {
        var result = DoodadSpawnPlacement.Resolve(2, 1, 0, 0, 4, MathF.PI / 2, 0, 1, 180);

        await Assert.That(result.HasValue).IsTrue();
        await Assert.That(result.Value.Yaw).IsEqualTo(MathF.PI).Within(0.001f);
    }

    [Test]
    public async Task SourceToSpawnFacesResolvedPosition()
    {
        var result = DoodadSpawnPlacement.Resolve(1, 3, 3, 5, 7, 0, 90, 4, 0);

        await Assert.That(result.HasValue).IsTrue();
        await Assert.That(result.Value.X).IsEqualTo(3).Within(0.001f);
        await Assert.That(result.Value.Y).IsEqualTo(9).Within(0.001f);
        await Assert.That(result.Value.Yaw).IsEqualTo(MathF.PI / 2).Within(0.001f);
    }

    [Test]
    public async Task ExactRangesAreAcceptedAndVariableRangesAreRejected()
    {
        await Assert.That(DoodadSpawnPlacement.ResolveExact(5, 5)).IsEqualTo(5);
        await Assert.That(DoodadSpawnPlacement.ResolveExact(1, 5)).IsNull();
    }

    [Test]
    public async Task UnknownDirectionFailsWithoutPlacement()
    {
        await Assert.That(DoodadSpawnPlacement.Resolve(9, 1, 0, 0, 0, 0, 0, 1, 0)).IsNull();
        await Assert.That(DoodadSpawnPlacement.Resolve(2, 9, 0, 0, 0, 0, 0, 1, 0)).IsNull();
    }
}

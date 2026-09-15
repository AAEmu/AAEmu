using AAEmu.Game.Scripts.Commands;

namespace AAEmu.UnitTests.Game.Scripts.Commands;

public class AddCargoMaterialsTests
{
    [Test]
    public async Task TryParseArguments_NoAmounts_UsesAuthoredRecipeAndCurrentZone()
    {
        var result = AddCargoMaterials.TryParseArguments([], 8, out var zoneGroupId, out var amounts);

        await Assert.That(result).IsTrue();
        await Assert.That(zoneGroupId).IsEqualTo(8u);
        await Assert.That(amounts).IsNull();
    }

    [Test]
    public async Task TryParseArguments_OneAmount_AppliesToEveryMaterial()
    {
        var result = AddCargoMaterials.TryParseArguments(["2"], 8, out var zoneGroupId, out var amounts);

        await Assert.That(result).IsTrue();
        await Assert.That(zoneGroupId).IsEqualTo(8u);
        await Assert.That(amounts).IsEquivalentTo(new uint[] { 2 });
    }

    [Test]
    public async Task TryParseArguments_PerMaterialAmounts_AcceptsZoneOverride()
    {
        var result = AddCargoMaterials.TryParseArguments(
            ["50", "30", "10", "8"],
            21,
            out var zoneGroupId,
            out var amounts);

        await Assert.That(result).IsTrue();
        await Assert.That(zoneGroupId).IsEqualTo(8u);
        await Assert.That(amounts).IsEquivalentTo(new uint[] { 50, 30, 10 });
    }

    [Test]
    public async Task TryParseArguments_InvalidShape_IsRejected()
    {
        var result = AddCargoMaterials.TryParseArguments(["50", "30"], 8, out _, out _);

        await Assert.That(result).IsFalse();
    }
}

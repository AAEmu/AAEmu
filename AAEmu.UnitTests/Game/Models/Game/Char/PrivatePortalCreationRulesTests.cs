using AAEmu.Game.Models.Game.Char;

namespace AAEmu.UnitTests.Game.Models.Game.Char;

public class PrivatePortalCreationRulesTests
{
    [Test]
    public async Task AcceptsFiniteCoordinatesAndAName()
    {
        await Assert.That(PrivatePortalCreationRules.IsValid("camp", 1f, 2f, 3f, 4f)).IsTrue();
    }

    [Test]
    public async Task RejectsMissingNamesAndNonFiniteCoordinates()
    {
        await Assert.That(PrivatePortalCreationRules.IsValid(null, 1f, 2f, 3f, 4f)).IsFalse();
        await Assert.That(PrivatePortalCreationRules.IsValid(" ", 1f, 2f, 3f, 4f)).IsFalse();
        await Assert.That(PrivatePortalCreationRules.IsValid("camp", float.NaN, 2f, 3f, 4f)).IsFalse();
        await Assert.That(PrivatePortalCreationRules.IsValid("camp", 1f, float.PositiveInfinity, 3f, 4f)).IsFalse();
        await Assert.That(PrivatePortalCreationRules.IsValid("camp", 1f, 2f, float.NegativeInfinity, 4f)).IsFalse();
        await Assert.That(PrivatePortalCreationRules.IsValid("camp", 1f, 2f, 3f, float.NaN)).IsFalse();
    }

    [Test]
    public async Task BoundsNamesByUtf8Bytes()
    {
        await Assert.That(PrivatePortalCreationRules.IsValidName(new string('a', 128))).IsTrue();
        await Assert.That(PrivatePortalCreationRules.IsValidName(new string('a', 129))).IsFalse();
        await Assert.That(PrivatePortalCreationRules.IsValidName(new string('€', 42))).IsTrue();
        await Assert.That(PrivatePortalCreationRules.IsValidName(new string('€', 43))).IsFalse();
    }
}

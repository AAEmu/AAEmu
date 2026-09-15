using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

public class BlinkLandingRulesTests
{
    // AddDistanceToFrontDeg walks distance * (cos, sin) from the caster position, so yaw 0 moves along
    // +X and yaw 90 along +Y; the offset is what the side-step blinks pass as value2.
    [Test]
    public async Task ForwardBlink_MovesAlongTheFacing()
    {
        var (x, y) = BlinkLandingRules.GetLanding(x: 0f, y: 0f, casterYawDegrees: 0f, distanceMetres: 15, offsetDegrees: 0);
        await Assert.That(x).IsEqualTo(15f).Within(0.01f);
        await Assert.That(y).IsEqualTo(0f).Within(0.01f);
    }

    [Test]
    public async Task SideStepBlink_AppliesTheOffsetDegrees()
    {
        var (rightX, rightY) = BlinkLandingRules.GetLanding(0f, 0f, 0f, 15, 90);
        await Assert.That(rightX).IsEqualTo(0f).Within(0.01f);
        await Assert.That(rightY).IsEqualTo(15f).Within(0.01f);

        var (leftX, leftY) = BlinkLandingRules.GetLanding(0f, 0f, 0f, 15, -90);
        await Assert.That(leftX).IsEqualTo(0f).Within(0.01f);
        await Assert.That(leftY).IsEqualTo(-15f).Within(0.01f);
    }

    [Test]
    public async Task OffsetIsRelativeToTheCasterFacing()
    {
        var (x, y) = BlinkLandingRules.GetLanding(x: 100f, y: 200f, casterYawDegrees: 90f, distanceMetres: 10, offsetDegrees: -90);
        // yaw 90 + (-90) = 0 -> straight along +X from the caster position.
        await Assert.That(x).IsEqualTo(110f).Within(0.01f);
        await Assert.That(y).IsEqualTo(200f).Within(0.01f);
    }
}

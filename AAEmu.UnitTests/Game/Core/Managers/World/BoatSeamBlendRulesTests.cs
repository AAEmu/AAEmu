using AAEmu.Game.Core.Managers.World;

namespace AAEmu.UnitTests.Game.Core.Managers.World;

public class BoatSeamBlendRulesTests
{
    [Test]
    public async Task Residual_IsTheOutgoingTrackMinusTheIncomingBody()
    {
        // Live 19:04:46 (full lock): 218's track at the switch vs 186's first body.
        var r = BoatSeamBlendRules.Residual(13087.01f, 10184.52f, 100.26f, 11.5f, 13087.93f, 10184.68f, 100.25f, 11.7f);
        await Assert.That(r).IsNotNull();
        await Assert.That(r!.Value.X).IsEqualTo(-0.92f).Within(0.01f);
        await Assert.That(r.Value.Y).IsEqualTo(-0.16f).Within(0.01f);
        await Assert.That(r.Value.Z).IsEqualTo(0.01f).Within(0.001f);
        await Assert.That(r.Value.YawDegrees).IsEqualTo(-0.2f).Within(0.01f);
    }

    [Test]
    public async Task Residual_IgnoresNothingAndRefusesAJump()
    {
        await Assert.That(BoatSeamBlendRules.Residual(1f, 2f, 3f, 90f, 1f, 2f, 3f, 90f)).IsNull();
        // A different plant, not a seam residual.
        await Assert.That(BoatSeamBlendRules.Residual(0f, 0f, 100f, 0f, 3f, 0f, 100f, 0f)).IsNull();
        await Assert.That(BoatSeamBlendRules.Residual(0f, 0f, 100f, 0f, 0f, 0f, 96f, 0f)).IsNull();
    }

    [Test]
    public async Task Residual_YawWrapsAcrossTheSeam()
    {
        var r = BoatSeamBlendRules.Residual(0f, 0f, 100f, 179f, 0f, 0f, 100f, -179f);
        await Assert.That(r!.Value.YawDegrees).IsEqualTo(-2f).Within(0.01f);
    }

    [Test]
    public async Task Weight_FallsLinearlyOverTheBlendWindow()
    {
        await Assert.That(BoatSeamBlendRules.BlendMs).IsEqualTo(500L);
        await Assert.That(BoatSeamBlendRules.Weight(0)).IsEqualTo(1f);
        await Assert.That(BoatSeamBlendRules.Weight(250)).IsEqualTo(0.5f).Within(0.001f);
        await Assert.That(BoatSeamBlendRules.Weight(500)).IsEqualTo(0f);
        await Assert.That(BoatSeamBlendRules.IsActive(499)).IsTrue();
        await Assert.That(BoatSeamBlendRules.IsActive(500)).IsFalse();
        await Assert.That(BoatSeamBlendRules.IsActive(-1)).IsFalse();
    }
}

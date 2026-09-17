using AAEmu.Game.Models.Game.Skills.Effects;

namespace AAEmu.UnitTests.Game.Models.Game.Skills.Effects;

/// <summary>
/// <see cref="SpawnScatterRules"/>: the placement band <c>spawn_effects</c> authors as
/// <c>pos_angle_min/_max</c> and <c>pos_distance_min/_max</c>.
/// </summary>
public class SpawnScatterRulesTests
{
    [Test]
    public async Task APointBand_IsExactAtEveryRoll()
    {
        // 2,422 of the 2,659 angle rows and 2,411 distance rows author min == max. Those rows must place
        // exactly where they always did, at any roll.
        foreach (var roll in new[] { 0, 1, 137, 499, 500, 999, 1000 })
        {
            await Assert.That(SpawnScatterRules.Scatter(30f, 30f, roll)).IsEqualTo(30f);
            await Assert.That(SpawnScatterRules.Scatter(0f, 0f, roll)).IsEqualTo(0f);
            await Assert.That(SpawnScatterRules.Scatter(-45f, -45f, roll)).IsEqualTo(-45f);
        }
    }

    [Test]
    public async Task AWholeCircleBand_CoversBothEnds()
    {
        // Mate effects 3438/3439 author 0-360; 0 and 1000 hand back both ends.
        await Assert.That(SpawnScatterRules.Scatter(0f, 360f, 0)).IsEqualTo(0f);
        await Assert.That(SpawnScatterRules.Scatter(0f, 360f, 1000)).IsEqualTo(360f);
        await Assert.That(SpawnScatterRules.Scatter(0f, 360f, 500)).IsEqualTo(180f);
    }

    [Test]
    public async Task TheEffectResolvesAPointBandExactly()
    {
        // What SpawnEffect itself draws, over the 2,422 angle rows and 2,411 distance rows that author
        // min == max: those summons must land exactly where they always did.
        await Assert.That(SpawnEffect.ResolvePosAngle(30f, 30f)).IsEqualTo(30f);
        await Assert.That(SpawnEffect.ResolvePosDistance(5f, 5f)).IsEqualTo(5f);
        await Assert.That(SpawnEffect.ResolvePosAngle(0f, 0f)).IsEqualTo(0f);
    }

    [Test]
    public async Task TheEffectResolvesABandInsideIt()
    {
        // Mate effect 3438 authors angle 0-360 and distance 1-5; mate effect 2266 distance 50-100.
        for (var i = 0; i < 100; i++)
        {
            var angle = SpawnEffect.ResolvePosAngle(0f, 360f);
            var distance = SpawnEffect.ResolvePosDistance(1f, 5f);

            await Assert.That(angle).IsGreaterThanOrEqualTo(0f);
            await Assert.That(angle).IsLessThanOrEqualTo(360f);
            await Assert.That(distance).IsGreaterThanOrEqualTo(1f);
            await Assert.That(distance).IsLessThanOrEqualTo(5f);
        }
    }

    [Test]
    public async Task TwoSpawnsFromTheSameBand_Differ()
    {
        // The acceptance: two spawns from one effect land at different offsets.
        var places = new HashSet<(int Angle, int Distance)>();

        for (var i = 0; i < 50; i++)
        {
            places.Add((
                (int)SpawnEffect.ResolvePosAngle(0f, 360f),
                (int)SpawnEffect.ResolvePosDistance(1f, 5f)));
        }

        await Assert.That(places.Count).IsGreaterThan(1);
    }

    [Test]
    public async Task TheEffectRayCastOrigin_WithoutAnOffset_IsTheAnchorZ()
    {
        // 2,255 of the 2,803 rows carry ray_off_set 0 and keep placing at the anchor's own Z.
        await Assert.That(SpawnEffect.ResolveRayCastOriginZ(123.5f, 0f)).IsEqualTo(123.5f);
    }

    [Test]
    public async Task ADistanceBand_StaysInsideIt()
    {
        // Mate effect 2266 authors distance 50-100.
        for (var roll = 0; roll <= 1000; roll += 50)
        {
            var distance = SpawnScatterRules.Distance(50f, 100f, roll);
            await Assert.That(distance).IsGreaterThanOrEqualTo(50f);
            await Assert.That(distance).IsLessThanOrEqualTo(100f);
        }
    }

    [Test]
    public async Task Scatters_OnlyWhenTheEndsDiffer()
    {
        await Assert.That(SpawnScatterRules.Scatters(0f, 0f)).IsFalse();
        await Assert.That(SpawnScatterRules.Scatters(1f, 1f)).IsFalse();
        await Assert.That(SpawnScatterRules.Scatters(0f, 360f)).IsTrue();
        await Assert.That(SpawnScatterRules.Scatters(1f, 5f)).IsTrue();
    }

    [Test]
    public async Task AnInvertedBand_DoesNotMoveThePoint()
    {
        // A malformed row is the minimum, not a crash and not a negative span.
        await Assert.That(SpawnScatterRules.Scatter(10f, 4f, 1000)).IsEqualTo(10f);
    }

    [Test]
    public async Task RayCastOrigin_WithoutAnOffset_IsTheAnchorZ()
    {
        // 2,255 of the 2,803 rows carry ray_off_set 0 and keep placing at the anchor's own Z.
        await Assert.That(SpawnScatterRules.RayCastOriginZ(123.5f, 0f)).IsEqualTo(123.5f);
    }

    [Test]
    public async Task RayCastOrigin_WithAnOffset_RaisesTheCastOrigin()
    {
        // 50 on 183 rows, 5 on 135, 10 on 74.
        await Assert.That(SpawnScatterRules.RayCastOriginZ(100f, 50f)).IsEqualTo(150f);
        await Assert.That(SpawnScatterRules.RayCastOriginZ(-5f, 5f)).IsEqualTo(0f);
        await Assert.That(SpawnEffect.ResolveRayCastOriginZ(100f, 50f)).IsEqualTo(150f);
    }
}

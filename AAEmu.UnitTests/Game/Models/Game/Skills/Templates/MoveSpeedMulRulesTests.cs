using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.UnitTests.Game.Models.Game.Skills.Templates;

public class MoveSpeedMulRulesTests
{
    [Test]
    public async Task FlatBonus_LeavesSailTrimAndSquareSailDeltasAlone()
    {
        await Assert.That(MoveSpeedMulRules.FlatBonus(6, false)).IsEqualTo(6);
        await Assert.That(MoveSpeedMulRules.FlatBonus(800, false)).IsEqualTo(800);
        await Assert.That(MoveSpeedMulRules.FlatBonus(1000, false)).IsEqualTo(1000);
    }

    [Test]
    public async Task FlatBonus_TreatsABasicEngineAsNoExtraOnTheBaseline()
    {
        await Assert.That(MoveSpeedMulRules.FlatBonus(1000, true)).IsEqualTo(0);
    }

    [Test]
    public async Task FlatBonus_KeepsTheUpgradeAboveTheBaseline()
    {
        await Assert.That(MoveSpeedMulRules.FlatBonus(1050, true)).IsEqualTo(50);
        await Assert.That(MoveSpeedMulRules.FlatBonus(1500, true)).IsEqualTo(500);
        await Assert.That(MoveSpeedMulRules.FlatBonus(1550, true)).IsEqualTo(550);
    }

    [Test]
    public async Task IsPropulsionRating_RequiresSpeedRatingAndHullHpTogether()
    {
        var engine = new[]
        {
            Value(UnitAttribute.MoveSpeedMul, 1000),
            Value(UnitAttribute.MaxHealth, 5000)
        };
        var dash = new[] { Value(UnitAttribute.MoveSpeedMul, 1000) };
        var sail = new[] { Value(UnitAttribute.MoveSpeedMul, 800) };
        var hullHpOnly = new[] { Value(UnitAttribute.MaxHealth, 5000) };

        await Assert.That(MoveSpeedMulRules.IsPropulsionRating(engine)).IsTrue();
        await Assert.That(MoveSpeedMulRules.IsPropulsionRating(dash)).IsFalse();
        await Assert.That(MoveSpeedMulRules.IsPropulsionRating(sail)).IsFalse();
        await Assert.That(MoveSpeedMulRules.IsPropulsionRating(hullHpOnly)).IsFalse();
    }

    [Test]
    public async Task ShouldRelayToZone_SkipsOnlyTheBasicEngineRating()
    {
        var basic = new[]
        {
            Value(UnitAttribute.MoveSpeedMul, 1000),
            Value(UnitAttribute.MaxHealth, 5000)
        };
        var mythic = new[]
        {
            Value(UnitAttribute.MoveSpeedMul, 1500),
            Value(UnitAttribute.MaxHealth, 15000)
        };
        var sail = new[] { Value(UnitAttribute.MoveSpeedMul, 800) };

        await Assert.That(MoveSpeedMulRules.ShouldRelayToZone(basic)).IsFalse();
        await Assert.That(MoveSpeedMulRules.ShouldRelayToZone(mythic)).IsTrue();
        await Assert.That(MoveSpeedMulRules.ShouldRelayToZone(sail)).IsTrue();
    }

    private static BonusTemplate Value(UnitAttribute attribute, long value) =>
        new()
        {
            Attribute = attribute,
            ModifierType = UnitModifierType.Value,
            Value = value
        };
}

using AAEmu.Game.Models.Game.Weather;

namespace AAEmu.UnitTests.Game.Models.Game.Weather;

public class SnowStateRulesTests
{
    [Test]
    [Arguments(false, false, false)]
    [Arguments(true, false, true)]
    [Arguments(false, true, true)]
    [Arguments(true, true, true)]
    public async Task WithoutAHold_EitherSourceTurnsSnowOn(bool configured, bool cycle, bool expected)
    {
        await Assert.That(SnowStateRules.Effective(null, configured, cycle)).IsEqualTo(expected);
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(true, false)]
    [Arguments(false, true)]
    [Arguments(true, true)]
    public async Task AHold_WinsOverBothSources(bool configured, bool cycle)
    {
        await Assert.That(SnowStateRules.Effective(true, configured, cycle)).IsTrue();
        await Assert.That(SnowStateRules.Effective(false, configured, cycle)).IsFalse();
    }
}

using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

/// <summary>
/// ConsumeLaborPower (type 86) charges labor. All 71 shipped rows carry zeros, so the slot only has to behave
/// sensibly for a row that does set it: a positive amount wins, anything else keeps the template's cost.
/// </summary>
public class LaborPowerRulesTests
{
    [Test]
    public async Task ARowThatNamesAnAmount_ChargesThat()
    {
        await Assert.That(LaborPowerRules.ResolveCost(150, 40)).IsEqualTo(150);
    }

    [Test]
    public async Task AZeroRow_KeepsTheTemplatesCost()
    {
        // What every shipped row does today.
        await Assert.That(LaborPowerRules.ResolveCost(0, 40)).IsEqualTo(40);
    }

    [Test]
    public async Task NoAmountAnywhere_ChargesNothing()
    {
        await Assert.That(LaborPowerRules.ResolveCost(0, 0)).IsEqualTo(0);
    }

    [Test]
    public async Task ANegativeTemplateCost_DoesNotCreditLabor()
    {
        await Assert.That(LaborPowerRules.ResolveCost(0, -25)).IsEqualTo(0);
        await Assert.That(LaborPowerRules.ResolveCost(-10, -25)).IsEqualTo(0);
    }
}

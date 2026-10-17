using AAEmu.Game.Models.Game.Char;

namespace AAEmu.UnitTests.Game.Models.Game.Char;

public class GoldCommandRulesTests
{
    [Test]
    public async Task NormalizeArgs_BareAmount_BecomesAddSelf()
    {
        var shaped = GoldCommandRules.NormalizeArgs(["100"]);
        await Assert.That(shaped).IsEquivalentTo(["add", "self", "100"]);

        shaped = GoldCommandRules.NormalizeArgs(["5", "50", "25"]);
        await Assert.That(shaped).IsEquivalentTo(["add", "self", "5", "50", "25"]);
    }

    [Test]
    public async Task NormalizeArgs_AlreadySubcommand_Unchanged()
    {
        var shaped = GoldCommandRules.NormalizeArgs(["add", "self", "10"]);
        await Assert.That(shaped).IsEquivalentTo(["add", "self", "10"]);
    }

    [Test]
    public async Task NormalizeArgs_NamedTarget_PrefixedWithAdd()
    {
        var shaped = GoldCommandRules.NormalizeArgs(["Tester", "10"]);
        await Assert.That(shaped).IsEquivalentTo(["add", "Tester", "10"]);
    }

    [Test]
    public async Task MoneyDelta_SetIsAbsolute_AddIsRelative()
    {
        await Assert.That(GoldCommandRules.MoneyDelta("add", 10000, 50000)).IsEqualTo(10000);
        await Assert.That(GoldCommandRules.MoneyDelta("remove", -10000, 50000)).IsEqualTo(-10000);
        await Assert.That(GoldCommandRules.MoneyDelta("set", 10000, 50000)).IsEqualTo(-40000);
        await Assert.That(GoldCommandRules.MoneyDelta("set", 80000, 50000)).IsEqualTo(30000);
    }
}

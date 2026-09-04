using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Slaves;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.UnitTests.Game.Models.Game.Slaves;

public class SlaveMassRulesTests
{
    [Test]
    public async Task KitAddedMass_SumsChildSlaveAndItemMass()
    {
        // Growling mythic squares / figure / engine are child-slave 188; bubbling masts are item 188.
        long[] items = [1000, 1000];
        long[] children = [2000, 2000, 6000, 5800, 11000];

        var added = SlaveMassRules.KitAddedMass(items, children);
        await Assert.That(added).IsEqualTo(28800);
        await Assert.That(SlaveMassRules.TotalDisplayedMass(45000, added)).IsEqualTo(73800);
    }

    [Test]
    public async Task TotalDisplayedMass_IsEmptyHullPlusKit()
    {
        // Summon card 72500 = ship_models.mass 45000 + 27500 of part 188s.
        await Assert.That(SlaveMassRules.TotalDisplayedMass(45000, 27500)).IsEqualTo(72500);
    }

    [Test]
    public async Task KitAddedMass_IgnoresNullListsAndCargoIsNotASource()
    {
        await Assert.That(SlaveMassRules.KitAddedMass(null, null)).IsEqualTo(0);
        await Assert.That(SlaveMassRules.KitAddedMass([], [2000])).IsEqualTo(2000);
        await Assert.That(SlaveMassRules.KitAddedMass([1000], [])).IsEqualTo(1000);
    }

    [Test]
    public async Task MassFromBonuses_TakesOnlyFlatMassRows()
    {
        var bonuses = new[]
        {
            Value(UnitAttribute.Mass, 2000),
            Value(UnitAttribute.Mass, 6000),
            Value(UnitAttribute.MaxHealth, 15000),
            new BonusTemplate
            {
                Attribute = UnitAttribute.Mass,
                ModifierType = UnitModifierType.Percent,
                Value = 50
            }
        };

        await Assert.That(SlaveMassRules.MassFromBonuses(bonuses)).IsEqualTo(8000);
        await Assert.That(SlaveMassRules.MassFromBonuses(null)).IsEqualTo(0);
    }

    private static BonusTemplate Value(UnitAttribute attribute, long value) =>
        new()
        {
            Attribute = attribute,
            ModifierType = UnitModifierType.Value,
            Value = value
        };
}

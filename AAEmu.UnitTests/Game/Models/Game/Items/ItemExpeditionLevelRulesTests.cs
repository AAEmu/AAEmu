using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.UnitTests.Game.Models.Game.Items;

/// <summary>
/// The guild-level floor that gates an item's use, from <c>items.expedition_level</c>.
/// </summary>
public class ItemExpeditionLevelRulesTests
{
    [Test]
    public async Task UngatedItem_AllowsAnyGuildLevel()
    {
        var template = new ItemTemplate { Id = 20, ExpeditionLevel = 0 };

        await Assert.That(ItemExpeditionLevelRules.AllowsUse(template, 0)).IsTrue();
        await Assert.That(ItemExpeditionLevelRules.AllowsUse(template, 9)).IsTrue();
    }

    [Test]
    public async Task GatedItem_RefusesAGuildBelowTheFloor()
    {
        var template = new ItemTemplate { Id = 21, ExpeditionLevel = 5 };

        await Assert.That(ItemExpeditionLevelRules.AllowsUse(template, 4)).IsFalse();
    }

    [Test]
    public async Task GatedItem_AllowsAGuildAtOrAboveTheFloor()
    {
        var template = new ItemTemplate { Id = 22, ExpeditionLevel = 5 };

        await Assert.That(ItemExpeditionLevelRules.AllowsUse(template, 5)).IsTrue();
        await Assert.That(ItemExpeditionLevelRules.AllowsUse(template, 6)).IsTrue();
    }

    [Test]
    public async Task GatedItem_RefusesAGuildlessCharacter()
    {
        var template = new ItemTemplate { Id = 23, ExpeditionLevel = 1 };

        // No guild means level 0, which sits below every positive floor.
        await Assert.That(ItemExpeditionLevelRules.AllowsUse(template, 0)).IsFalse();
    }

    [Test]
    public async Task MissingTemplate_CannotBeGated()
    {
        await Assert.That(ItemExpeditionLevelRules.AllowsUse(null, 0)).IsTrue();
    }

    [Test]
    public async Task ItemGate_IsAFloorAndKeepsAdmittingGuildsThatOutgrewIt()
    {
        // The item column names one number, so it is a floor and not a band. The skill-side
        // requirement operator takes two bounds and only admits a level between them; reusing
        // it here would lock a member out as soon as their guild passed the floor.
        var template = new ItemTemplate { Id = 30, ExpeditionLevel = 5 };

        for (uint level = 0; level <= 12; level++)
        {
            var viaItem = ItemExpeditionLevelRules.AllowsUse(template, level);
            var viaBand = UnitReqOperatorRules.PassesExpeditionLevel(5, 5, level);
            await Assert.That(viaItem).IsEqualTo(level >= 5);
            // Pin the difference: past the floor the two must disagree, or the band form is back.
            if (level > 5)
                await Assert.That(viaItem).IsNotEqualTo(viaBand);
        }
    }
}

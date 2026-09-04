using AAEmu.Game.Models.Game.Items.Templates;
using AAEmu.Game.Scripts.Commands;

namespace AAEmu.UnitTests.Game.Scripts.Commands;

public class SpawnTradePackTests
{
    [Test]
    public async Task ResolveProductionZoneGroupId_UsesAuthoredZoneAwayFromProductionArea()
    {
        var template = new BackpackTemplate { SpecialtyZoneId = 21 };

        var result = SpawnTradePack.ResolveProductionZoneGroupId(template, 8, null);

        await Assert.That(result).IsEqualTo(21u);
    }

    [Test]
    public async Task ResolveProductionZoneGroupId_UsesCurrentZoneWhenTemplateHasNoAuthoredZone()
    {
        var template = new BackpackTemplate();

        var result = SpawnTradePack.ResolveProductionZoneGroupId(template, 8, null);

        await Assert.That(result).IsEqualTo(8u);
    }

    [Test]
    public async Task ResolveProductionZoneGroupId_UsesExplicitOverrideForValidation()
    {
        var template = new BackpackTemplate { SpecialtyZoneId = 21 };

        var result = SpawnTradePack.ResolveProductionZoneGroupId(template, 8, 22);

        await Assert.That(result).IsEqualTo(22u);
    }
}

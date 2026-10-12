using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.DoodadObj.Funcs;
using AAEmu.Game.Models.Game.DoodadObj.Templates;

namespace AAEmu.UnitTests.Game.Models.Game.DoodadObj;

public class ExpeditionPortalDoodadRulesTests
{
    [Test]
    public async Task PhaseOpensPortalUi_RequiresExpeditionPortalUiOpen()
    {
        await Assert.That(ExpeditionPortalDoodadRules.PhaseOpensPortalUi(
            [nameof(DoodadFuncExpeditionPortalUiOpen)])).IsTrue();
        await Assert.That(ExpeditionPortalDoodadRules.PhaseOpensPortalUi(["DoodadFuncUse"])).IsFalse();
        await Assert.That(ExpeditionPortalDoodadRules.PhaseOpensPortalUi(null)).IsFalse();
    }

    [Test]
    public async Task TemplateHasPortalUi_ScansEveryPhaseGroup()
    {
        var template = new DoodadTemplate
        {
            FuncGroups =
            [
                new DoodadFuncGroups { Id = 1 },
                new DoodadFuncGroups { Id = 2 }
            ]
        };

        await Assert.That(ExpeditionPortalDoodadRules.TemplateHasPortalUi(template, id =>
            id == 2 ? [nameof(DoodadFuncExpeditionPortalUiOpen)] : ["DoodadFuncUse"])).IsTrue();
        await Assert.That(ExpeditionPortalDoodadRules.TemplateHasPortalUi(template, _ => ["DoodadFuncUse"]))
            .IsFalse();
    }
}

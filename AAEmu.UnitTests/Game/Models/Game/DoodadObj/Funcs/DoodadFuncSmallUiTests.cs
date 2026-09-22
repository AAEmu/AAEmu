using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.DoodadObj.Funcs;
using AAEmu.Game.Models.Game.DoodadObj.Templates;

namespace AAEmu.UnitTests.Game.Models.Game.DoodadObj.Funcs;

public class DoodadFuncSmallUiTests
{
    [Test]
    [Arguments("instant")]
    [Arguments("itemChanger")]
    [Arguments("nuonsArrow")]
    [Arguments("siegeRaid")]
    public async Task ClientOwnedMarker_LeavesDoodadStateUnchanged(string kind)
    {
        DoodadFuncTemplate template = kind switch
        {
            "instant" => new DoodadFuncInstantUiOpen { Id = 8, ZoneGroupId = 103 },
            "itemChanger" => new DoodadFuncItemChangerUiOpen { Id = 1 },
            "nuonsArrow" => new DoodadFuncNuonsArrowUiOpen { Id = 5 },
            "siegeRaid" => new DoodadFuncSiegeRaid { Id = 1 },
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        var doodad = new Doodad { Data = 27 };
        var func = new DoodadFunc { NextPhase = -1 };

        var completed = doodad.DoFuncWithApply(null, func,
            (caster, owner) => template.Use(caster, owner, skillId: 0, func.NextPhase));

        await Assert.That(completed).IsTrue();
        await Assert.That(doodad.Data).IsEqualTo(27);
        await Assert.That(doodad.ToNextPhase).IsFalse();
    }
}

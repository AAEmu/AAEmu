using AAEmu.Game.Models.Game.DoodadObj;

namespace AAEmu.UnitTests.Game.Models.Game.DoodadObj;

public class DoodadFuncActCountTests
{
    [Test]
    public async Task TryApply_NoCount_DoesNotGate()
    {
        var owner = new Doodad();
        var func = new DoodadFunc { Count = 0 };
        await Assert.That(DoodadFuncActCount.TryApply(owner, func, out var stay)).IsFalse();
        await Assert.That(stay).IsFalse();
        await Assert.That(owner.Data).IsEqualTo(0);
    }

    [Test]
    public async Task TryApply_IncrementsUntilQuotaThenResets()
    {
        var owner = new Doodad();
        var func = new DoodadFunc { Count = 3 };

        await Assert.That(DoodadFuncActCount.TryApply(owner, func, out var stay1)).IsTrue();
        await Assert.That(stay1).IsTrue();
        await Assert.That(owner.Data).IsEqualTo(1);

        await Assert.That(DoodadFuncActCount.TryApply(owner, func, out var stay2)).IsTrue();
        await Assert.That(stay2).IsTrue();
        await Assert.That(owner.Data).IsEqualTo(2);

        await Assert.That(DoodadFuncActCount.TryApply(owner, func, out var stay3)).IsTrue();
        await Assert.That(stay3).IsFalse();
        await Assert.That(owner.Data).IsEqualTo(0);
    }

    [Test]
    public async Task TryApply_CountOne_AdvancesImmediately()
    {
        var owner = new Doodad();
        var func = new DoodadFunc { Count = 1 };
        await Assert.That(DoodadFuncActCount.TryApply(owner, func, out var stay)).IsTrue();
        await Assert.That(stay).IsFalse();
        await Assert.That(owner.Data).IsEqualTo(0);
    }
}

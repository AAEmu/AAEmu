using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.DoodadObj.Funcs;

namespace AAEmu.UnitTests.Game.Models.Game.DoodadObj;

public class DoodadPhaseRatioTests
{
    [Test]
    public async Task LambMaturation_RatiosAreSeparateChances()
    {
        // doodad_almighties 2675, phase 12772: each ratio change is an independent chance.
        await Assert.That(SelectPhase((9159, 9160, 5808))).IsEqualTo(5808);
        await Assert.That(SelectPhase((9160, 9160, 5808), (559, 560, 5809))).IsEqualTo(5809);
        await Assert.That(SelectPhase((9160, 9160, 5808), (560, 560, 5809), (279, 280, 5810))).IsEqualTo(5810);
    }

    [Test]
    public async Task RatioChanges_KeepTheThirdEqualChanceTransitionReachable()
    {
        // doodad_func_group 9233: 327/328/329 each have ratio 5000.
        await Assert.That(SelectPhase((5000, 5000, 9234), (5000, 5000, 9538), (4999, 5000, 9539)))
            .IsEqualTo(9539);
    }

    [Test]
    public async Task RatioChange_LeavesResidualRollWithoutATransition()
    {
        var owner = new Doodad();
        owner.PhaseRatioRoller = () => 5000;
        var func = new DoodadFuncRatioChange { Ratio = 5000, NextPhase = 930 };

        await Assert.That(func.Use(null, owner)).IsFalse();
        await Assert.That(owner.OverridePhase).IsEqualTo(0);
    }

    [Test]
    public async Task RatioChange_ZeroWeightNeverSelectsRollZero()
    {
        var owner = new Doodad();
        owner.PhaseRatioRoller = () => 0;
        var zeroWeight = new DoodadFuncRatioChange { Ratio = 0, NextPhase = 930 };
        var remainingWeight = new DoodadFuncRatioChange { Ratio = 10000, NextPhase = 931 };

        await Assert.That(zeroWeight.Use(null, owner)).IsFalse();
        await Assert.That(remainingWeight.Use(null, owner)).IsTrue();
        await Assert.That(owner.OverridePhase).IsEqualTo(931);
    }

    [Test]
    public async Task RatioRespawn_UsesOrdered3000And7000Weights()
    {
        // Phase group 1308: 3000 -> doodad 930, then 7000 -> doodad 931.
        await Assert.That(SelectRespawn(2999)).IsEqualTo(930u);
        await Assert.That(SelectRespawn(3000)).IsEqualTo(931u);
        await Assert.That(SelectRespawn(9999)).IsEqualTo(931u);
    }

    private static int SelectPhase(params (int Roll, int Chance, int NextPhase)[] branches)
    {
        var owner = new Doodad();
        var rolls = new Queue<int>(branches.Select(branch => branch.Roll));
        owner.PhaseRatioRoller = rolls.Dequeue;

        foreach (var (_, chance, nextPhase) in branches)
        {
            var func = new DoodadFuncRatioChange { Ratio = chance, NextPhase = nextPhase };
            if (func.Use(null, owner))
                return owner.OverridePhase;
        }

        return 0;
    }

    private static uint SelectRespawn(int roll)
    {
        var owner = new Doodad { Spawner = new DoodadSpawner { Id = 1 } };
        owner.BeginPhaseRatioSelection(roll);

        var branches = new (int Weight, uint DoodadId)[] { (3000, 930), (7000, 931) };
        foreach (var (weight, doodadId) in branches)
        {
            var func = new DoodadFuncRatioRespawn { Ratio = weight, SpawnDoodadId = doodadId };
            if (func.Use(null, owner))
                return owner.Spawner.RespawnDoodadTemplateId;
        }

        return 0;
    }
}

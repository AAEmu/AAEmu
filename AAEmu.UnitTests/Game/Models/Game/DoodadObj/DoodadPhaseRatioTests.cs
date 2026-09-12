using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.DoodadObj.Funcs;

namespace AAEmu.UnitTests.Game.Models.Game.DoodadObj;

public class DoodadPhaseRatioTests
{
    [Test]
    public async Task LambMaturation_SelectsEveryWeightedBranchAtItsLowerBoundary()
    {
        // doodad_almighties 2675, phase 12772: 9160 -> 5808, 560 -> 5809, 280 -> 5810.
        await Assert.That(SelectPhase(0, (9160, 5808), (560, 5809), (280, 5810))).IsEqualTo(5808);
        await Assert.That(SelectPhase(9159, (9160, 5808), (560, 5809), (280, 5810))).IsEqualTo(5808);
        await Assert.That(SelectPhase(9160, (9160, 5808), (560, 5809), (280, 5810))).IsEqualTo(5809);
        await Assert.That(SelectPhase(9720, (9160, 5808), (560, 5809), (280, 5810))).IsEqualTo(5810);
        await Assert.That(SelectPhase(9999, (9160, 5808), (560, 5809), (280, 5810))).IsEqualTo(5810);
    }

    [Test]
    public async Task SheepPenOutcome_CoversTheEntireRatioScale()
    {
        // doodad_almighties 8035, phase 22224: all four weights sum to 10000.
        var branches = new (int Weight, int NextPhase)[]
        {
            (610, 22225),
            (125, 22226),
            (407, 22220),
            (8858, 22219)
        };
        var outcomes = new (int Roll, int Phase)[]
        {
            (0, 22225),
            (609, 22225),
            (610, 22226),
            (734, 22226),
            (735, 22220),
            (1141, 22220),
            (1142, 22219),
            (9999, 22219)
        };

        foreach (var (roll, phase) in outcomes)
        {
            await Assert.That(SelectPhase(roll, branches)).IsEqualTo(phase);
        }

        var counts = branches.ToDictionary(branch => branch.NextPhase, _ => 0);
        for (var roll = 0; roll < Doodad.PhaseRatioScale; roll++)
            counts[SelectPhase(roll, branches)]++;

        foreach (var (weight, nextPhase) in branches)
            await Assert.That(counts[nextPhase]).IsEqualTo(weight);
    }

    [Test]
    public async Task SuccessiveSelections_ResetTheCumulativeWeight()
    {
        var owner = new Doodad();
        var branches = new (int Weight, int NextPhase)[]
        {
            (9160, 5808),
            (560, 5809),
            (280, 5810)
        };

        await Assert.That(SelectPhase(owner, 9160, branches)).IsEqualTo(5809);
        await Assert.That(owner.CumulativePhaseRatio).IsEqualTo(9720);

        await Assert.That(SelectPhase(owner, 9160, branches)).IsEqualTo(5809);
        await Assert.That(owner.CumulativePhaseRatio).IsEqualTo(9720);
    }

    [Test]
    public async Task RatioChange_LeavesResidualRollWithoutATransition()
    {
        var owner = new Doodad();
        owner.BeginPhaseRatioSelection(5000);
        var func = new DoodadFuncRatioChange { Ratio = 5000, NextPhase = 930 };

        await Assert.That(func.Use(null, owner)).IsFalse();
        await Assert.That(owner.OverridePhase).IsEqualTo(0);
        await Assert.That(owner.CumulativePhaseRatio).IsEqualTo(5000);
    }

    [Test]
    public async Task RatioChange_ZeroWeightNeverSelectsRollZero()
    {
        var owner = new Doodad();
        owner.BeginPhaseRatioSelection(0);
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

    private static int SelectPhase(int roll, params (int Weight, int NextPhase)[] branches)
    {
        var owner = new Doodad();
        return SelectPhase(owner, roll, branches);
    }

    private static int SelectPhase(Doodad owner, int roll, params (int Weight, int NextPhase)[] branches)
    {
        owner.BeginPhaseRatioSelection(roll);

        foreach (var (weight, nextPhase) in branches)
        {
            var func = new DoodadFuncRatioChange { Ratio = weight, NextPhase = nextPhase };
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

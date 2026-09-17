using AAEmu.Game.Models.Game.Housing;

namespace AAEmu.UnitTests.Game.Models.Game.Housing;

/// <summary>
/// The rebuild decision: whether a house's pack offers the target at all, and whether the character can pay
/// for it. The order matters — a player who does not own the house is told that, not that they are short of
/// a material.
/// </summary>
public class HousingRebuildRulesTests
{
    private static HousingRebuildTarget Target(uint id, int laborPower = 0, params HousingRebuildMaterial[] materials)
    {
        return new HousingRebuildTarget
        {
            Id = id,
            Name = $"target {id}",
            SkillId = 28828 + id,
            HousingId = 400 + id,
            LaborPower = laborPower,
            Materials = [.. materials]
        };
    }

    private static HousingRebuildPack Pack(uint id, params uint[] targetIds)
    {
        return new HousingRebuildPack { Id = id, Name = $"pack {id}", TargetIds = [.. targetIds] };
    }

    private static Dictionary<uint, int> Bag(params (uint ItemId, int Count)[] items) =>
        items.ToDictionary(item => item.ItemId, item => item.Count);

    [Test]
    public async Task IsOfferedByPack_ReadsThePacksTargetList()
    {
        var pack = Pack(2, 3, 4);

        await Assert.That(HousingRebuildRules.IsOfferedByPack(pack, 3)).IsTrue();
        await Assert.That(HousingRebuildRules.IsOfferedByPack(pack, 5)).IsFalse();
        await Assert.That(HousingRebuildRules.IsOfferedByPack(null, 3)).IsFalse();
    }

    [Test]
    public async Task Check_AllowsAnOwnersTargetThePackOffersAndTheBagPays()
    {
        var pack = Pack(2, 3);
        var target = Target(3, laborPower: 10, new HousingRebuildMaterial(34983, 15));

        var refusal = HousingRebuildRules.Check(true, pack, target, Bag((34983, 15)), laborPower: 10);

        await Assert.That(refusal).IsEqualTo(HousingRebuildRefusal.None);
    }

    [Test]
    public async Task Check_TurnsDownACharacterWhoDoesNotOwnTheHouseFirst()
    {
        // Not the owner *and* short of everything: the owner answer is the one that matters.
        var pack = Pack(2, 9);
        var target = Target(3, laborPower: 500, new HousingRebuildMaterial(34983, 15));

        var refusal = HousingRebuildRules.Check(false, pack, target, Bag(), laborPower: 0);

        await Assert.That(refusal).IsEqualTo(HousingRebuildRefusal.NotOwner);
    }

    [Test]
    public async Task Check_TurnsDownATargetTheHousesPackDoesNotOffer()
    {
        var pack = Pack(2, 3);
        var target = Target(4);

        var refusal = HousingRebuildRules.Check(true, pack, target, Bag(), laborPower: 1000);

        await Assert.That(refusal).IsEqualTo(HousingRebuildRefusal.TargetNotInPack);
    }

    [Test]
    public async Task Check_TurnsDownATargetNoSkillNamed()
    {
        var refusal = HousingRebuildRules.Check(true, Pack(2, 3), null, Bag(), laborPower: 1000);

        await Assert.That(refusal).IsEqualTo(HousingRebuildRefusal.TargetNotInPack);
    }

    [Test]
    public async Task Check_TurnsDownAMissingMaterialEvenWhenTheRestIsHeld()
    {
        var pack = Pack(2, 3);
        var target = Target(3, laborPower: 0, new HousingRebuildMaterial(8318, 100), new HousingRebuildMaterial(23633, 10));

        var refusal = HousingRebuildRules.Check(true, pack, target, Bag((8318, 100), (23633, 9)), laborPower: 1000);

        await Assert.That(refusal).IsEqualTo(HousingRebuildRefusal.MissingMaterials);
    }

    [Test]
    public async Task Check_CountsAnItemTheBagDoesNotHoldAtAllAsZero()
    {
        var pack = Pack(2, 3);
        var target = Target(3, laborPower: 0, new HousingRebuildMaterial(23633, 50));

        var refusal = HousingRebuildRules.Check(true, pack, target, Bag(), laborPower: 1000);

        await Assert.That(refusal).IsEqualTo(HousingRebuildRefusal.MissingMaterials);
        await Assert.That(HousingRebuildRules.Check(true, pack, target, null, laborPower: 1000))
            .IsEqualTo(HousingRebuildRefusal.MissingMaterials);
    }

    [Test]
    public async Task Check_TurnsDownLaborBelowTheTargetsCost()
    {
        var pack = Pack(2, 3);
        var target = Target(3, laborPower: 25, new HousingRebuildMaterial(34983, 15));

        var refusal = HousingRebuildRules.Check(true, pack, target, Bag((34983, 15)), laborPower: 24);

        await Assert.That(refusal).IsEqualTo(HousingRebuildRefusal.NotEnoughLabor);
        await Assert.That(HousingRebuildRules.Check(true, pack, target, Bag((34983, 15)), laborPower: 25))
            .IsEqualTo(HousingRebuildRefusal.None);
    }

    [Test]
    public async Task Check_AllowsATargetThatCostsNothing()
    {
        var pack = Pack(1, 1);
        var target = Target(1);

        await Assert.That(HousingRebuildRules.Check(true, pack, target, Bag(), laborPower: 0))
            .IsEqualTo(HousingRebuildRefusal.None);
    }
}

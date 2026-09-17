using AAEmu.Game.Models.Game.Mate;

// Named apart from the model's own Mate namespace: a test namespace by that name would shadow the Mate
// type for every other test under this root.
namespace AAEmu.UnitTests.Game.Models.Game.MatePacks;

public class MateEquipSlotPackTests
{
    private static MateEquipSlotPack Pack(bool head, bool chest, bool waist, bool feet)
    {
        return new MateEquipSlotPack { Id = 1, MateTypeId = 1, Head = head, Chest = chest, Waist = waist, Feet = feet };
    }

    [Test]
    public async Task AllowsSlot_ReadsTheFourPositionsTheTableNames()
    {
        // a riding mate that may wear everything but a helmet (mate_equip_slot_packs row 1)
        var mount = Pack(head: false, chest: true, waist: true, feet: true);

        await Assert.That(mount.AllowsSlot(MateEquipSlot.Head)).IsFalse();
        await Assert.That(mount.AllowsSlot(MateEquipSlot.Chest)).IsTrue();
        await Assert.That(mount.AllowsSlot(MateEquipSlot.Waist)).IsTrue();
        await Assert.That(mount.AllowsSlot(MateEquipSlot.Feet)).IsTrue();
    }

    [Test]
    public async Task AllowsSlot_TurnsDownEveryPositionForAMateThatWearsNothing()
    {
        // row 4, "equipment not wearable": all four clear
        var bare = Pack(head: false, chest: false, waist: false, feet: false);

        foreach (var slot in Enum.GetValues<MateEquipSlot>())
            await Assert.That(bare.AllowsSlot(slot)).IsFalse();
    }
}

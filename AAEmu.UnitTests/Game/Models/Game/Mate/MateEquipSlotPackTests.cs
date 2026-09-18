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
        // shipped mate_equip_slot_packs row 1 (ride): head t, chest f, waist t, feet t
        var mount = Pack(head: true, chest: false, waist: true, feet: true);

        await Assert.That(mount.AllowsSlot(MateEquipSlot.Head)).IsTrue();
        await Assert.That(mount.AllowsSlot(MateEquipSlot.Chest)).IsFalse();
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

    [Test]
    [Arguments(0, MateEquipSlot.Head)]
    [Arguments(2, MateEquipSlot.Chest)]
    [Arguments(3, MateEquipSlot.Waist)]
    [Arguments(6, MateEquipSlot.Feet)]
    public async Task ForEquipmentSlot_ReadsTheEquipmentItemSlotTheContainerUses(int slot, MateEquipSlot expected)
    {
        await Assert.That(MateEquipSlots.ForEquipmentSlot(slot)).IsEqualTo(expected);
    }

    [Test]
    [Arguments(1)]
    [Arguments(4)]
    [Arguments(5)]
    [Arguments(7)]
    public async Task ForEquipmentSlot_IsNullForAPositionAMateDoesNotHave(int slot)
    {
        await Assert.That(MateEquipSlots.ForEquipmentSlot(slot)).IsNull();
    }
}

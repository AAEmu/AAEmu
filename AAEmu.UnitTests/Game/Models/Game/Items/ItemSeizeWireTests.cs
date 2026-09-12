using AAEmu.Commons.Network;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;

namespace AAEmu.UnitTests.Game.Models.Game.Items;

public class ItemSeizeWireTests
{
    [Test]
    public async Task ItemRemoveSlot_WritesTheExactNativeActionBody()
    {
        const ulong itemId = 0x0102030405060708;
        var stream = new ItemRemoveSlot(itemId, SlotType.Inventory, 9, 7).Write(new PacketStream());

        stream.Rollback();
        await Assert.That(stream.ReadByte()).IsEqualTo((byte)ItemAction.Seize);
        await Assert.That(stream.ReadByte()).IsEqualTo((byte)0);
        await Assert.That(stream.ReadByte()).IsEqualTo((byte)7);
        await Assert.That(stream.ReadByte()).IsEqualTo((byte)SlotType.Inventory);
        await Assert.That(stream.ReadByte()).IsEqualTo((byte)9);
        await Assert.That(stream.ReadUInt64()).IsEqualTo(itemId);
        await Assert.That(stream.LeftBytes).IsEqualTo(0);
    }

    [Test]
    public async Task ItemTaskSuccess_ConcatenatesSeizeActionsWithoutShiftingTheTrailer()
    {
        const ulong firstItemId = 0x0102030405060708;
        const ulong secondItemId = 0x1112131415161718;
        const ulong forceRemoveId = 0x2122232425262728;
        var tasks = new List<ItemTask>
        {
            new ItemRemoveSlot(firstItemId, SlotType.Inventory, 4),
            new ItemRemove(secondItemId, SlotType.Bank, 5, 0)
        };
        var stream = new SCItemTaskSuccessPacket(ItemTaskType.Destroy, tasks, [forceRemoveId])
            .Write(new PacketStream());

        stream.Rollback();
        await Assert.That(stream.ReadByte()).IsEqualTo((byte)0); // unitOwnerType
        await Assert.That(stream.ReadByte()).IsEqualTo((byte)ItemTaskType.Destroy);
        await Assert.That(stream.ReadByte()).IsEqualTo((byte)2);
        await AssertSeize(stream, 0, SlotType.Inventory, 4, firstItemId);
        await AssertSeize(stream, 0, SlotType.Bank, 5, secondItemId);
        await Assert.That(stream.ReadByte()).IsEqualTo((byte)1);
        await Assert.That(stream.ReadUInt64()).IsEqualTo(forceRemoveId);
        await Assert.That(stream.ReadInt64()).IsEqualTo(0L);
        await Assert.That(stream.ReadInt32()).IsEqualTo(0);
        await Assert.That(stream.ReadBoolean()).IsFalse();
        await Assert.That(stream.ReadUInt64()).IsEqualTo(0UL);
        await Assert.That(stream.LeftBytes).IsEqualTo(0);
    }

    private static async Task AssertSeize(PacketStream stream, byte actionOwnerType, SlotType slotType, byte slot,
        ulong itemId)
    {
        await Assert.That(stream.ReadByte()).IsEqualTo((byte)ItemAction.Seize);
        await Assert.That(stream.ReadByte()).IsEqualTo((byte)0);
        await Assert.That(stream.ReadByte()).IsEqualTo(actionOwnerType);
        await Assert.That(stream.ReadByte()).IsEqualTo((byte)slotType);
        await Assert.That(stream.ReadByte()).IsEqualTo(slot);
        await Assert.That(stream.ReadUInt64()).IsEqualTo(itemId);
    }
}

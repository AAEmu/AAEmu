using AAEmu.Commons.Network;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;

namespace AAEmu.UnitTests.Game.Models.Game.Items;

public class ItemMoveWireTests
{
    [Test]
    public async Task ItemMove_EquipmentToInventory_WritesSwapSlotBody()
    {
        const ulong itemId = 0x0102030405060708;
        var stream = new ItemMove(SlotType.Equipment, 0, itemId, SlotType.Inventory, 12, 0)
            .Write(new PacketStream());

        stream.Rollback();
        await Assert.That(stream.ReadByte()).IsEqualTo((byte)ItemAction.SwapSlot);
        await Assert.That(stream.ReadByte()).IsEqualTo((byte)0); // tLogt
        await Assert.That(stream.ReadByte()).IsEqualTo((byte)SlotType.Equipment);
        await Assert.That(stream.ReadByte()).IsEqualTo((byte)0);
        await Assert.That(stream.ReadByte()).IsEqualTo((byte)SlotType.Inventory);
        await Assert.That(stream.ReadByte()).IsEqualTo((byte)12);
        await Assert.That(stream.ReadUInt64()).IsEqualTo(itemId);
        await Assert.That(stream.ReadUInt64()).IsEqualTo(0UL);
        await Assert.That(stream.ReadInt32()).IsEqualTo(0);
        await Assert.That(stream.LeftBytes).IsEqualTo(0);
    }
}

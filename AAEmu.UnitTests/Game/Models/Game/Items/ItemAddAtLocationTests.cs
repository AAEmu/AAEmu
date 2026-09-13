using AAEmu.Commons.Network;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;

namespace AAEmu.UnitTests.Game.Models.Game.Items;

public class ItemAddAtLocationTests
{
    [Test]
    public async Task Write_UsesProtocolLocationWithoutMutatingDurableItemLocation()
    {
        var item = new Item(1)
        {
            Id = 500,
            TemplateId = 15566,
            SlotType = SlotType.System,
            Slot = 19,
            Count = 1
        };

        var stream = new ItemAddAtLocation(item, SlotType.Inventory, 0).Write(new PacketStream());

        stream.Rollback();
        await Assert.That(stream.ReadByte()).IsEqualTo((byte)ItemAction.Take);
        await Assert.That(stream.ReadByte()).IsEqualTo((byte)0);
        await Assert.That(stream.ReadByte()).IsEqualTo((byte)SlotType.Inventory);
        await Assert.That(stream.ReadByte()).IsEqualTo((byte)0);
        await Assert.That(stream.ReadUInt32()).IsEqualTo((uint)15566);
        await Assert.That(item.SlotType).IsEqualTo(SlotType.System);
        await Assert.That(item.Slot).IsEqualTo(19);
    }
}

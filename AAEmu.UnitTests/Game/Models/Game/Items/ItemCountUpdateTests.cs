using AAEmu.Commons.Network;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;

namespace AAEmu.UnitTests.Game.Models.Game.Items;

public class ItemCountUpdateTests
{
    [Test]
    [Arguments(-1)]
    [Arguments(256)]
    public async Task Constructor_RejectsSlotsThatCannotFitOnTheWire(int slot)
    {
        var item = new Item(1) { SlotType = SlotType.Inventory, Slot = slot };

        await Assert.That(() => new ItemCountUpdate(item, 1)).Throws<OverflowException>();
    }

    [Test]
    public async Task Write_UsesTheSlotAndIdentityCapturedAtCreation()
    {
        const ulong originalItemId = 0x0102030405060708;
        const uint originalTemplateId = 26744;
        var item = new Item(1)
        {
            Id = originalItemId,
            TemplateId = originalTemplateId,
            SlotType = SlotType.Inventory,
            Slot = 4
        };
        var update = new ItemCountUpdate(item, -2);

        item.Id = 0x8877665544332211;
        item.TemplateId = 32935;
        item.SlotType = SlotType.Bank;
        item.Slot = 19;

        var body = update.Write(new PacketStream()).GetBytes();
        var expected = new PacketStream();
        expected.Write((byte)ItemAction.Create);
        expected.Write((byte)0);
        expected.Write((byte)SlotType.Inventory);
        expected.Write((byte)4);
        expected.Write(originalItemId);
        expected.Write(-2);
        expected.Write(originalTemplateId);

        await Assert.That(body).IsEquivalentTo(expected.GetBytes());
    }
}

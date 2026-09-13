using AAEmu.Commons.Network;
using AAEmu.Game.Models.Game.Items;

namespace AAEmu.UnitTests.Game.Models.Game.Items;

public class EquipmentSerializerTests
{
    [Test]
    public async Task WriteButler_UsesTheModeSevenBodyImageAndFullItemBranches()
    {
        var equipment = new Dictionary<int, Item>
        {
            [0] = Item(1000, 10),
            [19] = Item(1019, 19),
            [25] = Item(1025, 25),
            [33] = Item(1033, 33)
        };
        var stream = new PacketStream();

        EquipmentSerializer.WriteButler(stream, equipment);

        stream.Rollback();
        await Assert.That(stream.ReadUInt64()).IsEqualTo((1UL << 0) | (1UL << 19) | (1UL << 25) | (1UL << 33));
        await AssertItem(stream, 1000, 10);
        await Assert.That(stream.ReadUInt32()).IsEqualTo(1019u);
        await Assert.That(stream.ReadUInt32()).IsEqualTo(1025u);
        await AssertItem(stream, 1033, 33);
        await Assert.That(stream.LeftBytes).IsEqualTo(0);
    }

    [Test]
    public async Task WriteButler_RejectsEntriesThatCannotMapToTheClientSlots()
    {
        var outOfRange = new Dictionary<int, Item> { [EquipmentSerializer.SlotCount] = Item(1000, 1) };
        var zeroTemplate = new Dictionary<int, Item> { [0] = new Item(1) };

        await Assert.That(() => EquipmentSerializer.WriteButler(new PacketStream(), outOfRange))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => EquipmentSerializer.WriteButler(new PacketStream(), zeroTemplate))
            .Throws<ArgumentException>();
    }

    private static Item Item(uint templateId, ulong id) => new(1)
    {
        TemplateId = templateId,
        Id = id,
        Count = 1
    };

    private static async Task AssertItem(PacketStream stream, uint templateId, ulong id)
    {
        var item = new Item(1);
        item.Read(stream);

        await Assert.That(item.TemplateId).IsEqualTo(templateId);
        await Assert.That(item.Id).IsEqualTo(id);
    }
}

using System.Buffers.Binary;

using AAEmu.Commons.Network;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Items.Templates;

namespace AAEmu.UnitTests.Game.Models.Game.Items.Actions;

public class ItemStackDeltaTests
{
    [Test]
    public async Task ExistingStackIncrease_WritesAcquiredDeltaInsteadOfResultingTotal()
    {
        var template = new ItemTemplate { Id = 19000, MaxCount = 1000 };
        var item = new Item(0x0102030405060708, template, 734)
        {
            SlotType = SlotType.Inventory,
            Slot = 9
        };

        var stream = new PacketStream();
        new ItemCountIncrease(item, 300).Write(stream);
        var body = stream.GetBytes();

        await Assert.That(body.Length).IsEqualTo(20);
        await Assert.That(body[0]).IsEqualTo((byte)ItemAction.Create);
        await Assert.That(body[2]).IsEqualTo((byte)SlotType.Inventory);
        await Assert.That(body[3]).IsEqualTo((byte)9);
        await Assert.That(BinaryPrimitives.ReadUInt64LittleEndian(body.AsSpan(4, sizeof(ulong))))
            .IsEqualTo(0x0102030405060708UL);
        await Assert.That(ReadInt32(body, 12)).IsEqualTo(300);
        await Assert.That(ReadUInt32(body, 16)).IsEqualTo(19000u);
    }

    [Test]
    public async Task ExistingStackDecrease_WritesNegativeRemovedDeltaInsteadOfResultingTotal()
    {
        var template = new ItemTemplate { Id = 19001, MaxCount = 1000 };
        var item = new Item(0x1112131415161718, template, 750)
        {
            SlotType = SlotType.Inventory,
            Slot = 10
        };

        var stream = new PacketStream();
        new ItemCountDecrease(item, 40).Write(stream);
        var body = stream.GetBytes();

        await Assert.That(body.Length).IsEqualTo(20);
        await Assert.That(body[0]).IsEqualTo((byte)ItemAction.Create);
        await Assert.That(body[2]).IsEqualTo((byte)SlotType.Inventory);
        await Assert.That(body[3]).IsEqualTo((byte)10);
        await Assert.That(BinaryPrimitives.ReadUInt64LittleEndian(body.AsSpan(4, sizeof(ulong))))
            .IsEqualTo(0x1112131415161718UL);
        await Assert.That(ReadInt32(body, 12)).IsEqualTo(-40);
        await Assert.That(ReadUInt32(body, 16)).IsEqualTo(19001u);
    }

    private static uint ReadUInt32(byte[] body, int offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(offset, sizeof(uint)));
    private static int ReadInt32(byte[] body, int offset) =>
        BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(offset, sizeof(int)));
}

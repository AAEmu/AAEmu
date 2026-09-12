using AAEmu.Commons.Network;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;

namespace AAEmu.UnitTests.Game.Models.Game.Items;

public class ButlerItemAddTests
{
    [Test]
    public async Task Write_UsesNativeAction21BodyWithCanonicalItemAndZeroOptionalIds()
    {
        var item = new Item(1)
        {
            Id = 0x0102030405060708,
            TemplateId = 23491,
            SlotType = SlotType.System,
            Slot = 19,
            Count = 1
        };

        var body = new ButlerItemAdd(item, 2, 54, 2, 0)
            .Write(new PacketStream())
            .GetBytes();
        var expected = new PacketStream();
        expected.Write((byte)0x15);
        expected.Write((byte)0);
        expected.Write((byte)7);
        expected.Write((byte)2);
        expected.Write((byte)54);
        expected.Write((byte)2);
        expected.Write((byte)0);
        item.Write(expected);
        expected.Write((ulong)0);
        expected.Write((ulong)0);

        await Assert.That(Convert.ToHexString(body)).IsEqualTo(Convert.ToHexString(expected.GetBytes()));
        await Assert.That(item.SlotType).IsEqualTo(SlotType.System);
        await Assert.That(item.Slot).IsEqualTo(19);
    }
}

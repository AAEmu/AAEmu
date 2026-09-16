using AAEmu.Commons.Network;
using AAEmu.Game.Core.Packets.C2G;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;

namespace AAEmu.UnitTests.Game.Models.Game.Items;

public class ItemSecurityWireTests
{
    private const ulong ItemId = 0x0102030405060708;

    [Test]
    public async Task ItemSecure_ParsesTypeIndexAndItemId()
    {
        var packet = new CSItemSecurePacket();
        packet.Read(BuildRequest());

        await Assert.That(packet.SlotType).IsEqualTo(SlotType.Inventory);
        await Assert.That(packet.Slot).IsEqualTo((byte)9);
        await Assert.That(packet.ItemId).IsEqualTo(ItemId);
    }

    [Test]
    public async Task ItemUnsecure_ParsesTheSameThreeFields()
    {
        var packet = new CSItemUnsecurePacket();
        packet.Read(BuildRequest());

        await Assert.That(packet.SlotType).IsEqualTo(SlotType.Inventory);
        await Assert.That(packet.Slot).IsEqualTo((byte)9);
        await Assert.That(packet.ItemId).IsEqualTo(ItemId);
    }

    [Test]
    public async Task ItemSecure_ConsumesExactlyTheBodyTheClientSends()
    {
        // 0x07B carries two bytes and the id; the old parser here ate four bytes plus the id and so
        // read a body that is two bytes longer than any the client can produce.
        var stream = BuildRequest();
        var packet = new CSItemSecurePacket();
        packet.Read(stream);

        await Assert.That(stream.LeftBytes).IsEqualTo(0);
    }

    [Test]
    public async Task ItemUpdateSecurity_WritesTheLockUnderTheItemLockTask()
    {
        var item = new Item
        {
            Id = ItemId,
            SlotType = SlotType.Inventory,
            Slot = 9,
            ItemFlags = ItemFlag.Secure
        };
        var task = new ItemUpdateSecurity(item, (byte)item.ItemFlags, false, false, false);
        var stream = new SCItemTaskSuccessPacket(ItemTaskType.ItemLock, task, []).Write(new PacketStream());

        stream.Rollback();
        await Assert.That(stream.ReadByte()).IsEqualTo((byte)0); // unitOwnerType
        await Assert.That(stream.ReadByte()).IsEqualTo((byte)ItemTaskType.ItemLock);
        await Assert.That(ItemTaskType.ItemLock).IsEqualTo((ItemTaskType)92);
        await Assert.That(stream.ReadByte()).IsEqualTo((byte)1); // one task
        await Assert.That(stream.ReadByte()).IsEqualTo((byte)ItemAction.UpdateFlags);
        await Assert.That(stream.ReadByte()).IsEqualTo((byte)0); // tLogt
        await Assert.That(stream.ReadByte()).IsEqualTo((byte)0); // actionOwnerType
        await Assert.That(stream.ReadByte()).IsEqualTo((byte)SlotType.Inventory);
        await Assert.That(stream.ReadByte()).IsEqualTo((byte)9);
        await Assert.That(stream.ReadUInt64()).IsEqualTo(ItemId);
        await Assert.That(stream.ReadByte()).IsEqualTo((byte)ItemFlag.Secure); // bits
        await Assert.That(stream.ReadByte()).IsEqualTo((byte)0); // prevBits
        await Assert.That(stream.ReadBoolean()).IsFalse(); // isUnsecureExcess
        await Assert.That(stream.ReadBoolean()).IsFalse(); // isUnsecureSet
        await Assert.That(stream.ReadBoolean()).IsFalse(); // isUnpack
        // "No unlock pending" is DateTime.MinValue on the item, but that value cannot survive the
        // wire: ReadDateTime runs the raw seconds through UnixTime, which floors at the epoch. The
        // client only asks whether the stamp lies in the future, and the epoch does not.
        await Assert.That(stream.ReadDateTime()).IsLessThan(new DateTime(2000, 1, 1)); // unsecureDateTime
        await Assert.That(stream.ReadDateTime()).IsLessThan(new DateTime(2000, 1, 1)); // unpackDateTime
        await Assert.That(stream.ReadByte()).IsEqualTo((byte)0); // forceRemove count
        await Assert.That(stream.ReadInt64()).IsEqualTo(0L);
        await Assert.That(stream.ReadInt32()).IsEqualTo(0);
        await Assert.That(stream.ReadBoolean()).IsFalse();
        await Assert.That(stream.ReadUInt64()).IsEqualTo(0UL);
        await Assert.That(stream.LeftBytes).IsEqualTo(0);
    }

    [Test]
    public async Task ItemUpdateSecurity_MarksTheUnlockDelayAndKeepsTheSecuredBit()
    {
        var unsecureTime = new DateTime(2026, 9, 19, 12, 0, 0, DateTimeKind.Utc);
        var item = new Item
        {
            Id = ItemId,
            SlotType = SlotType.Equipment,
            Slot = 3,
            ItemFlags = ItemFlag.Secure,
            UnsecureTime = unsecureTime
        };

        // The manager's own call shape: bits carry the still-set Secure flag, isUnsecureSet is what
        // tells the client the timestamp below is a running countdown.
        var task = new ItemUpdateSecurity(item, (byte)item.ItemFlags, false,
            item.UnsecureTime != DateTime.MinValue, item.HasFlag(ItemFlag.Unpacked), (byte)ItemFlag.Secure);
        var stream = new SCItemTaskSuccessPacket(ItemTaskType.ItemLock, task, []).Write(new PacketStream());

        stream.Rollback();
        stream.ReadByte(); // unitOwnerType
        stream.ReadByte(); // task
        stream.ReadByte(); // count
        stream.ReadByte(); // action
        stream.ReadByte(); // tLogt
        stream.ReadByte(); // actionOwnerType
        await Assert.That(stream.ReadByte()).IsEqualTo((byte)SlotType.Equipment);
        await Assert.That(stream.ReadByte()).IsEqualTo((byte)3);
        await Assert.That(stream.ReadUInt64()).IsEqualTo(ItemId);
        await Assert.That(stream.ReadByte()).IsEqualTo((byte)ItemFlag.Secure);
        await Assert.That(stream.ReadByte()).IsEqualTo((byte)ItemFlag.Secure); // prevBits
        await Assert.That(stream.ReadBoolean()).IsFalse(); // isUnsecureExcess
        await Assert.That(stream.ReadBoolean()).IsTrue(); // isUnsecureSet
        await Assert.That(stream.ReadBoolean()).IsFalse(); // isUnpack
        await Assert.That(stream.ReadDateTime()).IsEqualTo(unsecureTime);
    }

    private static PacketStream BuildRequest()
    {
        var stream = new PacketStream()
            .Write((byte)SlotType.Inventory)
            .Write((byte)9)
            .Write(ItemId);
        stream.Rollback();
        return stream;
    }
}

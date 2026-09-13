using AAEmu.Commons.Network;
using AAEmu.Game.Core.Packets.C2G;

namespace AAEmu.UnitTests.Game.Core.Packets.C2G;

public class CSButlerJobPacketTests
{
    [Test]
    public async Task HarvestJob_ParsesExactNativeBody()
    {
        var body = new PacketStream()
            .Write((sbyte)-7)
            .Write(0x1122334455667788L)
            .Write(0x12345678)
            .Write((short)0x2345);
        var stream = new PacketStream(body.GetBytes());
        var packet = new CSRequestButlerHarvestJobPacket();

        packet.Read(stream);

        await Assert.That(body.Count).IsEqualTo(15);
        await Assert.That(packet.JobKind).IsEqualTo((sbyte)-7);
        await Assert.That(packet.DbHarvestId).IsEqualTo(0x1122334455667788L);
        await Assert.That(packet.HarvestId).IsEqualTo(0x12345678);
        await Assert.That(packet.Amount).IsEqualTo((short)0x2345);
        await Assert.That(stream.LeftBytes).IsEqualTo(0);
    }

    [Test]
    public async Task HarvestJob_ClassifiesNativeRegisterAndCancelShapes()
    {
        var register = CSRequestButlerHarvestJobPacket.Classify(1, 0, 123, 4);
        var cancelWithClientGlobalTail = CSRequestButlerHarvestJobPacket.Classify(3, 456, -789, -2);
        var invalidRegister = CSRequestButlerHarvestJobPacket.Classify(1, 456, 123, 4);
        var invalidCancel = CSRequestButlerHarvestJobPacket.Classify(3, 0, 0, 0);

        await Assert.That(register).IsEqualTo(ButlerHarvestRequestOperation.Register);
        await Assert.That(cancelWithClientGlobalTail).IsEqualTo(ButlerHarvestRequestOperation.Cancel);
        await Assert.That(invalidRegister).IsEqualTo(ButlerHarvestRequestOperation.Invalid);
        await Assert.That(invalidCancel).IsEqualTo(ButlerHarvestRequestOperation.Invalid);
    }

    [Test]
    public async Task ExpandGardenSlots_ParsesOnlyTheNativeKindByte()
    {
        var stream = new PacketStream(new byte[] { 2 });
        var packet = new CSExpandButlerUsableSlotPacket();

        packet.Read(stream);

        await Assert.That(packet.Kind).IsEqualTo((sbyte)2);
        await Assert.That(stream.LeftBytes).IsEqualTo(0);
        await Assert.That(() => new CSExpandButlerUsableSlotPacket().Read(
                new PacketStream(new byte[] { 2, 0 })))
            .Throws<InvalidDataException>();
    }

    [Test]
    public async Task SpecialtyTradeJob_ParsesExactNativeBody()
    {
        var body = new PacketStream()
            .Write((sbyte)5)
            .Write(0x1020304050607080L)
            .Write(-123456789)
            .Write((short)-12345);
        var stream = new PacketStream(body.GetBytes());
        var packet = new CSRequestButlerSpecialtyTradeJobPacket();

        packet.Read(stream);

        await Assert.That(body.Count).IsEqualTo(15);
        await Assert.That(packet.JobKind).IsEqualTo((sbyte)5);
        await Assert.That(packet.DbSpecialtyTradeId).IsEqualTo(0x1020304050607080L);
        await Assert.That(packet.SpecialtyTradeType).IsEqualTo(-123456789);
        await Assert.That(packet.ToZoneGroupType).IsEqualTo((short)-12345);
        await Assert.That(stream.LeftBytes).IsEqualTo(0);
    }

    [Test]
    public async Task ChargeWorldResource_ParsesExactNativeBody()
    {
        var body = new PacketStream()
            .Write((sbyte)-3)
            .Write(0xF0E1D2C3u);
        var stream = new PacketStream(body.GetBytes());
        var packet = new CSChargeButlerWorldResourcePacket();

        packet.Read(stream);

        await Assert.That(body.Count).IsEqualTo(5);
        await Assert.That(packet.ChargeKind).IsEqualTo((sbyte)-3);
        await Assert.That(packet.Amount).IsEqualTo(0xF0E1D2C3u);
        await Assert.That(stream.LeftBytes).IsEqualTo(0);
    }

    [Test]
    public async Task TruncatedBodies_AreRejectedBeforeReading()
    {
        var harvestBody = new PacketStream()
            .Write((sbyte)1)
            .Write(2L)
            .Write(3)
            .Write((short)4)
            .GetBytes()[..^1];
        var specialtyBody = new PacketStream()
            .Write((sbyte)1)
            .Write(2L)
            .Write(3)
            .Write((short)4)
            .GetBytes()[..^1];
        var chargeBody = new PacketStream()
            .Write((sbyte)1)
            .Write(2u)
            .GetBytes()[..^1];
        var expandBody = Array.Empty<byte>();

        await Assert.That(() => new CSRequestButlerHarvestJobPacket().Read(new PacketStream(harvestBody)))
            .Throws<InvalidDataException>();
        await Assert.That(() => new CSRequestButlerSpecialtyTradeJobPacket().Read(new PacketStream(specialtyBody)))
            .Throws<InvalidDataException>();
        await Assert.That(() => new CSChargeButlerWorldResourcePacket().Read(new PacketStream(chargeBody)))
            .Throws<InvalidDataException>();
        await Assert.That(() => new CSExpandButlerUsableSlotPacket().Read(new PacketStream(expandBody)))
            .Throws<InvalidDataException>();
    }
}

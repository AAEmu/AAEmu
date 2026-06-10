using System;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Packets.G2C;

namespace AAEmu.UnitTests.Game.Core.Packets.G2C;

public class SCUnitImpulsePacketTests
{
    [Test]
    public async Task Write_WritesObjIdAndCraftedObjIdFloat()
    {
        const uint objId = 0x123456;
        var packet = new SCUnitImpulsePacket(
            objId,
            1f,
            2f,
            3f,
            4f,
            5f,
            6f,
            7f,
            8f,
            9f,
            10f,
            11f,
            12f);

        var stream = packet.Write(new PacketStream());
        stream.Rollback();

        await Assert.That(stream.ReadBc()).IsEqualTo(objId);
        await Assert.That(BitConverter.SingleToInt32Bits(stream.ReadSingle())).IsEqualTo(unchecked((int)(objId << 8)));
        await Assert.That(stream.ReadSingle()).IsEqualTo(1f);
        await Assert.That(stream.ReadSingle()).IsEqualTo(2f);
        await Assert.That(stream.ReadSingle()).IsEqualTo(3f);
    }

    [Test]
    public async Task EncodeUnitObjIdFloat_PacksObjIdIntoHighThreeBytes()
    {
        const uint objId = 0x123456;

        var bytes = BitConverter.GetBytes(SCUnitImpulsePacket.EncodeUnitObjIdFloat(objId));

        await Assert.That(bytes).IsEquivalentTo(new byte[] { 0x00, 0x56, 0x34, 0x12 });
    }
}

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Packets.G2C;

namespace AAEmu.UnitTests.Game.Core.Packets.G2C;

public class SCLoadInstancePacketTests
{
    [Test]
    public async Task FirstField_IsTheLiveInstanceId()
    {
        const uint instanceId = 0;
        const uint zoneId = 1;
        const float x = 10f;
        const float y = 20f;
        const float z = 30f;

        var body = new SCLoadInstancePacket(instanceId, zoneId, x, y, z, 0f, 0f, 0f)
            .Write(new PacketStream())
            .GetBytes();

        await Assert.That(BitConverter.ToUInt32(body, 0)).IsEqualTo(instanceId);
        await Assert.That(BitConverter.ToUInt32(body, 4)).IsEqualTo(zoneId);
        await Assert.That(BitConverter.ToSingle(body, 8)).IsEqualTo(x);
    }

    [Test]
    public async Task FirstField_IsNotAWorldTemplateId()
    {
        const uint liveInstanceId = 0;
        const uint worldTemplateId = 1;

        var body = new SCLoadInstancePacket(liveInstanceId, 1, 0f, 0f, 0f, 0f, 0f, 0f)
            .Write(new PacketStream())
            .GetBytes();

        await Assert.That(BitConverter.ToUInt32(body, 0)).IsEqualTo(liveInstanceId);
        await Assert.That(BitConverter.ToUInt32(body, 0)).IsNotEqualTo(worldTemplateId);
    }
}

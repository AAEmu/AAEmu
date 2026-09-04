using AAEmu.Commons.Network;
using AAEmu.Game.Core.Packets.G2C;

namespace AAEmu.UnitTests.Game.Core.Packets.G2C;

public class SCExpertLimitModifiedPacketTests
{
    [Test]
    public async Task Body_IsUpgradeThenPiscIdPointAndStep()
    {
        var body = new SCExpertLimitModifiedPacket(true, 7, 10000, 1)
            .Write(new PacketStream())
            .GetBytes();

        var expected = new PacketStream();
        expected.Write(true);
        expected.WritePisc(7u, 10000u);
        expected.Write((byte)1);

        await Assert.That(body).IsEquivalentTo(expected.GetBytes());
    }

    [Test]
    public async Task Body_IsNotAFixedWidthIdAndStep()
    {
        var body = new SCExpertLimitModifiedPacket(true, 7, 10000, 1)
            .Write(new PacketStream())
            .GetBytes();

        // bool + u32 id + step. Same length as the live entry for these values, wrong bytes.
        byte[] fixedWidth = [1, 7, 0, 0, 0, 1];

        await Assert.That(body).IsNotEquivalentTo(fixedWidth);
        await Assert.That(body[0]).IsEqualTo((byte)1);
        await Assert.That(body[^1]).IsEqualTo((byte)1);
    }
}

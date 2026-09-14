using AAEmu.Commons.Network;
using AAEmu.Game.Core.Packets.G2C;

namespace AAEmu.UnitTests.Game.Core.Packets.G2C;

public class SCBlinkUnitPacketTests
{
    private static byte[] Body(bool move3D, float z)
    {
        var stream = new PacketStream();
        new SCBlinkUnitPacket(1500u, 15f, 0f, move3D, 10811.4f, 10527.4f, z).Write(stream);
        return stream.GetBytes();
    }

    [Test]
    public async Task Body_KeepsTheByteTheClientReads()
    {
        // Wire layout: unitId(bc), f32 distance, f32 degree, u8 move3D, then the position. Dropping
        // move3D made the packet one byte short: the client consumed the position one byte early, logged
        // "not enough buffer for z" and ignored the blink.
        var body = Body(move3D: true, z: 178.4f);

        await Assert.That(body.Length).IsEqualTo(3 + 4 + 4 + 1 + 8 + 8 + 4);
    }

    [Test]
    public async Task Move3DFlag_SitsBetweenDegreeAndPosition()
    {
        await Assert.That(Body(move3D: true, z: 1f)[11]).IsEqualTo((byte)1);
        await Assert.That(Body(move3D: false, z: 1f)[11]).IsEqualTo((byte)0);
    }

    [Test]
    public async Task Z_IsTheTrailingField()
    {
        const float z = 178.4375f;
        var body = Body(move3D: false, z: z);

        await Assert.That(BitConverter.ToSingle(body, body.Length - 4)).IsEqualTo(z);
    }
}

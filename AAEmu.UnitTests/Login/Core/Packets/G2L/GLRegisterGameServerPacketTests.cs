using AAEmu.Commons.Network;
using AAEmu.Login.Core.Packets.G2L;

namespace AAEmu.UnitTests.Login.Core.Packets.G2L;

public class GLRegisterGameServerPacketTests
{
    private static PacketStream Body(string secret, byte gsId, params byte[] mirrors)
    {
        var stream = new PacketStream();
        stream.Write(secret);
        stream.Write(gsId);
        stream.Write(mirrors.Length);
        foreach (var id in mirrors)
            stream.Write(id);
        stream.Pos = 0;
        return stream;
    }

    [Test]
    public async Task Read_ValidMirrors_PopulatesFields()
    {
        var packet = new GLRegisterGameServerPacket();
        packet.Read(Body("secret", 1, 2, 3));

        await Assert.That(packet.SecretKey).IsEqualTo("secret");
        await Assert.That(packet.GsId.Value).IsEqualTo((byte)1);
        await Assert.That(packet.Mirrors).IsNotNull();
        await Assert.That(packet.Mirrors!.Count).IsEqualTo(2);
        await Assert.That(packet.Mirrors[0].Value).IsEqualTo((byte)2);
        await Assert.That(packet.Mirrors[1].Value).IsEqualTo((byte)3);
    }

    [Test]
    public async Task Read_ZeroMirrors_Succeeds()
    {
        var packet = new GLRegisterGameServerPacket();
        packet.Read(Body("secret", 1));

        await Assert.That(packet.Mirrors).IsNotNull();
        await Assert.That(packet.Mirrors!.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Read_TruncatedCount_Throws()
    {
        var stream = new PacketStream();
        stream.Write("secret");
        stream.Write((byte)1);
        stream.Pos = 0;

        await Assert.That(() => new GLRegisterGameServerPacket().Read(stream))
            .Throws<InvalidDataException>();
    }

    [Test]
    public async Task Read_CountDoesNotMatchRemaining_Throws()
    {
        var stream = new PacketStream();
        stream.Write("secret");
        stream.Write((byte)1);
        stream.Write(5);
        stream.Write((byte)2);
        stream.Pos = 0;

        await Assert.That(() => new GLRegisterGameServerPacket().Read(stream))
            .Throws<InvalidDataException>();
    }

    [Test]
    public async Task Read_CountExceedsMax_Throws()
    {
        var extra = new byte[GLRegisterGameServerPacket.MaxMirrorCount + 1];
        var stream = Body("secret", 1, extra);

        await Assert.That(() => new GLRegisterGameServerPacket().Read(stream))
            .Throws<InvalidDataException>();
    }
}

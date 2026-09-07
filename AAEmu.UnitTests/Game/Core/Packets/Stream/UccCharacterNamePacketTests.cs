using AAEmu.Commons.Network;
using AAEmu.Game.Core.Packets.C2S;
using AAEmu.Game.Core.Packets.S2C;

namespace AAEmu.UnitTests.Game.Core.Packets.Stream;

public class UccCharacterNamePacketTests
{
    [Test]
    public async Task Response_WritesEightByteCharacterId()
    {
        var encoded = new TCUccCharNamePacket(6, "Tester").Encode().GetBytes();
        byte[] expected =
        [
            0x12, 0x00,
            0x08, 0x00,
            0x06, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x06, 0x00, 0x54, 0x65, 0x73, 0x74, 0x65, 0x72
        ];

        await Assert.That(encoded).IsEquivalentTo(expected);
    }

    [Test]
    public async Task Request_ConsumesEightByteCharacterId()
    {
        var stream = new PacketStream();
        stream.Write((ulong)uint.MaxValue + 1);
        stream.Rollback();

        new CTUccCharacterNamePacket().Read(stream);

        await Assert.That(stream.LeftBytes).IsEqualTo(0);
    }
}

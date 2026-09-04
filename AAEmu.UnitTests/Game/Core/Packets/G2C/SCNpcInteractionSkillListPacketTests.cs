using AAEmu.Commons.Network;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.StaticValues;

namespace AAEmu.UnitTests.Game.Core.Packets.G2C;

public class SCNpcInteractionSkillListPacketTests
{
    [Test]
    public async Task SpecialtyStore_WritesClientInteractionLayout()
    {
        var stream = new PacketStream();
        new SCNpcInteractionSkillListPacket(
            0x010203, 0x040506, -7, 8, 1, 0x11223344, SkillsEnum.UseSpecialtyStore).Write(stream);

        stream.Rollback();
        await Assert.That(stream.ReadBc()).IsEqualTo(0x010203u);
        await Assert.That(stream.ReadBc()).IsEqualTo(0x040506u);
        await Assert.That(stream.ReadInt32()).IsEqualTo(-7);
        await Assert.That(stream.ReadInt32()).IsEqualTo(8);
        await Assert.That(stream.ReadByte()).IsEqualTo((byte)1);
        await Assert.That(stream.ReadInt32()).IsEqualTo(1);
        await Assert.That(stream.ReadUInt32()).IsEqualTo(16376u);
        await Assert.That(stream.ReadBoolean()).IsTrue();
        await Assert.That(stream.ReadInt32()).IsEqualTo(0x11223344);
        await Assert.That(stream.Pos).IsEqualTo(stream.Count);
    }
}

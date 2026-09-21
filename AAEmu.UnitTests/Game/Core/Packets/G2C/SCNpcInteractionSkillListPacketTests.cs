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

    [Test]
    public async Task OfferedActions_WritesEveryEntryInOrder()
    {
        var stream = new PacketStream();
        new SCNpcInteractionSkillListPacket(7, 0, 1, -1, 2, 0, [11u, 12u, 13u]).Write(stream);

        stream.Rollback();
        await Assert.That(stream.ReadBc()).IsEqualTo(7u);
        await Assert.That(stream.ReadBc()).IsEqualTo(0u);
        await Assert.That(stream.ReadInt32()).IsEqualTo(1);
        await Assert.That(stream.ReadInt32()).IsEqualTo(-1);
        await Assert.That(stream.ReadByte()).IsEqualTo((byte)2);
        await Assert.That(stream.ReadInt32()).IsEqualTo(3);
        await Assert.That(stream.ReadUInt32()).IsEqualTo(11u);
        await Assert.That(stream.ReadUInt32()).IsEqualTo(12u);
        await Assert.That(stream.ReadUInt32()).IsEqualTo(13u);
        await Assert.That(stream.ReadBoolean()).IsTrue();
        await Assert.That(stream.ReadInt32()).IsEqualTo(0);
        await Assert.That(stream.Pos).IsEqualTo(stream.Count);
    }

    [Test]
    public async Task OfferedActions_StopsAtTheClientsTenEntryLimit()
    {
        var skills = Enumerable.Range(0, 12).Select(index => 50000u + (uint)index).ToArray();
        var stream = new PacketStream();
        new SCNpcInteractionSkillListPacket(1, 0, 1, -1, 2, 0, skills).Write(stream);

        stream.Rollback();
        stream.ReadBc();
        stream.ReadBc();
        stream.ReadInt32();
        stream.ReadInt32();
        stream.ReadByte();
        await Assert.That(stream.ReadInt32()).IsEqualTo(10);
        for (var index = 0; index < 10; index++)
            await Assert.That(stream.ReadUInt32()).IsEqualTo(50000u + (uint)index);
        await Assert.That(stream.ReadBoolean()).IsTrue();
        await Assert.That(stream.ReadInt32()).IsEqualTo(0);
        await Assert.That(stream.Pos).IsEqualTo(stream.Count);
    }

    [Test]
    public async Task NoOfferedActions_WritesAnEmptyList()
    {
        var stream = new PacketStream();
        new SCNpcInteractionSkillListPacket(7, 0, 1, -1, 2, 0, []).Write(stream);

        stream.Rollback();
        stream.ReadBc();
        stream.ReadBc();
        stream.ReadInt32();
        stream.ReadInt32();
        stream.ReadByte();
        await Assert.That(stream.ReadInt32()).IsEqualTo(0);
        await Assert.That(stream.ReadBoolean()).IsTrue();
        await Assert.That(stream.ReadInt32()).IsEqualTo(0);
        await Assert.That(stream.Pos).IsEqualTo(stream.Count);
    }
}

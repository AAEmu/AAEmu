using AAEmu.Commons.Network;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Expeditions.Recruitment;

namespace AAEmu.UnitTests.Game.Core.Packets.G2C;

public sealed class SCExpeditionRecruitmentsGetPacketTests
{
    [Test]
    public async Task Write_AlwaysEmitsFixedSlotCountEvenWhenEmpty()
    {
        var page = new ExpeditionRecruitmentPage(0, 1, []);
        var body = new SCExpeditionRecruitmentsGetPacket(page, new HashSet<uint>())
            .Write(new PacketStream());
        var reader = new PacketStream(body.GetBytes());

        await Assert.That(reader.ReadUInt32()).IsEqualTo(0u);
        await Assert.That(reader.ReadUInt32()).IsEqualTo(1u);
        await Assert.That(reader.ReadUInt32()).IsEqualTo(0u);
        for (var i = 0; i < SCExpeditionRecruitmentsGetPacket.SlotCount; i++)
            await ReadEmptyRow(reader);
        await Assert.That(reader.ReadByte()).IsEqualTo((byte)0);
        await Assert.That(reader.LeftBytes).IsEqualTo(0);
    }

    [Test]
    public async Task SlotCount_MatchesClientFixedArray() =>
        await Assert.That(SCExpeditionRecruitmentsGetPacket.SlotCount).IsEqualTo(15);

    private static async Task ReadEmptyRow(PacketStream reader)
    {
        await Assert.That(reader.ReadInt32()).IsEqualTo(0);
        await Assert.That(reader.ReadString()).IsEqualTo(string.Empty);
        await Assert.That(reader.ReadInt32()).IsEqualTo(0);
        await Assert.That(reader.ReadString()).IsEqualTo(string.Empty);
        await Assert.That(reader.ReadString()).IsEqualTo(string.Empty);
        await Assert.That(reader.ReadInt64()).IsEqualTo(0L);
        await Assert.That(reader.ReadInt64()).IsEqualTo(0L);
        await Assert.That(reader.ReadInt16()).IsEqualTo((short)0);
        await Assert.That(reader.ReadUInt32()).IsEqualTo(0u);
        await Assert.That(reader.ReadBoolean()).IsFalse();
    }
}

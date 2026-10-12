using AAEmu.Commons.Network;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Expeditions.Recruitment;
using AAEmu.Game.Models.StaticValues;

namespace AAEmu.UnitTests.Game.Core.Packets.G2C;

public sealed class SCExpeditionApplicantsGetPacketTests
{
    [Test]
    public async Task Write_AlwaysEmitsFixedSlotCountEvenWhenEmpty()
    {
        var body = new SCExpeditionApplicantsGetPacket([])
            .Write(new PacketStream());
        var reader = new PacketStream(body.GetBytes());

        await Assert.That(reader.ReadUInt32()).IsEqualTo(0u);
        await Assert.That(reader.ReadUInt32()).IsEqualTo(0u);
        for (var i = 0; i < SCExpeditionApplicantsGetPacket.SlotCount; i++)
            await ReadEmptyRow(reader);
        await Assert.That(reader.LeftBytes).IsEqualTo(0);
    }

    [Test]
    public async Task Write_OneApplicant_PadsRemainingSlots()
    {
        var registered = new DateTime(2026, 10, 12, 1, 55, 1, DateTimeKind.Utc);
        var application = new ExpeditionRecruitmentApplication(1002, 39, "I am good player", registered);
        var character = new ExpeditionJoinCandidate(39, 1, "Temu", 55, 0, (FactionsEnum)103, 1, 2, 3, 0, 0);
        var body = new SCExpeditionApplicantsGetPacket([(application, character)])
            .Write(new PacketStream());
        var reader = new PacketStream(body.GetBytes());

        await Assert.That(reader.ReadUInt32()).IsEqualTo(1u);
        await Assert.That(reader.ReadUInt32()).IsEqualTo(1u);
        await Assert.That(reader.ReadUInt64()).IsEqualTo(39ul);
        await Assert.That(reader.ReadString()).IsEqualTo("Temu");
        await Assert.That(reader.ReadByte()).IsEqualTo((byte)55);
        await Assert.That(reader.ReadByte()).IsEqualTo((byte)0);
        await Assert.That(reader.ReadInt32()).IsEqualTo(103);
        await Assert.That(reader.ReadString()).IsEqualTo("I am good player");
        await Assert.That(reader.ReadInt64()).IsEqualTo(HelpersUnix(registered));
        for (var i = 1; i < SCExpeditionApplicantsGetPacket.SlotCount; i++)
            await ReadEmptyRow(reader);
        await Assert.That(reader.LeftBytes).IsEqualTo(0);
    }

    [Test]
    public async Task SlotCount_MatchesClientFixedArray() =>
        await Assert.That(SCExpeditionApplicantsGetPacket.SlotCount).IsEqualTo(50);

    private static long HelpersUnix(DateTime utc) =>
        AAEmu.Commons.Utils.Helpers.UnixTime(utc);

    private static async Task ReadEmptyRow(PacketStream reader)
    {
        await Assert.That(reader.ReadUInt64()).IsEqualTo(0ul);
        await Assert.That(reader.ReadString()).IsEqualTo(string.Empty);
        await Assert.That(reader.ReadByte()).IsEqualTo((byte)0);
        await Assert.That(reader.ReadByte()).IsEqualTo((byte)0);
        await Assert.That(reader.ReadInt32()).IsEqualTo(0);
        await Assert.That(reader.ReadString()).IsEqualTo(string.Empty);
        await Assert.That(reader.ReadInt64()).IsEqualTo(0L);
    }
}

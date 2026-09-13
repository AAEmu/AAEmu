using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Expeditions.Activities;

namespace AAEmu.UnitTests.Game.Core.Packets.G2C;

public sealed class SCExpeditionActivityPacketTests
{
    [Test]
    public async Task PortalPoint_WritePreservesNonzeroRadianYaw()
    {
        var portal = new ExpeditionPortalPoint
        {
            Id = 7,
            Name = "Quarter turn",
            ZoneId = 11,
            X = 1.25f,
            Y = 2.5f,
            Z = 3.75f,
            ZRot = MathF.PI / 2f
        };
        var stream = portal.Write(new PacketStream());
        var reader = new PacketStream(stream.GetBytes());

        await Assert.That(reader.ReadUInt32()).IsEqualTo(7u);
        await Assert.That(reader.ReadString()).IsEqualTo("Quarter turn");
        await Assert.That(reader.ReadUInt32()).IsEqualTo(11u);
        _ = reader.ReadInt64();
        _ = reader.ReadInt64();
        await Assert.That(reader.ReadSingle()).IsEqualTo(3.75f);
        await Assert.That(reader.ReadSingle()).IsEqualTo(MathF.PI / 2f);
        await Assert.That(reader.LeftBytes).IsEqualTo(0);
    }

    [Test]
    [Arguments(1, 8)]
    [Arguments(2, 0)]
    [Arguments(3, 8)]
    public async Task ManagementHistory_WritesDetailOnlyForNativeDetailKinds(int type, int detailBytes)
    {
        var history = new ExpeditionManagementHistory(
            "Member", type, 10, new DateTime(2026, 9, 13, 0, 0, 0, DateTimeKind.Utc), 20, 30);
        var stream = history.Write(new PacketStream());
        var reader = new PacketStream(stream.GetBytes());

        await Assert.That(reader.ReadString()).IsEqualTo("Member");
        await Assert.That(reader.ReadInt32()).IsEqualTo(type);
        await Assert.That(reader.ReadUInt64()).IsEqualTo(10UL);
        _ = reader.ReadDateTime();
        await Assert.That(reader.LeftBytes).IsEqualTo(detailBytes);
        if (detailBytes > 0)
        {
            await Assert.That(reader.ReadUInt32()).IsEqualTo(20u);
            await Assert.That(reader.ReadInt32()).IsEqualTo(30);
        }
    }

    [Test]
    public async Task InstanceHistoryList_WritesCurrentNativeEnvelopeAndMemberRecords()
    {
        var recordedAt = new DateTime(2026, 9, 13, 15, 0, 0, DateTimeKind.Utc);
        var history = new ExpeditionInstanceHistory
        {
            HistoryId = 55,
            InstanceRankDetailId = 41,
            InstanceId = 69,
            Score = 12,
            PlayResult = ExpeditionInstancePlayResult.Lose,
            RecordedAt = recordedAt,
            Members =
            [
                new ExpeditionInstanceHistoryMember(55, 101, ExpeditionInstanceMemberStatus.Finished),
                new ExpeditionInstanceHistoryMember(55, 102, ExpeditionInstanceMemberStatus.Started)
            ]
        };
        var body = new SCExpeditionInstanceHistoryInfoListPacket(true, 700, [history])
            .Write(new PacketStream());
        var reader = new PacketStream(body.GetBytes());

        await Assert.That(reader.ReadBoolean()).IsTrue();
        await Assert.That(reader.ReadByte()).IsEqualTo((byte)1);
        await Assert.That(reader.ReadUInt32()).IsEqualTo(700u);
        await Assert.That(reader.ReadUInt64()).IsEqualTo(55UL);
        await Assert.That(reader.ReadUInt32()).IsEqualTo(41u);
        await Assert.That(reader.ReadUInt32()).IsEqualTo(69u);
        await Assert.That(reader.ReadUInt32()).IsEqualTo(12u);
        await Assert.That(reader.ReadByte()).IsEqualTo((byte)ExpeditionInstancePlayResult.Lose);
        await Assert.That(reader.ReadInt64()).IsEqualTo(Helpers.UnixTime(recordedAt));
        await Assert.That(reader.ReadByte()).IsEqualTo((byte)2);
        await AssertInstanceMember(reader, 55, 101, ExpeditionInstanceMemberStatus.Finished);
        await AssertInstanceMember(reader, 55, 102, ExpeditionInstanceMemberStatus.Started);
        await Assert.That(reader.LeftBytes).IsEqualTo(0);
    }

    [Test]
    public async Task InstanceNewHistory_WritesExpeditionRatingThenHistory()
    {
        var recordedAt = new DateTime(2026, 9, 13, 15, 30, 0, DateTimeKind.Utc);
        var history = new ExpeditionInstanceHistory
        {
            HistoryId = 56,
            InstanceRankDetailId = 41,
            InstanceId = 69,
            Score = 20,
            PlayResult = ExpeditionInstancePlayResult.Win,
            RecordedAt = recordedAt
        };
        var body = new SCExpeditionInstanceNewHistoryInfoPacket(701,
                new ExpeditionInstanceRating(41, 4, 2, 1, 1500, 1600), history)
            .Write(new PacketStream());
        var reader = new PacketStream(body.GetBytes());

        await Assert.That(reader.ReadUInt32()).IsEqualTo(701u);
        await Assert.That(reader.ReadUInt32()).IsEqualTo(41u);
        await Assert.That(reader.ReadUInt32()).IsEqualTo(4u);
        await Assert.That(reader.ReadUInt32()).IsEqualTo(2u);
        await Assert.That(reader.ReadUInt32()).IsEqualTo(1u);
        await Assert.That(reader.ReadUInt32()).IsEqualTo(1500u);
        await Assert.That(reader.ReadUInt32()).IsEqualTo(1600u);
        await Assert.That(reader.ReadUInt64()).IsEqualTo(56UL);
        await Assert.That(reader.ReadUInt32()).IsEqualTo(41u);
        await Assert.That(reader.ReadUInt32()).IsEqualTo(69u);
        await Assert.That(reader.ReadUInt32()).IsEqualTo(20u);
        await Assert.That(reader.ReadByte()).IsEqualTo((byte)ExpeditionInstancePlayResult.Win);
        await Assert.That(reader.ReadInt64()).IsEqualTo(Helpers.UnixTime(recordedAt));
        await Assert.That(reader.ReadByte()).IsEqualTo((byte)0);
        await Assert.That(reader.LeftBytes).IsEqualTo(0);
    }

    private static async Task AssertInstanceMember(PacketStream reader, ulong historyId, ulong characterId,
        ExpeditionInstanceMemberStatus status)
    {
        await Assert.That(reader.ReadUInt64()).IsEqualTo(historyId);
        await Assert.That(reader.ReadUInt64()).IsEqualTo(characterId);
        await Assert.That(reader.ReadByte()).IsEqualTo((byte)status);
    }
}

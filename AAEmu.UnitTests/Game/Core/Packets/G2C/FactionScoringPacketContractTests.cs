using AAEmu.Commons.Network;
using AAEmu.Game.Core.Packets.G2C;

namespace AAEmu.UnitTests.Game.Core.Packets.G2C;

public sealed class FactionScoringPacketContractTests
{
    [Test]
    public async Task FactionScoringOpcodesArePinned()
    {
        var competition = new SCFactionCompetitionUpdatePointPacket(1, 2, 3);
        var list = new SCZoneScoreListPacket([]);
        var update = new SCZoneScoreUpdatePacket(4, 5);
        var reset = new SCZoneScoreResetPacket(6);

        await Assert.That(competition.TypeId).IsEqualTo(SCOffsets.SCFactionCompetitionUpdatePointPacket);
        await Assert.That(list.TypeId).IsEqualTo(SCOffsets.SCZoneScoreListPacket);
        await Assert.That(update.TypeId).IsEqualTo(SCOffsets.SCZoneScoreUpdatePacket);
        await Assert.That(reset.TypeId).IsEqualTo(SCOffsets.SCZoneScoreResetPacket);
        await Assert.That(SCOffsets.SCFactionCompetitionUpdatePointPacket).IsEqualTo((ushort)0x338);
        await Assert.That(SCOffsets.SCZoneScoreListPacket).IsEqualTo((ushort)0x34F);
        await Assert.That(SCOffsets.SCZoneScoreUpdatePacket).IsEqualTo((ushort)0x350);
        await Assert.That(SCOffsets.SCZoneScoreResetPacket).IsEqualTo((ushort)0x351);
    }

    [Test]
    public async Task CompetitionPointUpdateWritesKindAndSignedDelta()
    {
        var body = new SCFactionCompetitionUpdatePointPacket(7, 42, -9)
            .Write(new PacketStream())
            .GetBytes();

        var expected = new PacketStream();
        expected.Write((ushort)7);
        expected.Write(42u);
        expected.Write(-9);

        await Assert.That(body).IsEquivalentTo(expected.GetBytes());
        await Assert.That(body.Length).IsEqualTo(10);
    }

    [Test]
    public async Task ZoneScoreListWritesCountThenTypeAndSignedScore()
    {
        var body = new SCZoneScoreListPacket([
                new ZoneScoreListEntry(3, 120),
                new ZoneScoreListEntry(4, -8)
            ])
            .Write(new PacketStream())
            .GetBytes();

        var expected = new PacketStream();
        expected.Write(2);
        expected.Write(3u);
        expected.Write(120);
        expected.Write(4u);
        expected.Write(-8);

        await Assert.That(body).IsEquivalentTo(expected.GetBytes());
        await Assert.That(body.Length).IsEqualTo(20);
    }

    [Test]
    public async Task ZoneScoreListWritesEveryProvidedEntry()
    {
        var entries = Enumerable.Range(1, 21)
            .Select(index => new ZoneScoreListEntry((uint)index, index))
            .ToArray();

        var body = new SCZoneScoreListPacket(entries).Write(new PacketStream()).GetBytes();
        var expected = new PacketStream();
        expected.Write(entries.Length);
        foreach (var entry in entries)
        {
            expected.Write(entry.Type);
            expected.Write(entry.Score);
        }

        await Assert.That(body).IsEquivalentTo(expected.GetBytes());
        await Assert.That(body.Length).IsEqualTo(4 + entries.Length * 8);
    }

    [Test]
    public async Task ZoneScoreUpdateWritesKindAndSignedDelta()
    {
        var body = new SCZoneScoreUpdatePacket(12, -4).Write(new PacketStream()).GetBytes();

        var expected = new PacketStream();
        expected.Write(12u);
        expected.Write(-4);

        await Assert.That(body).IsEquivalentTo(expected.GetBytes());
        await Assert.That(body.Length).IsEqualTo(8);
    }

    [Test]
    public async Task ZoneScoreResetWritesOnlyItsKind()
    {
        var body = new SCZoneScoreResetPacket(12).Write(new PacketStream()).GetBytes();

        var expected = new PacketStream();
        expected.Write(12u);

        await Assert.That(body).IsEquivalentTo(expected.GetBytes());
        await Assert.That(body.Length).IsEqualTo(4);
    }
}

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Rankings;

namespace AAEmu.UnitTests.Game.Core.Packets.G2C;

public class SCRankRewardSnapshotPacketTests
{
    private static RankingOrderedEntry Row(uint ranking, long v1)
    {
        return new RankingOrderedEntry
        {
            V1 = v1,
            V2 = 12,
            Timestamp = 1789530000,
            WorldId = 1,
            Id = 8,
            AccountId = 3,
            Type = 8,
            PrivacyStatus = 0,
            Ranking = ranking
        };
    }

    [Test]
    public async Task RewardSnapshot_CarriesTheBoardsBodyAndStopsThere()
    {
        var stream = new SCRankRewardSnapshotPacket(42, 1, [Row(1, 12345)], 42)
            .Write(new PacketStream());

        stream.Rollback();
        await Assert.That(stream.ReadUInt32()).IsEqualTo(42u);      // the board asked for
        await Assert.That(stream.ReadUInt32()).IsEqualTo(1u);       // its division
        await Assert.That(stream.ReadUInt32()).IsEqualTo(1u);       // one line

        await Assert.That(stream.ReadInt64()).IsEqualTo(12345L);    // V1
        await Assert.That(stream.ReadInt64()).IsEqualTo(12L);       // V2
        await Assert.That(stream.ReadInt64()).IsEqualTo(1789530000L);
        await Assert.That(stream.ReadByte()).IsEqualTo((byte)1);    // worldId
        await Assert.That(stream.ReadUInt64()).IsEqualTo(8UL);      // id
        await Assert.That(stream.ReadUInt64()).IsEqualTo(3UL);      // accountId
        await Assert.That(stream.ReadInt64()).IsEqualTo(8L);        // type
        await Assert.That(stream.ReadByte()).IsEqualTo((byte)0);    // privacyStatus
        await Assert.That(stream.ReadBoolean()).IsFalse();          // no line detail
        await Assert.That(stream.ReadUInt32()).IsEqualTo(1u);       // the place it held

        await Assert.That(stream.ReadUInt32()).IsEqualTo(0u);       // no tiers
        await Assert.That(stream.ReadInt64()).IsEqualTo(42L);       // the board
        await Assert.That(stream.LeftBytes).IsEqualTo(0);           // and no time: the window reads its own
    }

    [Test]
    public async Task RewardSnapshot_AnswersEmptyForASeasonWithNoLines()
    {
        var stream = new SCRankRewardSnapshotPacket(28, 2003, [], 28).Write(new PacketStream());

        stream.Rollback();
        await Assert.That(stream.ReadUInt32()).IsEqualTo(28u);
        await Assert.That(stream.ReadUInt32()).IsEqualTo(2003u);
        await Assert.That(stream.ReadUInt32()).IsEqualTo(0u);
        await Assert.That(stream.ReadUInt32()).IsEqualTo(0u);
        await Assert.That(stream.ReadInt64()).IsEqualTo(28L);
        await Assert.That(stream.LeftBytes).IsEqualTo(0);
    }

    [Test]
    public async Task RewardSnapshot_CapsTheLinesTheClientReads()
    {
        var rows = Enumerable.Range(0, SCRankSnapshotPacket.MaxEntries + 5).Select(i => Row((uint)i, i)).ToList();

        var stream = new SCRankRewardSnapshotPacket(42, 0, rows, 42).Write(new PacketStream());

        stream.Rollback();
        stream.ReadBytes(8);
        await Assert.That(stream.ReadUInt32()).IsEqualTo((uint)SCRankSnapshotPacket.MaxEntries);
        stream.ReadBytes(SCRankSnapshotPacket.MaxEntries * 55);
        await Assert.That(stream.ReadUInt32()).IsEqualTo(0u);
        await Assert.That(stream.ReadInt64()).IsEqualTo(42L);
        await Assert.That(stream.LeftBytes).IsEqualTo(0);
    }
}

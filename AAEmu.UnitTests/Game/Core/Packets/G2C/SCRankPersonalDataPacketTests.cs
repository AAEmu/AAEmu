using AAEmu.Commons.Network;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Rankings;

namespace AAEmu.UnitTests.Game.Core.Packets.G2C;

public class SCRankPersonalDataPacketTests
{
    private static RankingEntry Entry()
    {
        return new RankingEntry
        {
            V1 = 12345,
            V2 = 67,
            Timestamp = 1789530000,
            WorldId = 4,
            Id = 8,
            AccountId = 3,
            Type = 23,
            PrivacyStatus = 1,
            IsAllocated = true
        };
    }

    [Test]
    public async Task PersonalData_WritesTheRankingThenEachLine()
    {
        var stream = new SCRankPersonalDataPacket(0, [new RankingEntryLine(23, Entry())]).Write(new PacketStream());

        stream.Rollback();
        await Assert.That(stream.ReadInt64()).IsEqualTo(0L);          // the ranking asked for
        await Assert.That(stream.ReadUInt32()).IsEqualTo(1u);         // one line
        await Assert.That(stream.ReadUInt32()).IsEqualTo(23u);        // the line's key
        await Assert.That(stream.ReadInt64()).IsEqualTo(12345L);      // V1
        await Assert.That(stream.ReadInt64()).IsEqualTo(67L);         // V2
        await Assert.That(stream.ReadInt64()).IsEqualTo(1789530000L); // timestamp
        await Assert.That(stream.ReadByte()).IsEqualTo((byte)4);      // worldId
        await Assert.That(stream.ReadUInt64()).IsEqualTo(8UL);        // id
        await Assert.That(stream.ReadUInt64()).IsEqualTo(3UL);        // accountId
        await Assert.That(stream.ReadInt64()).IsEqualTo(23L);         // type
        await Assert.That(stream.ReadByte()).IsEqualTo((byte)1);      // privacyStatus
        await Assert.That(stream.ReadBoolean()).IsTrue();             // isAllocated
        await Assert.That(stream.LeftBytes).IsEqualTo(0);
    }

    [Test]
    public async Task PersonalData_WithNoLinesStillCarriesTheCount()
    {
        var stream = new SCRankPersonalDataPacket(7, []).Write(new PacketStream());

        stream.Rollback();
        await Assert.That(stream.ReadInt64()).IsEqualTo(7L);
        await Assert.That(stream.ReadUInt32()).IsEqualTo(0u);
        await Assert.That(stream.LeftBytes).IsEqualTo(0);
    }

    [Test]
    public async Task PersonalData_EachLineCostsWhatTheClientReads()
    {
        // 8 (ranking) + 4 (count) + 4 (key) + 51 (entry: 8+8+8+1+8+8+8+1+1)
        var stream = new SCRankPersonalDataPacket(0, [new RankingEntryLine(1, Entry()), new RankingEntryLine(2, Entry())])
            .Write(new PacketStream());

        stream.Rollback();

        await Assert.That(stream.ReadInt64()).IsEqualTo(0L);
        await Assert.That(stream.ReadUInt32()).IsEqualTo(2u);
        stream.ReadBytes(2 * 55);
        await Assert.That(stream.LeftBytes).IsEqualTo(0);
    }
}

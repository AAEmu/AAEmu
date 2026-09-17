using AAEmu.Commons.Network;
using AAEmu.Game.Models.Game.Rankings;

namespace AAEmu.UnitTests.Game.Models.Game.Rankings;

public class RankingSubDataTests
{
    [Test]
    public async Task ForItem_WritesItsKindAndTheItemTemplate()
    {
        var stream = new PacketStream();
        RankingSubData.ForItem(54160).Write(stream);

        stream.Rollback();
        await Assert.That(stream.ReadByte()).IsEqualTo((byte)RankingSubDataKind.ItemId);
        await Assert.That(stream.ReadUInt32()).IsEqualTo(54160u);
        await Assert.That(stream.ReadByte()).IsEqualTo((byte)0); // the byte the window skips
        await Assert.That(stream.LeftBytes).IsEqualTo(0);
    }

    [Test]
    public async Task ForOneCount_WritesOneCount()
    {
        var stream = new PacketStream();
        RankingSubData.ForOneCount(7).Write(stream);

        stream.Rollback();
        await Assert.That(stream.ReadByte()).IsEqualTo((byte)RankingSubDataKind.OneCount);
        await Assert.That(stream.ReadInt32()).IsEqualTo(7);
        await Assert.That(stream.LeftBytes).IsEqualTo(0);
    }

    [Test]
    public async Task ForBattleRecord_WritesTheFiveFiguresInTheOrderTheWindowReadsThem()
    {
        var stream = new PacketStream();
        RankingSubData.ForBattleRecord(win: 3, lose: 1, draw: 0, kill: 11, death: 4).Write(stream);

        stream.Rollback();
        await Assert.That(stream.ReadByte()).IsEqualTo((byte)RankingSubDataKind.BattleRecord);
        await Assert.That(stream.ReadInt32()).IsEqualTo(3);   // wins, which the ratio is taken from
        await Assert.That(stream.ReadInt32()).IsEqualTo(1);   // losses
        await Assert.That(stream.ReadInt32()).IsEqualTo(0);   // draws
        await Assert.That(stream.ReadInt32()).IsEqualTo(11);  // kills
        await Assert.That(stream.ReadInt32()).IsEqualTo(4);   // deaths
        await Assert.That(stream.LeftBytes).IsEqualTo(0);
    }

    [Test]
    [Arguments(RankingSubDataKind.ItemId)]
    [Arguments(RankingSubDataKind.OneCount)]
    [Arguments(RankingSubDataKind.TwoCounts)]
    [Arguments(RankingSubDataKind.WinRecord)]
    [Arguments(RankingSubDataKind.BattleRecord)]
    public async Task StoredBytes_ReadBackAsTheSameBlock(RankingSubDataKind kind)
    {
        var block = kind switch
        {
            RankingSubDataKind.ItemId => RankingSubData.ForItem(30219),
            RankingSubDataKind.OneCount => RankingSubData.ForOneCount(12),
            RankingSubDataKind.TwoCounts => RankingSubData.ForTwoCounts(3, 40),
            RankingSubDataKind.WinRecord => RankingSubData.ForWinRecord(9, 4, 2),
            _ => RankingSubData.ForBattleRecord(9, 4, 2, 30, 12)
        };

        var read = RankingSubData.FromBytes(block.ToBytes());

        await Assert.That(read).IsNotNull();
        await Assert.That(read.Kind).IsEqualTo(kind);
        await Assert.That(read.ItemId).IsEqualTo(block.ItemId);
        await Assert.That(read.Counts).IsEquivalentTo(block.Counts);
    }

    [Test]
    public async Task FromBytes_IsNullForNothingAndForAKindTheClientDoesNotRead()
    {
        await Assert.That(RankingSubData.FromBytes(null)).IsNull();
        await Assert.That(RankingSubData.FromBytes([])).IsNull();

        // a kind byte the client has no case for, and a payload too short for the kind it claims
        await Assert.That(RankingSubData.FromBytes([9, 1, 2, 3])).IsNull();
        await Assert.That(RankingSubData.FromBytes([(byte)RankingSubDataKind.OneCount, 0])).IsNull();
    }

    [Test]
    public async Task ToBytes_IsAsWideAsTheKindTheClientReadsItBy()
    {
        // a kind byte plus the payload the client takes for it
        await Assert.That(RankingSubData.ForItem(1).ToBytes().Length).IsEqualTo(6);
        await Assert.That(RankingSubData.ForOneCount(1).ToBytes().Length).IsEqualTo(5);
        await Assert.That(RankingSubData.ForTwoCounts(1, 2).ToBytes().Length).IsEqualTo(9);
        await Assert.That(RankingSubData.ForWinRecord(1, 2, 3).ToBytes().Length).IsEqualTo(13);
        await Assert.That(RankingSubData.ForBattleRecord(1, 2, 3, 4, 5).ToBytes().Length).IsEqualTo(21);
    }
}

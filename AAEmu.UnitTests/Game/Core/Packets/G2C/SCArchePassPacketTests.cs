using AAEmu.Commons.Network;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.UnitTests.Game.Core.Packets.G2C;

public class SCArchePassPacketTests
{
    [Test]
    public async Task List_WritesCountLastThenRows()
    {
        var row = new ArchePassProgress
        {
            PassId = 88,
            Status = ArchePassStatus.Owned
        };
        var body = new SCArchePassesPacket([row], true)
            .Write(new PacketStream())
            .GetBytes();

        var expected = new PacketStream();
        expected.Write(1);
        expected.Write(true);
        ArchePassRules.WriteRow(expected, row);

        await Assert.That(Hex.Of(body)).IsEqualTo(Hex.Of(expected.GetBytes()));
    }

    [Test]
    public async Task List_EmptyLast_IsCountZeroAndLast()
    {
        var body = new SCArchePassesPacket([], true)
            .Write(new PacketStream())
            .GetBytes();

        var expected = new PacketStream();
        expected.Write(0);
        expected.Write(true);

        await Assert.That(Hex.Of(body)).IsEqualTo(Hex.Of(expected.GetBytes()));
    }

    [Test]
    public async Task Update_WritesRowThenReasonDiffAndAllDone()
    {
        var row = new ArchePassProgress
        {
            PassId = 88,
            Status = ArchePassStatus.Progress,
            Point = 3
        };
        var body = new SCUpdateArchePassPacket(row, 6, 0, false)
            .Write(new PacketStream())
            .GetBytes();

        var expected = new PacketStream();
        ArchePassRules.WriteRow(expected, row);
        expected.Write((byte)6);
        expected.Write(0);
        expected.Write(false);

        await Assert.That(Hex.Of(body)).IsEqualTo(Hex.Of(expected.GetBytes()));
    }

    [Test]
    public async Task CompletedList_WritesCountThenIdxAndBody()
    {
        var words = ArchePassRules.PackCompleted([88]);
        var body = new SCCompletedArchePassesPacket(words)
            .Write(new PacketStream())
            .GetBytes();

        var expected = new PacketStream();
        expected.Write(words.Count);
        foreach (var (idx, word) in words)
        {
            expected.Write(idx);
            expected.Write(word);
        }

        await Assert.That(Hex.Of(body)).IsEqualTo(Hex.Of(expected.GetBytes()));
    }
}

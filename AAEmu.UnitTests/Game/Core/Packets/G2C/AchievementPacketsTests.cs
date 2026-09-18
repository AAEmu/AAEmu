using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Achievement;

namespace AAEmu.UnitTests.Game.Core.Packets.G2C;

/// <summary>
/// The achievement packets the manager pushes: the list the client opens its window from, and the two
/// updates it gets while playing.
/// </summary>
public class AchievementPacketsTests
{
    [Test]
    public async Task List_WritesCountThenOneRowPerAchievement()
    {
        var completed = new DateTime(2026, 9, 18, 12, 0, 0, DateTimeKind.Utc);
        List<AchievementInfo> rows =
        [
            new() { Id = 9, Amount = 50, Complete = completed },
            new() { Id = 168, Amount = 1, Complete = default }
        ];

        var body = new SCAchievementsPacket(rows).Write(new PacketStream()).GetBytes();

        var expected = new PacketStream();
        expected.Write(2);
        expected.Write(9u);
        expected.Write(50u);
        expected.Write(completed);
        expected.Write(168u);
        expected.Write(1u);
        // Never completed: the wire carries the epoch, not a year-one date.
        expected.Write((long)0);

        await Assert.That(body).IsEquivalentTo(expected.GetBytes());
    }

    [Test]
    public async Task List_HoldsAtMostFiftyRowsPerPacket()
    {
        // The client does not take a longer list in one packet; the manager splits on this number, so the
        // two must not drift apart.
        await Assert.That(AchievementManager.MaxEntriesPerPacket).IsEqualTo(50);

        List<AchievementInfo> rows = [.. Enumerable.Range(1, AchievementManager.MaxEntriesPerPacket)
            .Select(id => new AchievementInfo { Id = (uint)id, Amount = 1 })];

        var body = new SCAchievementsPacket(rows).Write(new PacketStream()).GetBytes();

        var expected = new PacketStream();
        expected.Write(AchievementManager.MaxEntriesPerPacket);
        foreach (var row in rows)
        {
            expected.Write(row.Id);
            expected.Write(row.Amount);
            expected.Write((long)0);
        }

        await Assert.That(body).IsEquivalentTo(expected.GetBytes());
    }

    [Test]
    public async Task Changed_WritesIdThenAmount()
    {
        var body = new SCAchievementChangedPacket(2333, 4).Write(new PacketStream()).GetBytes();

        var expected = new PacketStream();
        expected.Write(2333u);
        expected.Write(4);

        await Assert.That(body).IsEquivalentTo(expected.GetBytes());
    }

    [Test]
    public async Task Completed_WritesIdThenCompletionTime()
    {
        var body = new SCAchievementCompletedPacket(2333).Write(new PacketStream()).GetBytes();

        // The completion time is the moment the packet was built, in the client's own second domain.
        var expected = new PacketStream();
        expected.Write(2333u);
        expected.Write(Helpers.UnixTimeNow());

        var bytes = expected.GetBytes();
        await Assert.That(body.Length).IsEqualTo(bytes.Length);
        // Same id, and a timestamp within a second of now rather than a pinned one.
        await Assert.That(body.Take(4)).IsEquivalentTo(bytes.Take(4));
        await Assert.That(Math.Abs(BitConverter.ToInt64(body, 4) - BitConverter.ToInt64(bytes, 4)))
            .IsLessThanOrEqualTo(1);
    }

    [Test]
    public async Task Reset_WritesIdAmountAndTime()
    {
        var body = new SCAchievementResetedPacket(2333, 0).Write(new PacketStream()).GetBytes();

        await Assert.That(body.Length).IsEqualTo(4 + 4 + 8);
        await Assert.That(BitConverter.ToUInt32(body, 0)).IsEqualTo(2333u);
        await Assert.That(BitConverter.ToInt32(body, 4)).IsEqualTo(0);
    }
}

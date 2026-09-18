using AAEmu.Commons.Network;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Indun;

namespace AAEmu.UnitTests.Game.Core.Packets.G2C;

/// <summary>
/// The channel picker's list. The client reads a count and then that many fixed rows, so the body's shape and
/// its cap are the whole contract.
/// </summary>
public class SCSysIndunStatPacketTests
{
    [Test]
    public async Task List_WritesTypeCountThenOneRowPerChannel()
    {
        var rows = new List<SysIndunChannel>
        {
            new(ChannelId: 2, InstanceId: 4001, Current: 3, Restrict: 50),
            new(ChannelId: 5, InstanceId: 0, Current: 0, Restrict: 50)
        };

        var body = new SCSysIndunStatPacket(70u, rows).Write(new PacketStream()).GetBytes();

        var expected = new PacketStream();
        expected.Write(70u);
        expected.Write(2);
        expected.Write(2);       // channel
        expected.Write(4001u);   // instance copy
        expected.Write(50);      // restrict
        expected.Write(3);       // current
        expected.Write(5);
        expected.Write(0u);
        expected.Write(50);
        expected.Write(0);

        await Assert.That(body).IsEquivalentTo(expected.GetBytes());
    }

    [Test]
    public async Task EmptyList_WritesTypeAndZeroCount()
    {
        var body = new SCSysIndunStatPacket(70u, []).Write(new PacketStream()).GetBytes();

        var expected = new PacketStream();
        expected.Write(70u);
        expected.Write(0);

        await Assert.That(body).IsEquivalentTo(expected.GetBytes());
    }

    [Test]
    public async Task List_StopsAtTheClientsCap()
    {
        // The client reads a fixed loop over its clamped count; a longer body would have it read past the rows.
        var rows = Enumerable.Range(0, SysIndunChannelRules.MaxChannels + 8)
            .Select(i => new SysIndunChannel(i, (uint)(5000 + i), 1, 50))
            .ToList();

        var body = new SCSysIndunStatPacket(70u, rows).Write(new PacketStream()).GetBytes();

        var bytesPerRow = 16;
        var header = 4 + 4;
        await Assert.That(body.Length).IsEqualTo(header + SysIndunChannelRules.MaxChannels * bytesPerRow);
        await Assert.That(BitConverter.ToInt32(body, 4)).IsEqualTo(SysIndunChannelRules.MaxChannels);
    }
}

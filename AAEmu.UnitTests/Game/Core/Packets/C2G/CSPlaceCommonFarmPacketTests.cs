using AAEmu.Commons.Network;
using AAEmu.Game.Core.Packets.C2G;
using AAEmu.Game.Models.Game.CommonFarm.Static;

namespace AAEmu.UnitTests.Game.Core.Packets.C2G;

/// <summary>
/// Pins the farm placement request parser (CS 0x164) to the real wire: <c>u32 type</c>, signed
/// <c>s32 count</c>, then — only when count &gt; 0 — a loop of <c>count</c> 12-byte vec3 points. The
/// count is signed (so 0 and negatives carry no points) and the whole loop must be consumed; a
/// parser that reads one point would leave the rest of the body unread.
/// </summary>
public class CSPlaceCommonFarmPacketTests
{
    [Test]
    public async Task Read_ConsumesTheWholePointLoop()
    {
        var packet = Parse(2, [(1f, 2f, 3f), (4f, 5f, 6f)]);

        await Assert.That(packet.TypeValue).IsEqualTo((uint)FarmType.Farm);
        await Assert.That(packet.SignedCount).IsEqualTo(2);
        await Assert.That(packet.Points.Count).IsEqualTo(2);
        await Assert.That(packet.Points[0].X).IsEqualTo(1f);
        await Assert.That(packet.Points[0].Y).IsEqualTo(2f);
        await Assert.That(packet.Points[0].Z).IsEqualTo(3f);
        await Assert.That(packet.Points[1].X).IsEqualTo(4f);
        await Assert.That(packet.Points[1].Y).IsEqualTo(5f);
        await Assert.That(packet.Points[1].Z).IsEqualTo(6f);
    }

    [Test]
    public async Task Read_ZeroCount_CarriesNoPoints()
    {
        // A zero count is a well-formed empty request: the guarded loop is skipped entirely.
        var packet = Parse(0, []);

        await Assert.That(packet.SignedCount).IsEqualTo(0);
        await Assert.That(packet.Points.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Read_NegativeCount_CarriesNoPoints()
    {
        // count is signed, so a negative value is not a huge unsigned loop — it is simply empty.
        var packet = Parse(-3, []);

        await Assert.That(packet.SignedCount).IsEqualTo(-3);
        await Assert.That(packet.Points.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Read_HostileCount_IsBoundedByTheWireClamp()
    {
        // A count far past the clamp is bounded instead of looping; the extra points are ignored.
        var packet = Parse(int.MaxValue, [(9f, 9f, 9f)]);

        await Assert.That(packet.Points.Count).IsEqualTo(CSPlaceCommonFarmPacket.MaxPointCount);
    }

    [Test]
    public async Task Read_SinglePoint_MatchesAOneEntryRequest()
    {
        var packet = Parse(1, [(7f, 8f, 9f)]);

        await Assert.That(packet.SignedCount).IsEqualTo(1);
        await Assert.That(packet.Points.Count).IsEqualTo(1);
        await Assert.That(packet.Points[0].Z).IsEqualTo(9f);
    }

    private static CSPlaceCommonFarmPacket Parse(int count, (float x, float y, float z)[] points)
    {
        var stream = new PacketStream();
        stream.Write((uint)FarmType.Farm);
        stream.Write(count);
        foreach (var (x, y, z) in points)
        {
            stream.Write(x);
            stream.Write(y);
            stream.Write(z);
        }

        var packet = new CSPlaceCommonFarmPacket();
        packet.Read(stream);
        return packet;
    }
}

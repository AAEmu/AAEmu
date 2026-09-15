using AAEmu.Commons.Network;
using AAEmu.Game.Core.Packets.G2C;

namespace AAEmu.UnitTests.Game.Core.Packets.G2C;

/// <summary>
/// The record rows of SCCrimeRecordsPacket. The client reads the counters and then, while the count is
/// above zero, one row per offence in the order pinned here - a wrong row width would put every later
/// field of the trial window out of step (the reporter cell was the first casualty).
/// </summary>
public class SCCrimeRecordsPacketTests
{
    private static byte[] Body(Action<PacketStream> write)
    {
        var stream = new PacketStream();
        write(stream);
        return stream.GetBytes();
    }

    [Test]
    public async Task Counters_WithoutRows_StayFourFields()
    {
        var body = Body(s => new SCCrimeRecordsPacket(7ul, 0u, 3u, 0u).Write(s));

        await Assert.That(body.Length).IsEqualTo(8 + 4 + 4 + 4);
        await Assert.That(BitConverter.ToUInt64(body, 0)).IsEqualTo(7ul);
        await Assert.That(BitConverter.ToUInt32(body, 16)).IsEqualTo(0u);
    }

    [Test]
    public async Task Rows_FollowTheCounters_InTheClientSlotWidths()
    {
        var entry = new CrimeRecordEntry(
            Id: 11, Type: 0, VictimName: "V", Type2: 0, ReporterName: "R",
            Field1: 0, Field2: 0, CrimeKind: 3, Field3: 0, Field4: 0,
            X: 1.5, Y: 2.5, Z: 3.5f, Description: "D", Time: 99);

        var body = Body(s => new SCCrimeRecordsPacket(7ul, 0u, 1u, new[] { entry }).Write(s));

        // header 20 + id u32 4 + type u64 8 + victim (2+1) + type2 u64 8 + reporter (2+1)
        // + u32 + u32 + u8 crimeKind + u32 + u32 + x f64 8 + y f64 8 + z f32 4 + description (2+1)
        // + time u64 8
        await Assert.That(body.Length).IsEqualTo(20 + 4 + 8 + 3 + 8 + 3 + 4 + 4 + 1 + 4 + 4 + 8 + 8 + 4 + 3 + 8);

        await Assert.That(BitConverter.ToUInt32(body, 12)).IsEqualTo(1u);   // count follows the list
        await Assert.That(BitConverter.ToUInt32(body, 20)).IsEqualTo(11u);  // id
        await Assert.That(body[54]).IsEqualTo((byte)3);                     // crimeKind
        await Assert.That(BitConverter.ToDouble(body, 63)).IsEqualTo(1.5);  // x
        await Assert.That(BitConverter.ToSingle(body, 79)).IsEqualTo(3.5f); // z
        await Assert.That(BitConverter.ToUInt64(body, 86)).IsEqualTo(99ul); // time
    }
}

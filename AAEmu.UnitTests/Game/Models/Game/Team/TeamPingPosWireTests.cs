using AAEmu.Commons.Network;
using AAEmu.Game.Models.Game.Team;
using AAEmu.Game.Models.Game.World.Transform;

namespace AAEmu.UnitTests.Game.Models.Game.Team;

public class TeamPingPosWireTests
{
    [Test]
    public async Task WriteRead_RoundTripsSinglePingSlot()
    {
        var stream = new PacketStream();
        var pos = new WorldSpawnPosition { X = 1234.5f, Y = 5678.25f, Z = 120.0f };
        TeamPingPosWire.Write(stream, teamId: 42, setPingType: 1, hasPing: true, position: pos, instanceId: 7);

        var read = new PacketStream(stream);
        var (teamId, setPingType, hasPing, position, instanceId) = TeamPingPosWire.Read(read);

        await Assert.That(teamId).IsEqualTo(42u);
        await Assert.That(setPingType).IsEqualTo((byte)1);
        await Assert.That(hasPing).IsTrue();
        await Assert.That(instanceId).IsEqualTo(7u);
        await Assert.That(Math.Abs(position.X - pos.X)).IsLessThan(0.05f);
        await Assert.That(Math.Abs(position.Y - pos.Y)).IsLessThan(0.05f);
        await Assert.That(Math.Abs(position.Z - pos.Z)).IsLessThan(0.05f);
    }

    [Test]
    public async Task Write_ClearPing_HasZeroFlag()
    {
        var stream = new PacketStream();
        TeamPingPosWire.Write(stream, 0, 0, hasPing: false, new WorldSpawnPosition(), 0);
        var read = new PacketStream(stream);
        var (_, _, hasPing, _, _) = TeamPingPosWire.Read(read);
        await Assert.That(hasPing).IsFalse();
    }
}

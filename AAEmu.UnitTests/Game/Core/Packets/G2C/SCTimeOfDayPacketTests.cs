using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;

namespace AAEmu.UnitTests.Game.Core.Packets.G2C;

public class SCTimeOfDayPacketTests
{
    [Test]
    public async Task DetailedPacket_WritesAllFourZoneValues()
    {
        var stream = new PacketStream();
        new SCDetailedTimeOfDayPacket(7.5f, 0.25f, 3f, 21f).Write(stream);
        var body = stream.GetBytes();

        await Assert.That(body.Length).IsEqualTo(16);
        await Assert.That(BitConverter.ToSingle(body, 0)).IsEqualTo(7.5f);
        await Assert.That(BitConverter.ToSingle(body, 4)).IsEqualTo(0.25f);
        await Assert.That(BitConverter.ToSingle(body, 8)).IsEqualTo(3f);
        await Assert.That(BitConverter.ToSingle(body, 12)).IsEqualTo(21f);
    }

    [Test]
    public async Task TimePacket_WritesOneFloatAndNoTail()
    {
        var stream = new PacketStream();
        new SCTimeOfDayPacket(18.75f).Write(stream);
        var body = stream.GetBytes();

        await Assert.That(body.Length).IsEqualTo(4);
        await Assert.That(BitConverter.ToSingle(body, 0)).IsEqualTo(18.75f);
    }

    [Test]
    public async Task PeriodicAndZoneReport_AreTheHourOnlyOpcode()
    {
        var periodic = TimeOfDayClientPackets.Periodic(11.25f);
        var fromZone = TimeOfDayClientPackets.FromZoneReport(11.25f);

        await Assert.That(periodic.TypeId).IsEqualTo(SCOffsets.SCTimeOfDayPacket);
        await Assert.That(fromZone.TypeId).IsEqualTo(SCOffsets.SCTimeOfDayPacket);
    }

    [Test]
    public async Task EnvironmentSeed_IsTheFourFieldPacket()
    {
        var seed = TimeOfDayClientPackets.EnvironmentSeed(6.5f);
        var stream = new PacketStream();
        seed.Write(stream);
        var body = stream.GetBytes();

        await Assert.That(seed.TypeId).IsEqualTo(SCOffsets.SCDetailedTimeOfDayPacket);
        await Assert.That(body.Length).IsEqualTo(16);
        await Assert.That(BitConverter.ToSingle(body, 0)).IsEqualTo(6.5f);
        await Assert.That(BitConverter.ToSingle(body, 4)).IsEqualTo(TimeManager.DefaultGameHourSpeed);
        await Assert.That(BitConverter.ToSingle(body, 8)).IsEqualTo(0f);
        await Assert.That(BitConverter.ToSingle(body, 12)).IsEqualTo(24f);
    }

    [Test]
    public async Task BindBeforeWorldLoad_IsTheHourOnlyOpcode()
    {
        var sent = new List<GamePacket>();
        TimeOfDayClientPackets.BindBeforeWorldLoad(sent.Add, 7.783f);

        await Assert.That(sent.Count).IsEqualTo(1);
        await Assert.That(sent[0].TypeId).IsEqualTo(SCOffsets.SCTimeOfDayPacket);

        var hour = new PacketStream();
        sent[0].Write(hour);
        await Assert.That(hour.GetBytes().Length).IsEqualTo(4);
        await Assert.That(BitConverter.ToSingle(hour.GetBytes(), 0)).IsEqualTo(7.783f);
    }

    [Test]
    public async Task SendEnterWorld_BindsTheHourAndDoesNotReloadEnvironment()
    {
        var sent = new List<GamePacket>();
        TimeOfDayClientPackets.SendEnterWorld(sent.Add, 7.783f);

        await Assert.That(sent.Count).IsEqualTo(1);
        await Assert.That(sent[0].TypeId).IsEqualTo(SCOffsets.SCTimeOfDayPacket);

        var hour = new PacketStream();
        sent[0].Write(hour);
        await Assert.That(hour.GetBytes().Length).IsEqualTo(4);
        await Assert.That(BitConverter.ToSingle(hour.GetBytes(), 0)).IsEqualTo(7.783f);
    }
}

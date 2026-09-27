using AAEmu.Commons.Network;
using AAEmu.Game.Core.Packets.C2G;

namespace AAEmu.UnitTests.Game.Core.Packets.C2G;

public class CSTeamJointSummonPacketReadTests
{
    [Test]
    public async Task JointInfo_ReadsTypeModeNameAndWorld()
    {
        var stream = new PacketStream();
        stream.Write(-5L);
        stream.Write((sbyte)2);
        stream.Write("Target");
        stream.Write((sbyte)1);
        stream.Rollback();

        var packet = new CSTeamJointInfoPacket();
        packet.Read(stream);
        await Assert.That(packet.Type).IsEqualTo(unchecked((ulong)(-5L)));
        await Assert.That(packet.Mode).IsEqualTo((sbyte)2);
        await Assert.That(packet.Name).IsEqualTo("Target");
        await Assert.That(packet.WorldId).IsEqualTo((sbyte)1);
        await Assert.That(stream.LeftBytes).IsEqualTo(0);
    }

    [Test]
    public async Task Joint_ReadsLeaderAcceptAndTimeoutFlags()
    {
        var stream = new PacketStream();
        stream.Write(42UL);
        stream.Write(true);
        stream.Write(true);
        stream.Write(false);
        stream.Rollback();

        var packet = new CSTeamJointPacket();
        packet.Read(stream);
        await Assert.That(packet.TypeValue).IsEqualTo(42UL);
        await Assert.That(packet.MyTeamLeader).IsTrue();
        await Assert.That(packet.Accept).IsTrue();
        await Assert.That(packet.Timeout).IsFalse();
        await Assert.That(stream.LeftBytes).IsEqualTo(0);
    }

    [Test]
    public async Task JointBreak_ReadsAskAndAcceptFlags()
    {
        var stream = new PacketStream();
        stream.Write(true);
        stream.Write(false);
        stream.Rollback();

        var packet = new CSTeamJointBreakPacket();
        packet.Read(stream);
        await Assert.That(packet.Ask).IsTrue();
        await Assert.That(packet.Accept).IsFalse();
        await Assert.That(stream.LeftBytes).IsEqualTo(0);
    }

    [Test]
    public async Task SummonReply_ReadsResultAndSummonerName()
    {
        var stream = new PacketStream();
        stream.Write(true);
        stream.Write("Summoner");
        stream.Rollback();

        var packet = new CSTeamSummonReplyPacket();
        packet.Read(stream);
        await Assert.That(packet.Result).IsTrue();
        await Assert.That(packet.Name).IsEqualTo("Summoner");
        await Assert.That(stream.LeftBytes).IsEqualTo(0);
    }

    [Test]
    public async Task SummonGet_ConsumesNoBody()
    {
        var stream = new PacketStream();
        new CSTeamSummonGetPacket().Read(stream);
        await Assert.That(stream.LeftBytes).IsEqualTo(0);
    }
}

using System.IO;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Packets.C2G;

namespace AAEmu.UnitTests.Game.Core.Packets.C2G;

public class ContentRosterSurveyPacketParseTests
{
    [Test]
    public async Task RosterDelete_WellFormedBody_ReadsEveryRosterId()
    {
        var stream = new PacketStream();
        stream.Write((uint)2);
        stream.Write((ulong)11);
        stream.Write((ulong)22);
        var packet = new CSContentRosterDeletePacket();

        packet.Read(stream);

        await Assert.That(packet.RosterIds.Count).IsEqualTo(2);
        await Assert.That(packet.RosterIds[0]).IsEqualTo(11UL);
        await Assert.That(packet.RosterIds[1]).IsEqualTo(22UL);
    }

    [Test]
    public void RosterDelete_TruncatedCount_FailsLoud()
    {
        var stream = new PacketStream();

        Assert.Throws<InvalidDataException>(() => new CSContentRosterDeletePacket().Read(stream));
    }

    [Test]
    public void RosterDelete_CountLargerThanTheBody_FailsLoud()
    {
        var stream = new PacketStream();
        stream.Write((uint)3);
        stream.Write((ulong)11);

        Assert.Throws<InvalidDataException>(() => new CSContentRosterDeletePacket().Read(stream));
    }

    [Test]
    public async Task SurveyReply_WellFormedBody_ReadsTypeAndForceFuture()
    {
        var stream = new PacketStream();
        stream.Write((uint)5);
        stream.Write((byte)1);
        var packet = new CSSurveyFormReplyPacket();

        packet.Read(stream);

        await Assert.That(packet.TypeValue).IsEqualTo(5u);
        await Assert.That(packet.ForceFuture).IsEqualTo((byte)1);
    }

    [Test]
    public void SurveyReply_TruncatedBody_FailsLoud()
    {
        var stream = new PacketStream();
        stream.Write((uint)5);

        Assert.Throws<InvalidDataException>(() => new CSSurveyFormReplyPacket().Read(stream));
    }

    [Test]
    public void SurveyReply_EmptyBody_FailsLoud()
    {
        var stream = new PacketStream();

        Assert.Throws<InvalidDataException>(() => new CSSurveyFormReplyPacket().Read(stream));
    }
}

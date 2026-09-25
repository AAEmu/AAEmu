using AAEmu.Commons.Network;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Team;

namespace AAEmu.UnitTests.Game.Core.Packets.G2C;

public class SCTeamJointSummonPacketWireTests
{
    [Test]
    public async Task JointInfo_WritesModeThenSharedStructure()
    {
        var info = new TeamJointInfo(-77, "Raid Captain", 4242u, 12, 909u, true);
        var stream = new SCTeamJointInfoPacket(TeamJointModes.ResponsePrompt, info).Write(new PacketStream());
        stream.Rollback();

        await Assert.That(stream.ReadByte()).IsEqualTo((byte)4);
        await Assert.That(stream.ReadInt64()).IsEqualTo(-77);
        await Assert.That(stream.ReadString()).IsEqualTo("Raid Captain");
        await Assert.That(stream.ReadUInt32()).IsEqualTo(4242u);
        await Assert.That(stream.ReadInt32()).IsEqualTo(12);
        await Assert.That(stream.ReadUInt32()).IsEqualTo(909u);
        await Assert.That(stream.ReadBoolean()).IsTrue();
        await Assert.That(stream.LeftBytes).IsEqualTo(0);
    }

    [Test]
    public async Task JointInfo_CarriesEveryKnownWireModeUnchanged()
    {
        var info = new TeamJointInfo(0, "N", 1u, 1, 0, false);
        foreach (var mode in new[] { 1, 2, 3, 4 })
        {
            var stream = new SCTeamJointInfoPacket((sbyte)mode, info).Write(new PacketStream());
            stream.Rollback();
            await Assert.That(stream.ReadByte()).IsEqualTo((byte)mode);
        }
    }

    [Test]
    public async Task Joint_WritesTeamsThenTypeModeAndOrder()
    {
        var stream = new SCTeamJointPacket(7u, 3u, 1234L, SCTeamJointPacket.PacketModeUnresolved, 2).Write(new PacketStream());
        stream.Rollback();

        await Assert.That(stream.ReadUInt32()).IsEqualTo(7u);
        await Assert.That(stream.ReadUInt32()).IsEqualTo(3u);
        await Assert.That(stream.ReadInt64()).IsEqualTo(1234L);
        await Assert.That(stream.ReadByte()).IsEqualTo(SCTeamJointPacket.PacketModeUnresolved);
        await Assert.That(stream.ReadInt32()).IsEqualTo(2);
        await Assert.That(stream.LeftBytes).IsEqualTo(0);
    }

    [Test]
    public async Task JointBreak_WritesAskThenAccept()
    {
        var stream = new SCTeamJointBreakPacket(true, false).Write(new PacketStream());
        stream.Rollback();

        await Assert.That(stream.ReadBoolean()).IsTrue();
        await Assert.That(stream.ReadBoolean()).IsFalse();
        await Assert.That(stream.LeftBytes).IsEqualTo(0);
    }

    [Test]
    public async Task SummonGet_WritesCountThenSignedCharacterIds()
    {
        var stream = new SCTeamSummonGetPacket([1u, uint.MaxValue]).Write(new PacketStream());
        stream.Rollback();

        await Assert.That(stream.ReadInt32()).IsEqualTo(2);
        await Assert.That(stream.ReadInt64()).IsEqualTo(1L);
        await Assert.That(stream.ReadInt64()).IsEqualTo(4294967295L);
        await Assert.That(stream.LeftBytes).IsEqualTo(0);
    }

    [Test]
    public async Task SummonSuggest_WritesNameTypeZoneAndPosition()
    {
        var stream = new SCTeamSummonSuggestPacket("Summoner", 51u, 133u, 1.5f, -2.25f, 3.75f)
            .Write(new PacketStream());
        stream.Rollback();

        await Assert.That(stream.ReadString()).IsEqualTo("Summoner");
        await Assert.That(stream.ReadUInt32()).IsEqualTo(51u);
        await Assert.That(stream.ReadUInt32()).IsEqualTo(133u);
        await Assert.That(stream.ReadSingle()).IsEqualTo(1.5f);
        await Assert.That(stream.ReadSingle()).IsEqualTo(-2.25f);
        await Assert.That(stream.ReadSingle()).IsEqualTo(3.75f);
        await Assert.That(stream.LeftBytes).IsEqualTo(0);
    }

    [Test]
    public async Task Summon_HasNoBody()
    {
        var stream = new SCTeamSummonPacket().Write(new PacketStream());
        stream.Rollback();
        await Assert.That(stream.LeftBytes).IsEqualTo(0);
    }
}

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.UnitTests.Game.Core.Packets.G2C;

public class SCBlessUthstinPacketTests
{
    [Test]
    public async Task Apply_WritesFiveSignedStatsThenCountsAndLogin()
    {
        var page = new BlessUthstinPage
        {
            Strength = 4,
            Dexterity = -1,
            ApplyNormalCount = 1,
            ApplySpecialCount = 0
        };
        var body = new SCBlessUthstinApplyStatsPacket(1, true, page, 0, false)
            .Write(new PacketStream())
            .GetBytes();

        var expected = new PacketStream();
        expected.WriteBc(1);
        expected.Write(true);
        BlessUthstinRules.WriteAppliedStats(expected, page);
        expected.Write(0);
        expected.Write(1);
        expected.Write(0);
        expected.Write(false);

        await Assert.That(Hex.Of(body)).IsEqualTo(Hex.Of(expected.GetBytes()));
    }

    [Test]
    public async Task Copy_WritesDestinationThenFiveStatsAndCounts()
    {
        var page = new BlessUthstinPage { Strength = 6, Spirit = -2, ApplyNormalCount = 2 };
        var body = new SCBlessUthstinCopyPagePacket(1, true, 1, page)
            .Write(new PacketStream())
            .GetBytes();

        var expected = new PacketStream();
        expected.WriteBc(1);
        expected.Write(true);
        expected.Write(1);
        BlessUthstinRules.WriteAppliedStats(expected, page);
        expected.Write(2);
        expected.Write(0);

        await Assert.That(Hex.Of(body)).IsEqualTo(Hex.Of(expected.GetBytes()));
    }

    [Test]
    public async Task SkillObjectPage_ReadsOneByte()
    {
        var obj = SkillObject.GetByType(SkillObjectType.BlessUthstinPage);
        obj.Read(new PacketStream([(byte)2]));

        await Assert.That(obj).IsTypeOf<SkillObjectBlessUthstinPage>();
        await Assert.That(((SkillObjectBlessUthstinPage)obj).PageIndex).IsEqualTo((byte)2);
        await Assert.That(SkillObject.IsKnownType((int)SkillObjectType.BlessUthstinPage)).IsTrue();
    }
}

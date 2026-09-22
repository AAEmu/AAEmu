using AAEmu.Commons.Network;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Models.Game.Skills.Templates;

namespace AAEmu.UnitTests.Game.Core.Packets.G2C;

/// <summary>
/// SCSkillStarted is the only packet that carries a SkillResult. The result rides in the cast tail: a flag
/// byte, then the result byte (bit 1), the u16 detail (bit 2) and the u32 value (bit 4), in that order.
/// </summary>
public class SCSkillStartedPacketTests
{
    private static byte[] Body(SkillResult result, ushort detail = 0, uint value = 0)
    {
        var stream = new PacketStream();
        new SCSkillStartedPacket(1u, 0, new SkillCasterUnit(1500u), new SkillCastUnitTarget(1500u),
                new Skill { Template = new SkillTemplate() }, null)
            .SetSkillResult(result)
            .SetResultUShort(detail)
            .SetResultUInt(value)
            .Write(stream);
        return stream.GetBytes();
    }

    [Test]
    public async Task Success_EndsWithAnEmptyTail()
    {
        var body = Body(SkillResult.Success);

        await Assert.That(body[^1]).IsEqualTo((byte)0);
    }

    [Test]
    public async Task Failure_WritesTheResultByteAfterTheTailFlag()
    {
        var body = Body(SkillResult.TooFarRange);

        await Assert.That(body[^2]).IsEqualTo((byte)1);
        await Assert.That(body[^1]).IsEqualTo((byte)0x0F);
    }

    [Test]
    public async Task EveryResult_PutsItsOwnByteOnTheWire()
    {
        foreach (var result in Enum.GetValues<SkillResult>().Where(r => r != SkillResult.Success))
        {
            var body = Body(result);

            await Assert.That($"{result}: flag {body[^2]} byte 0x{body[^1]:X2}")
                .IsEqualTo($"{result}: flag 1 byte 0x{(byte)result:X2}");
        }
    }

    [Test]
    public async Task DetailAndValue_FollowTheResultByteInOrder()
    {
        // The leadership period failure: 0x90, detail 0x355 (WRONG_LEADERSHIP_POINT), value1 7.
        var body = Body(SkillResult.UrkLeadershipTotal, 0x355, 7);
        var tail = body[^8..];

        await Assert.That(tail[0]).IsEqualTo((byte)(1 | 2 | 4));
        await Assert.That(tail[1]).IsEqualTo((byte)0x90);
        await Assert.That(BitConverter.ToUInt16(tail, 2)).IsEqualTo((ushort)0x355);
        await Assert.That(BitConverter.ToUInt32(tail, 4)).IsEqualTo(7u);
    }

    [Test]
    public async Task ZeroDetailAndValue_AreNotWritten()
    {
        var plain = Body(SkillResult.UrkLevel);
        var withValue = Body(SkillResult.UrkLevel, 0, 30);

        await Assert.That(withValue.Length - plain.Length).IsEqualTo(4);
        await Assert.That(plain[^2]).IsEqualTo((byte)1);
        await Assert.That(withValue[^6]).IsEqualTo((byte)(1 | 4));
    }
}

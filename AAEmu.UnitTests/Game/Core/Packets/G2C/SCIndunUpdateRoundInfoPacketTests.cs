using AAEmu.Commons.Network;
using AAEmu.Game.Core.Packets.G2C;

namespace AAEmu.UnitTests.Game.Core.Packets.G2C;

/// <summary>
/// Wire order of SCIndunUpdateRoundInfo (0x2DA): curRound, isTimeLimitRound, roundLimitTime,
/// roundPlayTime, bossRound. Struct field offsets in the client object are not wire order.
/// </summary>
public class SCIndunUpdateRoundInfoPacketTests
{
    [Test]
    public async Task Write_EmitsClientSerializerOrder()
    {
        var packet = new SCIndunUpdateRoundInfoPacket(
            curRound: 3,
            roundLimitTime: 960u,
            roundPlayTime: 120u,
            isTimeLimitRound: true,
            bossRound: false);

        var bytes = packet.Write(new PacketStream()).GetBytes();

        await Assert.That(bytes.Length).IsEqualTo(1 + 1 + 4 + 4 + 1);
        await Assert.That(bytes[0]).IsEqualTo((byte)3);          // curRound sbyte
        await Assert.That(bytes[1]).IsEqualTo((byte)1);          // isTimeLimitRound
        await Assert.That(BitConverter.ToUInt32(bytes, 2)).IsEqualTo(960u);
        await Assert.That(BitConverter.ToUInt32(bytes, 6)).IsEqualTo(120u);
        await Assert.That(bytes[10]).IsEqualTo((byte)0);         // bossRound
    }
}

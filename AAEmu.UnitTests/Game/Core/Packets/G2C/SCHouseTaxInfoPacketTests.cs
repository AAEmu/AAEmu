using AAEmu.Commons.Network;
using AAEmu.Game.Core.Packets.G2C;

namespace AAEmu.UnitTests.Game.Core.Packets.G2C;

/// <summary>
/// The house-info tax reply. The house's timeline id leads; the rates and money pair follow.
/// Moving the id to the end made opening a house fire the community tax handler and crash.
/// </summary>
public class SCHouseTaxInfoPacketTests
{
    private static byte[] Body(ushort tl = 1234, uint dominionTaxRate = 5, uint hostileTaxRate = 7,
        ulong money = 11, ulong money2 = 22)
    {
        var stream = new PacketStream();
        new SCHouseTaxInfoPacket(tl, dominionTaxRate, hostileTaxRate, money, money2,
                new DateTime(2026, 9, 18, 0, 0, 0, DateTimeKind.Utc), false, 0, 0, true, 2)
            .Write(stream);
        return stream.GetBytes();
    }

    [Test]
    public async Task Body_IsTheDocumentedLength()
    {
        // u16 tl, u32, u32, u64, u64, date, five u8.
        await Assert.That(Body().Length).IsEqualTo(2 + 4 + 4 + 8 + 8 + 8 + 5);
    }

    [Test]
    public async Task TimelineId_Leads()
    {
        await Assert.That(BitConverter.ToUInt16(Body(tl: 4321), 0)).IsEqualTo((ushort)4321);
    }

    [Test]
    public async Task Rates_FollowTheTimelineId()
    {
        var body = Body(dominionTaxRate: 5, hostileTaxRate: 7);

        await Assert.That(BitConverter.ToUInt32(body, 2)).IsEqualTo(5u);
        await Assert.That(BitConverter.ToUInt32(body, 6)).IsEqualTo(7u);
    }

    [Test]
    public async Task MoneyPair_FollowsTheRates()
    {
        var body = Body(money: 11, money2: 22);

        await Assert.That(BitConverter.ToUInt64(body, 10)).IsEqualTo(11ul);
        await Assert.That(BitConverter.ToUInt64(body, 18)).IsEqualTo(22ul);
    }

    [Test]
    public async Task Flags_SitAfterTheDueDate()
    {
        var stream = new PacketStream();
        new SCHouseTaxInfoPacket(9, 0, 0, 0, 0, DateTime.UnixEpoch, isAlreadyPaid: true, weeksWithoutPay: 3,
                weeksPrepay: 1, isHeavyTaxHouse: false, taxType: 4)
            .Write(stream);
        var body = stream.GetBytes();

        await Assert.That(body[34]).IsEqualTo((byte)1); // isAlreadyPaid
        await Assert.That((sbyte)body[35]).IsEqualTo((sbyte)3); // weeksWithoutPay
        await Assert.That(body[36]).IsEqualTo((byte)1); // weeksPrepay
        await Assert.That(body[37]).IsEqualTo((byte)0); // isHeavyTaxHouse
        await Assert.That(body[38]).IsEqualTo((byte)4); // taxType
    }
}

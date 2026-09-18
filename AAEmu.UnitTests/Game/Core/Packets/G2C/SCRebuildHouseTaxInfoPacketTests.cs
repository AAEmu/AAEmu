using AAEmu.Commons.Network;
using AAEmu.Game.Core.Packets.G2C;

namespace AAEmu.UnitTests.Game.Core.Packets.G2C;

/// <summary>
/// The remodel window waits on this packet. The header is the house's timeline id, a dominion rate and
/// a row count; each quote is bt, vt, pd, wp, dtr with no pad between vt and pd.
/// </summary>
public class SCRebuildHouseTaxInfoPacketTests
{
    private static byte[] Body(ushort tl = 14, uint dominionRate = 5,
        params RebuildHouseTaxQuote[] quotes)
    {
        var stream = new PacketStream();
        new SCRebuildHouseTaxInfoPacket(tl, dominionRate, quotes).Write(stream);
        return stream.GetBytes();
    }

    [Test]
    public async Task Opcode_IsTheRebuildTaxReply()
    {
        await Assert.That(SCOffsets.SCRebuildHouseTaxInfoPacket).IsEqualTo((ushort)0x2BA);
        await Assert.That(SCOffsets.SCRebuildHouseTaxInfoPacket)
            .IsNotEqualTo(SCOffsets.SCHouseTaxInfoPacket);
        await Assert.That(SCOffsets.SCRebuildHouseTaxInfoPacket)
            .IsNotEqualTo(SCOffsets.SCConstructHouseTaxPacket);
    }

    [Test]
    public async Task EmptyQuotes_AreHeaderOnly()
    {
        var body = Body(tl: 14, dominionRate: 5);

        await Assert.That(body.Length).IsEqualTo(2 + 4 + 4);
        await Assert.That(BitConverter.ToUInt16(body, 0)).IsEqualTo((ushort)14);
        await Assert.That(BitConverter.ToUInt32(body, 2)).IsEqualTo(5u);
        await Assert.That(BitConverter.ToUInt32(body, 6)).IsEqualTo(0u);
    }

    [Test]
    public async Task TimelineId_Leads_SoTheClientCanFindTheHouseItAlreadyHas()
    {
        await Assert.That(BitConverter.ToUInt16(Body(tl: 4321), 0)).IsEqualTo((ushort)4321);
    }

    [Test]
    public async Task QuoteRow_HasNoPadBetweenTheFlagAndTheMoney()
    {
        var body = Body(14, 0, new RebuildHouseTaxQuote(432, 1, 99ul, 7, 3));

        await Assert.That(BitConverter.ToUInt32(body, 6)).IsEqualTo(1u);
        await Assert.That(BitConverter.ToUInt32(body, 10)).IsEqualTo(432u);
        await Assert.That(body[14]).IsEqualTo((byte)1);
        await Assert.That(BitConverter.ToUInt64(body, 15)).IsEqualTo(99ul);
        await Assert.That(BitConverter.ToUInt32(body, 23)).IsEqualTo(7u);
        await Assert.That(BitConverter.ToUInt32(body, 27)).IsEqualTo(3u);
        await Assert.That(body.Length).IsEqualTo(2 + 4 + 4 + 21);
    }

    [Test]
    public async Task QuoteCount_IsCappedAtTheClientLimit()
    {
        var quotes = Enumerable.Repeat(new RebuildHouseTaxQuote(1, 0, 0, 0, 0), 101).ToArray();
        var body = Body(1, 0, quotes);

        await Assert.That(BitConverter.ToUInt32(body, 6)).IsEqualTo(100u);
        await Assert.That(body.Length).IsEqualTo(2 + 4 + 4 + 21 * 100);
    }
}

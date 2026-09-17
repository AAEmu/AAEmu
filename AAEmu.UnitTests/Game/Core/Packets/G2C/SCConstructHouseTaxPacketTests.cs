using AAEmu.Commons.Network;
using AAEmu.Game.Core.Packets.G2C;

namespace AAEmu.UnitTests.Game.Core.Packets.G2C;

/// <summary>
/// The construction quote the client reads under the housing id it carries. That quote opens the
/// placement dialog; it is not the remodel window's reply.
/// </summary>
public class SCConstructHouseTaxPacketTests
{
    private static byte[] Body(uint designId = 432, ulong baseTax = 11, ulong deposit = 22,
        ulong total = 33, ulong weekly = 44, uint hostileRate = 7)
    {
        var stream = new PacketStream();
        new SCConstructHouseTaxPacket(designId, 3, 4, true, baseTax, deposit, total, weekly, hostileRate)
            .Write(stream);
        return stream.GetBytes();
    }

    [Test]
    public async Task Body_IsTheDocumentedLayout()
    {
        // designId(u32), heavy(u32), normal(u32), isHeavy(u8), four u64 money fields, hostileRate(u32).
        var body = Body();

        await Assert.That(body.Length).IsEqualTo(4 + 4 + 4 + 1 + 8 * 4 + 4);
    }

    [Test]
    public async Task DesignId_Leads_SoTheQuoteIsKeyedByTheTargetHousing()
    {
        await Assert.That(BitConverter.ToUInt32(Body(designId: 434), 0)).IsEqualTo(434u);
    }

    [Test]
    public async Task MoneyFields_KeepTheirOrder()
    {
        var body = Body(baseTax: 11, deposit: 22, total: 33, weekly: 44);

        await Assert.That(BitConverter.ToUInt64(body, 13)).IsEqualTo(11ul);
        await Assert.That(BitConverter.ToUInt64(body, 21)).IsEqualTo(22ul);
        await Assert.That(BitConverter.ToUInt64(body, 29)).IsEqualTo(33ul);
        await Assert.That(BitConverter.ToUInt64(body, 37)).IsEqualTo(44ul);
    }

    [Test]
    public async Task HostileRate_IsTheTrailingField()
    {
        var body = Body(hostileRate: 9);

        await Assert.That(BitConverter.ToUInt32(body, body.Length - 4)).IsEqualTo(9u);
    }
}

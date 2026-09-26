using AAEmu.Commons.Network;
using AAEmu.Game.Core.Packets.G2C;

namespace AAEmu.UnitTests.Game.Core.Packets.G2C;

public class SCSiegeMemberPacketTests
{
    [Test]
    public async Task Write_CarriesTheZoneGroupTheTeamKeyAndTheCharacter()
    {
        // The client looks its dominion record up by the zone group and its team up by the alliance id; a
        // packet that carries anything else in those two slots is looked up, misses, and dropped.
        var body = new SCSiegeMemberPacket(33, 148, 1752ul, true).Write(new PacketStream()).GetBytes();

        var expected = new PacketStream();
        expected.Write((ushort)33);
        expected.Write(148);
        expected.Write(1752ul);
        expected.Write(true);

        await Assert.That(body).IsEquivalentTo(expected.GetBytes());
    }

    [Test]
    public async Task TypeId_IsTheClientSiegeMemberOpcode()
    {
        await Assert.That(new SCSiegeMemberPacket(0, 0, 0, false).TypeId).IsEqualTo((ushort)0x12A);
    }

    [Test]
    public async Task Write_KeepsTheAddedFlagLast()
    {
        var added = new SCSiegeMemberPacket(33, 148, 1752ul, true).Write(new PacketStream()).GetBytes();
        var removed = new SCSiegeMemberPacket(33, 148, 1752ul, false).Write(new PacketStream()).GetBytes();

        await Assert.That(added.Length).IsEqualTo(removed.Length);
        await Assert.That(added[^1]).IsNotEqualTo(removed[^1]);
    }
}

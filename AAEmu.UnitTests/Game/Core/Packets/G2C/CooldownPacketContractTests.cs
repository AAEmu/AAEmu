using AAEmu.Commons.Network;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.UnitTests.Game.Core.Packets.G2C;

public class CooldownPacketContractTests
{
    [Test]
    public async Task ReducePacket_PreservesAuthoredNegativeValues()
    {
        var body = new SCSkillCooldownReducePacket(
                0x123456,
                0x11223344,
                0x55667788,
                -7,
                -3,
                -11,
                true,
                false,
                true)
            .Write(new PacketStream())
            .GetBytes();

        var expected = new PacketStream();
        expected.WriteBc(0x123456);
        expected.Write(0x11223344u);
        expected.Write(0x55667788u);
        expected.Write(-7);
        expected.Write(-3);
        expected.Write(-11);
        expected.Write(true);
        expected.Write(false);
        expected.Write(true);

        await Assert.That(body).IsEquivalentTo(expected.GetBytes());
        await Assert.That(BitConverter.ToInt32(body, 11)).IsEqualTo(-7);
        await Assert.That(BitConverter.ToInt32(body, 15)).IsEqualTo(-3);
        await Assert.That(BitConverter.ToInt32(body, 19)).IsEqualTo(-11);
    }

    [Test]
    public async Task ChargePacket_PreservesAuthoredNegativeValues()
    {
        var body = new SCChargeSkillCooldownChangedPacket(0x123456, 0x11223344, -7, -3, -11)
            .Write(new PacketStream())
            .GetBytes();

        var expected = new PacketStream();
        expected.WriteBc(0x123456);
        expected.Write(0x11223344u);
        expected.Write(-7);
        expected.Write(-3);
        expected.Write(-11);

        await Assert.That(body).IsEquivalentTo(expected.GetBytes());
        await Assert.That(BitConverter.ToInt32(body, 7)).IsEqualTo(-7);
        await Assert.That(BitConverter.ToInt32(body, 11)).IsEqualTo(-3);
        await Assert.That(BitConverter.ToInt32(body, 15)).IsEqualTo(-11);
    }

    [Test]
    public async Task ResetPacket_WritesAllFourFlagsInOrder()
    {
        var character = new Character(new UnitCustomModelParams()) { ObjId = 0x123456 };
        var body = new SCSkillCooldownResetPacket(character, 0x11223344, 0x55667788, true, true, false, true)
            .Write(new PacketStream())
            .GetBytes();

        var expected = new PacketStream();
        expected.WriteBc(0x123456);
        expected.Write(0x11223344u);
        expected.Write(0x55667788u);
        expected.Write(true);
        expected.Write(true);
        expected.Write(false);
        expected.Write(true);

        await Assert.That(body).IsEquivalentTo(expected.GetBytes());
        await Assert.That(body.Length).IsEqualTo(15);
        await Assert.That(body[11]).IsEqualTo((byte)1);
        await Assert.That(body[12]).IsEqualTo((byte)1);
        await Assert.That(body[13]).IsEqualTo((byte)0);
        await Assert.That(body[14]).IsEqualTo((byte)1);
    }

    [Test]
    public async Task CooldownPacket_ContainsSkillTagAndChargeBucketCounts()
    {
        var cooldowns = new UnitCooldowns();
        cooldowns.AddCooldown(0x11223344, 60_000);

        var body = new SCCooldownsPacket(cooldowns)
            .Write(new PacketStream())
            .GetBytes();

        await Assert.That(body.Length).IsEqualTo(24);
        await Assert.That(BitConverter.ToUInt32(body, 0)).IsEqualTo(1u);
        await Assert.That(BitConverter.ToUInt32(body, 4)).IsEqualTo(0x11223344u);
        await Assert.That(BitConverter.ToUInt32(body, 8)).IsEqualTo(60_000u);
        await Assert.That(BitConverter.ToUInt32(body, 12)).IsGreaterThan(0u);
        await Assert.That(BitConverter.ToUInt32(body, 16)).IsEqualTo(0u);
        await Assert.That(BitConverter.ToUInt32(body, 20)).IsEqualTo(0u);
    }

    [Test]
    public async Task EmptyCooldownPacket_StillWritesAllThreeBucketCounts()
    {
        var body = new SCCooldownsPacket(new UnitCooldowns())
            .Write(new PacketStream())
            .GetBytes();

        await Assert.That(body.Length).IsEqualTo(12);
        await Assert.That(BitConverter.ToUInt32(body, 0)).IsEqualTo(0u);
        await Assert.That(BitConverter.ToUInt32(body, 4)).IsEqualTo(0u);
        await Assert.That(BitConverter.ToUInt32(body, 8)).IsEqualTo(0u);
    }
}

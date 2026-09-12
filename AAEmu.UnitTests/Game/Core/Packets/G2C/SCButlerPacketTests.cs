using AAEmu.Commons.Network;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Butlers;

namespace AAEmu.UnitTests.Game.Core.Packets.G2C;

public class SCButlerPacketTests
{
    [Test]
    public async Task InitInfo_WritesHouseNameBeforeAllCommonStateFields()
    {
        var info = new ButlerInfoWire(sbyte.MinValue, "Farmhand 한", ushort.MaxValue, uint.MaxValue, 0,
            ushort.MaxValue);
        var stream = new SCButlerInitInfoPacket("Dawn's Cottage", info).Write(new PacketStream());

        stream.Rollback();
        await Assert.That(stream.ReadString()).IsEqualTo("Dawn's Cottage");
        await AssertCommonInfo(stream, info);
        await Assert.That(stream.LeftBytes).IsEqualTo(0);
    }

    [Test]
    public async Task Bound_WritesCommonStateThenHouseAndErrorMessage()
    {
        var info = new ButlerInfoWire(sbyte.MaxValue, "Maid", 0, 1, ushort.MaxValue, 0);
        var stream = new SCButlerBoundPacket(info, string.Empty, ushort.MaxValue).Write(new PacketStream());

        stream.Rollback();
        await AssertCommonInfo(stream, info);
        await Assert.That(stream.ReadString()).IsEqualTo(string.Empty);
        await Assert.That(stream.ReadUInt16()).IsEqualTo(ushort.MaxValue);
        await Assert.That(stream.LeftBytes).IsEqualTo(0);
    }

    [Test]
    public async Task InfoUpdated_WritesCompleteFixedTailWithoutInterpretingFlags()
    {
        var stream = new SCButlerInfoUpdatedPacket(ushort.MaxValue, short.MinValue, true, false, uint.MaxValue, 0,
            ushort.MaxValue, "Updated 한").Write(new PacketStream());

        stream.Rollback();
        await Assert.That(stream.ReadUInt16()).IsEqualTo(ushort.MaxValue);
        await Assert.That(stream.ReadInt16()).IsEqualTo(short.MinValue);
        await Assert.That(stream.ReadBoolean()).IsTrue();
        await Assert.That(stream.ReadBoolean()).IsFalse();
        await Assert.That(stream.ReadUInt32()).IsEqualTo(uint.MaxValue);
        await Assert.That(stream.ReadUInt16()).IsEqualTo((ushort)0);
        await Assert.That(stream.ReadUInt16()).IsEqualTo(ushort.MaxValue);
        await Assert.That(stream.ReadString()).IsEqualTo("Updated 한");
        await Assert.That(stream.LeftBytes).IsEqualTo(0);
    }

    [Test]
    public async Task ButlerOpcodes_MatchTheClientRegistrationSlots()
    {
        await Assert.That(SCOffsets.SCButlerInitInfoPacket).IsEqualTo((ushort)0x345);
        await Assert.That(SCOffsets.SCButlerBoundPacket).IsEqualTo((ushort)0x346);
        await Assert.That(SCOffsets.SCButlerInfoUpdatedPacket).IsEqualTo((ushort)0x34B);
        await Assert.That(SCOffsets.SCButlerSpawnedPacket).IsEqualTo((ushort)0x347);
        await Assert.That(SCOffsets.SCButlerDespawnedPacket).IsEqualTo((ushort)0x349);
        await Assert.That(SCOffsets.SCButlerLookChangedPacket).IsEqualTo((ushort)0x34D);
    }

    private static async Task AssertCommonInfo(PacketStream stream, ButlerInfoWire expected)
    {
        await Assert.That(stream.ReadSByte()).IsEqualTo(expected.WorldId);
        await Assert.That(stream.ReadString()).IsEqualTo(expected.Name);
        await Assert.That(stream.ReadUInt16()).IsEqualTo(expected.HouseTlId);
        await Assert.That(stream.ReadUInt32()).IsEqualTo(expected.LaborPower);
        await Assert.That(stream.ReadUInt16()).IsEqualTo(expected.LpChargedAmount);
        await Assert.That(stream.ReadUInt16()).IsEqualTo(expected.RemainProductionCost);
    }
}

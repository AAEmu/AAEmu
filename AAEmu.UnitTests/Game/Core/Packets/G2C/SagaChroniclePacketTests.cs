using AAEmu.Commons.Network;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;

namespace AAEmu.UnitTests.Game.Core.Packets.G2C;

/// <summary>
/// The chronicle (saga) sync wire: the list pushed at login / world entry, the buy answer, and the
/// status update sent when a group changes — field order from the client's serializer.
/// </summary>
public class SagaChroniclePacketTests
{
    [Test]
    public async Task Offsets_MatchTheWireCatalog()
    {
        await Assert.That(SCOffsets.SCChronicleInfoBuyPacket).IsEqualTo((ushort)0x355);
        await Assert.That(SCOffsets.SCChronicleInfoListPacket).IsEqualTo((ushort)0x357);
        await Assert.That(SCOffsets.SCChronicleInfoUpdatePacket).IsEqualTo((ushort)0x358);
    }

    [Test]
    public async Task List_WritesFlagsCountThenOneTypeStatusEntryPerRecord()
    {
        (int Type, sbyte Status)[] entries = [(8, 0), (9, 1)];
        var body = new SCChronicleInfoListPacket(isFirst: true, endList: true, entries)
            .Write(new PacketStream())
            .GetBytes();

        var expected = new PacketStream();
        expected.Write(true);
        expected.Write(true);
        expected.Write(2);
        expected.Write(8);
        expected.Write((sbyte)0);
        expected.Write(9);
        expected.Write((sbyte)1);

        await Assert.That(body).IsEquivalentTo(expected.GetBytes());
    }

    [Test]
    public async Task List_EmptyStillCarriesBothBoundaryFlagsAndZeroCount()
    {
        var body = new SCChronicleInfoListPacket(isFirst: true, endList: true, [])
            .Write(new PacketStream())
            .GetBytes();

        var expected = new PacketStream();
        expected.Write(true);
        expected.Write(true);
        expected.Write(0);

        await Assert.That(body).IsEquivalentTo(expected.GetBytes());
    }

    [Test]
    public async Task Buy_WritesResultErrorTypeAndStatus()
    {
        var body = new SCChronicleInfoBuyPacket(true, ErrorMessageType.NoErrorMessage, 8, 0)
            .Write(new PacketStream())
            .GetBytes();

        var expected = new PacketStream();
        expected.Write(true);
        expected.Write((ushort)ErrorMessageType.NoErrorMessage);
        expected.Write(8);
        expected.Write((sbyte)0);

        await Assert.That(body).IsEquivalentTo(expected.GetBytes());
    }

    [Test]
    public async Task Update_WritesPreviousAndCurrentStatusThenType()
    {
        var body = new SCChronicleInfoUpdatePacket((sbyte)0, (sbyte)1, 9)
            .Write(new PacketStream())
            .GetBytes();

        var expected = new PacketStream();
        expected.Write((sbyte)0);
        expected.Write((sbyte)1);
        expected.Write(9);

        await Assert.That(body).IsEquivalentTo(expected.GetBytes());
    }
}

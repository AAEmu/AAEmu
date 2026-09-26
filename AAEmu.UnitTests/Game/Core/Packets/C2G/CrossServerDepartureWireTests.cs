using AAEmu.Commons.Network;
using AAEmu.Game.Core.Packets.C2G;
using AAEmu.Game.Core.Packets.G2C;

namespace AAEmu.UnitTests.Game.Core.Packets.C2G;

/// <summary>
/// Wire half of GF-S15. Field lists come from the 10.0.2.13 client's own packet structs and
/// the protocol catalogs (protocol-10.0.2.13/catalog_CS.md and catalog_SC.md):
/// <list type="bullet">
/// <item><c>CSDepartToForeignServerPacket</c> 0x1C7, CS, fields [] — the frame carries no body
/// and no destination.</item>
/// <item><c>SCDepartureServerGrantedPacket</c> 0x006, SC, fields [] — the grant writes nothing.</item>
/// <item><c>CSReentryReponsePacket</c> 0x12D, CS, u8 cancel — exactly one byte.</item>
/// </list>
/// These tests pin that shape so a future edit cannot silently widen or truncate the stream the
/// client parses.
/// </summary>
public class CrossServerDepartureWireTests
{
    [Test]
    public async Task Opcodes_ArePinnedToTheShippedClient()
    {
        await Assert.That(CSOffsets.CSDepartToForeignServerPacket).IsEqualTo((ushort)0x1C7);
        await Assert.That(SCOffsets.SCDepartureServerGrantedPacket).IsEqualTo((ushort)0x006);
        await Assert.That(CSOffsets.CSReentryReponsePacket).IsEqualTo((ushort)0x12D);
    }

    [Test]
    public async Task DepartureGranted_WritesNoBody()
    {
        var body = new PacketStream();
        new SCDepartureServerGrantedPacket().Write(body);

        await Assert.That(body.Count).IsEqualTo(0);
    }

    [Test]
    public async Task DepartureFrame_WithNoBody_IsAcceptedAndChangesNoSessionState()
    {
        var packet = new CSDepartToForeignServerPacket();

        // Connection is null outside a live session: the parse itself must still succeed and
        // must not resolve the manager or touch any character.
        packet.Read(new PacketStream());

        await Assert.That(packet).IsNotNull();
    }

    [Test]
    public async Task DepartureFrame_WithAnyBodyBytes_FailsLoud()
    {
        await Assert.That(() => new CSDepartToForeignServerPacket().Read(new PacketStream(new byte[] { 0x01 })))
            .Throws<InvalidDataException>();
        await Assert.That(() => new CSDepartToForeignServerPacket().Read(new PacketStream(new byte[] { 0x01, 0x02 })))
            .Throws<InvalidDataException>();
    }

    [Test]
    public async Task ReentryResponse_RoundTripsThePinnedCancelByte()
    {
        var accepted = new CSReentryReponsePacket();
        accepted.Read(new PacketStream().Write((byte)1));
        await Assert.That(accepted.Cancel).IsTrue();
        await Assert.That(accepted.Cancel).IsNotEqualTo(false);

        var cancelled = new CSReentryReponsePacket();
        cancelled.Read(new PacketStream().Write((byte)0));
        await Assert.That(cancelled.Cancel).IsFalse();
    }

    [Test]
    public async Task ReentryResponse_TruncatedOrOverlongFrame_FailsLoud()
    {
        // Truncated: the overrun default would read as "cancel = false" — a silent wrong answer.
        await Assert.That(() => new CSReentryReponsePacket().Read(new PacketStream()))
            .Throws<InvalidDataException>();

        // Overlong: the pinned shape is exactly one u8.
        await Assert.That(() => new CSReentryReponsePacket().Read(new PacketStream(new byte[] { 1, 2 })))
            .Throws<InvalidDataException>();
    }
}

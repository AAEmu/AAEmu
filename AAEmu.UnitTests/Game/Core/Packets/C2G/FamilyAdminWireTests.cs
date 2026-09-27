using System.IO;
using System.Text;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Packets.C2G;
using AAEmu.Game.Models.Game.Families;

namespace AAEmu.UnitTests.Game.Core.Packets.C2G;

/// <summary>
/// Wire guards for the family administration requests.
/// </summary>
/// <remarks>
/// A notice and a name are the two client-authored strings that overwrite persisted family state.
/// Both arrive length-prefixed, and the generic reader answers a short body with an empty string,
/// which is a legal notice. These tests pin that a body which does not fit its own declared length,
/// or which declares more than the documented read limit, is refused instead of relayed.
/// </remarks>
public class FamilyAdminWireTests
{
    [Test]
    public async Task ReadBoundedString_AcceptsAPayloadAtTheLimit()
    {
        var text = new string('a', FamilyProgressionRules.MaximumNoticeUtf8Bytes);
        var stream = Body(text);

        await Assert.That(FamilyAdminWire.ReadBoundedString(stream, "test",
            FamilyProgressionRules.MaximumNoticeUtf8Bytes)).IsEqualTo(text);
        await Assert.That(stream.Overran).IsFalse();
    }

    [Test]
    public async Task ReadBoundedString_AcceptsTheEmptyNotice()
    {
        var stream = Body(string.Empty);

        await Assert.That(FamilyAdminWire.ReadBoundedString(stream, "test",
            FamilyProgressionRules.MaximumNoticeUtf8Bytes)).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task ReadBoundedString_RefusesADeclaredLengthPastTheLimit()
    {
        // The limit is a byte count, so a multi-byte body that crosses it is refused as well.
        var stream = new PacketStream();
        stream.Write((short)(FamilyProgressionRules.MaximumNoticeUtf8Bytes + 1));

        await Assert.That(() => FamilyAdminWire.ReadBoundedString(stream, "test",
            FamilyProgressionRules.MaximumNoticeUtf8Bytes)).Throws<InvalidDataException>();
    }

    [Test]
    public async Task ReadBoundedString_RefusesATruncatedBody()
    {
        // Declares a longer string than the body carries. The generic reader would answer an empty
        // string here, which the notice path would then persist as an empty notice.
        var stream = new PacketStream();
        stream.Write((short)64);
        stream.Write("short", appendSize: false);

        await Assert.That(() => FamilyAdminWire.ReadBoundedString(stream, "test",
            FamilyProgressionRules.MaximumNoticeUtf8Bytes)).Throws<InvalidDataException>();
    }

    [Test]
    public async Task ReadBoundedString_RefusesABodyWithNoLengthPrefix()
    {
        var stream = new PacketStream();
        stream.Write((byte)0x41);

        await Assert.That(() => FamilyAdminWire.ReadBoundedString(stream, "test",
            FamilyProgressionRules.MaximumNoticeUtf8Bytes)).Throws<InvalidDataException>();
    }

    [Test]
    public async Task ReadBoundedString_RefusesANegativeDeclaredLength()
    {
        var stream = new PacketStream();
        stream.Write(short.MinValue);

        await Assert.That(() => FamilyAdminWire.ReadBoundedString(stream, "test",
            FamilyProgressionRules.MaximumNoticeUtf8Bytes)).Throws<InvalidDataException>();
    }

    [Test]
    public async Task ChangeMemberRole_RefusesATruncatedBody()
    {
        var stream = new PacketStream();
        stream.Write(7u); // only half of the member id

        var packet = new CSFamilyChangeMemberRolePacket();
        await Assert.That(() => packet.Read(stream)).Throws<InvalidDataException>();
    }

    [Test]
    public async Task ChangeMemberRole_ReadsAWellFormedBody()
    {
        var stream = new PacketStream();
        stream.Write(9_000_000_000UL);
        stream.Write(3);

        var packet = new CSFamilyChangeMemberRolePacket();
        // No connection is attached, so the request is parsed and then dropped before the manager call.
        packet.Read(stream);
        await Assert.That(packet.TypeValue).IsEqualTo(9_000_000_000UL);
        await Assert.That(packet.TypeValue2).IsEqualTo(3);
    }

    [Test]
    public async Task NoticeSet_RefusesABodyLongerThanTheNoticeLimit()
    {
        var stream = new PacketStream();
        stream.Write(new string('a', FamilyProgressionRules.MaximumNoticeUtf8Bytes + 1));

        var packet = new CSFamilyNoticeSetPacket();
        await Assert.That(() => packet.Read(stream)).Throws<InvalidDataException>();
    }

    [Test]
    public async Task NameSet_RefusesABodyLongerThanTheNameLimit()
    {
        var stream = new PacketStream();
        stream.Write(new string('a', FamilyProgressionRules.MaximumFamilyNameUtf8Bytes + 1));

        var packet = new CSFamilyNameSetPacket();
        await Assert.That(() => packet.Read(stream)).Throws<InvalidDataException>();
    }

    [Test]
    public async Task NameSet_AcceptsAMultiByteNameInsideTheByteLimit()
    {
        // The limit counts UTF-8 bytes, so a short CJK name is well inside it.
        var name = "가장";
        var stream = new PacketStream();
        stream.Write(name);

        var packet = new CSFamilyNameSetPacket();
        packet.Read(stream);
        await Assert.That(packet.Name).IsEqualTo(name);
        await Assert.That(Encoding.UTF8.GetByteCount(packet.Name))
            .IsLessThanOrEqualTo(FamilyProgressionRules.MaximumFamilyNameUtf8Bytes);
    }

    [Test]
    public async Task NoticeSet_RefusesATruncatedBodyInsteadOfStoringAnEmptyNotice()
    {
        // The generic reader answers a short body with an empty string, which is a legal notice, so a
        // truncated request would otherwise wipe the stored notice.
        var stream = new PacketStream();
        stream.Write((short)64);
        stream.Write("short", appendSize: false);

        var packet = new CSFamilyNoticeSetPacket();
        await Assert.That(() => packet.Read(stream)).Throws<InvalidDataException>();
        await Assert.That(packet.Notice).IsNull();
    }

    private static PacketStream Body(string text)
    {
        var stream = new PacketStream();
        stream.Write(text);
        stream.Rollback();
        return stream;
    }
}
